using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>L: one patched DMX channel. Address is absolute and 1-based (513 = universe 2, channel 1).</summary>
    public sealed class PatchChannel : RecordView
    {
        /// <summary>Creates the view on an L record.</summary>
        public PatchChannel(ShwRecord r) : base(r) { }
        /// <summary>Absolute DMX address (1-based).</summary>
        public int Address { get => Record.GetHex(1); set => Record.SetHex(1, value); }
        /// <summary>Head name.</summary>
        public string HeadName { get => Record.GetString(2); set => Record.SetString(2, value); }
        /// <summary>MagicQ attribute of the channel.</summary>
        public int Attribute => Record.GetHex(3);
        /// <summary>Personality (P record id).</summary>
        public int Personality => Record.GetHex(4);
        /// <summary>Offset of this channel in its head.</summary>
        public int ChannelInHead => Record.GetHex(5);
        /// <summary>Head number.</summary>
        public int HeadNumber { get => Record.GetHex(10); set => Record.SetHex(10, value); }
        /// <summary>
        /// Gel / colour id from MagicQ's colour table (allcols.csv): 0 none, 2 mix, 1000+n = Lee n, 2000+n = Rosco n.
        /// </summary>
        public int Gel { get => Record.GetHex(11); set => Record.SetHex(11, value); }
        /// <summary>
        /// Patch &gt; View Heads, generic heads only: MagicVis shape id (0 = default; names not mapped yet) and beam angle
        /// in whole degrees (0 = default from the personality).
        /// Short L records (some dimmer channels) don't have these fields: read 0, writing is ignored.
        /// </summary>
        public int Shape { get => Optional(21); set => SetOptional(21, value); }
        /// <summary>Beam angle of generic heads (whole degrees, 0 = default).</summary>
        public int BeamAngle { get => Optional(22); set => SetOptional(22, value); }
        /// <summary>Gel id, 0 for short records without one.</summary>
        public int GelOrNone => Optional(11);
        /// <summary>
        /// Dimmer curve (f9): 0 = linear; 4 = User 1 (U record 1), so user curve n = value - 3 (seen in RT2025_028+:
        /// f9 4 with f3 flag 0x1000, output matched U 1). 1..3 are probably Limit / Offset / Offset No Wrap (unverified).
        /// </summary>
        public int Curve => Optional(9);

        private int Optional(int field) => Record.FieldCount > field ? Record.GetHex(field) : 0;

        private void SetOptional(int field, int value)
        {
            if (Record.FieldCount > field)
                Record.SetHex(field, value);
        }
    }
}
