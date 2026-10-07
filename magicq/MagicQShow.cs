using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// A MagicQ show: the lossless .shw document + .xhw archive, with typed views on the records we understand.
// The views read and write the underlying fields, so edits made through them end up in Save().
// Record layouts are reverse engineered from MagicQ 1.9.7 (see CLAUDE.md). No Godot dependencies.
public sealed class MagicQShow
{
    public ShwDocument Shw { get; private set; }
    public XhwArchive Xhw { get; private set; }      // null if the show has no .xhw

    public List<Personality> Personalities { get; } = new();
    public List<PatchChannel> Patch { get; } = new();
    public List<Group> Groups { get; } = new();
    public List<VisItem> Vis { get; } = new();
    public List<CueStack> CueStacks { get; } = new();      // C
    public List<Cue> Cues { get; } = new();                // S
    public List<NamedRecord> Palettes { get; } = new();    // F
    public List<NamedRecord> Effects { get; } = new();     // W

    public static MagicQShow Load(string shwPath)
    {
        var show = new MagicQShow { Shw = ShwDocument.Load(shwPath) };
        string xhw = Path.ChangeExtension(shwPath, ".xhw");
        if (File.Exists(xhw))
            show.Xhw = XhwArchive.Load(xhw);
        show.Index();
        return show;
    }

    // Writes the .shw and (if present) the .xhw next to it. Unchanged content is written byte-identical.
    public void Save(string shwPath)
    {
        Shw.Save(shwPath);
        Xhw?.Save(Path.ChangeExtension(shwPath, ".xhw"));
    }

    public string HeadName(int address) => Patch.FirstOrDefault(p => p.Address == address)?.HeadName ?? "";

