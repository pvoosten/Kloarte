using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Kloarte.Model;

namespace Kloarte.MagicQ;

public partial class MagicQColours
{
    /// <summary>A colour of the table (linear 0..1 RGB from the BGR hex).</summary>
    /// <param name="Id">MagicQ colour id.</param>
    /// <param name="Name">Name, e.g. "L201 Full C.T. Blue".</param>
    /// <param name="R">Red.</param>
    /// <param name="G">Green.</param>
    /// <param name="B">Blue.</param>
    /// <param name="Brightness">Transmission 0..1.</param>
    public sealed record Entry(int Id, string Name, float R, float G, float B, float Brightness) : IGel
    {
        /// <summary>Gels that don't colour the light (no colour, colour wheel, colour mixing).</summary>
        public bool IsNeutral => Id is 0 or 1 or 2;
    }
}
