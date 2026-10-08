using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// A MagicQ show: the lossless .shw document + .xhw archive, with typed views on the records we understand.
/// The views read and write the underlying fields, so edits made through them end up in Save().
/// Record layouts are reverse engineered from MagicQ 1.9.7 (see CLAUDE.md). No Godot dependencies.
/// </summary>
public sealed partial class MagicQShow
{
    /// <summary>The lossless .shw document.</summary>
    public ShwDocument Shw { get; private set; }
    /// <summary>null if the show has no .xhw</summary>
    public XhwArchive Xhw { get; private set; }

    /// <summary>P records.</summary>
    public List<Personality> Personalities { get; } = new();
    /// <summary>L records (one per patched DMX channel).</summary>
    public List<PatchChannel> Patch { get; } = new();
    /// <summary>G records.</summary>
    public List<Group> Groups { get; } = new();
    /// <summary>H records (MagicVis scene).</summary>
    public List<VisItem> Vis { get; } = new();
    /// <summary>C</summary>
    public List<CueStack> CueStacks { get; } = new();
    /// <summary>S</summary>
    public List<Cue> Cues { get; } = new();
    /// <summary>F</summary>
    public List<Palette> Palettes { get; } = new();
    /// <summary>W</summary>
    private List<NamedRecord> Effects { get; } = new();

    /// <summary>
    /// Reads a .shw and the .xhw next to it. MagicQ's lastshow.shw has no .xhw of its own: then the .xhw of the show
    /// it is a copy of (header "Show saved as") is read.
    /// </summary>
    public static MagicQShow Load(string shwPath)
    {
        var show = new MagicQShow { Shw = ShwDocument.Load(shwPath) };
        string xhw = Path.ChangeExtension(shwPath, ".xhw");
        if (!File.Exists(xhw) && SavedAs(shwPath) is { } original)
            xhw = Path.ChangeExtension(original, ".xhw");
        if (File.Exists(xhw))
            show.Xhw = XhwArchive.Load(xhw);
        show.Index();
        return show;
    }

    /// <summary>
    /// Writes the .shw and (if present) the .xhw next to it. Unchanged content is written byte-identical.
    /// </summary>
    public void Save(string shwPath)
    {
        Shw.Save(shwPath);
        Xhw?.Save(Path.ChangeExtension(shwPath, ".xhw"));
    }

    /// <summary>
    /// The path in the file's header comment "\ Show saved as '...'" (where MagicQ saved it; for lastshow.shw the show
    /// that was loaded, usually its .sbk autosave), or null.
    /// </summary>
    public static string SavedAs(string shwPath)
    {
        using var reader = new StreamReader(shwPath, Encoding.Latin1);
        for (int i = 0; i < 6 && reader.ReadLine() is { } line; i++)
        {
            const string marker = "Show saved as '";
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            int end = line.LastIndexOf('\'');
            if (line.StartsWith('\\') && start >= 0 && end > start + marker.Length)
                return line[(start + marker.Length)..end];
        }
        return null;
    }

    /// <summary>Head name of the channel at an address ("" if not patched).</summary>
    public string HeadName(int address) => Patch.FirstOrDefault(p => p.Address == address)?.HeadName ?? "";

    /// <summary>Heads = patched channels grouped by head number (first channel = DMX start address).</summary>
    public IEnumerable<Head> Heads =>
        Patch.GroupBy(p => (p.HeadNumber, p.HeadName))
             .Select(g => new Head(g.Key.HeadNumber, g.Key.HeadName, g.First().Personality,
                                   g.Min(p => p.Address), g.Count()));

    private void Index()
    {
        foreach (var r in Shw.Records)
        {
            if (r.FieldCount < 3)
                continue;
            switch (r.Type)
            {
                case "P": Personalities.Add(new Personality(r)); break;
                case "L": Patch.Add(new PatchChannel(r)); break;
                case "G": Groups.Add(new Group(r)); break;
                case "H": Vis.Add(new VisItem(r)); break;
                case "C": CueStacks.Add(new CueStack(r)); break;
                case "S": Cues.Add(new Cue(r)); break;
                case "F": Palettes.Add(new Palette(r)); break;
                case "U": AddCurve(r); break;
                case "W": Effects.Add(new NamedRecord(r)); break;
            }
        }
    }

    // --- Dimmer curves ---

    /// <summary>User curves by U record id: 16-bit output for inputs 0..254 (input 255 = full).</summary>
    private readonly Dictionary<int, int[]> _curves = new();

