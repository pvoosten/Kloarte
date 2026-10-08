namespace Kloarte.Model;

/// <summary>A picture of a contact sheet (<see cref="ContactSheetPdf"/>) and the caption under it.</summary>
/// <param name="Image">The picture (JPEG).</param>
/// <param name="Caption">The text under it ("Cue 5  Storm"), or "".</param>
public sealed record ContactSheetFrame(PdfImage Image, string Caption);
