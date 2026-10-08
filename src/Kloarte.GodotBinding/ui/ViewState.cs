namespace Kloarte.GodotBinding;

/// <summary>What the view host does with a view (the main view is chosen separately).</summary>
public enum ViewState
{
    /// <summary>Shown as a small card (preview); click it to make it the main view.</summary>
    Small,

    /// <summary>Not shown (still selectable in the view bar).</summary>
    Hidden,
}
