using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The blocking as its three files and back: <c>blocking/actors.yaml</c> (<see cref="ActorsFile"/>),
/// <c>blocking/scenery.yaml</c> (<see cref="SceneryFile"/>: the set pieces) and <c>blocking/steps.yaml</c>
/// (<see cref="StepsFile"/>: per cue, in cue order, the hold, the smoke, the scenery changes and the movement steps). Numbers
/// rounded for reading (metres 3 decimals, degrees and seconds 0.1).
/// </summary>
public static class BlockingFiles
{
    /// <summary>Decimals of metres and of degrees / seconds.</summary>
    private const int Metres = 3, Tenths = 1;

    /// <summary>The actors' file.</summary>
    public static ActorsFile ActorsOf(Blocking blocking) => new()
    {
        Actors = blocking.Actors.Select(a => new ActorEntry
        {
            Id = a.Id,
            Name = a.Name,
            Female = a.Female ? true : null,
            ShirtColor = ShirtColors.Parse(a.ShirtColor) != null ? "#" + a.ShirtColor.TrimStart('#').ToLowerInvariant() : null,
            Height = a.Height > 0 ? FileNumbers.Round(a.Height, 2) : null,
            Initial = Entry(a.Initial),
        }).ToList(),
    };

    /// <summary>The scenery file: the set pieces in their order; a value equal to the shape's default is left out.</summary>
    public static SceneryFile SceneryOf(Blocking blocking) => new()
    {
        Pieces = blocking.Scenery.Select(Entry).ToList(),
    };

    /// <summary>The steps' file: every cue with a hold, smoke, a scenery change or movement, in the show's cue order.</summary>
    public static StepsFile StepsOf(Blocking blocking, CueIndex cues)
    {
        var ids = blocking.Holds.Keys.Concat(blocking.Smoke.Keys)
            .Concat(blocking.SceneryCues.Where(c => c.Value.Values.Any(v => v is { IsEmpty: false })).Select(c => c.Key))
            .Concat(blocking.Cues.Where(c => c.Value.Values.Any(s => s.Count > 0)).Select(c => c.Key))
            .Distinct().OrderBy(cues.Order).ThenBy(id => id);
        var file = new StepsFile();
        foreach (int id in ids)
        {
            var actors = blocking.Cues.GetValueOrDefault(id)?
                .Where(a => a.Value.Count > 0)
                .ToDictionary(a => a.Key, a => a.Value.Select(Entry).ToList());
            var scenery = blocking.SceneryCues.GetValueOrDefault(id)?
                .Where(p => p.Value is { IsEmpty: false })
                .ToDictionary(p => p.Key, p => new SceneryChangeEntry
                {
                    Visible = p.Value.Visible,
                    Open = FileNumbers.Round(p.Value.Open, Tenths),
                    Time = p.Value.Open != null ? FileNumbers.Round(p.Value.Time, Tenths) : null,
                });
            file.Cues.Add(new CueEntry
            {
                Cue = cues.NumberOf(id) is { Length: > 0 } number ? number : null,
                Id = id,
                Hold = blocking.Holds.TryGetValue(id, out float hold) ? FileNumbers.Round(hold, Tenths) : null,
                Smoke = blocking.Smoke.TryGetValue(id, out var smoke) ? FileNumbers.Round(smoke.Amount, Tenths) : null,
                SmokeTime = blocking.Smoke.TryGetValue(id, out var timed) && timed.Time is { } time ? FileNumbers.Round(time, Tenths) : null,
                Scenery = scenery is { Count: > 0 } ? scenery : null,
                Actors = actors is { Count: > 0 } ? actors : null,
            });
        }
        return file;
    }

