using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>P: personality (head type).</summary>
    public sealed class Personality : RecordView
    {
        /// <summary>Creates the view on a P record.</summary>
        public Personality(ShwRecord r) : base(r) { }
        /// <summary>Personality id (referred to by L and H records).</summary>
        public int Id => Record.GetHex(1);
        /// <summary>Name, e.g. "Robe_RobinT1Profile_Mode1".</summary>
        public string Name => Record.GetString(2);
        /// <summary>Manufacturer.</summary>
        private string Manufacturer => Record.GetString(3);
        /// <summary>Mode name.</summary>
        public string Mode => Record.GetString(4);
        /// <summary>Short name.</summary>
        private string ShortName => Record.GetString(5);
        /// <summary>Number of DMX channels.</summary>
        public int ChannelCount => Record.GetHex(6);

        /// <summary>
        /// Channel list: from field 16, 3 fields per channel: name, flags, attribute (0 = dimmer/intensity).
        /// </summary>
        public IReadOnlyList<(string Name, int Attribute)> Channels =>
            Enumerable.Range(0, ChannelCount)
                .TakeWhile(i => 18 + 3 * i < Record.FieldCount)
                .Select(i => (Record.GetString(16 + 3 * i), Record.GetHex(18 + 3 * i)))
                .ToList();

        /// <summary>Offset of the (coarse) intensity channel within the head, or -1.</summary>
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
}
