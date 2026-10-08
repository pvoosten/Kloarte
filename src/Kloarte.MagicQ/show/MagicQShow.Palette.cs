using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>
    /// F: palette entry. 1 id (type &lt;&lt; 12 | entry), 2 name, 3 number of values, then per value: absolute DMX address,
    /// DMX value, link (0 or the id of another palette: a linked palette), then fields we don't understand (icon?,
    /// default palette data...). A position palette can hold other attributes too (MagicQ's record mask): a
    /// "combined" palette.
    /// </summary>
    public sealed class Palette : RecordView
    {
        /// <summary>Field of the first value triplet.</summary>
        public const int FirstValue = 4;
        /// <summary>Creates the view on an F record.</summary>
        public Palette(ShwRecord r) : base(r) { }
        /// <summary>Palette id: type &lt;&lt; 12 | entry.</summary>
        public int Id => Record.GetHex(1);
        /// <summary>Palette type (window).</summary>
        public PaletteType Type => (PaletteType)(Id >> 12);
        /// <summary>Entry in its window (0-based).</summary>
        public int Entry => Id & 0xfff;
        /// <summary>Palette name.</summary>
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }
        /// <summary>Number of values.</summary>
        public int Count => Record.GetHex(3);

        /// <summary>The values: absolute DMX address, DMX value, link (0 or another palette's id).</summary>
        public IReadOnlyList<(int Address, int Value, int Link)> Values
        {
            get
            {
                var list = new List<(int, int, int)>();
                for (int i = 0; i < Count && FirstValue + 3 * i + 2 < Record.FieldCount; i++)
                    list.Add((Record.GetHex(FirstValue + 3 * i), Record.GetHex(FirstValue + 3 * i + 1),
                              Record.GetHex(FirstValue + 3 * i + 2)));
                return list;
            }
        }

        /// <summary>The value (and link) of an address, if the palette has it.</summary>
        public bool TryGetValue(int address, out int value, out int link)
        {
            for (int i = 0; i < Count; i++)
                if (Record.GetHex(FirstValue + 3 * i) == address)
                {
                    value = Record.GetHex(FirstValue + 3 * i + 1);
                    link = Record.GetHex(FirstValue + 3 * i + 2);
                    return true;
                }
            value = link = 0;
            return false;
        }

        /// <summary>
        /// Merges values in: existing addresses get the new value (and lose a link: hard values, as in MagicQ), new
        /// addresses are added in address order.
        /// </summary>
        public void Merge(IEnumerable<(int Address, int Value)> values)
        {
            foreach (var (address, value) in values)
            {
                int count = Count, insertAfter = FirstValue - 1;
                bool done = false;
                for (int i = 0; i < count; i++)
                {
                    int a = Record.GetHex(FirstValue + 3 * i);
                    if (a == address)
                    {
                        Record[FirstValue + 3 * i + 1] = value.ToString("x", CultureInfo.InvariantCulture);
                        Record[FirstValue + 3 * i + 2] = "0";
                        done = true;
                        break;
                    }
                    if (a < address)
                        insertAfter = FirstValue + 3 * i + 2;
                }
                if (done)
                    continue;
                Record.InsertFieldsAfter(insertAfter, new[]
                {
                    address.ToString("x", CultureInfo.InvariantCulture), value.ToString("x", CultureInfo.InvariantCulture), "0",
                });
                Record.SetHex(3, count + 1);
            }
        }

        /// <summary>Replaces all values (hard values, no links), in address order.</summary>
        public void SetValues(IEnumerable<(int Address, int Value)> values)
        {
            if (Count > 0)
                Record.RemoveFields(FirstValue, 3 * Count);
            Record.SetHex(3, 0);
            Merge(values.OrderBy(v => v.Address));
        }

        /// <summary>Moves values to other addresses (repatched head); keeps address order and links.</summary>
        public void MoveAddresses(IReadOnlyDictionary<int, int> map)
        {
            var moved = new List<(int Address, int Value, int Link)>();
            for (int i = Count - 1; i >= 0; i--)
            {
                int a = Record.GetHex(FirstValue + 3 * i);
                if (!map.ContainsKey(a))
                    continue;
                moved.Add((map[a], Record.GetHex(FirstValue + 3 * i + 1), Record.GetHex(FirstValue + 3 * i + 2)));
                Record.RemoveFields(FirstValue + 3 * i, 3);
                Record.SetHex(3, Count - 1);
            }
            if (moved.Count == 0)
                return;
            Merge(moved.Select(m => (m.Address, m.Value)));
            foreach (var m in moved.Where(m => m.Link != 0))
                for (int i = 0; i < Count; i++)
                    if (Record.GetHex(FirstValue + 3 * i) == m.Address)
                        Record[FirstValue + 3 * i + 2] = m.Link.ToString("x", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Which attribute types (I, P, C, B) the palette holds, from the personalities of the patched channels.
        /// </summary>
        public string Contents(MagicQShow show)
        {
            var attributes = new HashSet<char>();
            foreach (var (address, _, _) in Values)
            {
                var ch = show.Patch.FirstOrDefault(p => p.Address == address);
                if (ch != null)
                    attributes.Add(AttributeClass(show, ch));
            }
            return new string("IPCB".Where(attributes.Contains).ToArray());
        }
    }
}
