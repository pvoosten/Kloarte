using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Changes to a native show made from the application (<see cref="IShowEditor"/>): the patch with the plan's rules (the
/// same as on a console show: join another head, take its own head along, or get a head of its own; conflicts refused
/// with a <see cref="ShowEditException"/>), new lamps of the catalogue's fixture types, gels, names and beam angles per
/// head, and cue and palette values from DMX values. Every change calls <see cref="DesignShow.Changed"/>.
/// </summary>
public sealed class DesignShowEditor : IShowEditor
{
    /// <summary>The show being edited.</summary>
    private readonly DesignShow _show;

    /// <summary>The editor of a show.</summary>
    public DesignShowEditor(DesignShow show) => _show = show;

    /// <summary>The head with this number, or null.</summary>
    private DesignHead Head(int number) => _show.Head(number);

    /// <summary>The lamps on a head.</summary>
    private List<DesignItem> Lamps(int head) => _show.Rig.Items.Where(i => i.IsLamp && i.Head == head).ToList();

    /// <inheritdoc />
    public int ChannelCount(int head) => Head(head)?.Channels ?? 0;

    /// <inheritdoc />
    public int LampCount(int head) => Lamps(head).Count;

    /// <summary>A head number nobody uses yet (highest + 1).</summary>
    public int FreeHeadNumber() => _show.Rig.Heads.Select(h => h.Number).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The first address after the highest patched channel with room for `count` channels in one universe.</summary>
    public int FreeAddress(int count)
    {
        int a = _show.Rig.Heads.Select(h => h.Address + h.Channels).DefaultIfEmpty(1).Max();
        if ((a - 1) % 512 + count > 512)
            a = ((a - 1) / 512 + 1) * 512 + 1;
        return a;
    }

    /// <summary>Refuses an address range that is out of range or used by another head.</summary>
    private void CheckFree(int address, int count, int? ignoreHead)
    {
        if (address < 1 || address + count - 1 > DmxAddress.Max)
            throw new ShowEditException($"DMX address {DmxAddress.Format(address)} is out of range");
        foreach (var head in _show.Rig.Heads.Where(h => h.Number != ignoreHead))
            if (head.Address < address + count && address < head.Address + head.Channels)
                throw new ShowEditException($"DMX {DmxAddress.Format(Math.Max(address, head.Address))} is used by head {head.Number} ({head.Name})");
    }

    /// <summary>
    /// Sets the head number and DMX address of a lamp, with the plan's rules: an address or number of another head = the
    /// lamp joins it (same fixture type); the only lamp on its head moves / renumbers the head (cue and palette values
    /// follow, they are by head); a lamp sharing its head and given another address gets a head of its own.
    /// </summary>
    public string ApplyLampPatch(IRigItem lamp, int head, int address)
    {
        var item = (DesignItem)lamp;
        int h0 = item.Head ?? 0, a0 = item.Address ?? 0;
        if (head == h0 && address == a0)
            return "No change";
        var atAddress = address != a0 ? _show.HeadCovering(address) : null;
        string result;
        if (atAddress != null && atAddress.Number != h0)
        {
            if (head != h0 && head != atAddress.Number)
                throw new ShowEditException($"DMX {DmxAddress.Format(address)} is used by head {atAddress.Number}");
            Join(item, atAddress);
            result = $"Joined head {atAddress.Number} at DMX {DmxAddress.Format(atAddress.Address)}";
        }
        else if (head != h0 && Head(head) is { } other)
        {
            if (address != a0 && address != other.Address)
                throw new ShowEditException($"Head {head} is at DMX {DmxAddress.Format(other.Address)}");
            Join(item, other);
            result = $"Joined head {head} at DMX {DmxAddress.Format(other.Address)}";
        }
        else if (Lamps(h0).Count <= 1 && Head(h0) is { } own)
        {
            var done = new List<string>();
            if (address != a0)
            {
                CheckFree(address, own.Channels, own.Number);
                own.Address = address;
                item.Address = address;
                done.Add($"moved to DMX {DmxAddress.Format(address)}");
            }
            if (head != h0)
            {
                Renumber(h0, head);
                done.Add($"renumbered to {head}");
            }
            result = $"Head {h0} " + string.Join(" and ", done);
        }
        else
        {
            if (address == a0)
                throw new ShowEditException($"Head {h0} has {Lamps(h0).Count} lamps on DMX {DmxAddress.Format(a0)}: give this lamp another " +
                                            "DMX address to give it a head of its own, or type the number of an existing head to join it");
            int number = head != h0 ? head : FreeHeadNumber();
            NewHead(item, number, address, null);
            result = $"New head {number} at DMX {DmxAddress.Format(address)}";
        }
        _show.Changed();
        return result;
    }

    /// <summary>A lamp joins a head (same fixture type).</summary>
    private void Join(DesignItem lamp, DesignHead head)
    {
        var own = lamp.Head is { } h ? Head(h) : null;
        if (own != null && own.Type != head.Type)
            throw new ShowEditException($"Head {head.Number} is a different fixture type");
        lamp.Head = head.Number;
        lamp.Address = head.Address;
    }

