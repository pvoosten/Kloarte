using System.Collections.Generic;
using System.Linq;

// Blocking: the actors on stage and what they do per cue (not something MagicQ can store). Like MagicQ cues it
// tracks: a cue only stores what changes; the state at a step = the initial state + all changes of the steps up to
// and including it, in cue order. Within one cue an actor can have several movement steps that run one after the
// other (e.g. walk to the table, wait, walk to the chair and sit down). Cues are keyed by their MagicQ cue id
// (S record), so renumbering in MagicQ keeps the blocking. Positions are stage coordinates (origin = downstage
// centre on the stage floor). No Godot dependencies.
public sealed class Blocking
{
    public List<ActorDef> Actors { get; set; } = new();

    // cue id -> actor id -> movement steps of that actor in that cue, in order
    public Dictionary<int, Dictionary<string, List<ActorState>>> Cues { get; set; } = new();

    public ActorDef Find(string actorId) => Actors.FirstOrDefault(a => a.Id == actorId);

    // State of an actor after the given cues (in order) have run completely.
    public ActorState StateAfter(string actorId, IEnumerable<int> cueIds)
    {
        var actor = Find(actorId);
        var state = (actor?.Initial ?? new ActorState()).Copy();
        foreach (int cue in cueIds)
            foreach (var step in Steps(cue, actorId))
                state.Apply(step);
        return state;
    }

    // The movement steps of an actor in a cue (empty when the actor doesn't change in that cue).
    public List<ActorState> Steps(int cueId, string actorId, bool create = false)
    {
        if (!Cues.TryGetValue(cueId, out var actors))
        {
            if (!create)
                return new List<ActorState>();
            Cues[cueId] = actors = new Dictionary<string, List<ActorState>>();
        }
        if (!actors.TryGetValue(actorId, out var steps))
        {
            steps = new List<ActorState>();
            if (create)
                actors[actorId] = steps;
        }
        return steps;
    }

    // Records a change in movement step `step` of an actor in a cue (merged with what that step already does);
    // missing steps up to `step` are added empty.
    public void Record(int cueId, string actorId, int step, ActorState change)
    {
        var steps = Steps(cueId, actorId, create: true);
        while (steps.Count <= step)
            steps.Add(new ActorState());
        steps[step].Apply(change);
    }

    public void RemoveStep(int cueId, string actorId, int step)
    {
        var steps = Steps(cueId, actorId);
        if (step >= 0 && step < steps.Count)
            steps.RemoveAt(step);
        if (steps.Count == 0 && Cues.TryGetValue(cueId, out var actors))
            actors.Remove(actorId);
    }

    public void ClearCue(int cueId, string actorId)
    {
        if (Cues.TryGetValue(cueId, out var actors))
            actors.Remove(actorId);
    }

    public void RemoveActor(string actorId)
    {
        Actors.RemoveAll(a => a.Id == actorId);
        foreach (var actors in Cues.Values)
            actors.Remove(actorId);
    }
}

public sealed class ActorDef
{
    public const float DefaultMaleHeight = 1.76f, DefaultFemaleHeight = 1.66f;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Female { get; set; }
    public float Height { get; set; }   // metres; 0 = default for a man / woman
    public ActorState Initial { get; set; } = new() { Visible = true, Pose = ActorPose.Standing, Position = new float[3], Facing = 0f };

    public float EffectiveHeight => Height > 0f ? Height : Female ? DefaultFemaleHeight : DefaultMaleHeight;
}

public static class ActorPose
{
    public const string Standing = "standing", Seated = "seated", Lying = "lying";
    public static readonly string[] All = { Standing, Seated, Lying };
}

// One state change / movement step. All fields optional: null = unchanged (tracking).
// Delay and MoveTime describe how the step is played, not where the actor ends up.
public sealed class ActorState
{
    public bool? Visible { get; set; }
    public string Pose { get; set; }            // ActorPose
    public float[] Position { get; set; }       // x, y, z in stage coordinates
    public float? Facing { get; set; }          // degrees around Y; 0 = facing the audience
    public float? MoveTime { get; set; }        // seconds to walk to the new position; null = walking speed
    public float? Delay { get; set; }           // seconds to wait before this step starts (after the previous step / GO)

    public ActorState Copy() => new()
    {
        Visible = Visible,
        Pose = Pose,
        Position = (float[])Position?.Clone(),
        Facing = Facing,
        MoveTime = MoveTime,
        Delay = Delay,
    };

    public void Apply(ActorState change)
    {
        if (change.Visible != null) Visible = change.Visible;
        if (change.Pose != null) Pose = change.Pose;
        if (change.Position != null) Position = (float[])change.Position.Clone();
        if (change.Facing != null) Facing = change.Facing;
        if (change.MoveTime != null) MoveTime = change.MoveTime;
        if (change.Delay != null) Delay = change.Delay;
    }
}
