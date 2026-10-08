using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>Any record with a number and a name in fields 1 and 2 (palettes, FX).</summary>
    public sealed class NamedRecord : RecordView
    {
        /// <summary>Creates the view on a record.</summary>
        public NamedRecord(ShwRecord r) : base(r) { }
        /// <summary>Number (field 1).</summary>
        public int Number => Record.GetHex(1);
        /// <summary>Name (field 2).</summary>
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }
    }
}
