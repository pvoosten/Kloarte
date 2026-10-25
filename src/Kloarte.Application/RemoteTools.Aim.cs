using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the actors and aiming lamps at them (user, 25 Oct 2026), two different kinds of aiming:
/// <list type="bullet">
/// <item>moving heads: pan / tilt in a cue (the head turns per cue; the angles come from the rigged head, as "Aim at the
///   stage" computes them);</item>
/// <item>mechanical: a conventional lamp's focus point in its mechanics, as rigged and focused by hand, the same in every
///   cue.</item>
/// </list>
/// Targets are actors (their face at the end of the cue, from the blocking), the middle of several actors, or a stage
/// point.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The description of a target, shared by the aiming tools.</summary>
    private const string TargetHelp =
        "The target: 'actor' (id or name: their face at the end of the cue), 'actors' (the middle of their faces) or 'at' " +
        "([x, y, z] m, stage coordinates); 'height' replaces the face height (m above the floor at the actor).";

    /// <summary>Face height of a standing actor, as a part of their height (eyes to chin).</summary>
    private const float StandingFace = 0.93f;

    /// <summary>Face height of a seated actor, as a part of their height.</summary>
    private const float SeatedFace = 0.68f;

    /// <summary>Face height of a lying actor (m).</summary>
    private const float LyingFace = 0.25f;

    /// <summary>The tools of the actors and aiming.</summary>
    private IEnumerable<RemoteTool> AimTools()
    {
        yield return Tool("list_actors", "Actors",
            "The actors at the end of a cue (from the blocking): id, name, on stage or not, pose, position [x, y, z] (m, " +
            "stage coordinates: origin on the floor at the proscenium line, +X audience's right, +Z towards the audience), " +
            "facing (deg, 0 = the audience), height, and the face point the aiming tools aim at.",
            Object(Optional("cue", TextOrNumber("Cue number (default: the current cue)"))),
            readOnly: true, Actors);

        yield return Tool("aim_moving_heads", "Aim moving heads",
            "Moving-head aiming: pan / tilt of moving heads in a cue (later cues track them), computed from each rigged " +
            "head as 'Aim at the stage' does. Either 'heads' (numbers / groups) with one target for all of them, or " +
            "'aims': one target per head. " + TargetHelp + " One undo step. Conventional lamps: use focus_lamps.",
            Object(new[]
            {
                Optional("cue", TextOrNumber("Cue number (default: the current cue)")),
                Optional("heads", List("Moving heads (numbers or group names), all aimed at the target")),
                Optional("aims", ListOf(Object(new[] { Required("head", Integer("Head number")) }.Concat(Target()).ToArray()),
                    "One target per head")),
            }.Concat(Target()).ToArray()),
            readOnly: false, AimMovingHeads);

        yield return Tool("focus_lamps", "Focus lamps (mechanical)",
            "Mechanical aiming: focuses conventional lamps (PC, fresnel, profile, par) on a target, as rigged and focused " +
            "by hand: their focus point in the mechanics, the same in EVERY cue (not per cue). Either 'lamps' (rig item " +
            "ids) / 'heads' with one target, or 'aims': one target per lamp. Positions of actors are taken at 'cue'. " +
            TargetHelp + " One undo step. Moving heads: use aim_moving_heads.",
            Object(new[]
            {
                Optional("cue", TextOrNumber("The cue whose actor positions count (default: the current cue)")),
                Optional("lamps", List("Rig item ids")),
                Optional("heads", List("Heads (numbers or group names): their lamps")),
                Optional("aims", ListOf(Object(new[]
                {
                    Optional("lamp", Integer("Rig item id")), Optional("head", Integer("Head number (its lamps)")),
                }.Concat(Target()).ToArray()), "One target per lamp or head")),
            }.Concat(Target()).ToArray()),
            readOnly: false, FocusLamps);
    }

    /// <summary>The target properties of an aiming tool's schema (new nodes each time: a node has one parent).</summary>
    private static (string, JsonObject, bool)[] Target() => new[]
    {
        Optional("actor", String("Actor id or name")),
        Optional("actors", List("Actor ids or names: aimed at the middle of their faces")),
        Optional("at", Point("A stage point [x, y, z] (m)")),
        Optional("height", Number("Aim point height above the floor at the actor(s) (m), instead of the face", 0, 10)),
    };

    /// <summary>The answer of list_actors.</summary>
    private object Actors(ToolArgs args)
    {
        var show = Show;
        int index = show.CueOrder.Count == 0 ? -1 : args.Has("cue") || CurrentIndex(show) >= 0 ? CueIndex(show, args) : -1;
        return new
        {
            atCue = index >= 0 ? show.CueOrder[index].Cue : "before the first cue",
            actors = Model.Blocking.Actors.Select(a =>
            {
                var state = StateOf(show, a, index);
                return new
                {
                    id = a.Id, name = a.Name, onStage = state.Visible != false, pose = state.Pose ?? ActorPose.Standing,
                    position = Rounded(PositionOf(state)), facing = state.Facing ?? 0f,
                    height = Math.Round(a.EffectiveHeight, 2), face = Rounded(FaceOf(a, state, null)),
                };
            }).ToList(),
        };
    }

    /// <summary>aim_moving_heads.</summary>
    private object AimMovingHeads(ToolArgs args)
    {
        var show = Show;
        int index = CueIndex(show, args);
        var cue = show.CueOrder[index];
        var aims = Assignments(show, args, index, (aim, name) =>
        {
            int head = aim.Int(name) ?? throw ToolArgs.Missing(name);
            return new[] { head };
        }, HeadsOf(show, args));
        var angles = new List<(int Head, float Pan, float Tilt, string Target)>();
        foreach (var (head, point, label) in aims)
        {
            var layout = show.LayoutOf(head) ?? throw new ArgumentException($"No head {head}");
            if (layout["Pan"] == null || layout["Tilt"] == null)
                throw new ArgumentException($"Head {head} has no pan / tilt: aim a conventional lamp with focus_lamps");
            var (pan, tilt) = _app.AimAngles(head, point)
                              ?? throw new InvalidOperationException($"Head {head} has no moving head on stage");
            angles.Add((head, (float)Math.Round(pan, 2), (float)Math.Round(tilt, 2), label));
        }
        _app.EditShow($"Aim in cue {cue.Cue}", _ =>
        {
            foreach (var a in angles)
            {
                DesignEdits.Set(cue.Heads, a.Head, "Pan", LookValue.Of(a.Pan));
                DesignEdits.Set(cue.Heads, a.Head, "Tilt", LookValue.Of(a.Tilt));
            }
        });
        return new
        {
            done = Done($"Aimed {angles.Count} moving head{(angles.Count == 1 ? "" : "s")} in cue {cue.Cue}"),
            heads = angles.Select(a => new { head = a.Head, pan = a.Pan, tilt = a.Tilt, target = a.Target }).ToList(),
        };
    }

    /// <summary>focus_lamps.</summary>
    private object FocusLamps(ToolArgs args)
    {
        var show = Show;
        int index = show.CueOrder.Count == 0 ? -1 : args.Has("cue") || CurrentIndex(show) >= 0 ? CueIndex(show, args) : -1;
        var lamps = args.Strings("lamps").Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? id : throw new ArgumentException($"'{s}' is not a rig item id"))
            .Concat(HeadsOf(show, args).SelectMany(h => LampsOfHead(show, h)))
            .Distinct().ToList();
        var aims = Assignments(show, args, index, (aim, _) =>
        {
            if (aim.Int("lamp") is { } lamp)
                return new[] { lamp };
            if (aim.Int("head") is { } head)
                return LampsOfHead(show, head);
            throw new ArgumentException("aims: each needs 'lamp' (rig item id) or 'head'");
        }, lamps);
        var points = new Dictionary<int, Vector3>();
        foreach (var (lamp, point, _) in aims)
        {
            var item = show.Rig.Items.FirstOrDefault(i => i.IsLamp && i.Id == lamp) ?? throw new ArgumentException($"No lamp {lamp}");
            if (item.Head is { } head && show.LayoutOf(head)?["Pan"] != null)
                throw new ArgumentException($"Lamp {lamp} (head {head}) is a moving head: aim it with aim_moving_heads");
            points[lamp] = point;
        }
        _app.FocusLamps(points.Count == 1 ? $"Focus lamp {points.Keys.First()}" : $"Focus {points.Count} lamps", points);
        return new
        {
            done = Done($"Focused {points.Count} lamp{(points.Count == 1 ? "" : "s")} (mechanics: every cue)"),
            lamps = aims.Select(a => new { lamp = a.Who, focusPoint = Rounded(a.Point), target = a.Target }).ToList(),
        };
    }

    /// <summary>
    /// Who is aimed where: the 'aims' list (each item names its lamps or heads through `who`), or `shared` (heads or
    /// lamps) all at the call's own target. Points are stage points; actors are taken at the end of cue `index`.
    /// </summary>
    private List<(int Who, Vector3 Point, string Target)> Assignments(DesignShow show, ToolArgs args, int index,
        Func<ToolArgs, string, IReadOnlyList<int>> who, IReadOnlyList<int> shared)
    {
        var result = new List<(int, Vector3, string)>();
        foreach (var aim in args.Objects("aims"))
        {
            var (point, label) = TargetOf(show, aim, index);
            result.AddRange(who(aim, "head").Select(w => (w, point, label)));
        }
        if (shared.Count > 0)
        {
            var (point, label) = TargetOf(show, args, index);
            result.AddRange(shared.Select(w => (w, point, label)));
        }
        if (result.Count == 0)
            throw new ArgumentException("Nothing to aim: give the lamps or heads with a target, or 'aims'");
        return result;
    }

    /// <summary>The stage point and a description of the target the arguments name (actor, actors or at).</summary>
    private (Vector3 Point, string Label) TargetOf(DesignShow show, ToolArgs args, int index)
    {
        float? height = args.Number("height") is { } h ? (float)h : null;
        if (args.Point("at") is { } at)
            return (at, $"[{string.Join(", ", Rounded(at))}]");
        var names = args.Strings("actors").Concat(args.OptionalString("actor") is { } one ? new[] { one } : Array.Empty<string>()).ToList();
        if (names.Count == 0)
            throw new ArgumentException("No target: give 'actor', 'actors' or 'at'");
        string cue = index >= 0 ? $"cue {show.CueOrder[index].Cue}" : "the start";
        var faces = names.Select(n =>
        {
            var actor = ActorOf(n);
            var state = StateOf(show, actor, index);
            if (state.Visible == false)
                throw new ArgumentException($"{actor.Name} is not on stage at {cue}");
            return FaceOf(actor, state, height);
        }).ToList();
        var middle = faces.Aggregate(Vector3.Zero, (sum, f) => sum + f) / faces.Count;
        return (middle, string.Join(" + ", names.Select(n => ActorOf(n).Name)));
    }

    /// <summary>The actor with this id or name (without case).</summary>
    private ActorDef ActorOf(string idOrName)
    {
        var actors = Model.Blocking.Actors;
        return actors.FirstOrDefault(a => a.Id == idOrName)
               ?? actors.FirstOrDefault(a => a.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
               ?? actors.FirstOrDefault(a => a.Id == ActorIds.Slug(idOrName))
               ?? throw new ArgumentException(
                   $"No actor '{idOrName}' (actors: {string.Join(", ", actors.Select(a => a.Name))})");
    }

    /// <summary>An actor's state at the end of cue `index` of the cue list (-1: before the first cue).</summary>
    private ActorState StateOf(DesignShow show, ActorDef actor, int index) =>
        Model.Blocking.StateAfter(actor.Id, show.CueOrder.Take(index + 1).Where(c => c.Id != null).Select(c => c.Id.Value));

    /// <summary>A state's position (the origin when it has none).</summary>
    private static Vector3 PositionOf(ActorState state) =>
        state.Position is { Length: >= 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero;

    /// <summary>The point an actor is aimed at: their face for their pose, or `height` above the floor at them.</summary>
    private static Vector3 FaceOf(ActorDef actor, ActorState state, float? height)
    {
        float face = height ?? state.Pose switch
        {
            ActorPose.Seated => actor.EffectiveHeight * SeatedFace,
            ActorPose.Lying => LyingFace,
            _ => actor.EffectiveHeight * StandingFace,
        };
        return PositionOf(state) + new Vector3(0, face, 0);
    }

    /// <summary>The rig item ids of a head's lamps.</summary>
    private static IReadOnlyList<int> LampsOfHead(DesignShow show, int head)
    {
        var lamps = show.Rig.Items.Where(i => i.IsLamp && i.Head == head).Select(i => i.Id).ToList();
        return lamps.Count > 0 ? lamps : throw new ArgumentException($"Head {head} has no lamp in the rig");
    }

    /// <summary>A point as [x, y, z], rounded to the centimetre.</summary>
    private static double[] Rounded(Vector3 p) => new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(p.Z, 2) };
}