    /// <summary>Renumbers a head: patch, lamps, groups, and the cues' and palettes' values of it.</summary>
    private void Renumber(int from, int to)
    {
        if (to <= 0)
            throw new ShowEditException("Head numbers start at 1");
        if (Head(to) != null)
            throw new ShowEditException($"Head number {to} is already used; put the lamps on that head instead");
        Head(from).Number = to;
        foreach (var lamp in Lamps(from))
            lamp.Head = to;
        foreach (var group in _show.Rig.Groups)
            group.Heads = group.Heads.Select(h => h == from ? to : h).ToArray();
        string a = DesignShow.Key(from), b = DesignShow.Key(to);
        foreach (var looks in _show.CueOrder.Select(c => c.Heads).Concat(_show.Palettes.Select(p => p.Heads)))
            if (looks.Remove(a, out var values))
                looks[b] = values;
    }

    /// <summary>A new head for a lamp, patched like its current head (type, channel count; `name` or the old head's name).</summary>
    private void NewHead(DesignItem lamp, int number, int address, string name)
    {
        if (Head(number) != null)
            throw new ShowEditException($"Head number {number} is already used; choose 'join head' to put the lamp on that head");
        var template = lamp.Head is { } h ? Head(h) : null;
        string type = template?.Type ?? lamp.Type ?? DesignTypes.GenericName;
        int channels = template?.Channels ?? _show.Type(type)?.Channels.Count ?? 1;
        CheckFree(address, channels, null);
        _show.Rig.Heads.Add(new DesignHead
        {
            Number = number, Name = name ?? template?.Name ?? "", Type = type, Address = address, Channels = channels,
            Gel = name == null ? template?.Gel : null, Beam = name == null ? template?.Beam : null, Shape = template?.Shape,
        });
        lamp.Head = number;
        lamp.Address = address;
    }

    /// <summary>Gives a new lamp (still on its template's head) a head of its own: the next free number and address, open white.</summary>
    public string AddLampHead(IRigItem lamp, string headName)
    {
        var item = (DesignItem)lamp;
        int count = Math.Max(1, item.Head is { } h ? Head(h)?.Channels ?? 1 : 1);
        int number = FreeHeadNumber(), address = FreeAddress(count);
        NewHead(item, number, address, HeadNames.Clean(headName));
        _show.Changed();
        return $"Head {number} at DMX {DmxAddress.Format(address)}";
    }

    /// <summary>Removes a head's patch when no lamp, group, cue or palette uses it; true when removed.</summary>
    public bool RemoveUnusedHead(int head)
    {
        var patch = Head(head);
        string key = DesignShow.Key(head);
        if (patch == null || Lamps(head).Count > 0 || _show.Rig.Groups.Any(g => g.Heads.Contains(head))
            || _show.CueOrder.Any(c => c.Heads.ContainsKey(key)) || _show.Palettes.Any(p => p.Heads.ContainsKey(key)))
            return false;
        _show.Rig.Heads.Remove(patch);
        _show.Changed();
        return true;
    }

    /// <summary>
    /// Adds a lamp of a fixture type on a head of its own (the next free number and address): the fixture type is the
    /// show's, else the catalogue's (added to the show). The lamp hangs on nothing; the caller places it.
    /// </summary>
    public IRigItem ImportLamp(string personality, string headName, string typeName)
    {
        var type = _show.Type(personality);
        if (type == null)
        {
            type = DesignTypes.Catalogue.FirstOrDefault(t => t.Name == personality) is { } known ? DesignTypes.Copy(known)
                : personality == DesignTypes.GenericName ? DesignTypes.Generic()
                : throw new ShowEditException($"There is no fixture type '{personality}' for a {typeName}");
            _show.Rig.Types.Add(type);
        }
        int channels = Math.Max(1, type.Channels.Count);
        int number = FreeHeadNumber(), address = FreeAddress(channels);
        _show.Rig.Heads.Add(new DesignHead { Number = number, Name = HeadNames.Clean(headName), Type = type.Name, Address = address, Channels = channels });
        var lamp = new DesignItem { Id = _show.NextItemId, Address = address, Head = number, Type = type.Name, At = new double[] { 0, 6, 0 } };
        _show.Rig.Items.Add(lamp);
        _show.Changed();
        return lamp;
    }

    /// <summary>Sets the beam angle of the head with a channel at the address; false when nothing is patched there.</summary>
    public bool SetBeamAngle(int address, int degrees)
    {
        var head = _show.HeadCovering(address);
        if (head == null)
            return false;
        head.Beam = degrees;
        _show.Changed();
        return true;
    }

    /// <summary>Names a head (made fit for the console). Returns 1 when it changed, else 0.</summary>
    public int RenameHead(int head, string name)
    {
        var patch = Head(head);
        name = HeadNames.Clean(name);
        if (patch == null || patch.Name == name)
            return 0;
        patch.Name = name;
        _show.Changed();
        return 1;
    }

