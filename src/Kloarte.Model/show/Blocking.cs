using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Blocking: the actors on stage and what they do per cue (not something MagicQ can store). Like MagicQ cues it
/// tracks: a cue only stores what changes; the state at a step = the initial state + all changes of the steps up to
/// and including it, in cue order. Within one cue an actor can have several movement steps that run one after the
/// other (e.g. walk to the table, wait, walk to the chair and sit down). Cues are keyed by their MagicQ cue id
/// (S record), so renumbering in MagicQ keeps the blocking. Positions are stage coordinates (origin = downstage
/// centre on the stage floor). Holds: the time from the end of a cue to the next GO (the timeline's timing). Smoke: the
/// haze amount a cue sets (it tracks into the later cues; <see cref="CueSmoke"/>). Scenery: the set pieces and what each cue
/// changes of them (out / in, a curtain opening; tracks too). No Godot dependencies.
/// </summary>
public sealed class Blocking
{
    /// <summary>Seconds a hazer takes to reach a cue's smoke amount when the cue doesn't say.</summary>
    public const float DefaultSmokeTime = 20f;

    /// <summary>Seconds a split curtain takes to open or close when the cue doesn't say.</summary>
    public const float DefaultOpenTime = 5f;

    /// <summary>The actors.</summary>
    public List<ActorDef> Actors { get; set; } = new();

    /// <summary>The set pieces (<c>blocking/scenery.yaml</c>); before the first cue every piece is on stage.</summary>
    public List<SceneryPiece> Scenery { get; set; } = new();

    /// <summary>cue id -&gt; piece id -&gt; what that cue changes of the piece (out / in, a curtain opening); tracks.</summary>
    public Dictionary<int, Dictionary<string, SceneryChange>> SceneryCues { get; set; } = new();

    /// <summary>The set piece with this id, or null.</summary>
    public SceneryPiece FindPiece(string pieceId) => Scenery.FirstOrDefault(p => p.Id == pieceId);

    /// <summary>What a cue changes of a piece, or null (created empty when `create`).</summary>
    public SceneryChange SceneryChangeOf(int cueId, string pieceId, bool create = false)
    {
        if (!SceneryCues.TryGetValue(cueId, out var pieces))
        {
            if (!create)
                return null;
            SceneryCues[cueId] = pieces = new Dictionary<string, SceneryChange>();
        }
        if (!pieces.TryGetValue(pieceId, out var change) && create)
            pieces[pieceId] = change = new SceneryChange();
        return change;
    }

    /// <summary>Removes what a cue changes of a piece (and the cue's entry when nothing is left).</summary>
    public void ClearSceneryChange(int cueId, string pieceId)
    {
        if (!SceneryCues.TryGetValue(cueId, out var pieces))
            return;
        pieces.Remove(pieceId);
        if (pieces.Count == 0)
            SceneryCues.Remove(cueId);
    }

    /// <summary>A piece's state after the given cues (in order) have run completely.</summary>
    public SceneryFrame SceneryAfter(string pieceId, IEnumerable<int> cueIds)
    {
        var piece = FindPiece(pieceId);
        var frame = new SceneryFrame(true, piece?.Open ?? 0f);
        foreach (int cue in cueIds)
            if (SceneryChangeOf(cue, pieceId) is { } change)
                frame = new SceneryFrame(change.Visible ?? frame.Visible, change.Open ?? frame.Open);
        return frame;
    }

    /// <summary>Removes a set piece and what the cues change of it.</summary>
    public void RemovePiece(string pieceId)
    {
        Scenery.RemoveAll(p => p.Id == pieceId);
        foreach (int cue in SceneryCues.Keys.ToList())
            ClearSceneryChange(cue, pieceId);
    }

    /// <summary>cue id -&gt; the smoke that cue sets (from its GO on; later cues without an entry keep it).</summary>
    public Dictionary<int, CueSmoke> Smoke { get; set; } = new();

    /// <summary>cue id -&gt; wait (s) after the cue has finished until the next GO, as laid out in the timeline (and
    /// used by Auto playback and the movie). Cues without an entry use the player's default hold.</summary>
    public Dictionary<int, float> Holds { get; set; } = new();

    /// <summary>cue id -&gt; actor id -&gt; movement steps of that actor in that cue, in order</summary>
    public Dictionary<int, Dictionary<string, List<ActorState>>> Cues { get; set; } = new();

    /// <summary>The actor with this id, or null.</summary>
    public ActorDef Find(string actorId) => Actors.FirstOrDefault(a => a.Id == actorId);

    /// <summary>State of an actor after the given cues (in order) have run completely.</summary>
    public ActorState StateAfter(string actorId, IEnumerable<int> cueIds)
    {
        var actor = Find(actorId);
        var state = (actor?.Initial ?? new ActorState()).Copy();
        foreach (int cue in cueIds)
            foreach (var step in Steps(cue, actorId))
                state.Apply(step);
        return state;
    }

    /// <summary>The movement steps of an actor in a cue (empty when the actor doesn't change in that cue).</summary>
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

    /// <summary>
    /// Records a change in movement step `step` of an actor in a cue (merged with what that step already does);
    /// missing steps up to `step` are added empty.
    /// </summary>
    public void Record(int cueId, string actorId, int step, ActorState change)
    {
        var steps = Steps(cueId, actorId, create: true);
        while (steps.Count <= step)
            steps.Add(new ActorState());
        steps[step].Apply(change);
    }

    /// <summary>Removes a movement step of an actor in a cue.</summary>
    public void RemoveStep(int cueId, string actorId, int step)
    {
        var steps = Steps(cueId, actorId);
        if (step >= 0 && step < steps.Count)
            steps.RemoveAt(step);
        if (steps.Count == 0 && Cues.TryGetValue(cueId, out var actors))
            actors.Remove(actorId);
    }

    /// <summary>Removes all movement of an actor in a cue.</summary>
    public void ClearCue(int cueId, string actorId)
    {
        if (Cues.TryGetValue(cueId, out var actors))
            actors.Remove(actorId);
    }

    /// <summary>Removes an actor and all its movement.</summary>
    public void RemoveActor(string actorId)
    {
        Actors.RemoveAll(a => a.Id == actorId);
        foreach (var actors in Cues.Values)
            actors.Remove(actorId);
    }
}
