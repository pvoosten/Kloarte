using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>The cue list of a native show as a show document's cue stack.</summary>
internal sealed class DesignStackView : ICueStack
{
    /// <summary>The show.</summary>
    private readonly DesignShow _show;

    /// <summary>The view of a show's cue list.</summary>
    public DesignStackView(DesignShow show) => _show = show;

    /// <inheritdoc />
    public int Number => _show.CueList.Stack ?? 1;

    /// <inheritdoc />
    public string Name => _show.CueList.Name ?? "";

    /// <inheritdoc />
    public IReadOnlyList<ICueStep> Steps =>
        _show.CueOrder.Where(c => c.Id != null)
            .Select(c => (ICueStep)new DesignStepView(c.Id.Value, c.Number, CueNumbers.Text(c.Number), c.Text ?? ""))
            .ToList();

    /// <summary>True for the view of the same show (views are made on each call).</summary>
    public override bool Equals(object obj) => obj is DesignStackView other && ReferenceEquals(other._show, _show);

    /// <inheritdoc />
    public override int GetHashCode() => _show.GetHashCode();
}
