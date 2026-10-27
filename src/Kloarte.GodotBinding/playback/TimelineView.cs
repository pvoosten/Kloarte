using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Interactive timeline of the cue stack and the actors together. Cue lane: each cue from its GO, with its In / Out fade
/// ramps (read only: set in MagicQ) and its hold until the next GO. One lane per actor: per movement step the wait,
/// the walk and the turn, with markers for appearing, disappearing and changing pose.
/// Mouse: drag on the ruler = move the playhead (the stage follows); drag a step's left edge = its wait (Delay), its
/// right edge = its walking time (MoveTime); drag the end of a cue's hold = the hold; double click in an actor lane =
/// add a movement step there; click a step = edit it in the inspector. Lane header (TimelineView.Lanes.cs): + Actor, and
/// on a name select / rename / the actor menu. Wheel = scroll, Ctrl+wheel / +/- = zoom; keys (key map): play / pause,
/// delete the selected step. Toolbar: key frames as PNG, movie of the show, dock under the 3D view (<see cref="ViewHost"/>).
/// </summary>
public partial class TimelineView : ViewBase
{
    private const float Gutter = 150f, RulerHeight = 30f, CueLane = 58f, MinLane = 24f, MaxLane = 40f, Grab = 6f;

    /// <summary>The player whose timeline is shown and edited.</summary>
    public CuePlayer Player { get; set; }

    /// <summary>Raised when the user asks for the key frame export / movie (the panel opens the dialogs).</summary>
    public event Action ExportKeyFramesRequested, RecordMovieRequested;

    /// <summary>Raised by the toolbar's Dock / Undock button: true = dock under the 3D view (the view host does it).</summary>
    public event Action<bool> DockRequested;

    /// <summary>The toolbar's Dock / Undock button.</summary>
    private Button _dock;

    private double _scroll = -1;     // time at the left edge of the lanes
    private float _pps = 20f;        // pixels per second
    private enum Drag { None, Playhead, Delay, MoveTime, Hold }
    private Drag _drag;
    private TimelineStep _dragStep;
    private TimelineSegment _dragSegment;
    private HBoxContainer _toolbar;
    private Button _play;

    /// <inheritdoc />
    public override string Title => "Timeline";

    /// <inheritdoc />
    public override string Context => "timeline";

    /// <inheritdoc />
    public override string ShowAction => "view_timeline";

