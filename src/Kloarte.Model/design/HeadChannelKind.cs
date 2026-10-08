namespace Kloarte.Model;

/// <summary>How a <see cref="HeadChannel"/>'s values read.</summary>
public enum HeadChannelKind
{
    /// <summary>The head's intensity: a number is a percentage.</summary>
    Intensity,

    /// <summary>A GDTF channel: a number is the physical value of its main function; names are its sets.</summary>
    Gdtf,

    /// <summary>A plain channel: a number is its DMX value.</summary>
    Raw,
}
