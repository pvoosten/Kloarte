using System;
using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// What the views share about editing the native show's cues and palettes (decided 14 Oct 2026: no programmer, values go
/// into a cue or a palette): the current cue (the playback's), edits (one named undo step each; the stage shows the
/// result), requests to jump to a cue, and the heads waiting for a click on the stage to aim at.
/// </summary>
public static class CueEditing
{
    /// <summary>Values of the show changed (the stage shows the current cue again, views refresh).</summary>
    public static event Action Edited;

    /// <summary>The current cue changed.</summary>
    public static event Action CueChanged;

    /// <summary>A view asks the playback to go to a cue (index in the cue list).</summary>
    public static event Action<int> JumpRequested;

    /// <summary>The current cue: its index in the cue list (-1 = before the first cue).</summary>
    public static int Cue { get; private set; } = -1;

    /// <summary>Heads waiting for a click on the stage to aim at (in the current cue), or null.</summary>
    public static IReadOnlyList<int> AimHeads { get; private set; }

    /// <summary>The native show of a builder, or null (another kind of show, or none).</summary>
    public static DesignShow Design(ShowBuilder builder) => builder?.Document as DesignShow;

    /// <summary>The playback says which cue is current.</summary>
    internal static void SetCue(int index)
    {
        if (index == Cue)
            return;
        Cue = index;
        CueChanged?.Invoke();
    }

    /// <summary>
    /// Changes the show (`change` edits the design's lists) as one undo step named `name`; the projections are refreshed
    /// and the stage shows the result.
    /// </summary>
    public static void Edit(ShowBuilder builder, string name, Action<DesignShow> change)
    {
        var design = Design(builder);
        if (design == null)
            return;
        builder.Files.NameNextEdit(name);
        change(design);
        design.Changed();
        Edited?.Invoke();
    }

    /// <summary>Tells the views that the show changed outside <see cref="Edit"/> (undo, a file, a rebuild).</summary>
    public static void Refresh() => Edited?.Invoke();

    /// <summary>Asks the playback to go to a cue.</summary>
    public static void Jump(int index) => JumpRequested?.Invoke(index);

    /// <summary>The next click on the stage aims these heads (null: no aiming).</summary>
    public static void StartAim(IReadOnlyList<int> heads) => AimHeads = heads;

    /// <summary>Ends aiming.</summary>
    public static void StopAim() => AimHeads = null;
}
