using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

public partial class Shutters
{
    /// <summary>One framing shutter blade.</summary>
    public sealed class Blade
    {
        /// <summary>Depth: 0 = out, 1 = to the beam axis, 2 = fully across.</summary>
        public float Depth { get; set; }
        /// <summary>Tilt (degrees, counter-clockwise).</summary>
        public float Angle { get; set; }
    }
}