    /// <summary>
    /// U record (Curve Editor; user curve n = U n): 1 id, 2 name, 6 table length (00ff), then the sampled table
    /// (16-bit, inputs 0..254), then the curve points (count, x / y pairs, spline flags).
    /// </summary>
    private void AddCurve(ShwRecord r)
    {
        if (r.FieldCount < 8)
            return;
        int length = r.GetHex(6);
        if (r.FieldCount < 7 + length)
            return;
        _curves[r.GetHex(1)] = Enumerable.Range(7, length).Select(r.GetHex).ToArray();
        _curveNames[r.GetHex(1)] = r.GetString(2);
    }

    /// <summary>User curve names by U record id.</summary>
    private readonly Dictionary<int, string> _curveNames = new();

    /// <summary>The user curves (U records): id, name and the 16-bit output for inputs 0..254.</summary>
    public IReadOnlyList<(int Id, string Name, int[] Table)> UserCurves =>
        _curves.OrderBy(c => c.Key).Select(c => (c.Key, _curveNames.GetValueOrDefault(c.Key, ""), c.Value)).ToList();

    /// <summary>
    /// What MagicQ outputs for channel values: user dimmer curves of the patch applied (other channels unchanged).
    /// Returns the same dictionary when no patched channel has a user curve.
    /// </summary>
    public Dictionary<int, int> WithCurves(Dictionary<int, int> dmx)
    {
        Dictionary<int, int> result = null;
        foreach (var ch in Patch)
        {
            if (ch.Curve < 4 || !_curves.TryGetValue(ch.Curve - 3, out var table) || !dmx.TryGetValue(ch.Address, out int value))
                continue;
            result ??= new Dictionary<int, int>(dmx);
            result[ch.Address] = value >= table.Length ? 255 : table[Math.Clamp(value, 0, 255)] >> 8;
        }
        return result ?? dmx;
    }

    // --- Palettes ---

    /// <summary>
    /// DMX value of a cue channel: the low byte, or - when the value refers to a palette (upper bits = palette id) -
    /// the palette's current value for that address (following one level of linking). The low byte in the cue is
    /// MagicQ's cached copy of the palette value.
    /// </summary>
    public int Resolve(int address, int rawValue)
    {
        int paletteId = rawValue >> 8;
        if (paletteId == 0)
            return rawValue & 0xff;
        var palette = FindPalette(paletteId);
        if (palette != null && palette.TryGetValue(address, out int value, out int link))
        {
            if (link != 0 && FindPalette(link) is { } linked && linked.TryGetValue(address, out int v2, out _))
                return v2;
            return value;
        }
        return rawValue & 0xff;
    }

    /// <summary>The palette with this id, or null.</summary>
    public Palette FindPalette(int id) => Palettes.FirstOrDefault(p => p.Id == id);

    /// <summary>Next free entry of a palette type (0..999).</summary>
    public int FreePaletteEntry(PaletteType type)
    {
        var used = Palettes.Where(p => p.Type == type).Select(p => p.Entry).ToHashSet();
        int entry = 0;
        while (used.Contains(entry))
            entry++;
        return entry;
    }

    /// <summary>
    /// Creates a palette of the given type (MagicQ's "record into an empty palette entry"). The record layout is copied
    /// from an existing palette of the show (preferably of the same type): the fields after the values are not fully
    /// understood, see CLAUDE.md. Values: (address, DMX value).
    /// </summary>
    public Palette AddPalette(PaletteType type, string name, IEnumerable<(int Address, int Value)> values)
    {
        // The simplest palette of that type (shortest record after its values) is the template.
        var template = Palettes.Where(p => p.Type == type).OrderBy(p => p.Record.FieldCount - 3 * p.Count).FirstOrDefault()
                       ?? Palettes.OrderBy(p => p.Record.FieldCount - 3 * p.Count).FirstOrDefault()
                       ?? throw new InvalidOperationException("the show has no palette to copy the record layout from");
        var after = Palettes.Last().Record;
        int id = ((int)type << 12) | FreePaletteEntry(type);
        var record = Shw.InsertRecordAfter(after, template.Record.CopyLines());
        var palette = new Palette(record);
        palette.Record.RemoveFields(Palette.FirstValue, 3 * palette.Count);
        palette.Record.SetHex(3, 0);
        palette.Record[1] = id.ToString("x4", CultureInfo.InvariantCulture);
        palette.Name = name;
        palette.Merge(values);
        Palettes.Add(palette);
        return palette;
    }

