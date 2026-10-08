using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class ShwRecord
{
    /// <summary>One line of a record: its comma-separated tokens and line ending.</summary>
    public sealed class Line
    {
        /// <summary>The tokens (incl. the trailing empty token and ";").</summary>
        public List<string> Tokens;
        /// <summary>Line ending.</summary>
        public string LineEnding;

        /// <summary>Creates a line.</summary>
        public Line(List<string> tokens, string lineEnding)
        {
            Tokens = tokens;
            LineEnding = lineEnding;
        }
    }
}
