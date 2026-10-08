namespace Kloarte.Model;

/// <summary>The outcome of writing a project file (see <see cref="FileWrite"/>).</summary>
public enum FileWriteKind
{
    /// <summary>The content was written.</summary>
    Written,

    /// <summary>The content is the file's already; nothing was written.</summary>
    Unchanged,

    /// <summary>The file was deleted.</summary>
    Deleted,

    /// <summary>Refused: the file changed outside Kloarte (or is not understood); nothing was written.</summary>
    Conflict,
}
