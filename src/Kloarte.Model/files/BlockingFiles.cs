using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The blocking as its two files and back: <c>blocking/actors.yaml</c> (<see cref="ActorsFile"/>) and
/// <c>blocking/steps.yaml</c> (<see cref="StepsFile"/>: per cue, in cue order, the hold, the smoke and the movement steps). Numbers
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

    /// <summary>The steps' file: every cue with a hold or movement, in the show's cue order.</summary>
    public static StepsFile StepsOf(Blocking blocking, CueIndex cues)
    {
        var ids = blocking.Holds.Keys.Concat(blocking.Smoke.Keys).Concat(blocking.Cues.Where(c => c.Value.Values.Any(s => s.Count > 0)).Select(c => c.Key))
            .Distinct().OrderBy(cues.Order).ThenBy(id => id);
        var file = new StepsFile();
        foreach (int id in ids)
        {
            var actors = blocking.Cues.GetValueOrDefault(id)?
                .Where(a => a.Value.Count > 0)
                .ToDictionary(a => a.Key, a => a.Value.Select(Entry).ToList());
            file.Cues.Add(new CueEntry
            {
                Cue = cues.NumberOf(id) is { Length: > 0 } number ? number : null,
                Id = id,
                Hold = blocking.Holds.TryGetValue(id, out float hold) ? FileNumbers.Round(hold, Tenths) : null,
                Smoke = blocking.Smoke.TryGetValue(id, out var smoke) ? FileNumbers.Round(smoke.Amount, Tenths) : null,
                SmokeTime = blocking.Smoke.TryGetValue(id, out var timed) && timed.Time is { } time ? FileNumbers.Round(time, Tenths) : null,
                Actors = actors is { Count: > 0 } ? actors : null,
            });
        }
        return file;
    }

    /// <summary>
    /// The blocking of the two files (either may be null = empty). An entry whose cue can't be found (no id, an unknown
    /// number) is left out with a warning in `problems` (for the steps file `stepsPath`).
    /// </summary>
    public static Blocking FromFiles(ActorsFile actors, StepsFile steps, CueIndex cues, string stepsPath, List<FileProblem> problems)
    {
        var blocking = new Blocking();
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
