using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// Patch changes on a MagicQ show (<see cref="PatchEditor"/>), and new lamps from <see cref="TemplatesPath"/> for
/// personalities the show doesn't have.
/// </summary>
public sealed class MagicQShowEditor : IShowEditor
{
    /// <summary>
    /// MagicQ records (personality, one head's patch, one MagicVis fixture) for each personality the catalogue uses
    /// (generic_dimmer, the moving heads): lamps are added from them when the show has no lamp of that kind yet.
    /// </summary>
    public const string TemplatesPath = "res://data/magicq_templates.shw";

    /// <summary>The show being edited.</summary>
    private readonly MagicQShow _show;

    /// <summary>Patch changes on <see cref="_show"/>.</summary>
    private readonly PatchEditor _patch;

    /// <summary>Creates the editor of a show.</summary>
    public MagicQShowEditor(MagicQShow show)
    {
        _show = show;
        _patch = new PatchEditor(show);
    }

    /// <inheritdoc />
    public string ApplyLampPatch(IRigItem lamp, int head, int address) => _patch.ApplyLampPatch((MagicQShow.VisItem)lamp, head, address);

    /// <inheritdoc />
    public string AddLampHead(IRigItem lamp, string headName) => _patch.AddLampHead((MagicQShow.VisItem)lamp, headName);

    /// <inheritdoc />
    public bool RemoveUnusedHead(int head) => _patch.RemoveUnusedHead(head);

    /// <inheritdoc />
    public int ChannelCount(int head) => _patch.Channels(head).Count;

    /// <inheritdoc />
    public int LampCount(int head) => _patch.Lamps(head).Count;

    /// <summary>Imports the personality, one head's patch and one MagicVis fixture of the template show.</summary>
    public IRigItem ImportLamp(string personality, string headName, string typeName)
    {
        var source = MagicQShow.Load(AppPaths.File(TemplatesPath));
        var sourceLamp = source.Vis.FirstOrDefault(v => v.Kind == MagicQShow.VisKind.Fixture
                && source.Personalities.FirstOrDefault(p => p.Id == v.Personality)?.Name == personality)
            ?? throw new ShowEditException(
                $"The show has no {typeName} and there is no MagicQ personality '{personality}' to add one; patch one in MagicQ first");
        int channels = source.Patch.Count(p => p.HeadNumber == sourceLamp.HeadNumber && p.Personality == sourceLamp.Personality);
        return _show.ImportLamp(source, sourceLamp, _patch.FreeHeadNumber(), _patch.FreeAddress(channels), headName);
    }

    /// <inheritdoc />
    public int NudgeLevel(ICueStack stack, int step, int address, int delta) =>
        _show.NudgeLevel((MagicQShow.CueStack)stack, step, address, delta);

    /// <summary>
    /// "Recorded 3 channels of heads 12, 14 into cue 4.5 (1 as palette "Front"); head 20 as in the cue before: not
    /// stored, tracks; the next 2 cues track it (not saved yet)".
    /// </summary>
    public string RecordIntoCue(ICueStack stack, int step, IReadOnlyList<(int Address, int Value)> values)
    {
        var result = _show.RecordIntoCue((MagicQShow.CueStack)stack, step, values);
        string cue = stack.Steps[step].CueNumberText;
        string Heads(IEnumerable<int> addresses)
        {
            var heads = addresses.Select(_show.HeadAt).Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
            return (heads.Count == 1 ? "head " : "heads ") + string.Join(", ", heads.Take(8)) + (heads.Count > 8 ? ", ..." : "");
        }
        string text;
        if (result.Recorded.Count == 0)
            text = $"Nothing new for cue {cue}: {Heads(result.Tracked)} as in the cue before (tracks)";
        else
        {
            text = $"Recorded {result.Recorded.Count} channel{(result.Recorded.Count == 1 ? "" : "s")} of {Heads(result.Recorded)} into cue {cue}";
            if (result.Palettes.Count > 0)
                text += $" ({result.Palettes.Count} as palette " +
                        string.Join(", ", result.Palettes.Values.Distinct().Select(p => $"\"{p.Name}\"")) + ")";
            if (result.Tracked.Count > 0)
                text += $"; {Heads(result.Tracked)} as in the cue before: not stored, tracks";
        }
        if (result.LaterCues > 0)
            text += result.LaterCues == 1 ? "; the next cue tracks it" : $"; the next {result.LaterCues} cues track it";
        return text + " (not saved yet)";
    }

    /// <summary>
    /// A position palette (MagicQ's record mask allows colour and beam channels in it too: a combined palette); the cue
    /// refers to it with palette id &lt;&lt; 8 | value. Saved with the show.
    /// </summary>
    public string RecordPalette(string name, int cueId, IReadOnlyList<(int Address, int Value)> values)
    {
        var palette = _show.Palettes.FirstOrDefault(p => p.Type == MagicQShow.PaletteType.Position && p.Name == name);
        if (palette == null)
            palette = _show.AddPalette(MagicQShow.PaletteType.Position, name, values);
        else
            _show.RecordPalette(palette, values);
        var cue = _show.Cues.FirstOrDefault(c => c.Id == cueId);
        if (cue != null)
            foreach (var (address, value) in values)
                cue.SetChannel(address, palette.Id << 8 | value);
        return $"Position palette {palette.Entry + 1} \"{name}\" ({palette.Contents(_show)}, {values.Count} channels)" +
               (cue != null ? $", used in cue {cue.Name}" : ", no cue chosen") + " (not saved yet)";
    }

    /// <inheritdoc />
    public int FreePositionPaletteEntry() => _show.FreePaletteEntry(MagicQShow.PaletteType.Position);

    /// <inheritdoc />
    public ICueStep InsertCue(ICueStack stack, float number, string text, bool blackout) =>
        _show.InsertCue((MagicQShow.CueStack)stack, number, text, blackout);

    /// <summary>Sets field 2 (the head name) of every L record of the head.</summary>
    public int RenameHead(int head, string name)
    {
        name = HeadNames.Clean(name);
        int changed = 0;
        foreach (var channel in _show.Patch.Where(p => p.HeadNumber == head))
            if (channel.HeadName != name)
            {
                channel.HeadName = name;
                changed++;
            }
        return changed;
    }

    /// <summary>Sets field 11 (gel) of every L record of the head.</summary>
    public int SetGel(int head, int gelId)
    {
        int changed = 0;
        foreach (var channel in _show.Patch.Where(p => p.HeadNumber == head))
            if (channel.Gel != gelId)
            {
                channel.Gel = gelId;
                changed++;
            }
        return changed;
    }

    /// <summary>Sets field 22 of every L record at the address.</summary>
    public bool SetBeamAngle(int address, int degrees)
    {
        var channels = _show.Patch.Where(p => p.Address == address).ToList();
        foreach (var channel in channels)
            channel.BeamAngle = degrees;
        return channels.Count > 0;
    }
}
