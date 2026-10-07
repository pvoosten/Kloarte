using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// Plays a MagicQ cue stack with timing, together with the blocking (actors).
// GO: every fixture fades from its current level to the tracked level of the next cue, rising with the cue's In
// time, falling with its Out time. Each actor runs its movement steps of that cue one after the other: wait the
// step's Delay, then walk to its position (MoveTime, else walking speed) facing the way it goes, turn to its facing
// and take its pose. Appearing happens at the start of the step (at its position); disappearing at the end of the
// step (the last step also waits for the cue's Out fade). Auto: after a cue has finished, wait Hold s and GO again.
// Back / Jump snap without fades. A GO while a cue is running completes it first.
public partial class CuePlayer : Node
{
    public const float WalkSpeed = 1.2f;    // m/s
    private const float TurnTime = 0.6f;    // s, turning to the final facing after a walk

    public ShowBuilder Builder { get; set; }
    public MagicQShow.CueStack Stack { get; private set; }
    public int Step { get; private set; } = -1;    // last cue taken; -1 = before the first cue
    public bool Auto { get; set; }
    public float Hold { get; set; } = 3f;

    public event Action Changed;

    public bool Running => _fades.Count > 0 || _tracks.Count > 0;
    public int CueId => Stack != null && Step >= 0 ? Stack.Steps[Step].CueId : -1;

    private sealed class Fade
    {
        public ProfileFixture Fixture;
        public float From, To, Time, T;
    }

    // One movement step of an actor, played after the previous one.
    private sealed class Segment
    {
        public ActorState Target;     // state after this step
        public float MoveTime, Delay, ExtraHold;
    }

    private sealed class Move
    {
        public Vector3 From, To;
        public float Walk, Turn, Hold, T, FromFacing, ToFacing, WalkFacing;
        public string EndPose;
        public bool EndVisible;
        public float Duration => Walk + Turn + Hold;
    }

    private sealed class Track
    {
        public Actor Actor;
        public ActorState Final;
        public readonly Queue<Segment> Pending = new();
        public float Wait;
        public Move Active;
    }

    private readonly List<Fade> _fades = new();
    private readonly List<Track> _tracks = new();
    private double _hold;

    // Chooses a stack without changing the stage (the first GO fades from what is on stage now).
    public void Select(MagicQShow.CueStack stack)
    {
        Stack = stack;
        Step = -1;
        Auto = false;
        _fades.Clear();
        _tracks.Clear();
        Changed?.Invoke();
    }

    public void SetStack(MagicQShow.CueStack stack)
    {
        Stack = stack;
        Jump(-1);
    }

    // Snaps to the state after `step` (no fades).
    public void Jump(int step)
    {
        if (Builder?.Model == null || Stack == null)
            return;
        _fades.Clear();
        _tracks.Clear();
        Step = Math.Clamp(step, -1, Stack.Steps.Count - 1);
        Builder.ApplyCueStack(Stack, Step);
        _hold = 0;
        Changed?.Invoke();
    }

    public void Back() => Jump(Step - 1);

    public void Go()
    {
        if (Builder?.Model == null || Stack == null || Step + 1 >= Stack.Steps.Count)
        {
            Auto = false;
            Changed?.Invoke();
            return;
        }
        FinishRunning();
        int next = Step + 1;
        var step = Stack.Steps[next];
        var cue = Builder.Model.MagicQ.Cues.FirstOrDefault(c => c.Id == step.CueId);
        float fadeIn = cue?.FadeIn ?? 0f, fadeOut = cue?.FadeOut ?? 0f;

        foreach (var (fixture, level) in Builder.CueLevels(Stack, next))
        {
            float time = level > fixture.Intensity ? fadeIn : fadeOut;
            if (time <= 0f || Mathf.IsEqualApprox(level, fixture.Intensity))
                fixture.Intensity = level;
            else
                _fades.Add(new Fade { Fixture = fixture, From = fixture.Intensity, To = level, Time = time });
        }

        var blocking = Builder.Model.Blocking;
        foreach (var (id, actor) in Builder.Actors)
        {
            var steps = blocking.Steps(step.CueId, id);
            if (steps.Count == 0)
                continue;
            var state = Builder.ActorStateAt(id, Stack, Step);
            var track = new Track { Actor = actor };
            for (int i = 0; i < steps.Count; i++)
            {
                state.Apply(steps[i]);
                track.Pending.Enqueue(new Segment
                {
                    Target = state.Copy(),
                    MoveTime = steps[i].MoveTime ?? 0f,
                    Delay = steps[i].Delay ?? 0f,
                    ExtraHold = i == steps.Count - 1 ? fadeOut : 0f,
                });
            }
            track.Final = state;
            _tracks.Add(track);
        }
        Step = next;
        _hold = 0;
        Changed?.Invoke();
    }

