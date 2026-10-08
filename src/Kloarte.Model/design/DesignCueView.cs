using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>A cue of a native show as a show document's cue: its DMX channels (resolved) and fade times.</summary>
internal sealed class DesignCueView : ICue
{
    /// <summary>The show.</summary>
    private readonly DesignShow _show;

    /// <summary>The cue.</summary>
    private readonly DesignCue _cue;

    /// <summary>The view of a cue of a show.</summary>
    public DesignCueView(DesignShow show, DesignCue cue)
    {
        _show = show;
        _cue = cue;
    }

    /// <inheritdoc />
    public int Id => _cue.Id ?? 0;

    /// <inheritdoc />
    public string Name => _cue.Name ?? _cue.Text ?? "";

    /// <inheritdoc />
    public float FadeIn => (float)(_cue.FadeIn ?? 0);

    /// <inheritdoc />
    public float FadeOut => (float)(_cue.FadeOut ?? 0);

    /// <inheritdoc />
    public float Delay => (float)(_cue.Delay ?? 0);

    /// <inheritdoc />
    public IReadOnlyList<(int Address, int Value)> Channels =>
        _show.ChannelsOf(_cue).Select(c => (c.Address, c.Value)).ToList();
}
