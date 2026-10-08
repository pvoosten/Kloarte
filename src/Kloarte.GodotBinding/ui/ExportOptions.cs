using Godot;

namespace Kloarte.GodotBinding;

/// <summary>An export as chosen in the export dialog (UI review 32).</summary>
/// <param name="Kind">Stills, movie or contact sheet.</param>
/// <param name="Viewpoint">Where the camera is: "3d" (the 3D view's camera), "audience" (the audience view's seat) or
/// "vp:n" (the venue's named viewpoint n, e.g. Regie).</param>
/// <param name="FromStep">First cue (step index of the stack; -1 = from before the first cue).</param>
/// <param name="ToStep">Last cue (step index; its hold included).</param>
/// <param name="Captions">Show the cue as a caption.</param>
/// <param name="Size">Picture size in pixels.</param>
/// <param name="PerActor">One export per actor: only their name on their shirt, their name in the caption.</param>
/// <param name="Target">The folder (stills, contact sheet) or the movie file (.avi; per actor one next to it per
/// person).</param>
internal sealed record ExportOptions(ExportKind Kind, string Viewpoint, int FromStep, int ToStep, bool Captions,
                                     Vector2I Size, bool PerActor, string Target);
