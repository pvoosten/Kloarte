using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kloarte.Model;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>
    /// H: MagicVis scene item. Position in metres, rotation in degrees; MagicVis axes = ours
    /// (+X audience right, +Y up, +Z towards the audience). Fixtures point down at zero rotation, Euler Y-X-Z.
    /// Object types: 1 proscenium, 2 truss/pipe, 4 box, 6 person, 7 podium, 8 custom model, 9 border.
    /// </summary>
    public sealed class VisItem : RecordView, IRigItem
    {
        private const int ValuesField = 13;
        /// <summary>Creates the view on an H record.</summary>
        public VisItem(ShwRecord r) : base(r) { }
        /// <inheritdoc />
        bool IRigItem.IsFixture => Kind == VisKind.Fixture;
        /// <inheritdoc />
        RigObjectKind IRigItem.ObjectKind => Kind == VisKind.Fixture ? RigObjectKind.Fixture : (RigObjectKind)ObjectType;
        /// <inheritdoc />
        string IRigItem.ModelName => Model;

        /// <summary>MagicVis id.</summary>
        public int Id => Record.GetHex(1);
        /// <summary>Fixture or object.</summary>
        public VisKind Kind => (VisKind)int.Parse(Record[2], CultureInfo.InvariantCulture);
        /// <summary>Object type (1 proscenium, 2 truss, 4 box, 6 person, 7 podium, 8 model, 9 border).</summary>
        public int ObjectType => Record.GetHex(3);
        /// <summary>Object this item hangs on (0 = none).</summary>
        public int ParentId { get => Record.GetHex(8); set => Record.SetHex(8, value); }

        /// <summary>Position x (m).</summary>
        public float X { get => Get(0); set => Set(0, value); }
        /// <summary>Position y (m).</summary>
        public float Y { get => Get(1); set => Set(1, value); }
        /// <summary>Position z (m).</summary>
        public float Z { get => Get(2); set => Set(2, value); }
        /// <summary>Rotation about x (degrees; tilt).</summary>
        public float RotX { get => Get(3); set => Set(3, value); }
        /// <summary>Rotation about y (degrees; pan).</summary>
        public float RotY { get => Get(4); set => Set(4, value); }
        /// <summary>Rotation about z (degrees; roll).</summary>
        public float RotZ { get => Get(5); set => Set(5, value); }
        /// <summary>Seventh MagicVis number (meaning unknown).</summary>
        public float Extra { get => Get(6); set => Set(6, value); }

        /// <summary>Fixtures only.</summary>
        public int Address { get => Record.GetHex(20); set => Record.SetHex(20, value); }
        /// <summary>Head number (fixtures).</summary>
        public int HeadNumber { get => Record.GetHex(21); set => Record.SetHex(21, value); }
        /// <summary>Personality (fixtures).</summary>
        public int Personality => Record.GetHex(22);

        /// <summary>
        /// Objects only. ObjectCode = MagicVis object variant (e.g. 20001 truss, 30005 box, 60001/60003 people).
        /// </summary>
        public string ObjectCode => Record[20];
        /// <summary>Size A (objects; truss length, box depth, proscenium width).</summary>
        public float SizeA { get => Record.GetFloat(21); set => Record.SetFloat(21, value); }
        /// <summary>Size B (objects).</summary>
        public float SizeB { get => Record.GetFloat(22); set => Record.SetFloat(22, value); }
        /// <summary>Size C (objects).</summary>
        public float SizeC { get => Record.GetFloat(23); set => Record.SetFloat(23, value); }
        /// <summary>Object name.</summary>
        public string Name { get => Record.GetString(24); set => Record.SetString(24, value); }
        /// <summary>Custom model file (objects of type 8).</summary>
        public string Model => Record.FieldCount > 26 ? Record.GetString(26) : "";

        private float Get(int i) => DecodeNumber(Record.GetString(ValuesField + i)) ?? 0f;
        private void Set(int i, float v) => Record.SetString(ValuesField + i, EncodeNumber(v));
    }
}
