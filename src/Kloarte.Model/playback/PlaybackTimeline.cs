using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// The whole show as a function of time: a MagicQ cue stack with its fade times plus the blocking (actors), laid out as
/// cue segments (GO, fades / actor steps, hold, next GO). <see cref="DmxAt"/> and <see cref="ActorAt"/> give the state
/// at any time, so playback, scrubbing in the timeline view and the movie export agree. Time 0 is the first GO;
/// before it everything is in its initial state (lights off).
/// Lights: MagicQ tracks, so the DMX state after a cue = all cues up to it; during a cue every channel fades linearly
/// from the previous state, rising with the cue's In time and falling with its Out time (palette references resolved).
/// Actors: each actor's movement steps of a cue run one after the other: wait Delay, walk (MoveTime, else
/// <see cref="WalkSpeed"/>) facing the way it goes, turn (<see cref="TurnTime"/>) to the step's facing and take its pose;
/// appearing happens at the start of a step (at its place), disappearing at its end, the last step waiting for the
/// cue's Out fade.
/// Smoke: a cue that sets smoke (<see cref="Blocking.Smoke"/>) starts a ramp at its GO from the haze the room has at
/// that moment (a ramp that hasn't finished goes on from where it got to) to its amount, taking its time; later cues
/// without smoke leave it (<see cref="HazeAt"/>). No Godot dependencies.
/// </summary>
public sealed class PlaybackTimeline
{
    /// <summary>Walking speed in m/s when a step has no move time.</summary>
    public const float WalkSpeed = 1.2f;

    /// <summary>Seconds to turn to the final facing after a walk (or a turn on the spot).</summary>
    private const float TurnTime = 0.6f;

    private readonly List<TimelineSegment> _segments = new();
    private readonly List<TimelineStep> _steps = new();
    private readonly List<Dictionary<int, int>> _tracked = new();   // DMX after each step
    private readonly Dictionary<string, List<ActorFrame>> _afterCue = new();   // actor state after each cue
    private readonly Dictionary<string, ActorFrame> _initial = new();
    private readonly List<SmokeRamp> _smoke = new();

    /// <summary>One cue's haze ramp: from its GO, from the haze then to the cue's amount, in `Time` seconds.</summary>
    private readonly record struct SmokeRamp(int Segment, double Start, float From, float To, float Time, double Percent);

    /// <summary>The cue segments in order.</summary>
    public IReadOnlyList<TimelineSegment> Segments => _segments;

    /// <summary>All actor movement steps.</summary>
    public IReadOnlyList<TimelineStep> Steps => _steps;

    /// <summary>The actors (ids) on the timeline.</summary>
    public IEnumerable<string> Actors => _initial.Keys;

    /// <summary>End of the last cue (incl. its hold).</summary>
    public double Length => _segments.Count == 0 ? 0 : _segments[^1].End;

    /// <summary>The haze density before the first cue that sets smoke (the project's haze).</summary>
    public float BaseHaze { get; }

    /// <summary>True when a cue sets smoke: the haze follows the cues (<see cref="HazeAt"/>), else it stays as it is.</summary>
    public bool HasSmoke => _smoke.Count > 0;

