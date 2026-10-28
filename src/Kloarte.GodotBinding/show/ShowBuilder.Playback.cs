using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>ShowBuilder: cue playback (levels from the MagicQ cues, tracking) and actors (blocking).</summary>
public partial class ShowBuilder
{
    /// <summary>
    /// Fixtures recorded in a cue (S record id), with their level 0..255 for single-channel heads (dimmers), else -1.
    /// A cue lists DMX addresses; they are mapped to heads through the patch (L field 10).
    /// </summary>
    public Dictionary<ProfileFixture, int> FixturesInCue(int cueId)
    {
        var result = new Dictionary<ProfileFixture, int>();
        var show = Document;
        var cue = show?.Cues.FirstOrDefault(c => c.Id == cueId);
        if (cue == null)
            return result;
        var levelOfHead = new Dictionary<int, int>();
        foreach (var (address, value) in cue.Channels)
            if (show.HeadAt(address) is var head and > 0)
                levelOfHead[head] = show.ChannelCountOfHead(head) == 1 ? value & 0xff : -1;

        foreach (var fixture in _visOf.Keys)
            if (show.HeadAt(fixture.DmxAddress) is var head and > 0 && levelOfHead.TryGetValue(head, out int level))
                result[fixture] = level;
        return result;
    }

    /// <summary>
    /// Plays a cue stack up to and including step `upTo` (index in cue-number order): MagicQ tracks, so the cues of
    /// steps 0..upTo are accumulated. Every fixture's intensity is set from its intensity channel (dimmers: their only
    /// channel; moving heads: the personality's dimmer attribute); moving heads also take their other attributes.
    /// Returns the number of fixtures above 0.
    /// </summary>
    public int ApplyCueStack(ICueStack stack, int upTo)
    {
        int lit = 0;
        foreach (var (fixture, level) in CueLevels(stack, upTo))
        {
            fixture.Intensity = level;
            if (level > 0f)
                lit++;
        }
        ApplyMovingHeads(DmxAfter(stack, upTo));
        foreach (var (id, actor) in _actors)
            actor.Apply(ActorStateAt(id, stack, upTo));
        ApplySceneryAfter(stack, upTo);
        return lit;
    }

    /// <summary>
    /// DMX values (address -&gt; 0..255) after steps 0..upTo of the stack (tracking; palette references resolved;
    /// dimmer curves applied, as MagicQ outputs them).
    /// </summary>
    public Dictionary<int, int> DmxAfter(ICueStack stack, int upTo) => Playback.DmxAfter(stack, upTo);

    /// <summary>
    /// Intensity (0..1) of every fixture after steps 0..upTo of the stack (tracking). upTo = -1: all off.
    /// </summary>
    public Dictionary<ProfileFixture, float> CueLevels(ICueStack stack, int upTo) => LevelsFrom(DmxAfter(stack, upTo));

    /// <summary>
    /// Intensity (0..1) of every lamp from DMX values (its intensity channel / 255).
    /// </summary>
    public Dictionary<ProfileFixture, float> LevelsFrom(Dictionary<int, int> dmx)
    {
        var merged = dmx ?? new Dictionary<int, int>();
        var levels = new Dictionary<ProfileFixture, float>();
        foreach (var fixture in _visOf.Keys)
        {
            int channel = IntensityAddress(fixture);
            levels[fixture] = channel > 0 && merged.TryGetValue(channel, out int v) ? v / 255f : 0f;
        }
        return levels;
    }

    /// <summary>
    /// Sets a lamp's intensity (0..1) in the cue of a step (its head's intensity channel: every lamp on the head
    /// follows; one undo step). Returns the DMX level, or -1 (no intensity channel, no cue).
    /// </summary>
    public int SetLevel(ProfileFixture f, ICueStack stack, int step, float level) =>
        VisOf(f) is { } vis ? Playback.SetLevel(stack, step, vis.Id, level) : -1;

    /// <inheritdoc />
    public void SetIntensityAt(int intensityAddress, float level)
    {
        foreach (var lamp in _visOf.Keys.Where(l => IsInstanceValid(l) && IntensityAddress(l) == intensityAddress))
            lamp.Intensity = level;
    }

