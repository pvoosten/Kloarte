using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel as a remote client (the MCP server's tools) uses the app (<see cref="IRemoteControl"/>): the same paths
/// as the UI's (cue edits through <see cref="CueEditing.Edit"/>, undo, Save, the export), each edit recorded as one undo
/// step at once, and pictures of the views.
/// </summary>
public partial class FixturePanel : IRemoteControl
{
    /// <summary>Frames a 3D view is rendered for a picture before it is taken.</summary>
    private const int CaptureFrames = 3;

    /// <summary>The size a 3D view is rendered at for a picture when it is hidden or small (a card): 16:9.</summary>
    private static readonly Vector2I PictureSize = new(1280, 720);

    /// <summary>A 3D view smaller than this (a small card) is rendered at <see cref="PictureSize"/> for a picture.</summary>
    private static readonly Vector2I SmallView = new(640, 360);

    /// <inheritdoc />
    IShowFiles IRemoteControl.Files => CurrentBuilder?.Files;

    /// <inheritdoc />
    DesignShow IRemoteControl.Design => CueEditing.Design(CurrentBuilder);

    /// <inheritdoc />
    int? IRemoteControl.CueId => _player?.Stack != null && _player.CueId >= 0 ? _player.CueId : null;

    /// <inheritdoc />
    bool IRemoteControl.Running => _player?.Running == true;

    /// <inheritdoc />
    void IRemoteControl.GoToCue(int? cueId)
    {
        if (_player?.Stack == null)
            throw new InvalidOperationException("No cue list to play");
        int step = -1;
        if (cueId is { } id)
        {
            var steps = _player.Stack.Steps;
            for (step = 0; step < steps.Count && steps[step].CueId != id; step++) { }
            if (step == steps.Count)
                throw new InvalidOperationException($"No cue with id {id} in the cue list");
        }
        _player.Jump(step);
    }

    /// <inheritdoc />
    void IRemoteControl.Go()
    {
        if (_player?.Stack == null)
            throw new InvalidOperationException("No cue list to play");
        _player.Go();
    }

    /// <inheritdoc />
    float IRemoteControl.Haze
    {
        get => CurrentBuilder?.Files.Look?.Haze ?? HazeControl.Current;
        set
        {
            float density = Mathf.Clamp(value, 0f, ProjectLook.MaxHaze);
            if (CurrentBuilder?.Files.Look is { } look)
                look.Haze = density;
            SetHaze(density);
            _player?.Refresh();   // before the first cue that sets smoke, this is the haze of the show
            RebuildParamsIfIdle();
        }
    }

    /// <inheritdoc />
    float IRemoteControl.StrayLight
    {
        get => CurrentBuilder?.Files.Look?.StrayLight ?? ProjectLook.DefaultStrayLight;
        set
        {
            float amount = Mathf.Clamp(value, 0f, 2f);
            if (CurrentBuilder?.Files.Look is { } look)
                look.StrayLight = amount;
            SetStrayLight(amount);
            RebuildParamsIfIdle();
        }
    }

    /// <inheritdoc />
    IReadOnlyList<int> IRemoteControl.SelectedLamps =>
        CurrentBuilder is { } builder
            ? Selection.Fixtures.Where(IsInstanceValid).Select(f => builder.VisOf(f)?.Id ?? -1).Where(id => id >= 0).Distinct().ToList()
            : Array.Empty<int>();

    /// <inheritdoc />
    int IRemoteControl.SelectLamps(IReadOnlyCollection<int> lampIds)
    {
        var builder = CurrentBuilder ?? throw new InvalidOperationException("No show loaded");
        var lamps = builder.Fixtures.Where(f => builder.VisOf(f) is { } vis && lampIds.Contains(vis.Id)).ToList();
        if (lamps.Count == 0)
        {
            Deselect();
            return 0;
        }
        Select(lamps[0]);
        Selection.Select(lamps);
        return lamps.Count;
    }

    /// <inheritdoc />
    string IRemoteControl.EditShow(string name, Action<DesignShow> change)
    {
        var builder = CurrentBuilder;
        if (CueEditing.Design(builder) == null)
            throw new InvalidOperationException("No native show is open (open or import a project first)");
        builder.Checkpoint();   // an edit made in the UI before is its own undo step
        string entry = null;
        try
        {
            CueEditing.Edit(builder, name, change);
            if (_player?.Stack is { } stack && _player.Step >= stack.Steps.Count)
                _player.Jump(stack.Steps.Count - 1);   // the current cue was deleted
            entry = builder.Checkpoint();
        }
        finally
        {
            // Nothing recorded (no change, or the change threw): the name must not go to the next UI edit.
            if (entry == null)
                builder.Files.NameNextEdit(null);
        }
        RefreshUnsaved();
        UpdateProjectBox();
        return entry;
    }

    /// <inheritdoc />
    async Task<string> IRemoteControl.EditModel(string name, Action<IShowModel> change, bool rebuild)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        builder.Checkpoint();
        builder.Files.NameNextEdit(name);
        string entry = null;
        try
        {
            if (rebuild)
            {
                var view = TakeView(builder);
                change(builder.Model);
                // Recorded before the rebuild, which takes the state after it as the baseline (ResyncHistory); the
                // session's checkpoint, not the builder's: that would copy the old nodes' mechanics back first.
                entry = builder.Files.Checkpoint();
                builder.RebuildFromModel();
                await RestoreView(builder, view);
            }
            else
            {
                change(builder.Model);
                _player?.Invalidate();
                if (_player?.Stack != null && _player.Step >= 0)
                    _player.Seek(_player.Time);
                SyncPlayback();
                entry = builder.Checkpoint();
            }
        }
        finally
        {
            // Nothing recorded (no change, or the change threw): the name must not go to the next UI edit.
            if (entry == null)
                builder.Files.NameNextEdit(null);
        }
        RefreshUnsaved();
        UpdateProjectBox();
        return entry;
    }

    /// <inheritdoc />
    (float Pan, float Tilt)? IRemoteControl.AimAngles(int head, System.Numerics.Vector3 stagePoint)
    {
        var builder = CurrentBuilder ?? throw new InvalidOperationException("No show loaded");
        var mover = builder.Fixtures.Where(f => f.HeadNumber == head).Select(builder.MoverOf).FirstOrDefault(m => m != null);
        return mover?.AimAt(builder.ToGlobal(stagePoint.ToGodot()));
    }

    /// <inheritdoc />
    string IRemoteControl.FocusLamps(string name, IReadOnlyDictionary<int, System.Numerics.Vector3> points)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        var lamps = new List<(ProfileFixture Lamp, System.Numerics.Vector3 Point)>();
        foreach (var (id, point) in points)
        {
            var lamp = builder.FixtureOf(id) ?? throw new ArgumentException($"Lamp {id} is not on stage");
            if (builder.MoverOf(lamp) != null)
                throw new ArgumentException($"Lamp {id} is a moving head: aim it with pan / tilt in a cue");
            lamps.Add((lamp, point));
        }
        string entry = null;
        builder.Checkpoint();   // an edit made in the UI before is its own undo step
        try
        {
            foreach (var (lamp, point) in lamps)
                builder.FocusAt(lamp, point.ToGodot());
            builder.Files.NameNextEdit(name);   // FocusAt names each lamp's edit; the whole call is one step
            entry = builder.Checkpoint();
        }
        finally
        {
            if (entry == null)
                builder.Files.NameNextEdit(null);
        }
        Selection.Touch();   // the lamp settings (focus point) and the plan follow
        RefreshUnsaved();
        UpdateProjectBox();
        return entry;
    }

    /// <inheritdoc />
    StageFrame IRemoteControl.Frame => CurrentBuilder?.Frame ?? throw new InvalidOperationException("No show loaded");

    /// <inheritdoc />
    IReadOnlyList<int> IRemoteControl.AddLamps(string name, string modelId, int supportId, IReadOnlyList<float> stageX,
        int? firstHead, int? firstAddress)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        var support = new Rig(builder.Document).Find(supportId) ?? throw new ArgumentException($"No support {supportId}");
        float z = builder.Frame.ToStage(support.Start.X, support.Start.Y, support.Start.Z).Z;
        var ids = new List<int>();
        string entry = null;
        builder.Checkpoint();   // an edit made in the UI before is its own undo step
        try
        {
            int? head = firstHead, address = firstAddress;
            foreach (float x in stageX)
            {
                // Added near its place on the support (the plan snaps it to the nearest one), then hung on that one.
                int id = builder.RigCommands.AddLamp(modelId, new System.Numerics.Vector2(x, z));
                builder.RigCommands.SetParent(id, supportId);
                if (head != null || address != null)
                {
                    var lamp = builder.Document.RigItems.First(i => i.Id == id);
                    int channels = CueEditing.Design(builder)?.ChannelCountOfHead(lamp.HeadNumber) ?? 1;
                    builder.RigCommands.ApplyLampPatch(id, head ?? lamp.HeadNumber, address ?? lamp.Address);
                    head = head + 1;
                    address = address + Math.Max(1, channels);
                }
                ids.Add(id);
            }
            builder.Files.NameNextEdit(name);   // the commands name each step; the whole call is one
            entry = builder.Checkpoint();
        }
        finally
        {
            if (entry == null)
                builder.Files.NameNextEdit(null);
        }
        RefreshUnsaved();
        UpdateProjectBox();
        return ids;
    }

    /// <inheritdoc />
    string IRemoteControl.RemoveLamps(string name, IReadOnlyCollection<int> lampIds)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        foreach (int id in lampIds)
            if (builder.FixtureOf(id) == null)
                throw new ArgumentException($"Lamp {id} is not on stage");
        var heads = lampIds.Select(id => builder.Document.RigItems.First(i => i.Id == id).HeadNumber).Distinct().ToList();
        var done = new List<string>();
        string entry = null;
        Deselect();   // the removed lamps' nodes go
        builder.Checkpoint();
        try
        {
            foreach (int id in lampIds)
                done.Add(builder.RigCommands.RemoveLamp(id));
            if (CueEditing.Design(builder) is { } design)
            {
                var forgotten = heads.Where(h => design.Head(h) != null && DesignEdits.ForgetHead(design, h)).ToList();
                if (forgotten.Count > 0)
                {
                    design.Changed();
                    builder.PatchChanged();
                    CueEditing.Refresh();
                    done.Add($"head{(forgotten.Count == 1 ? "" : "s")} {string.Join(", ", forgotten)} unpatched and taken out of the cues");
                }
            }
            builder.Files.NameNextEdit(name);
            entry = builder.Checkpoint();
        }
        finally
        {
            if (entry == null)
                builder.Files.NameNextEdit(null);
        }
        RefreshUnsaved();
        UpdateProjectBox();
        return string.Join("; ", done);
    }

    /// <inheritdoc />
    string IRemoteControl.SetGel(IReadOnlyCollection<int> lampIds, int gelId)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        builder.Checkpoint();   // an edit made in the UI before is its own undo step
        string result = builder.RigCommands.SetGel(lampIds, gelId);   // names the step "Gel L119"
        builder.Checkpoint();
        Selection.Touch();   // the lamp settings and the fixture list show the gel
        RefreshUnsaved();
        return result;
    }

    /// <inheritdoc />
    Task<string> IRemoteControl.Undo() => RemoteHistory(redo: false);

    /// <inheritdoc />
    Task<string> IRemoteControl.Redo() => RemoteHistory(redo: true);

    /// <summary>Undo or redo as the Edit menu does it (the view stays); the entry's name, or null.</summary>
    private async Task<string> RemoteHistory(bool redo)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        builder.Checkpoint();
        if ((redo ? builder.Files.RedoName : builder.Files.UndoName) == null)
            return null;
        var view = TakeView(builder);
        string name = redo ? builder.Redo() : builder.Undo();
        await RestoreView(builder, view);
        return name;
    }

    /// <inheritdoc />
    string IRemoteControl.Save()
    {
        if (CurrentBuilder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        CurrentBuilder.Checkpoint();
        return SaveProject();
    }

    /// <inheritdoc />
    string IRemoteControl.ExportConsoleShow()
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            throw new InvalidOperationException("No show loaded");
        string target = builder.Files.NextShowFile;
        if (string.IsNullOrEmpty(target))
            throw new InvalidOperationException("The project has no next console show file to export to");
        if (System.IO.File.Exists(target))
            throw new InvalidOperationException($"The next console show '{target}' already exists (exports never overwrite)");
        string result = builder.SaveShowAs(target);
        UpdateProjectBox();
        return result;
    }

    /// <inheritdoc />
    IReadOnlyList<string> IRemoteControl.Views => _host?.Views.Select(v => v.Title).ToList() ?? new List<string>();

    /// <inheritdoc />
    string IRemoteControl.MainView => _host?.Main?.Title ?? "";

    /// <inheritdoc />
    async Task<byte[]> IRemoteControl.Capture(string view, int maxWidth)
    {
        Image image;
        if (string.IsNullOrEmpty(view))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            image = ToSrgb(GetViewport().GetTexture().GetImage(), GetViewport(), "the window");
        }
        else
        {
            var found = _host?.Views.FirstOrDefault(v => v.Title.Equals(view, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(
                            $"No view '{view}' (views: {string.Join(", ", _host?.Views.Select(v => v.Title) ?? Array.Empty<string>())})");
            if (found is View3D view3D)
            {
                // A small card or a hidden view renders tiny (or not at all): it is rendered at a picture's size for it.
                var size = view3D.Viewport3D.Size;
                if (!view3D.IsVisibleInTree() || size.X < SmallView.X || size.Y < SmallView.Y)
                    size = PictureSize;
                image = ToSrgb(await view3D.CaptureAt(size, CaptureFrames), view3D.Viewport3D, $"the view '{found.Title}'");
            }
            else
            {
                if (!found.IsVisibleInTree())
                    throw new ArgumentException($"The view '{found.Title}' is not shown: only 3D views are rendered for a picture");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                // The view's own viewport; its final transform maps canvas units to the texture's pixels (the UI
                // scale and the stretch offset of the window).
                var vp = found.GetViewport();
                var window = ToSrgb(vp.GetTexture().GetImage(), vp, $"the view '{found.Title}'");
                var global = vp.GetFinalTransform() * found.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, found.Size);
                var rect = new Rect2I((Vector2I)global.Position.Round(), (Vector2I)global.Size.Round())
                    .Intersection(new Rect2I(Vector2I.Zero, window.GetSize()));
                if (rect.Size.X <= 0 || rect.Size.Y <= 0)
                    throw new InvalidOperationException($"The view '{found.Title}' has no area on the screen to take a picture of");
                image = window.GetRegion(rect);
            }
        }
        if (maxWidth > 0 && image.GetWidth() > maxWidth)
            image.Resize(maxWidth, Math.Max(1, image.GetHeight() * maxWidth / image.GetWidth()), Image.Interpolation.Lanczos);
        return image.SavePngToBuffer();
    }

    /// <inheritdoc />
    void IRemoteControl.Report(string text) => _status.Text = text;

    /// <summary>
    /// A viewport's picture as sRGB RGBA8: an HDR 2D viewport's texture is linear (half floats), so it is converted
    /// and brought to sRGB. Throws when the picture is empty (<paramref name="what"/> names it in the error).
    /// </summary>
    private static Image ToSrgb(Image image, Viewport viewport, string what)
    {
        if (image == null || image.IsEmpty() || image.GetWidth() <= 0 || image.GetHeight() <= 0)
            throw new InvalidOperationException($"No picture of {what}: the rendered image is empty");
        if (viewport.UseHdr2D)
        {
            image.Convert(Image.Format.Rgba8);
            image.LinearToSrgb();
        }
        return image;
    }

    /// <summary>The inspector shows the Look when nothing is selected: it is built again for the new values.</summary>
    private void RebuildParamsIfIdle()
    {
        if (_selected == null && _selectedActor == null)
            CallDeferred(MethodName.BuildParams);
    }
}