    /// <summary>
    /// Lays out a cue stack with its blocking. `defaultHold` = wait before the next GO when a cue has no hold;
    /// `baseHaze` = the haze density before the first cue that sets smoke.
    /// </summary>
    public PlaybackTimeline(IShowDocument show, ICueStack stack, Blocking blocking, float defaultHold, float baseHaze = ProjectLook.DefaultHaze)
    {
        _show = show;
        BaseHaze = baseHaze;
        var cues = show.Cues.ToDictionary(c => c.Id);
        var dmx = new Dictionary<int, int>();
        foreach (var actor in blocking.Actors)
        {
            _initial[actor.Id] = Frame(actor.Initial, new ActorFrame(true, ActorPose.Standing, Vector3.Zero, 0f));
            _afterCue[actor.Id] = new List<ActorFrame>();
        }
        double t = 0;
        var steps = stack?.Steps ?? new List<ICueStep>();
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            cues.TryGetValue(step.CueId, out var cue);
            if (cue != null)
                foreach (var (address, value) in cue.Channels)
                    dmx[address] = show.Resolve(address, value);
            _tracked.Add(new Dictionary<int, int>(dmx));
            float fadeIn = cue?.FadeIn ?? 0f, fadeOut = cue?.FadeOut ?? 0f;

            double actorsEnd = 0;
            foreach (var actor in blocking.Actors)
            {
                var before = i == 0 ? _initial[actor.Id] : _afterCue[actor.Id][i - 1];
                var state = before;
                double clock = t;
                var actorSteps = blocking.Steps(step.CueId, actor.Id);
                for (int k = 0; k < actorSteps.Count; k++)
                {
                    var s = Layout(actor.Id, i, k, clock, state, actorSteps[k], k == actorSteps.Count - 1 ? fadeOut : 0f);
                    _steps.Add(s);
                    state = s.To;
                    clock = s.End;
                }
                _afterCue[actor.Id].Add(state);
                actorsEnd = Math.Max(actorsEnd, clock - t);
            }
            if (blocking.Smoke.TryGetValue(step.CueId, out var smoke) && smoke != null)
                _smoke.Add(new SmokeRamp(i, t, HazeAt(t), ProjectLook.DensityOf(smoke.Amount), smoke.Seconds, smoke.Amount));
            var segment = new TimelineSegment
            {
                Index = i,
                CueId = step.CueId,
                CueNumber = step.CueNumberText,
                Text = step.Text.Length > 0 ? step.Text : cue?.Name ?? "",
                Start = t,
                FadeIn = fadeIn,
                FadeOut = fadeOut,
                ActorsEnd = actorsEnd,
                Hold = blocking.Holds.TryGetValue(step.CueId, out float hold) ? hold : defaultHold,
            };
            _segments.Add(segment);
            t = segment.End;
        }
    }

    private static ActorFrame Frame(ActorState s, ActorFrame fallback) => new(
        s.Visible ?? fallback.Visible,
        s.Pose ?? fallback.Pose,
        s.Position is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : fallback.Position,
        s.Facing ?? fallback.Facing);

    /// <summary>Timing of one movement step (the same rules as playing it).</summary>
    private static TimelineStep Layout(string actorId, int segment, int index, double clock, ActorFrame from, ActorState change, float extraHold)
    {
        var to = Frame(change, from);
        double start = clock + (change.Delay ?? 0f);
        double walk = 0, turn = 0, hold = 0;
        float walkFacing = from.Facing;
        if (from.Visible)
        {
            float distance = Vector3.Distance(from.Position, to.Position);
            walk = distance < 0.05f ? 0 : change.MoveTime is > 0f ? change.MoveTime.Value : distance / WalkSpeed;
            bool turns = Math.Abs(Wrap(to.Facing - from.Facing)) > 0.01f;
            turn = turns || walk > 0 ? TurnTime : 0;
            if (walk > 0)
            {
                var d = to.Position - from.Position;
                walkFacing = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
            }
            if (!to.Visible)
                hold = Math.Max(0, extraHold - walk - turn);
        }
        return new TimelineStep
        {
            ActorId = actorId,
            Segment = segment,
            StepIndex = index,
            WaitStart = clock,
            Start = start,
            Walk = walk,
            Turn = turn,
            End = start + walk + turn + hold,
            From = from,
            To = to,
            WalkFacing = walkFacing,
        };
    }

    /// <summary>Index of the cue segment running at time t (-1 before the first GO).</summary>
    public int SegmentAt(double t)
    {
        int i = -1;
        while (i + 1 < _segments.Count && _segments[i + 1].Start <= t)
            i++;
        return i;
    }

    /// <summary>The show (for its dimmer curves).</summary>
    private readonly IShowDocument _show;

    /// <summary>DMX values (address -&gt; 0..255) at time t, as MagicQ outputs them (dimmer curves applied).</summary>
    public Dictionary<int, int> DmxAt(double t) => _show.WithCurves(LevelsAt(t));

    /// <summary>Channel levels at time t before the dimmer curves (fades run on these).</summary>
    private Dictionary<int, int> LevelsAt(double t)
    {
        int i = SegmentAt(t);
        if (i < 0)
            return new Dictionary<int, int>();
        var seg = _segments[i];
        var from = i == 0 ? new Dictionary<int, int>() : _tracked[i - 1];
        var to = _tracked[i];
        double tau = t - seg.Start;
        var result = new Dictionary<int, int>(to.Count);
        foreach (var (address, target) in to)
        {
            int start = from.GetValueOrDefault(address);
            double time = target > start ? seg.FadeIn : seg.FadeOut;
            result[address] = time <= 0 || tau >= time ? target : (int)Math.Round(start + (target - start) * (tau / time));
        }
        return result;
    }

    /// <summary>
    /// The haze density at time t (before the first GO: the base haze): the ramp of the last cue that set smoke, eased
    /// from the haze when its GO came to its amount.
    /// </summary>
    public float HazeAt(double t)
    {
        SmokeRamp? current = null;
        foreach (var ramp in _smoke)
        {
            if (ramp.Start > t)
                break;
            current = ramp;
        }
        if (current is not { } r)
            return BaseHaze;
        if (r.Time <= 0f || t >= r.Start + r.Time)
            return r.To;
        float k = (float)((t - r.Start) / r.Time);
        return r.From + (r.To - r.From) * (k * k * (3 - 2 * k));
    }

    /// <summary>The haze density once the ramps up to cue segment `segment` have finished (-1 = before the first cue).</summary>
    public float HazeAfter(int segment)
    {
        float haze = BaseHaze;
        foreach (var ramp in _smoke.TakeWhile(r => r.Segment <= segment))
            haze = ramp.To;
        return haze;
    }

    /// <summary>The smoke % of a cue's amount reached by the end of its ramp, or null when the cue sets none.</summary>
    public double? SmokeOf(int segment) => _smoke.Where(r => r.Segment == segment).Select(r => (double?)r.Percent).FirstOrDefault();

    /// <summary>An actor's state at time t.</summary>
    public ActorFrame ActorAt(string actorId, double t)
    {
        if (!_initial.TryGetValue(actorId, out var initial))
            return default;
        int i = SegmentAt(t);
        if (i < 0)
            return initial;
        var state = i == 0 ? initial : _afterCue[actorId][i - 1];
        foreach (var s in _steps.Where(s => s.ActorId == actorId && s.Segment == i))
        {
            if (t < s.Start)
                return state;
            if (t >= s.End)
            {
                state = s.To;
                continue;
            }
            return During(s, t);
        }
        return state;
    }

    /// <summary>State part-way through a step.</summary>
    private static ActorFrame During(TimelineStep s, double t)
    {
        if (s.Appears)
            return s.To;
        double tau = t - s.Start;
        if (s.Walk > 0 && tau < s.Walk)
        {
            float k = (float)(tau / s.Walk);
            float smooth = k * k * (3 - 2 * k);
            float turnIn = (float)Math.Clamp(tau / TurnTime, 0, 1);
            return new ActorFrame(true, ActorPose.Standing, Vector3.Lerp(s.From.Position, s.To.Position, smooth),
                LerpAngle(s.From.Facing, s.WalkFacing, turnIn));
        }
        float startFacing = s.Walk > 0 ? s.WalkFacing : s.From.Facing;
        float kt = s.Turn > 0 ? (float)Math.Clamp((tau - s.Walk) / TurnTime, 0, 1) : 1f;
        // Disappearing happens at the end of the step: still visible until then.
        return new ActorFrame(true, s.To.Pose, s.To.Position, LerpAngle(startFacing, s.To.Facing, kt));
    }

    /// <summary>
    /// Key moments for exporting stills: every GO, the end of every fade, and the start and end of every actor step;
    /// with a description. Sorted, duplicates within 50 ms merged.
    /// </summary>
    public List<(double Time, string What)> KeyFrames()
    {
        var keys = new List<(double, string)>();
        foreach (var s in _segments)
        {
            keys.Add((s.Start, $"GO {s.CueNumber} {s.Text}".TrimEnd()));
            if (s.FadeIn > 0)
                keys.Add((s.Start + s.FadeIn, $"cue {s.CueNumber} fade in done"));
            if (s.FadeOut > 0 && Math.Abs(s.FadeOut - s.FadeIn) > 0.05)
                keys.Add((s.Start + s.FadeOut, $"cue {s.CueNumber} fade out done"));
        }
        foreach (var ramp in _smoke)
            if (ramp.Time > 0.05f && ramp.Start + ramp.Time <= Length)
                keys.Add((ramp.Start + ramp.Time, $"cue {_segments[ramp.Segment].CueNumber} smoke {ramp.Percent:0} % reached"));
        foreach (var s in _steps)
        {
            if (s.End - s.Start > 0.05)
                keys.Add((s.Start, $"{s.ActorId} step {s.StepIndex + 1} starts (cue {_segments[s.Segment].CueNumber})"));
            keys.Add((s.End, $"{s.ActorId} step {s.StepIndex + 1} done (cue {_segments[s.Segment].CueNumber})"));
        }
        var sorted = keys.OrderBy(k => k.Item1).ToList();
        var merged = new List<(double Time, string What)>();
        foreach (var k in sorted)
            if (merged.Count > 0 && k.Item1 - merged[^1].Time < 0.05)
                merged[^1] = (merged[^1].Time, merged[^1].What + "; " + k.Item2);
            else
                merged.Add(k);
        return merged;
    }

    /// <summary>
    /// Every actor going on stage (a step it appears in: at the step's start, where it appears) or off stage (a step
    /// it disappears in: at the step's end, where it was last), in time order; one actor's only, or all (null).
    /// </summary>
    public List<ActorEntrance> Entrances(string actorId = null) => _steps
        .Where(s => (actorId == null || s.ActorId == actorId) && (s.Appears || s.Disappears))
        .Select(s => s.Appears
            ? new ActorEntrance(s.ActorId, s.Start, true, ActorEntrance.SideOf(s.To.Position), s.Segment, _segments[s.Segment].CueNumber)
            : new ActorEntrance(s.ActorId, s.End, false, ActorEntrance.SideOf(s.To.Position), s.Segment, _segments[s.Segment].CueNumber))
        .OrderBy(e => e.Time)
        .ToList();

    private static float Wrap(float degrees) => ((degrees + 180f) % 360f + 360f) % 360f - 180f;

    private static float LerpAngle(float a, float b, float k) => a + Wrap(b - a) * k;
}
