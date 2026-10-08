using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class XhwArchive
{
    /// <summary>One file of the archive.</summary>
    public sealed class Entry
    {
        /// <summary>Path in the archive.</summary>
        public string Name = "";
        /// <summary>Contents.</summary>
        public byte[] Data = Array.Empty<byte>();
        /// <summary>null for new entries or after Data/Name changed via SetData</summary>
        internal byte[] OriginalHeader;

        /// <summary>Replaces the contents (the header is regenerated on save).</summary>
        public void SetData(byte[] data)
        {
            Data = data;
            OriginalHeader = null;
        }
    }
}
