namespace Kloarte.GodotBinding;

/// <summary>What an export makes (UI review 32).</summary>
internal enum ExportKind
{
    /// <summary>A PNG at every key moment of the timeline (GO, fade end, actor step) + index.csv.</summary>
    Stills,

    /// <summary>A movie of the timeline (Movie Maker in a second process; MP4 when ffmpeg is found).</summary>
    Movie,

    /// <summary>A PDF with a picture of every cue (when its fades are done) and its caption.</summary>
    ContactSheet,
}