    /// <summary>Builds the toolbar.</summary>
    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        _toolbar = new HBoxContainer { Name = "TimelineToolbar" };
        Toolbar = _toolbar;   // in the host's tool strip (UI review 29)
        _play = new Button { Text = "Play", TooltipText = "Play from the playhead through the holds (as in the movie)" };
        _play.Pressed += () => Player?.SetPlaying(!Player.Playing);
        var fit = new Button { Text = "Fit" };
        fit.Pressed += Fit;
        var zoomIn = new Button { Text = "+" };
        zoomIn.Pressed += () => Zoom(1.25f, Gutter);
        var zoomOut = new Button { Text = "-" };
        zoomOut.Pressed += () => Zoom(0.8f, Gutter);
        var keys = new Button { Text = "Stills...", TooltipText = "Export a PNG of the stage at every GO, fade end and actor step (also per actor)" };
        keys.Pressed += () => ExportKeyFramesRequested?.Invoke();
        var movie = new Button { Text = "Movie...", TooltipText = "Export the show as a movie (a cue range, a viewpoint, per actor)" };
        movie.Pressed += () => RecordMovieRequested?.Invoke();
        _dock = new Button { Text = "Dock" };
        KeyMap.Tooltip(_dock, "Dock the timeline under the 3D, audience or actor view / undock it", "timeline_dock_toggle");
        _dock.Pressed += () => DockRequested?.Invoke(!IsDocked);
        foreach (var c in new Control[] { _play, fit, zoomOut, zoomIn, keys, movie, new VSeparator(), _dock })
            _toolbar.AddChild(c);
        BuildLaneHeader();   // TimelineView.Lanes.cs
        Selection.Changed += QueueRedraw;
        TreeExiting += () => Selection.Changed -= QueueRedraw;
    }

    /// <inheritdoc />
    protected override void OnRoleChanged()
    {
        if (_addActor != null)
            _addActor.Visible = !Compact;
        if (_dock != null)
            _dock.Text = IsDocked ? "Undock" : "Dock";
        QueueRedraw();
    }

    private PlaybackTimeline Timeline => Player?.Timeline;

    private float X(double t) => Gutter + (float)((t - _scroll) * _pps);

    /// <summary>View x of a time (for tests and overlays).</summary>
    public float TimeToX(double t) => X(t);

    /// <summary>View y of the middle of an actor's lane (index in the blocking's actor list).</summary>
    public float LaneCentre(int actorIndex)
    {
        int count = ActorIds.Count;
        return LaneTop(actorIndex, count) + LaneHeight(count) / 2f;
    }

    private double T(float x) => _scroll + (x - Gutter) / _pps;

    private List<string> ActorIds => Builder?.Model?.Blocking.Actors.Select(a => a.Id).ToList() ?? new();

    private float LaneHeight(int actors) =>
        Mathf.Clamp((Size.Y - RulerHeight - CueLane - 4) / Mathf.Max(1, actors), MinLane, MaxLane);

    private float LaneTop(int index, int actors) => RulerHeight + CueLane + index * LaneHeight(actors);

    /// <summary>Shows the time range [start, end] across the lanes.</summary>
    public void ZoomTo(double start, double end)
    {
        _pps = Mathf.Clamp((Size.X - Gutter - 10) / (float)Mathf.Max(end - start, 0.1), 0.5f, 400f);
        _scroll = start;
        QueueRedraw();
    }

    /// <summary>Shows the whole stack.</summary>
    public void Fit()
    {
        var tl = Timeline;
        if (tl == null || tl.Length <= 0 || Size.X < Gutter + 50)
            return;
        _scroll = -1;
        _pps = (Size.X - Gutter - 10) / (float)(tl.Length + 2);
        QueueRedraw();
    }

    private void Zoom(float factor, float atX)
    {
        double t = T(atX);
        _pps = Mathf.Clamp(_pps * factor, 0.5f, 400f);
        _scroll = t - (atX - Gutter) / _pps;
        QueueRedraw();
    }

    /// <summary>Follows the playhead while playing; redraws.</summary>
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || Player == null)
            return;
        if (_play != null)
            _play.Text = Player.Playing ? "Pause" : "Play";
        if ((Player.Playing || Player.Running) && !Compact)
        {
            float x = X(Player.Time);
            if (x > Size.X - 40 || x < Gutter)
                _scroll = Player.Time - 2;
        }
        if (Compact && Timeline != null && _pps * Timeline.Length < Size.X / 2f)
            Fit();
        QueueRedraw();
    }

    /// <summary>Ruler, cue lane, actor lanes, playhead.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.11f, 0.11f, 0.12f));
        var tl = Timeline;
        var font = ThemeDB.FallbackFont;
        if (tl == null)
        {
            DrawString(font, new Vector2(10, 40), "No cue stack (choose one in the Playback tab)", HorizontalAlignment.Left, -1, 14, Colors.Gray);
            return;
        }
        var actors = ActorIds;
        float lane = LaneHeight(actors.Count);
        int fs = Compact ? 9 : 12;

        // Ruler with ticks.
        DrawRect(new Rect2(0, 0, Size.X, RulerHeight), new Color(0.18f, 0.18f, 0.2f));
        double tick = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 }.FirstOrDefault(s => s * _pps >= 60, 600);
        for (double t = Math.Floor(T(Gutter) / tick) * tick; X(t) < Size.X; t += tick)
        {
            if (X(t) < Gutter)
                continue;
            DrawLine(new Vector2(X(t), RulerHeight - 8), new Vector2(X(t), Size.Y), new Color(1, 1, 1, 0.06f));
            DrawString(font, new Vector2(X(t) + 2, RulerHeight - 10), Clock(t), HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.7f));
        }

        // Cue lane.
        float top = RulerHeight + 4, bottom = RulerHeight + CueLane - 4;
        foreach (var s in tl.Segments)
        {
            float x0 = X(s.Start), x1 = X(s.ActiveEnd), x2 = X(s.End);
            if (x2 < Gutter || x0 > Size.X)
                continue;
            bool current = Player.Step == s.Index;
            DrawRect(new Rect2(x0, top, Mathf.Max(1, x1 - x0), bottom - top), current ? new Color(0.35f, 0.3f, 0.2f) : new Color(0.25f, 0.25f, 0.27f));
            DrawRect(new Rect2(x1, top, Mathf.Max(0, x2 - x1), bottom - top), new Color(0.2f, 0.2f, 0.22f));
            DrawLine(new Vector2(x2, top), new Vector2(x2, bottom), new Color(0.7f, 0.7f, 0.7f), 2f);   // hold handle
            if (s.FadeIn > 0)
                DrawLine(new Vector2(x0, bottom), new Vector2(X(s.Start + s.FadeIn), top + 18), new Color(0.4f, 0.9f, 0.4f), 2f);
            if (s.FadeOut > 0)
                DrawLine(new Vector2(x0, top + 18), new Vector2(X(s.Start + s.FadeOut), bottom), new Color(0.95f, 0.45f, 0.35f), 2f);
            DrawLine(new Vector2(x0, RulerHeight), new Vector2(x0, Size.Y), new Color(1f, 0.6f, 0.2f, 0.6f), 1.5f);   // GO
            string label = $"{s.CueNumber} {s.Text}";
            DrawString(font, new Vector2(x0 + 3, top + 13), label, HorizontalAlignment.Left, Mathf.Max(10, x2 - x0 - 4), fs, new Color(1f, 0.85f, 0.6f));
            if (!Compact && x2 - x1 > 40)
                DrawString(font, new Vector2(x1 + 3, bottom - 4), $"hold {s.Hold:0.#}s", HorizontalAlignment.Left, x2 - x1 - 4, 10, new Color(1, 1, 1, 0.5f));
        }

        // Actor lanes.
        for (int a = 0; a < actors.Count; a++)
        {
            float y = LaneTop(a, actors.Count);
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), new Color(1, 1, 1, 0.08f));
            foreach (var st in tl.Steps.Where(s => s.ActorId == actors[a]))
                DrawStep(st, y, lane, font, fs);
        }

        // Gutter with the lane names (over the lanes).
        DrawRect(new Rect2(0, RulerHeight, Gutter, Size.Y - RulerHeight), new Color(0.15f, 0.15f, 0.16f));
        DrawString(font, new Vector2(6, RulerHeight + 22), "Cues", HorizontalAlignment.Left, Gutter - 8, fs + 1, new Color(1f, 0.85f, 0.6f));
        if (actors.Count == 0 && !Compact)
            DrawString(font, new Vector2(6, RulerHeight + CueLane + 20), "No actors yet: + Actor", HorizontalAlignment.Left, -1, fs, Colors.Gray);
        for (int a = 0; a < actors.Count; a++)
        {
            var def = Builder.Model.Blocking.Find(actors[a]);
            bool sel = Selection.Actor == actors[a];
            DrawString(font, new Vector2(6, LaneTop(a, actors.Count) + lane * 0.65f), def?.Name ?? actors[a], HorizontalAlignment.Left,
                Gutter - 8, fs, sel ? new Color(1f, 0.6f, 0.2f) : new Color(0.85f, 0.85f, 0.85f));
        }

        // Playhead.
        float px = X(Player.Time);
        if (px >= Gutter)
        {
            DrawLine(new Vector2(px, 0), new Vector2(px, Size.Y), new Color(1f, 0.25f, 0.2f), 2f);
            DrawString(font, new Vector2(px + 3, 12), Clock(Player.Time), HorizontalAlignment.Left, -1, 11, new Color(1f, 0.4f, 0.35f));
        }
    }

    private void DrawStep(TimelineStep st, float y, float lane, Font font, int fs)
    {
        float y0 = y + 4, h = lane - 8;
        bool selected = Selection.Actor == st.ActorId && Selection.ActorStep == st.StepIndex && Player.CueId == Timeline.Segments[st.Segment].CueId;
        // Wait (delay): thin line.
        if (st.Start > st.WaitStart)
            DrawLine(new Vector2(X(st.WaitStart), y0 + h / 2), new Vector2(X(st.Start), y0 + h / 2), new Color(1, 1, 1, 0.35f), 1f, true);
        float xs = X(st.Start), xw = X(st.Start + st.Walk), xt = X(st.Start + st.Walk + st.Turn), xe = X(st.End);
        if (st.Walk > 0)
            DrawRect(new Rect2(xs, y0, Mathf.Max(2, xw - xs), h), new Color(0.3f, 0.55f, 0.95f));
        if (st.Turn > 0)
            DrawRect(new Rect2(xw, y0 + h * 0.25f, Mathf.Max(2, xt - xw), h * 0.5f), new Color(0.4f, 0.8f, 0.85f));
        if (xe > xt + 1)
            DrawRect(new Rect2(xt, y0 + h * 0.4f, xe - xt, h * 0.2f), new Color(0.5f, 0.5f, 0.5f));
        if (st.Walk <= 0 && st.Turn <= 0)
            DrawRect(new Rect2(xs - 2, y0, 4, h), new Color(0.6f, 0.75f, 1f));
        string side = ActorEntrance.SideText(ActorEntrance.SideOf(st.To.Position));
        string mark = st.Appears ? "OP " + side : st.Disappears ? "AF " + side : st.To.Pose != st.From.Pose ? st.To.Pose[..1].ToUpperInvariant() : "";
        if (mark != "" && !Compact)
            DrawString(font, new Vector2(xe + 3, y0 + h * 0.75f), mark, HorizontalAlignment.Left, -1, fs - 1, new Color(1, 1, 0.6f));
        if (selected)
            DrawRect(new Rect2(xs - 2, y0 - 2, Mathf.Max(6, xe - xs) + 4, h + 4), new Color(1f, 0.5f, 0f), false, 2f);
    }

    private static string Clock(double t)
    {
        if (t < 0)
            return "-" + Clock(-t);
        int m = (int)(t / 60);
        double s = t - m * 60;
        return m > 0 ? $"{m}:{s.ToString("00.0", CultureInfo.InvariantCulture)}" : s.ToString("0.0", CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>The step under a view position (actor lanes), and which part: left edge, right edge of the walk, body.</summary>
    private (TimelineStep Step, Drag Part) StepAt(Vector2 p)
    {
        var tl = Timeline;
        var actors = ActorIds;
        float lane = LaneHeight(actors.Count);
        int a = (int)((p.Y - RulerHeight - CueLane) / lane);
        if (tl == null || p.Y < RulerHeight + CueLane || a < 0 || a >= actors.Count)
            return (null, Drag.None);
        foreach (var st in tl.Steps.Where(s => s.ActorId == actors[a]))
        {
            float xs = X(st.Start), xw = X(st.Start + st.Walk), xe = Mathf.Max(X(st.End), xs + 6);
            float dStart = Mathf.Abs(p.X - xs), dWalk = st.Walk > 0 ? Mathf.Abs(p.X - xw) : float.MaxValue;
            if (Mathf.Min(dStart, dWalk) <= Grab)
                return (st, dWalk < dStart ? Drag.MoveTime : Drag.Delay);   // the nearer edge
            if (p.X >= xs && p.X <= xe)
                return (st, Drag.None);
        }
        return (null, Drag.None);
    }

    /// <summary>Scrub, select, drag edges, add steps, zoom / scroll.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (!(IsMain || IsDocked) || Timeline == null || Player == null || Builder?.Model == null)
            return;
        if (LaneHeaderInput(e))   // TimelineView.Lanes.cs
        {
            AcceptEvent();
            QueueRedraw();
            return;
        }
        var tl = Timeline;
        var vp = GetTree().Root;
        if (KeyMap.Pressed(e, "timeline_zoom_in", vp) && e is InputEventMouseButton zi)
        {
            Zoom(1.2f, zi.Position.X);
            AcceptEvent();
            return;
        }
        if (KeyMap.Pressed(e, "timeline_zoom_out", vp) && e is InputEventMouseButton zo)
        {
            Zoom(1 / 1.2f, zo.Position.X);
            AcceptEvent();
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                _scroll += (wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1) * 60.0 / _pps;
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right:
                ShowMenu(right.Position);   // TimelineView.Menu.cs
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } dbl:
                AddStepAt(dbl.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                GrabFocus();
                if (down.Position.Y < RulerHeight && down.Position.X > Gutter)
                {
                    _drag = Drag.Playhead;
                    Player.Seek(Snap(T(down.Position.X)));
                }
                else if (down.Position.Y < RulerHeight + CueLane && down.Position.X > Gutter
                         && tl.Segments.FirstOrDefault(s => Mathf.Abs(X(s.End) - down.Position.X) <= Grab) is { } seg)
                {
                    _drag = Drag.Hold;
                    _dragSegment = seg;
                }
                else
                {
                    var (step, part) = StepAt(down.Position);
                    if (step != null)
                    {
                        SelectStep(step);
                        _drag = part;
                        _dragStep = step;
                    }
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion move when _drag != Drag.None && (move.ButtonMask & MouseButtonMask.Left) != 0:
                double t = Snap(T(move.Position.X));
                switch (_drag)
                {
                    case Drag.Playhead:
                        Player.Seek(t);
                        break;
                    case Drag.Hold:
                        Builder.Model.Blocking.Holds[_dragSegment.CueId] = (float)Math.Max(0, t - _dragSegment.ActiveEnd);
                        Player.Invalidate();
                        _dragSegment = Player.Timeline.Segments[_dragSegment.Index];
                        break;
                    case Drag.Delay:
                        EditStep(_dragStep, s => s.Delay = (float)Math.Max(0, t - _dragStep.WaitStart));
                        break;
                    case Drag.MoveTime:
                        EditStep(_dragStep, s => s.MoveTime = (float)Math.Max(0.1, t - _dragStep.Start));
                        break;
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _drag = Drag.None;
                _dragStep = null;
                _dragSegment = null;
                AcceptEvent();
                break;
        }
        QueueRedraw();
    }

    /// <summary>Snaps a time to 0.1 s and to GO times within a few pixels.</summary>
    private double Snap(double t)
    {
        var go = Timeline.Segments.Select(s => s.Start).Where(s => Math.Abs(X(s) - X(t)) <= Grab).Cast<double?>().FirstOrDefault();
        return go ?? Math.Round(t * 10) / 10;
    }

    private void SelectStep(TimelineStep step)
    {
        var seg = Timeline.Segments[step.Segment];
        if (Player.CueId != seg.CueId)
            Player.Jump(seg.Index);
        Selection.SelectActor(step.ActorId, step.StepIndex);
    }

    /// <summary>Changes a movement step in the blocking and lays the timeline out again.</summary>
    private void EditStep(TimelineStep step, Action<ActorState> change)
    {
        int cue = Timeline.Segments[step.Segment].CueId;
        var steps = Builder.Model.Blocking.Steps(cue, step.ActorId);
        if (step.StepIndex >= steps.Count)
            return;
        change(steps[step.StepIndex]);
        Player.Invalidate();
        // Keep dragging the same step in the new layout.
        _dragStep = Player.Timeline.Steps.FirstOrDefault(s => s.ActorId == step.ActorId && s.Segment == step.Segment && s.StepIndex == step.StepIndex) ?? step;
    }

    /// <summary>Adds a movement step for the actor of the lane at the clicked time (its wait = from the previous step).</summary>
    private void AddStepAt(Vector2 p)
    {
        var actors = ActorIds;
        int a = (int)((p.Y - RulerHeight - CueLane) / LaneHeight(actors.Count));
        if (p.Y < RulerHeight + CueLane || a < 0 || a >= actors.Count)
            return;
        double t = Snap(T(p.X));
        int segIndex = Timeline.SegmentAt(t);
        if (segIndex < 0)
            return;
        var seg = Timeline.Segments[segIndex];
        var last = Timeline.Steps.Where(s => s.ActorId == actors[a] && s.Segment == segIndex).OrderBy(s => s.StepIndex).LastOrDefault();
        double from = last?.End ?? seg.Start;
        var list = Builder.Model.Blocking.Steps(seg.CueId, actors[a], create: true);
        list.Add(new ActorState { Delay = (float)Math.Max(0, t - from) });
        Player.Invalidate();
        if (Player.CueId != seg.CueId)
            Player.Jump(segIndex);
        Selection.SelectActor(actors[a], list.Count - 1);
    }

    /// <summary>Timeline keys: play / pause, delete the selected step.</summary>
    public override bool HandleKey(InputEvent e)
    {
        var vp = GetTree().Root;
        if (KeyMap.Pressed(e, "timeline_play", vp) && Player != null)
            Player.SetPlaying(!Player.Playing);
        else if (KeyMap.Pressed(e, "timeline_delete", vp) && Selection.Actor != null && Player?.CueId >= 0)
        {
            Builder.Model.Blocking.RemoveStep(Player.CueId, Selection.Actor, Selection.ActorStep);
            Player.Invalidate();
            Selection.SelectActor(Selection.Actor, Math.Max(0, Selection.ActorStep - 1));
        }
        else if (KeyMap.Pressed(e, "timeline_zoom_in", vp))
            Zoom(1.25f, Size.X / 2);
        else if (KeyMap.Pressed(e, "timeline_zoom_out", vp))
            Zoom(0.8f, Size.X / 2);
        else
            return false;
        QueueRedraw();
        return true;
    }

    /// <inheritdoc />
    protected override void OnShowChanged()
    {
        _scroll = -1;
        CallDeferred(MethodName.Fit);
    }
}
