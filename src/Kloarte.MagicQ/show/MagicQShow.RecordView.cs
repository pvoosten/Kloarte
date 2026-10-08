using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>Base of the typed views: a view reads and writes the fields of one record.</summary>
    public abstract class RecordView
    {
        /// <summary>The underlying record.</summary>
        public ShwRecord Record { get; }
        /// <summary>Creates the view on a record.</summary>
        protected RecordView(ShwRecord record) => Record = record;
    }
}
