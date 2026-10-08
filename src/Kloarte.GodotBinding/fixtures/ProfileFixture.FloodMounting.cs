namespace Kloarte.GodotBinding;

public partial class ProfileFixture
{
    /// <summary>How an asymmetric flood hangs (which way its reflector throws).</summary>
    public enum FloodMounting
    {
        /// <summary>Not an asymmetric flood.</summary>
        None,

        /// <summary>Hung above the cloth: throws down towards the far (bottom) end.</summary>
        Top,

        /// <summary>Ground row: throws up towards the top of the cloth.</summary>
        Bottom,
    }
}