    /// <summary>Sets the gel of a head (0 = none). Returns 1 when it changed, else 0.</summary>
    public int SetGel(int head, int gelId)
    {
        var patch = Head(head);
        int? gel = gelId > 0 ? gelId : null;
        if (patch == null || patch.Gel == gel)
            return 0;
        patch.Gel = gel;
        _show.Changed();
        return 1;
    }

    /// <summary>The cue of a step of the cue list.</summary>
    private DesignCue CueOf(ICueStack stack, int step)
    {
        var steps = stack.Steps;
        if (step < 0 || step >= steps.Count)
            throw new ArgumentOutOfRangeException(nameof(step), "no such step in the cue list");
        return _show.CueById(steps[step].CueId) ?? throw new InvalidOperationException($"no cue {steps[step].CueNumberText}");
    }

    /// <summary>Changes a channel's level in a cue by `delta` from its tracked level (clamped) and sets it in that cue.</summary>
    public int NudgeLevel(ICueStack stack, int step, int address, int delta)
    {
        var cue = CueOf(stack, step);
        int level = Math.Clamp(CueTracking.LevelsAfter(_show, stack, step).GetValueOrDefault(address) + delta, 0, 255);
        cue.Dmx ??= new Dictionary<string, int>();
        DesignEdits.SetDmx(_show, cue.Heads, cue.Dmx, address, level);
        if (cue.Dmx.Count == 0)
            cue.Dmx = null;
        _show.Changed();
        return level;
    }

    /// <summary>
    /// Sets DMX values in a cue (tracking-aware: a value equal to the state before the cue is taken out of the cue, so it
    /// tracks). Returns a status message.
    /// </summary>
    public string RecordIntoCue(ICueStack stack, int step, IReadOnlyList<(int Address, int Value)> values)
    {
        var cue = CueOf(stack, step);
        var before = CueTracking.LevelsAfter(_show, stack, step - 1);
        int recorded = 0, tracked = 0;
        cue.Dmx ??= new Dictionary<string, int>();
        foreach (var (address, value) in values)
        {
            if (before.GetValueOrDefault(address, -1) == value && _show.HeadCovering(address) is { } head)
            {
                var channel = _show.LayoutOf(head).AtOffset(address - head.Address + 1);
                if (channel != null)
                    DesignEdits.Set(cue.Heads, head.Number, channel.Offsets.Length == 1 ? channel.Key : "#" + (address - head.Address + 1), null);
                tracked++;
                continue;
            }
            DesignEdits.SetDmx(_show, cue.Heads, cue.Dmx, address, value);
            recorded++;
        }
        if (cue.Dmx.Count == 0)
            cue.Dmx = null;
        _show.Changed();
        return $"Recorded {recorded} channel{(recorded == 1 ? "" : "s")} into cue {cue.Cue}" +
               (tracked > 0 ? $"; {tracked} as in the cue before: not stored, tracks" : "") + " (not saved yet)";
    }

    /// <summary>
    /// Records DMX values into a palette of that name (new, or merged into it) and makes the cue (if any) refer to it for
    /// those channels. Returns a status message.
    /// </summary>
    public string RecordPalette(string name, int cueId, IReadOnlyList<(int Address, int Value)> values)
    {
        var palette = _show.Palette(name);
        if (palette == null)
            _show.Palettes.Add(palette = new DesignPalette { Name = name });
        foreach (var (address, value) in values)
            DesignEdits.SetDmx(_show, palette.Heads, null, address, value);
        var cue = _show.CueById(cueId);
        if (cue != null)
            foreach (var (head, keys) in palette.Heads)
                foreach (string key in keys.Keys)
                    if (DesignShow.HeadOfKey(head) is { } number && values.Any(v => _show.HeadCovering(v.Address)?.Number == number))
                        DesignEdits.Set(cue.Heads, number, key, LookValue.OfPalette(name));
        _show.Changed();
        return $"Palette \"{name}\" ({values.Count} channels)" + (cue != null ? $", used in cue {cue.Cue}" : ", no cue chosen") + " (not saved yet)";
    }

    /// <summary>The number of palettes (the next palette's entry).</summary>
    public int FreePositionPaletteEntry() => _show.Palettes.Count;

    /// <summary>Inserts a cue at a number: a blackout (every intensity at 0) or a cue without changes. Returns its step.</summary>
    public ICueStep InsertCue(ICueStack stack, float number, string text, bool blackout)
    {
        if (_show.CueOrder.Any(c => CueNumbers.Same(c.Number, number)))
            throw new ArgumentException($"cue {CueNumbers.Text(number)} already exists");
        var cue = new DesignCue
        {
            Cue = CueNumbers.Text(number), Id = _show.NextCueId, Text = string.IsNullOrEmpty(text) ? null : text,
            Heads = blackout ? DesignEdits.Blackout(_show) : new(),
        };
        _show.CueOrder.Add(cue);
        _show.Changed();
        return ((IShowDocument)_show).CueStacks[0].Steps.First(s => s.CueId == cue.Id);
    }

    /// <summary>Text of an id for messages.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
