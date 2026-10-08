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
    /// S: cue (the recorded data of a step). 1 id, 2 name, 3 number of channel/value pairs, then 10 fields, then
    /// the pairs: absolute DMX address, value (low byte = DMX level 0..255; upper bits are flags).
    /// </summary>
    public sealed class Cue : RecordView, ICue
    {
        private const int FirstPair = 16;
        /// <summary>Creates the view on an S record.</summary>
        public Cue(ShwRecord r) : base(r) { }
        /// <summary>Cue id (referred to by cue stack steps).</summary>
        public int Id => Record.GetHex(1);
        /// <summary>Cue name.</summary>
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }

        /// <summary>
        /// Times: fields 6..10 = In, Out, Pos, Col, Beam in units of 1/33.3 s (99 = 3 s, 999 = 30 s; the record also
        /// holds them in ms: 2971, 29971). Rising intensities fade with In, falling with Out.
        /// </summary>
        public float FadeIn => Record.GetHex(6) * 0.03f;
        /// <summary>Out time (s): falling levels.</summary>
        public float FadeOut => Record.GetHex(7) * 0.03f;

        /// <summary>
        /// Sets the In and / or Out time (null = leave it). The console counts 33.3 units a second (1 s = 33, 2 s = 66,
        /// 3 s = 99, 30 s = 999: rounded down); the other time fields of the record stay as they are.
        /// </summary>
        public void SetFade(float? fadeIn, float? fadeOut)
        {
            if (fadeIn is { } i)
                Record[6] = Units(i).ToString("x4", CultureInfo.InvariantCulture);
            if (fadeOut is { } o)
                Record[7] = Units(o).ToString("x4", CultureInfo.InvariantCulture);
        }

        /// <summary>Seconds as the console's time units (33.3 a second, rounded down).</summary>
        private static int Units(float seconds) => Math.Clamp((int)Math.Floor(seconds * 33.3 + 1e-6), 0, 0xffff);

        /// <summary>Recorded channels: absolute DMX address and raw value (palette id &lt;&lt; 8 | DMX value).</summary>
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

        /// <summary>Number of recorded channels.</summary>
        public int Count => Record.GetHex(3);

        /// <summary>
        /// Sets the raw value of a channel (low byte = DMX value, upper bits = palette id or 0). A channel that is not in
        /// the cue yet is inserted in address order (MagicQ keeps the pairs sorted) and the pair count is updated.
        /// </summary>
        public void SetChannel(int address, int rawValue)
        {
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                int a = Record.GetHex(FirstPair + 2 * i);
                if (a == address)
                {
                    Record[FirstPair + 2 * i + 1] = rawValue.ToString("x", CultureInfo.InvariantCulture);
                    return;
                }
                if (a > address)
                {
                    Record.InsertFieldsAfter(FirstPair + 2 * i - 1, Pair(address, rawValue));
                    Record.SetHex(3, count + 1);
                    return;
                }
            }
            Record.InsertFieldsAfter(count == 0 ? FirstPair - 1 : FirstPair + 2 * count - 1, Pair(address, rawValue));
            Record.SetHex(3, count + 1);
        }

        /// <summary>Changes the address of a channel (repatch). Keeps address order.</summary>
        private void MoveChannel(int from, int to)
        {
            var pair = Channels.FirstOrDefault(c => c.Address == from);
            if (pair.Address != from || from == to)
                return;
            RemoveChannel(from);
            SetChannel(to, pair.Value);
        }

        /// <summary>Removes a channel from the cue.</summary>
        public void RemoveChannel(int address)
        {
            int count = Count;
            for (int i = 0; i < count; i++)
                if (Record.GetHex(FirstPair + 2 * i) == address)
                {
                    Record.RemoveFields(FirstPair + 2 * i, 2);
                    Record.SetHex(3, count - 1);
                    return;
                }
        }

        private static string[] Pair(int address, int value) =>
            new[] { address.ToString("x", CultureInfo.InvariantCulture), value.ToString("x", CultureInfo.InvariantCulture) };
    }
}
