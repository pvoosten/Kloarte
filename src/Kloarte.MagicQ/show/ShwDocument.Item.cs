using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class ShwDocument
{
    /// <summary>Either a comment / blank line between records (Record == null) or a record.</summary>
    public sealed class Item
    {
        /// <summary>without line ending</summary>
        public string RawLine;
        /// <summary>Line ending of the line ("\r\n").</summary>
        public string LineEnding = "";
        /// <summary>The record, or null for a comment / blank line.</summary>
        public ShwRecord Record;
    }
}
