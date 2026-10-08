using System;

namespace Kloarte.GodotBinding;

/// <summary>
/// A mode that changes what the stage shows or what a click does (UI review 14): shown as a chip in the status bar
/// with an ✕ that ends it (group / cue filter, Solo, programmer, live Art-Net, plan placement, Aim).
/// </summary>
/// <param name="Text">The chip's text ("Solo", "Group: Front").</param>
/// <param name="Tooltip">What the mode does and how to end it.</param>
/// <param name="End">Ends the mode (the ✕).</param>
internal sealed record ModeChip(string Text, string Tooltip, Action End);
