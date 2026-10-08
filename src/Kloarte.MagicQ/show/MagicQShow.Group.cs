using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>G: group with a list of head numbers.</summary>
    public sealed class Group : RecordView
    {
        /// <summary>Creates the view on a G record.</summary>
        public Group(ShwRecord r) : base(r) { }

        /// <summary>Group number.</summary>
        public int Number => Record.GetHex(1);

        /// <summary>Group name.</summary>
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }

        /// <summary>Head numbers in the group.</summary>
        public IReadOnlyList<int> Heads =>
            Enumerable.Range(0, Record.GetHex(3)).Select(i => Record.GetHex(4 + i)).ToList();

        /// <summary>Sets the member list (head numbers, hex as MagicQ writes them, on the count's line).</summary>
        public void SetHeads(IReadOnlyList<int> heads)
        {
            int count = Record.GetHex(3);
            if (count > 0)
                Record.RemoveFields(4, count);
            Record.SetHex(3, heads.Count);
            if (heads.Count > 0)
                Record.InsertFieldsAfter(3, heads.Select(h => h.ToString("x", CultureInfo.InvariantCulture)));
        }

        /// <summary>Replaces a head number in the member list (renumbered head).</summary>
        public void ReplaceHead(int oldNumber, int newNumber)
        {
            int count = Record.GetHex(3);
            for (int i = 0; i < count; i++)
                if (Record.GetHex(4 + i) == oldNumber)
                    Record.SetHex(4 + i, newNumber);
        }
    }
}