    /// <summary>
    /// Creates a cue (S record) with these channels (address, raw value). The record layout is copied from the
    /// simplest cue of the show (fewest fields besides its channels; the fields after the channels are not fully
    /// understood, see CLAUDE.md), including its fade times; the id is the next free one.
    /// </summary>
    public Cue AddCue(string name, IEnumerable<(int Address, int Value)> channels)
    {
        var template = Cues.OrderBy(c => c.Record.FieldCount - 2 * c.Count).FirstOrDefault()
                       ?? throw new InvalidOperationException("the show has no cue to copy the record layout from");
        int id = Cues.Max(c => c.Id) + 1;
        var record = Shw.InsertRecordAfter(Cues.Last().Record, template.Record.CopyLines());
        var cue = new Cue(record);
        foreach (var (address, _) in cue.Channels.ToList())
            cue.RemoveChannel(address);
        cue.Record[1] = id.ToString("x4", CultureInfo.InvariantCulture);
        cue.Name = name;
        foreach (var (address, value) in channels.OrderBy(c => c.Address))
            cue.SetChannel(address, value);
        Cues.Add(cue);
        return cue;
    }

    /// <summary>
    /// Inserts a new cue into a cue stack as cue `number`: a blackout (every intensity channel of the patch at 0; with
    /// tracking, everything else stays) or a cue without changes (no channels: the previous look tracks through, e.g.
    /// for actor movement on its own GO). Returns the new step.
    /// </summary>
    public CueStack.Step InsertCue(CueStack stack, float number, string text, bool blackout)
    {
        if (stack.HasNumber(number))
            throw new ArgumentException($"cue {number.ToString("0.###", CultureInfo.InvariantCulture)} already exists in this stack");
        var channels = blackout
            ? Patch.Where(p => AttributeClass(this, p) == 'I').Select(p => p.Address).Distinct().Select(a => (a, 0))
            : Enumerable.Empty<(int, int)>();
        var cue = AddCue(text, channels);
        return stack.InsertStep(cue.Id, number, text);
    }

    /// <summary>
    /// Channel levels (address -&gt; 0..255) after steps 0..upTo of a cue stack: MagicQ tracks, so every cue up to that
    /// step counts; palette references resolved; before the dimmer curves (<see cref="WithCurves"/>).
    /// </summary>
    public Dictionary<int, int> LevelsAfter(CueStack stack, int upTo) => CueTracking.LevelsAfter(this, stack, upTo);

    /// <summary>
    /// Changes a channel's level in the cue of step `step` by `delta` from its tracked level at that step (clamped to
    /// 0..255) and records it in that cue as a hard value (a palette reference is replaced). Later cues that don't
    /// record the channel track the new level. Returns the new level.
    /// </summary>
    public int NudgeLevel(CueStack stack, int step, int address, int delta)
    {
        var steps = stack.Steps;
        if (step < 0 || step >= steps.Count)
            throw new ArgumentOutOfRangeException(nameof(step), "no such step in this cue stack");
        var cue = Cues.FirstOrDefault(c => c.Id == steps[step].CueId)
                  ?? throw new InvalidOperationException($"cue {steps[step].CueNumberText} has no cue record");
        int level = Math.Clamp(LevelsAfter(stack, step).GetValueOrDefault(address) + delta, 0, 255);
        cue.SetChannel(address, level);
        return level;
    }

    /// <summary>
    /// Record merge of programmer values into the cue of step `step` (MagicQ: hold S + REC), tracking-aware: a value
    /// equal to the state before the cue (the earlier cues, tracked) is not stored, and removed from the cue when it had
    /// one, so the channel tracks; any other value is stored, as a reference to an intensity palette holding exactly
    /// that value when there is one (the one the cue already used first), else as a hard value. Later cues that don't
    /// record a channel track the new level. Pairs stay in address order with the count updated.
    /// </summary>
    public CueRecording RecordIntoCue(CueStack stack, int step, IEnumerable<(int Address, int Value)> values)
    {
        var steps = stack.Steps;
        if (step < 0 || step >= steps.Count)
            throw new ArgumentOutOfRangeException(nameof(step), "no such step in this cue stack");
        var cue = Cues.FirstOrDefault(c => c.Id == steps[step].CueId)
                  ?? throw new InvalidOperationException($"cue {steps[step].CueNumberText} has no cue record");
        var before = LevelsAfter(stack, step - 1);
        var used = cue.Channels.ToDictionary(c => c.Address, c => c.Value >> 8);
        var intensityPalettes = Palettes.Where(p => p.Type == PaletteType.Intensity).OrderBy(p => p.Entry).ToList();
        var recorded = new List<int>();
        var tracked = new List<int>();
        var palettes = new Dictionary<int, Palette>();
        foreach (var (address, value) in values.OrderBy(v => v.Address))
        {
            if (before.GetValueOrDefault(address) == value)
            {
                cue.RemoveChannel(address);
                tracked.Add(address);
                continue;
            }
            bool Holds(Palette p) => p.TryGetValue(address, out int v, out int link) && v == value && link == 0;
            var palette = intensityPalettes.FirstOrDefault(p => p.Id == used.GetValueOrDefault(address) && Holds(p))
                          ?? intensityPalettes.FirstOrDefault(Holds);
            cue.SetChannel(address, palette == null ? value : palette.Id << 8 | value);
            recorded.Add(address);
            if (palette != null)
                palettes[address] = palette;
        }
        // Later steps that track a changed channel: until a cue records it again.
        var cues = Cues.ToDictionary(c => c.Id);
        var open = recorded.Concat(tracked).ToHashSet();
        int later = 0;
        for (int i = step + 1; i < steps.Count && open.Count > 0; i++)
        {
            if (cues.TryGetValue(steps[i].CueId, out var next))
                open.ExceptWith(next.Channels.Select(c => c.Address));
            if (open.Count > 0)
                later++;
        }
        return new CueRecording(recorded, palettes, tracked, later);
    }

    /// <summary>
    /// Re-recording a palette (merge, as MagicQ does for the selected heads) also refreshes the cached low byte in every
    /// cue that uses it.
    /// </summary>
    public void RecordPalette(Palette palette, IEnumerable<(int Address, int Value)> values)
    {
        palette.Merge(values);
        foreach (var cue in Cues)
            foreach (var (address, raw) in cue.Channels.ToList())
                if (raw >> 8 == palette.Id && palette.TryGetValue(address, out int v, out _))
                    cue.SetChannel(address, (palette.Id << 8) | v);
    }

    // --- Typed views ---

    /// <summary>
    /// MagicQ attribute class of a patched channel: I (intensity), P (position), C (colour), B (beam), from the
    /// personality's channel name (the attribute numbers are not decoded).
    /// </summary>
    public static char AttributeClass(MagicQShow show, PatchChannel channel)
    {
        var personality = show.Personalities.FirstOrDefault(p => p.Id == channel.Personality);
        var channels = personality?.Channels;
        int offset = channel.ChannelInHead;
        if (channels == null || offset >= channels.Count)
            return 'I';
        var (name, attribute) = channels[offset];
        if (attribute == 0)
            return 'I';
        string n = name.ToLowerInvariant();
        if (n.Contains("pan") || n.Contains("tilt"))
            return 'P';
        if (n.Contains("cyan") || n.Contains("magenta") || n.Contains("yellow") || n.Contains("cto") || n.Contains("col")
            || n.Contains("red") || n.Contains("green") || n.Contains("blue") || n.Contains("white") || n.Contains("amber")
            || n.Contains("lime") || n.Contains("cct") || n.Contains("tint"))
            return 'C';
        return 'B';
    }

    // --- MagicVis number obfuscation ---
    // Numbers are "%.2f" text. Per character i: x = (c XOR 0x64) - (3 - i), with c reduced by 0x20 when >= 0x60.
    // x = '0'..'9' is the 9-complement of the digit, 0x3b = '.', 0x3c = '-'.

    /// <summary>Decodes an obfuscated MagicVis number; null if it isn't one.</summary>
    public static float? DecodeNumber(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            int c = s[i];
            if (c >= 0x60)
                c -= 0x20;
            int x = (c ^ 0x64) - (3 - i);
            if (x == 0x3b) sb.Append('.');
            else if (x == 0x3c) sb.Append('-');
            else if (x is >= 0x30 and <= 0x39) sb.Append((char)('0' + 9 - (x - 0x30)));
            else return null;
        }
        return float.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;
    }

    /// <summary>Encodes a MagicVis number ("%.2f", obfuscated).</summary>
    public static string EncodeNumber(float value)
    {
        string plain = value.ToString("F2", CultureInfo.InvariantCulture);
        var sb = new StringBuilder(plain.Length);
        for (int i = 0; i < plain.Length; i++)
        {
            char p = plain[i];
            int x = p switch
            {
                '.' => 0x3b,
                '-' => 0x3c,
                _ => 0x30 + 9 - (p - '0'),
            };
            int c = (x + (3 - i)) ^ 0x64;
            if (c < 0x50)
                c += 0x20;
            sb.Append((char)c);
        }
        return sb.ToString();
    }
}
