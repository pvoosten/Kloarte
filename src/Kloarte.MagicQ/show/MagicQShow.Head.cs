using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>A patched head: its channels grouped.</summary>
    /// <param name="Number">Head number.</param>
    /// <param name="Name">Head name.</param>
    /// <param name="Personality">Personality id.</param>
    /// <param name="Address">Start address.</param>
    /// <param name="ChannelCount">Number of channels.</param>
    public sealed record Head(int Number, string Name, int Personality, int Address, int ChannelCount)
    {
        /// <summary>DMX universe (1-based).</summary>
        private int Universe => (Address - 1) / 512 + 1;
        /// <summary>Channel in the universe (1-based).</summary>
        public int Channel => (Address - 1) % 512 + 1;
    }
}
