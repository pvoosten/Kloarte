using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Plays a MagicQ cue stack with the blocking, on the <see cref="PlaybackTimeline"/>: the player only moves the
/// playhead (<see cref="Time"/>) and applies the timeline's state (fixture levels incl. fades, moving-head attributes,
/// actors), so GO, scrubbing in the timeline view and the movie export always agree.
/// GO: the playhead jumps to the next cue's GO and runs until its fades and actor steps are done (a GO while a cue is
/// still running completes it first). Back / Jump snap to the end state of a cue. Auto: after a cue the player waits its
/// hold (timeline) and GOes again. Play: runs the playhead continuously (through the holds), as in the movie.
/// Smoke: while a cue of the stack sets smoke (<see cref="Kloarte.Model.CueSmoke"/>) the haze of the room follows the
/// timeline (<see cref="HazeControl"/>): GO, Play and scrubbing ease it as it builds; Back / Jump / Reset show the
/// settled amount of the cue; the project's haze (the Look) is the start and what comes back when no cue sets smoke.
/// </summary>
public partial class CuePlayer : Node
{
    /// <summary>Walking speed (m/s) of actors without a move time.</summary>
    public const float WalkSpeed = PlaybackTimeline.WalkSpeed;

    /// <summary>The show to play.</summary>
    public ShowBuilder Builder { get; set; }

    /// <summary>The cue stack.</summary>
    public ICueStack Stack { get; private set; }

    /// <summary>Last cue taken (index in cue-number order); -1 = before the first cue.</summary>
    public int Step { get; private set; } = -1;

    /// <summary>GO again automatically after each cue's hold.</summary>
    public bool Auto { get; set; }

    /// <summary>Hold (s) of cues that have none in the blocking.</summary>
    public float Hold
    {
        get => _hold;
        set
        {
            _hold = value;
            Invalidate();
        }
    }

    /// <summary>Raised when the cue or the timeline changed.</summary>
    public event Action Changed;

    /// <summary>
    /// Raised when the running playhead (GO, Auto, Play) passes an actor going on or off stage; not when scrubbing,
    /// jumping or going back.
    /// </summary>
    public event Action<ActorEntrance> EntrancePassed;

    /// <summary>Playhead in seconds from the first GO (negative = before it).</summary>
    public double Time { get; private set; } = -1;

    /// <summary>Playing continuously (timeline play) rather than cue by cue.</summary>
    public bool Playing { get; private set; }

    /// <summary>True while the current cue's fades or actor steps are running.</summary>
    public bool Running => _running;

    /// <summary>MagicQ cue id of the current cue, or -1.</summary>
    public int CueId => Stack != null && Step >= 0 && Step < Stack.Steps.Count ? Stack.Steps[Step].CueId : -1;

    /// <summary>
    /// The timeline of the stack (rebuilt after blocking changes, and when the project's haze, the haze before the first
    /// smoke, changed).
    /// </summary>
    public PlaybackTimeline Timeline
    {
        get
        {
            if (_timeline != null && Math.Abs(_timeline.BaseHaze - BaseHaze) > 1e-6f)
                _timeline = null;
            return _timeline ??= Builder?.Model == null ? null
                : new PlaybackTimeline(Builder.Model.Show, Stack, Builder.Model.Blocking, _hold, BaseHaze);
        }
    }

    /// <summary>The project's haze density (the Look): the haze before the first cue that sets smoke.</summary>
    private float BaseHaze => Builder?.Files?.Look?.Haze ?? ProjectLook.DefaultHaze;

    /// <summary>The haze is driven by the cues (a cue sets smoke), so it has to go back to the Look's when none does.</summary>
    private bool _hazeDriven;

    /// <summary>The state was jumped to (Back, Jump, Reset): the haze shows the settled amount, not the ramp at that time.</summary>
    private bool _settled;

    private PlaybackTimeline _timeline;

    /// <summary>The timeline's entrances (laid out with it).</summary>
    private List<ActorEntrance> _entrances;

    /// <summary>Entrances after this time are still to be announced (the running playhead's last time).</summary>
    private double _announcedUpTo = double.MaxValue;
    private float _hold = 3f;
    private bool _running;
    private double _waited;

    /// <summary>Chooses a stack without changing the stage (the first GO fades from what is on stage now).</summary>
    public void Select(ICueStack stack)
    {
        Stack = stack;
        Step = -1;
        Time = -1;
        Auto = false;
        Playing = false;
        _running = false;
        _timeline = null;
        _entrances = null;
        Changed?.Invoke();
    }

    /// <summary>Chooses a stack and shows its state before the first cue.</summary>
    public void SetStack(ICueStack stack)
    {
        Stack = stack;
        _timeline = null;
        _entrances = null;
        Jump(-1);
    }

