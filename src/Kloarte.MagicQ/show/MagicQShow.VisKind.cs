namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>Kind of MagicVis item (H field 2).</summary>
    public enum VisKind
    {
        /// <summary>A fixture (lamp).</summary>
        Fixture = 1,

        /// <summary>An object (truss, box, person, ...).</summary>
        Object = 2,
    }
}
