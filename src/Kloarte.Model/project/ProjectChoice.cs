namespace Kloarte.Model;

/// <summary>
/// What to do with a project file that changed outside Kloarte while Kloarte has unsaved changes for it (user decision:
/// the same three choices as for a MagicQ save).
/// </summary>
public enum ProjectChoice
{
    /// <summary>Take the file's content (Kloarte's unsaved change for it goes; one undo step).</summary>
    TakeFile,

    /// <summary>Keep Kloarte's content (the file's change is ignored; the next save overwrites the file).</summary>
    KeepKloarte,

    /// <summary>Save Kloarte's content over the file first (the file's change is replaced).</summary>
    SaveKloarteFirst,
}
