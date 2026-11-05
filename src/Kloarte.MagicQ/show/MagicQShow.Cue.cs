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
        /// 3 s = 99, 30 s = 999: rounded down). Each time has a millisecond copy further on, which is what MagicQ PC
        /// 1.9.7.3 plays (RT2025_044 cue 1.01 with In 4.5 s in units and 2971 ms in the copy faded in 2.7 s): it is
        /// written too, as MagicQ writes it (<see cref="Milliseconds"/>). A Pos / Col / Beam time equal to the old In
        /// time follows the new In time (MagicQ records all five the same; Kloarte fades every channel with In / Out).
        /// </summary>
        public void SetFade(float? fadeIn, float? fadeOut)
        {
            var times = Times;
            if (fadeIn is { } i)
            {
                int units = Units(i);
                SetTime(InTime, units);
                for (int k = InTime + 2; k < InTime + 5; k++)
                    if (times[k - InTime] == times[0])
                        SetTime(k, units);
            }
            if (fadeOut is { } o)
                SetTime(InTime + 1, Units(o));
        }

        /// <summary>
        /// True when the millisecond copies of the five times are what MagicQ writes for their units (a show without
        /// the copies counts as true). Kloarte exports before 5 Nov 2026 set only the units (Kruimels_012 / 013).
        /// </summary>
        public bool TimeCopiesMatch
        {
            get
            {
                int ms = MillisecondsField();
                var times = Times;
                return ms < 0 || Enumerable.Range(0, times.Length).All(k => Record.GetHex(ms + k) == Milliseconds(times[k]));
            }
        }

        /// <summary>Writes the time copies of the five times from their units (see <see cref="TimeCopiesMatch"/>).</summary>
        public void RepairTimeCopies()
        {
            var times = Times;
            for (int k = 0; k < times.Length; k++)
                SetTime(InTime + k, times[k]);
        }

        /// <summary>The In, Out, Pos, Col and Beam times in units of 1/33.3 s (fields 6..10).</summary>
        public int[] Times => Enumerable.Range(InTime, 5).Select(Record.GetHex).ToArray();

        /// <summary>
        /// The In, Out, Pos, Col and Beam delays in units of 1/33.3 s (fields 11..15; their millisecond copies follow
        /// those of the times). RT2025_044 cues 6..27.01 have an In delay: MagicQ PC 1.9.7.3 starts their rising
        /// intensities that much later (cue 6, In 3 s + In delay 3 s: the output changes for 5.7 s).
        /// </summary>
        public int[] Delays => Enumerable.Range(InTime + 5, 5).Select(Record.GetHex).ToArray();

        /// <summary>
        /// The addresses an FX of the cue runs on. Field 4 = the number of FX blocks; a block follows the pairs: a
        /// 12-field header (0002, channel count, ...) and 7 fields per channel, the first the address.
        /// </summary>
        public IReadOnlyCollection<int> FxAddresses
        {
            get
            {
                var addresses = new HashSet<int>();
                int field = FirstPair + 2 * Count;
                for (int block = 0; block < Record.GetHex(4) && field + 12 <= Record.FieldCount && Record[field] == "0002"; block++)
                {
                    int channels = Record.GetHex(field + 1);
                    for (int i = 0; i < channels && field + 12 + 7 * i < Record.FieldCount; i++)
                        addresses.Add(Record.GetHex(field + 12 + 7 * i));
                    field += 12 + 7 * channels;
                }
                return addresses;
            }
        }

        /// <summary>
        /// Moves the FX channels of the cue to other addresses (repatch: MagicQ refers to FX channels by address, like the
        /// pairs). Returns the number of channels moved.
        /// </summary>
        public int MoveFxAddresses(IReadOnlyDictionary<int, int> map)
        {
            int moved = 0;
            int field = FirstPair + 2 * Count;
            for (int block = 0; block < Record.GetHex(4) && field + 12 <= Record.FieldCount && Record[field] == "0002"; block++)
            {
                int channels = Record.GetHex(field + 1);
                for (int i = 0; i < channels && field + 12 + 7 * i < Record.FieldCount; i++)
                    if (map.TryGetValue(Record.GetHex(field + 12 + 7 * i), out int to))
                    {
                        Record[field + 12 + 7 * i] = to.ToString("x", CultureInfo.InvariantCulture);
                        moved++;
                    }
                field += 12 + 7 * channels;
            }
            return moved;
        }

        /// <summary>The field of the In time.</summary>
        private const int InTime = 6;

        /// <summary>Sets time field `field` (6..10) to `units` and its millisecond copy.</summary>
        private void SetTime(int field, int units)
        {
            Record[field] = units.ToString("x4", CultureInfo.InvariantCulture);
            int ms = MillisecondsField();
            if (ms >= 0)
                Record[ms + field - InTime] = Milliseconds(units).ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The millisecond copy MagicQ writes for a time or delay: 30 x units + 1, 0 for 0 (3 s = 99 = 2971 ms, 1 s = 33 =
        /// 991, 30 s = 999 = 29971; 39815 of the 39870 times of the show folder, the others are Kloarte exports).
        /// </summary>
        public static int Milliseconds(int units) => units == 0 ? 0 : units * 30 + 1;

        /// <summary>
        /// The field of the In time's millisecond copy, or -1: the first 14 fields of 8 hex digits in a row after the
        /// pairs (the 5 times, the 5 delays, 4 more).
        /// </summary>
        private int MillisecondsField()
        {
            int run = 0;
            for (int f = FirstPair + 2 * Count; f < Record.FieldCount; f++)
            {
                run = Record[f].Length == 8 && int.TryParse(Record[f], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? run + 1 : 0;
                if (run == 14)
                    return f - 13;
            }
            return -1;
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