    /// <summary>
    /// The blocking of the three files (any may be null = empty). An entry whose cue can't be found (no id, an unknown
    /// number) is left out with a warning in `problems` (for the steps file `stepsPath`); so is a piece without an id,
    /// with an unknown shape or a second time the same id (for the scenery file `sceneryPath`). A cue's change of a piece
    /// that isn't in the scenery file is kept (it applies again when the piece comes back).
    /// </summary>
    public static Blocking FromFiles(ActorsFile actors, StepsFile steps, SceneryFile scenery, CueIndex cues, string stepsPath,
        string sceneryPath, List<FileProblem> problems)
    {
        var blocking = new Blocking();
        foreach (var p in scenery?.Pieces ?? new List<SceneryPieceEntry>())
        {
            if (string.IsNullOrEmpty(p?.Id))
            {
                problems.Add(new FileProblem(sceneryPath, 0, 0, $"a piece {p?.Name} without an id (left out)", false));
                continue;
            }
            if (Piece(p) is not { } piece)
            {
                problems.Add(new FileProblem(sceneryPath, 0, 0, $"piece {p.Id}: unknown shape \"{p.Shape}\" (left out)", false));
                continue;
            }
            if (blocking.FindPiece(piece.Id) != null)
            {
                problems.Add(new FileProblem(sceneryPath, 0, 0, $"two pieces with id {p.Id} (the second left out)", false));
                continue;
            }
            blocking.Scenery.Add(piece);
        }
        foreach (var a in actors?.Actors ?? new List<ActorEntry>())
        {
            if (string.IsNullOrEmpty(a?.Id))
                continue;
            var def = new ActorDef
            {
                Id = a.Id,
                Name = a.Name ?? a.Id,
                Female = a.Female ?? false,
                ShirtColor = a.ShirtColor?.TrimStart('#').ToLowerInvariant(),
                Height = (float)(a.Height ?? 0),
            };
            if (a.Initial != null)
                def.Initial.Apply(State(a.Initial));
            blocking.Actors.Add(def);
        }
        foreach (var cue in steps?.Cues ?? new List<CueEntry>())
        {
            if (cue == null)
                continue;
            int? id = cue.Id ?? (cue.Cue != null ? cues.IdOf(cue.Cue) : null);
            if (id == null)
            {
                problems.Add(new FileProblem(stepsPath, 0, 0,
                    $"cue {cue.Cue ?? "(no number)"}: no id and no such cue number in the show's cue stacks (left out)", false));
                continue;
            }
            if (cue.Hold is { } hold)
                blocking.Holds[id.Value] = (float)hold;
            if (cue.Smoke is { } smoke)
                blocking.Smoke[id.Value] = new CueSmoke { Amount = Math.Clamp(smoke, 0.0, 100.0), Time = cue.SmokeTime is { } seconds ? (float)seconds : null };
            foreach (var (piece, change) in cue.Scenery ?? new Dictionary<string, SceneryChangeEntry>())
            {
                if (change == null || (change.Visible == null && change.Open == null))
                    continue;
                var target = blocking.SceneryChangeOf(id.Value, piece, create: true);
                target.Visible = change.Visible;
                target.Open = change.Open is { } open ? (float)Math.Clamp(open, 0.0, 100.0) : null;
                target.Time = change.Time is { } time ? (float)time : null;
            }
            foreach (var (actor, list) in cue.Actors ?? new Dictionary<string, List<ActorStateEntry>>())
            {
                if (list is not { Count: > 0 })
                    continue;
                if (!blocking.Cues.TryGetValue(id.Value, out var cueActors))
                    blocking.Cues[id.Value] = cueActors = new Dictionary<string, List<ActorState>>();
                cueActors[actor] = list.Select(s => State(s ?? new ActorStateEntry())).ToList();
            }
        }
        return blocking;
    }

