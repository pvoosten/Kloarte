namespace Kloarte.Model;

/// <summary>A gel (colour filter) from the console's colour table.</summary>
public interface IGel
{
    /// <summary>Name, e.g. "Lee 201 Full C.T. Blue".</summary>
    string Name { get; }

    /// <summary>Red (0..1).</summary>
    float R { get; }

    /// <summary>Green (0..1).</summary>
    float G { get; }

    /// <summary>Blue (0..1).</summary>
    float B { get; }

    /// <summary>Transmission (0..1).</summary>
    float Brightness { get; }

    /// <summary>True for entries that don't colour the light (no colour, colour wheel, colour mixing).</summary>
    bool IsNeutral { get; }
}