    // Heads = patched channels grouped by head number (first channel = DMX start address).
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
                case "F": Palettes.Add(new NamedRecord(r)); break;
                case "W": Effects.Add(new NamedRecord(r)); break;
            }
        }
    }

    // --- Typed views ---

    public sealed record Head(int Number, string Name, int Personality, int Address, int ChannelCount)
    {
        public int Universe => (Address - 1) / 512 + 1;
        public int Channel => (Address - 1) % 512 + 1;
    }

    public abstract class RecordView
    {
        public ShwRecord Record { get; }
        protected RecordView(ShwRecord record) => Record = record;
    }

    // C: cue stack. Header: 1 number, 2 name, 4 step count; a 17-field line; then 7 fields per step:
    // step index, link, cue id (-> S record), ?, flags, cue number (e.g. 8.11), text.
    public sealed class CueStack : RecordView
    {
        private const int FirstStep = 23, StepFields = 7;
        public CueStack(ShwRecord r) : base(r) { }
        public int Number => Record.GetHex(1);
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }

        public IReadOnlyList<Step> Steps
        {
            get
            {
                var steps = new List<Step>();
                int count = Record.GetHex(4);
                for (int i = 0; i < count && FirstStep + (i + 1) * StepFields <= Record.FieldCount; i++)
                {
                    int f = FirstStep + i * StepFields;
                    steps.Add(new Step(Record.GetHex(f), Record.GetHex(f + 2), Record.GetFloat(f + 5), Record.GetString(f + 6)));
                }
                return steps.OrderBy(s => s.CueNumber).ToList();
            }
        }

        public sealed record Step(int Index, int CueId, float CueNumber, string Text)
        {
            public string CueNumberText => CueNumber.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // S: cue (the recorded data of a step). 1 id, 2 name, 3 number of channel/value pairs, then 10 fields, then
    // the pairs: absolute DMX address, value (low byte = DMX level 0..255; upper bits are flags).
    public sealed class Cue : RecordView
    {
        private const int FirstPair = 16;
        public Cue(ShwRecord r) : base(r) { }
        public int Id => Record.GetHex(1);
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }

        // Times: fields 6..10 = In, Out, Pos, Col, Beam in units of 1/33.3 s (99 = 3 s, 999 = 30 s; the record also
        // holds them in ms: 2971, 29971). Rising intensities fade with In, falling with Out.
        public float FadeIn => Record.GetHex(6) * 0.03f;
        public float FadeOut => Record.GetHex(7) * 0.03f;

        public IReadOnlyList<(int Address, int Value)> Channels
        {
            get
            {
                var list = new List<(int, int)>();
                int count = Record.GetHex(3);
                for (int i = 0; i < count && FirstPair + 2 * i + 1 < Record.FieldCount; i++)
                    list.Add((Record.GetHex(FirstPair + 2 * i), Record.GetHex(FirstPair + 2 * i + 1)));
                return list;
            }
        }
    }

    // Any record with a number and a name in fields 1 and 2 (palettes, FX).
    public sealed class NamedRecord : RecordView
    {
        public NamedRecord(ShwRecord r) : base(r) { }
        public int Number => Record.GetHex(1);
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }
    }

    // P: personality (head type).
    public sealed class Personality : RecordView
    {
        public Personality(ShwRecord r) : base(r) { }
        public int Id => Record.GetHex(1);
        public string Name => Record.GetString(2);
        public string Manufacturer => Record.GetString(3);
        public string Mode => Record.GetString(4);
        public string ShortName => Record.GetString(5);
        public int ChannelCount => Record.GetHex(6);

        // Channel list: from field 16, 3 fields per channel: name, flags, attribute (0 = dimmer/intensity).
        public IReadOnlyList<(string Name, int Attribute)> Channels =>
            Enumerable.Range(0, ChannelCount)
                .TakeWhile(i => 18 + 3 * i < Record.FieldCount)
                .Select(i => (Record.GetString(16 + 3 * i), Record.GetHex(18 + 3 * i)))
                .ToList();

        // Offset of the (coarse) intensity channel within the head, or -1.
        public int IntensityChannel
        {
            get
            {
                var channels = Channels;
                for (int i = 0; i < channels.Count; i++)
                    if (channels[i].Attribute == 0)
                        return i;
                return -1;
            }
        }
    }

    // L: one patched DMX channel. Address is absolute and 1-based (513 = universe 2, channel 1).
    public sealed class PatchChannel : RecordView
    {
        public PatchChannel(ShwRecord r) : base(r) { }
        public int Address { get => Record.GetHex(1); set => Record.SetHex(1, value); }
        public string HeadName { get => Record.GetString(2); set => Record.SetString(2, value); }
        public int Attribute => Record.GetHex(3);
        public int Personality => Record.GetHex(4);
        public int ChannelInHead => Record.GetHex(5);
        public int HeadNumber { get => Record.GetHex(10); set => Record.SetHex(10, value); }
        // Gel / colour id from MagicQ's colour table (allcols.csv): 0 none, 2 mix, 1000+n = Lee n, 2000+n = Rosco n.
        public int Gel { get => Record.GetHex(11); set => Record.SetHex(11, value); }
        // Patch > View Heads, generic heads only: MagicVis shape id (0 = default; names not mapped yet) and beam angle
        // in whole degrees (0 = default from the personality).
        // Short L records (some dimmer channels) don't have these fields: read 0, writing is ignored.
        public int Shape { get => Optional(21); set => SetOptional(21, value); }
        public int BeamAngle { get => Optional(22); set => SetOptional(22, value); }
        public int GelOrNone => Optional(11);

        private int Optional(int field) => Record.FieldCount > field ? Record.GetHex(field) : 0;

        private void SetOptional(int field, int value)
        {
            if (Record.FieldCount > field)
                Record.SetHex(field, value);
        }
    }

    // G: group with a list of head numbers.
    public sealed class Group : RecordView
    {
        public Group(ShwRecord r) : base(r) { }
        public int Number => Record.GetHex(1);
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }
        public IReadOnlyList<int> Heads =>
            Enumerable.Range(0, Record.GetHex(3)).Select(i => Record.GetHex(4 + i)).ToList();
    }

    public enum VisKind { Fixture = 1, Object = 2 }

    // H: MagicVis scene item. Position in metres, rotation in degrees; MagicVis axes = ours
    // (+X audience right, +Y up, +Z towards the audience). Fixtures point down at zero rotation, Euler Y-X-Z.
    // Object types: 1 proscenium, 2 truss/pipe, 4 box, 6 person, 7 podium, 8 custom model, 9 border.
    public sealed class VisItem : RecordView
    {
        private const int ValuesField = 13;
        public VisItem(ShwRecord r) : base(r) { }

        public int Id => Record.GetHex(1);
        public VisKind Kind => (VisKind)int.Parse(Record[2], CultureInfo.InvariantCulture);
        public int ObjectType => Record.GetHex(3);
        public int ParentId { get => Record.GetHex(8); set => Record.SetHex(8, value); }

        public float X { get => Get(0); set => Set(0, value); }
        public float Y { get => Get(1); set => Set(1, value); }
        public float Z { get => Get(2); set => Set(2, value); }
        public float RotX { get => Get(3); set => Set(3, value); }
        public float RotY { get => Get(4); set => Set(4, value); }
        public float RotZ { get => Get(5); set => Set(5, value); }
        public float Extra { get => Get(6); set => Set(6, value); }

        // Fixtures only.
        public int Address { get => Record.GetHex(20); set => Record.SetHex(20, value); }
        public int HeadNumber { get => Record.GetHex(21); set => Record.SetHex(21, value); }
        public int Personality => Record.GetHex(22);

        // Objects only. ObjectCode = MagicVis object variant (e.g. 20001 truss, 30005 box, 60001/60003 people).
        public string ObjectCode => Record[20];
        public float SizeA { get => Record.GetFloat(21); set => Record.SetFloat(21, value); }
        public float SizeB { get => Record.GetFloat(22); set => Record.SetFloat(22, value); }
        public float SizeC { get => Record.GetFloat(23); set => Record.SetFloat(23, value); }
        public string Name { get => Record.GetString(24); set => Record.SetString(24, value); }
        public string Model => Record.FieldCount > 26 ? Record.GetString(26) : "";

        private float Get(int i) => DecodeNumber(Record.GetString(ValuesField + i)) ?? 0f;
        private void Set(int i, float v) => Record.SetString(ValuesField + i, EncodeNumber(v));
    }

    // --- MagicVis number obfuscation ---
    // Numbers are "%.2f" text. Per character i: x = (c XOR 0x64) - (3 - i), with c reduced by 0x20 when >= 0x60.
    // x = '0'..'9' is the 9-complement of the digit, 0x3b = '.', 0x3c = '-'.

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