    /// <inheritdoc />
    public void SetLampIntensity(int lampId, float level)
    {
        if (FixtureOf(lampId) is { } f)
            f.Intensity = level;
    }

    private readonly Dictionary<int, int> _intensityAddress = new();   // DMX address of a head -> its intensity channel

    /// <summary>DMX address of the lamp's intensity channel (0 = none). Cached per head address.</summary>
    public int IntensityAddress(ProfileFixture f)
    {
        if (_intensityAddress.TryGetValue(f.DmxAddress, out int cached))
            return cached;
        return _intensityAddress[f.DmxAddress] = FindIntensityAddress(f);
    }

    private int FindIntensityAddress(ProfileFixture f) => Document.IntensityAddressOf(f.DmxAddress);

    // --- Actors (blocking) ---

    private readonly Dictionary<string, Actor> _actors = new();
    /// <summary>The actor nodes by actor id.</summary>
    public IReadOnlyDictionary<string, Actor> Actors => _actors;

    /// <summary>Actor state after steps 0..upTo of the stack (upTo = -1: the initial state).</summary>
    public ActorState ActorStateAt(string actorId, ICueStack stack, int upTo) =>
        Model.Blocking.StateAfter(actorId, stack == null ? Enumerable.Empty<int>()
            : stack.Steps.Take(upTo + 1).Select(s => s.CueId));

    /// <summary>
    /// Without saved blocking the MagicVis people become the actors (initial position, facing, standing/seated).
    /// </summary>
    private void BuildActors(IShowDocument show)
    {
        var node = new Node3D { Name = "Actors" };
        AddChild(node);
        var blocking = Model.Blocking;
        if (blocking.Actors.Count == 0)
            foreach (var item in show.RigItems.Where(v => v.ObjectKind == RigObjectKind.Person))
            {
                var (female, pose) = Mannequin.MagicVisCodes.GetValueOrDefault(item.ObjectCode, (false, Mannequin.Pose.Standing));
                var p = ToStage(item.X, item.Y, item.Z);
                string name = $"{item.Name} #{blocking.Actors.Count + 1}";
                blocking.Actors.Add(new ActorDef
                {
                    Id = ActorIds.For(name, blocking.Actors.Select(a => a.Id)),
                    Name = name,
                    Female = female,
                    Initial = new ActorState
                    {
                        Visible = true,
                        Pose = pose == Mannequin.Pose.Seated ? ActorPose.Seated : ActorPose.Standing,
                        Position = new[] { p.X, p.Y, p.Z },
                        Facing = item.RotY,
                    },
                });
            }
        foreach (var def in blocking.Actors)
            AddActorNode(node, def);
    }

    private Actor AddActorNode(Node parent, ActorDef def)
    {
        var actor = Actor.Create(def, _skin, _people, _molton, ShirtColors.Of(Model.Blocking, def));
        actor.Apply(def.Initial);
        parent.AddChild(actor);
        _actors[def.Id] = actor;
        return actor;
    }

    /// <summary>Adds an actor (blocking + node) at a stage position.</summary>
    public Actor AddActor(string name, bool female, Vector3 position)
    {
        var def = new ActorDef
        {
            Id = ActorIds.For(name, Model.Blocking.Actors.Select(a => a.Id)),
            Name = name,
            Female = female,
            Initial = new ActorState { Visible = true, Pose = ActorPose.Standing, Position = new[] { position.X, position.Y, position.Z }, Facing = 0f },
        };
        Model.Blocking.Actors.Add(def);
        return AddActorNode(GetNode("Actors"), def);
    }

    /// <summary>Removes an actor (blocking + node).</summary>
    public void RemoveActor(string actorId)
    {
        Model.Blocking.RemoveActor(actorId);
        if (_actors.Remove(actorId, out var actor))
            actor.QueueFree();
    }

    /// <summary>Writes the blocking JSON; returns a status message.</summary>
    public string SaveBlocking() => Files.SaveBlocking();
}
