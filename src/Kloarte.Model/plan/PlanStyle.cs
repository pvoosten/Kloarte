namespace Kloarte.Model;

/// <summary>How the plan is drawn: label sizes (device units), what to show.</summary>
public sealed class PlanStyle
{
    /// <summary>Text size of lamp labels.</summary>
    public float LabelSize { get; init; } = 11f;

    /// <summary>Show DMX addresses.</summary>
    public bool Addresses { get; init; }

    /// <summary>Show focus lines of all lamps (not only the selected ones).</summary>
    public bool AllFocusLines { get; init; }

    /// <summary>Draw the seats.</summary>
    public bool Seats { get; init; } = true;

    /// <summary>Symbol scale (1 = real fixture size).</summary>
    public float SymbolScale { get; init; } = 1f;
}

