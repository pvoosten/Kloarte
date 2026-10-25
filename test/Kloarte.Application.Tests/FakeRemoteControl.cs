using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kloarte.Application;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// The app as the remote tools see it, without the UI: a native show's session, a cue index, the look, and a fake
/// picture. Edits are recorded as undo steps as the UI records them (named, then a checkpoint).
/// </summary>
public sealed class FakeRemoteControl : IRemoteControl
{
    /// <summary>The session.</summary>
    private readonly ShowSession<DesignShow> _session;

    /// <summary>The app on a loaded session.</summary>
    public FakeRemoteControl(ShowSession<DesignShow> session) => _session = session;

    /// <summary>The messages shown on the status bar.</summary>
    public List<string> Reports { get; } = new();

    /// <summary>The bytes <see cref="Capture"/> returns (a PNG's signature).</summary>
    public static readonly byte[] Picture = { 0x89, 0x50, 0x4e, 0x47 };

    /// <inheritdoc />
    public IShowFiles Files => _session;

    /// <inheritdoc />
    public DesignShow Design => _session.Model?.Show;

    /// <inheritdoc />
    public int? CueId { get; private set; }

    /// <inheritdoc />
    public bool Running { get; private set; }

    /// <inheritdoc />
    public void GoToCue(int? cueId)
    {
        if (cueId is { } id && Design?.CueById(id) == null)
            throw new InvalidOperationException($"No cue with id {id} in the cue list");
        CueId = cueId;
        Running = false;
    }

    /// <inheritdoc />
    public void Go()
    {
        // the played cue list skips cues without an id, as the projected cue stack does
        var played = Design?.CueOrder.Where(c => c.Id != null).ToList() ?? new List<DesignCue>();
        int at = CueId is { } id ? played.FindIndex(c => c.Id == id) : -1;
        if (at + 1 < played.Count)
            CueId = played[at + 1].Id;
        Running = true;
    }

    /// <inheritdoc />
    public float Haze { get; set; } = ProjectLook.DefaultHaze;

    /// <inheritdoc />
    public float StrayLight { get; set; } = ProjectLook.DefaultStrayLight;

    /// <inheritdoc />
    public IReadOnlyList<int> SelectedLamps { get; private set; } = Array.Empty<int>();

    /// <inheritdoc />
    public int SelectLamps(IReadOnlyCollection<int> lampIds)
    {
        SelectedLamps = Design.Rig.Items.Where(i => i.IsLamp && lampIds.Contains(i.Id)).Select(i => i.Id).ToList();
        return SelectedLamps.Count;
    }

    /// <inheritdoc />
    public string EditShow(string name, Action<DesignShow> change)
    {
        var design = Design ?? throw new InvalidOperationException("No native show");
        _session.Checkpoint();
        _session.NameNextEdit(name);
        change(design);
        design.Changed();
        return _session.Checkpoint();
    }

    /// <inheritdoc />
    public Task<string> EditModel(string name, Action<IShowModel> change, bool rebuild)
    {
        _session.Checkpoint();
        _session.NameNextEdit(name);
        change(_session.Model);
        return Task.FromResult(_session.Checkpoint());
    }

    /// <summary>The stage points heads were aimed at, by head number (the last one).</summary>
    public Dictionary<int, System.Numerics.Vector3> AimedAt { get; } = new();

    /// <summary>The angles <see cref="AimAngles"/> gives: pan = 10 x the point's x, tilt = its z (no geometry here).</summary>
    public static (float Pan, float Tilt) AnglesFor(System.Numerics.Vector3 point) => (10f * point.X, point.Z);

    /// <inheritdoc />
    public (float Pan, float Tilt)? AimAngles(int head, System.Numerics.Vector3 stagePoint)
    {
        if (Design.LayoutOf(head)?["Pan"] == null)
            return null;
        AimedAt[head] = stagePoint;
        return AnglesFor(stagePoint);
    }

    /// <inheritdoc />
    public string FocusLamps(string name, IReadOnlyDictionary<int, System.Numerics.Vector3> points)
    {
        _session.Checkpoint();
        foreach (var (lamp, point) in points)
            _session.Model.Mechanics[lamp].FocusPoint = new[] { point.X, point.Y, point.Z };
        _session.NameNextEdit(name);
        return _session.Checkpoint();
    }

    /// <inheritdoc />
    public Task<string> Undo() => Task.FromResult(_session.Undo());

    /// <inheritdoc />
    public Task<string> Redo() => Task.FromResult(_session.Redo());

    /// <inheritdoc />
    public string Save() => _session.SaveProject();

    /// <inheritdoc />
    public string ExportConsoleShow() => _session.SaveShowAs(_session.NextShowFile);

    /// <inheritdoc />
    public IReadOnlyList<string> Views { get; } = new[] { "3D", "Audience", "Plan" };

    /// <inheritdoc />
    public string MainView => "3D";

    /// <summary>The view and width of the last picture.</summary>
    public (string View, int MaxWidth) Captured { get; private set; }

    /// <inheritdoc />
    public Task<byte[]> Capture(string view, int maxWidth)
    {
        if (!string.IsNullOrEmpty(view) && !Views.Contains(view))
            throw new ArgumentException($"No view '{view}'");
        Captured = (view, maxWidth);
        return Task.FromResult(Picture);
    }

    /// <inheritdoc />
    public void Report(string text) => Reports.Add(text);
}