    /// <summary>A piece as its file entry (rounded; a value equal to the shape's default left out).</summary>
    private static SceneryPieceEntry Entry(SceneryPiece p)
    {
        var d = SceneryPiece.New(p.Id, p.Name, p.Shape);
        double? Size(float value, float fallback) => Math.Abs(value - fallback) < 0.0005f ? null : FileNumbers.Round(value, Metres);
        bool curtain = p.Shape == SceneryShape.Curtain, table = p.Shape == SceneryShape.Table;
        return new SceneryPieceEntry
        {
            Id = p.Id,
            Name = p.Name,
            Shape = p.Shape.ToString().ToLowerInvariant(),
            Position = p.Position.Select(v => Math.Round((double)v, Metres, MidpointRounding.AwayFromZero)).ToArray(),
            Rotation = Math.Abs(p.Rotation) < 0.05f ? null : FileNumbers.Round(p.Rotation, Tenths),
            Width = Size(p.Width, d.Width),
            Depth = curtain ? null : Size(p.Depth, d.Depth),
            Height = Size(p.Height, d.Height),
            TopThickness = table ? Size(p.TopThickness, d.TopThickness) : null,
            LegThickness = table ? Size(p.LegThickness, d.LegThickness) : null,
            Fullness = curtain && Math.Abs(p.Fullness - d.Fullness) >= 0.05f ? FileNumbers.Round(p.Fullness, Tenths) : null,
            Split = curtain && p.Split ? true : null,
            Open = curtain && p.Split && p.Open > 0.05f ? FileNumbers.Round(p.Open, Tenths) : null,
            Color = p.ColorOrDefault != d.ColorOrDefault ? "#" + p.ColorOrDefault : null,
            Material = p.Material != d.Material ? SceneryMaterials.NameOf(p.Material) : null,
        };
    }

    /// <summary>A file entry as a piece (the shape's defaults for what is missing); null when the shape is unknown.</summary>
    private static SceneryPiece Piece(SceneryPieceEntry e)
    {
        if (!Enum.TryParse(e.Shape?.Trim() ?? nameof(SceneryShape.Block), true, out SceneryShape shape) || !Enum.IsDefined(shape))
            return null;
        var p = SceneryPiece.New(e.Id, e.Name ?? e.Id, shape);
        if (e.Position is { Length: 3 } at)
            p.Position = at.Select(v => (float)v).ToArray();
        p.Rotation = (float)(e.Rotation ?? 0);
        p.Width = (float)(e.Width ?? p.Width);
        p.Depth = (float)(e.Depth ?? p.Depth);
        p.Height = (float)(e.Height ?? p.Height);
        p.TopThickness = (float)(e.TopThickness ?? p.TopThickness);
        p.LegThickness = (float)(e.LegThickness ?? p.LegThickness);
        p.Fullness = (float)(e.Fullness ?? p.Fullness);
        p.Split = e.Split ?? false;
        p.Open = (float)Math.Clamp(e.Open ?? 0, 0, 100);
        if (ShirtColors.Parse(e.Color) != null)
            p.Color = e.Color.TrimStart('#').ToLowerInvariant();
        if (SceneryMaterials.Parse(e.Material) is { } material)
            p.Material = material;
        return p;
    }

    /// <summary>A state as its file entry (rounded).</summary>
    private static ActorStateEntry Entry(ActorState s) => new()
    {
        Delay = FileNumbers.Round(s.Delay, Tenths),
        MoveTime = FileNumbers.Round(s.MoveTime, Tenths),
        Visible = s.Visible,
        Pose = s.Pose,
        Position = s.Position?.Select(v => Math.Round((double)v, Metres, MidpointRounding.AwayFromZero)).ToArray(),
        Facing = FileNumbers.Round(s.Facing, Tenths),
    };

    /// <summary>A file entry as a state.</summary>
    private static ActorState State(ActorStateEntry e) => new()
    {
        Delay = (float?)e.Delay,
        MoveTime = (float?)e.MoveTime,
        Visible = e.Visible,
        Pose = e.Pose,
        Position = e.Position is { Length: 3 } p ? p.Select(v => (float)v).ToArray() : null,
        Facing = (float?)e.Facing,
    };
}