    // Starts a movement step; null when it completes instantly.
    private static Move StartMove(Actor actor, Segment seg)
    {
        var after = seg.Target;
        bool visible = after.Visible ?? true;
        var to = after.Position is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : actor.Position;
        float facing = after.Facing ?? 0f;
        string pose = after.Pose ?? ActorPose.Standing;

        if (!actor.Visible)
        {
            // Appearing (or staying hidden): no walk from the old place.
            actor.Apply(after);
            return null;
        }

        float distance = actor.Position.DistanceTo(to);
        float walk = distance < 0.05f ? 0f : seg.MoveTime > 0f ? seg.MoveTime : distance / WalkSpeed;
        bool turns = !Mathf.IsEqualApprox(Mathf.Wrap(facing - actor.Facing, -180f, 180f), 0f);
        if (walk <= 0f && pose != actor.Pose)
            actor.Pose = pose;   // sit down / stand up / lie down on the spot: immediately
        if (walk <= 0f && !turns && visible)
            return null;

        var d = to - actor.Position;
        var move = new Move
        {
            From = actor.Position,
            To = to,
            Walk = walk,
            Turn = turns || walk > 0f ? TurnTime : 0f,
            FromFacing = actor.Facing,
            ToFacing = facing,
            WalkFacing = walk > 0f ? Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)) : actor.Facing,
            EndPose = pose,
            EndVisible = visible,
        };
        if (!visible)
            move.Hold = Mathf.Max(0f, seg.ExtraHold - move.Walk - move.Turn);   // gone when the light has faded out
        if (walk > 0f && actor.Pose != ActorPose.Standing)
            actor.Pose = ActorPose.Standing;   // get up before walking
        return move;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _fades.Count - 1; i >= 0; i--)
        {
            var f = _fades[i];
            f.T += dt;
            if (!IsInstanceValid(f.Fixture))
            {
                _fades.RemoveAt(i);
                continue;
            }
            float k = Mathf.Clamp(f.T / f.Time, 0f, 1f);
            f.Fixture.Intensity = Mathf.Lerp(f.From, f.To, k);
            if (k >= 1f)
                _fades.RemoveAt(i);
        }

        for (int i = _tracks.Count - 1; i >= 0; i--)
            if (!Advance(_tracks[i], dt))
                _tracks.RemoveAt(i);

        if (Auto && !Running && Stack != null)
        {
            _hold += delta;
            if (_hold >= Hold)
                Go();
        }
    }

    // Advances an actor's movement steps; false when the actor is done in this cue.
    private bool Advance(Track track, float dt)
    {
        var actor = track.Actor;
        if (!IsInstanceValid(actor))
            return false;
        while (true)
        {
            if (track.Active == null)
            {
                if (track.Pending.Count == 0)
                    return false;
                var seg = track.Pending.Peek();
                if (track.Wait < seg.Delay)
                {
                    track.Wait += dt;
                    return true;
                }
                track.Pending.Dequeue();
                track.Wait = 0f;
                track.Active = StartMove(actor, seg);
                dt = 0f;
                if (track.Active == null)
                    continue;   // instant step: go on with the next one in the same frame
            }

            var m = track.Active;
            m.T += dt;
            if (m.Walk > 0f && m.T < m.Walk)
            {
                float k = m.T / m.Walk;
                actor.Position = m.From.Lerp(m.To, Mathf.SmoothStep(0f, 1f, k));
                float turnIn = Mathf.Clamp(m.T / TurnTime, 0f, 1f);   // turn towards the walking direction first
                actor.Facing = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(m.FromFacing), Mathf.DegToRad(m.WalkFacing), turnIn));
            }
            else
            {
                actor.Position = m.To;
                float start = m.Walk > 0f ? m.WalkFacing : m.FromFacing;
                float k = m.Turn > 0f ? Mathf.Clamp((m.T - m.Walk) / TurnTime, 0f, 1f) : 1f;
                actor.Facing = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(start), Mathf.DegToRad(m.ToFacing), k));
                actor.Pose = m.EndPose;
            }
            if (m.T < m.Duration)
                return true;
            actor.Facing = m.ToFacing;
            actor.Pose = m.EndPose;
            actor.Visible = m.EndVisible;
            track.Active = null;
        }
    }

    // A GO while the previous cue is still running completes it first.
    private void FinishRunning()
    {
        foreach (var f in _fades.Where(f => IsInstanceValid(f.Fixture)))
            f.Fixture.Intensity = f.To;
        foreach (var t in _tracks.Where(t => IsInstanceValid(t.Actor)))
            t.Actor.Apply(t.Final);
        _fades.Clear();
        _tracks.Clear();
    }
}