    /// <summary>The blocking or holds changed: the timeline is laid out again (the playhead stays).</summary>
    public void Invalidate()
    {
        _timeline = null;
        _entrances = null;
        Changed?.Invoke();
    }

    /// <summary>Snaps to the end state of cue `step` (no fades); -1 = before the first cue.</summary>
    public void Jump(int step)
    {
        if (Builder?.Model == null || Stack == null || Timeline == null)
            return;
        Step = Math.Clamp(step, -1, Stack.Steps.Count - 1);
        Time = Step < 0 ? -1 : Timeline.Segments[Step].ActiveEnd;
        _settled = true;
        _announcedUpTo = Time;
        _running = false;
        Playing = false;
        _waited = 0;
        Apply();
        Changed?.Invoke();
    }

    /// <summary>Previous cue (snap).</summary>
    public void Back() => Jump(Step - 1);

    /// <summary>Next cue with its fade times and actor movements.</summary>
    public void Go()
    {
        if (Builder?.Model == null || Stack == null || Timeline == null || Step + 1 >= Stack.Steps.Count)
        {
            Auto = false;
            Changed?.Invoke();
            return;
        }
        Step++;
        Time = Timeline.Segments[Step].Start;   // a running cue is completed: its end state is the next cue's start
        _announcedUpTo = Time - 1e-6;   // an actor appearing right at the GO is announced too
        _settled = false;
        _running = true;
        Playing = false;
        _waited = 0;
        Apply();
        Changed?.Invoke();
    }

    /// <summary>Moves the playhead (scrubbing): shows the state at that time.</summary>
    public void Seek(double time)
    {
        if (Timeline == null)
            return;
        Time = time;
        _settled = false;
        _announcedUpTo = time;
        Step = Timeline.SegmentAt(time);
        _running = false;
        Apply();
        Changed?.Invoke();
    }

    /// <summary>Plays continuously from the playhead (through the holds) / pauses.</summary>
    public void SetPlaying(bool playing)
    {
        Playing = playing && Timeline != null;
        _settled = false;
        _announcedUpTo = Time;
        _running = false;
        Changed?.Invoke();
    }

    /// <summary>Advances the playhead.</summary>
    public override void _Process(double delta)
    {
        if (Timeline == null)
            return;
        if (Playing)
        {
            Time += delta;
            int seg = Timeline.SegmentAt(Time);
            bool stepChanged = seg != Step;
            Step = seg;
            Apply();
            Announce();
            if (Time >= Timeline.Length)
                Playing = false;
            if (stepChanged || !Playing)
                Changed?.Invoke();
            return;
        }
        if (_running)
        {
            var seg = Timeline.Segments[Step];
            Time = Math.Min(Time + delta, seg.ActiveEnd);
            Apply();
            Announce();
            if (Time >= seg.ActiveEnd)
            {
                _running = false;
                Changed?.Invoke();
            }
            return;
        }
        if (Auto && Stack != null && Step >= 0)
        {
            _waited += delta;
            if (_waited >= Timeline.Segments[Step].Hold)
                Go();
        }
        else if (Auto && Step < 0)
            Go();
    }

    /// <summary>Raises <see cref="EntrancePassed"/> for the entrances the playhead passed since the last frame.</summary>
    private void Announce()
    {
        _entrances ??= Timeline.Entrances();
        foreach (var e in _entrances)
            if (e.Time > _announcedUpTo && e.Time <= Time)
                EntrancePassed?.Invoke(e);
        _announcedUpTo = Time;
    }

    /// <summary>Shows the timeline's state at the playhead again (after an edit that doesn't move the playhead).</summary>
    public void Refresh() => Apply();

    /// <summary>The haze of the room (<see cref="HazeControl"/>) from the cues' smoke.</summary>
    private void ApplyHaze()
    {
        if (!IsInsideTree() || Timeline is not { } timeline)
            return;
        if (timeline.HasSmoke)
        {
            _hazeDriven = true;
            float density = _settled ? timeline.HazeAfter(Step) : timeline.HazeAt(Time);
            if (Math.Abs(density - HazeControl.Current) > 1e-6f)
                HazeControl.Set(GetTree(), density);
        }
        else if (_hazeDriven)
        {
            _hazeDriven = false;
            HazeControl.Set(GetTree(), timeline.BaseHaze);
        }
    }

    /// <summary>Puts the timeline's state at the playhead on stage.</summary>
    private void Apply()
    {
        if (Builder?.Model == null || Timeline == null)
            return;
        var dmx = Timeline.DmxAt(Time);
        foreach (var (fixture, level) in Builder.LevelsFrom(dmx))
            if (IsInstanceValid(fixture))
                fixture.Intensity = level;
        Builder.ApplyMovingHeads(dmx);
        foreach (var (id, actor) in Builder.Actors)
            if (IsInstanceValid(actor))
                actor.Apply(Timeline.ActorAt(id, Time));
        ApplyHaze();
    }
}
