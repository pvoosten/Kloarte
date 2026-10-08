namespace Kloarte.Model;

/// <summary>What happened to a project file when Kloarte tried to write it (see <see cref="ProjectFolder.Write"/>).</summary>
/// <param name="Path">The file (absolute).</param>
/// <param name="Kind">Written, unchanged (same content, nothing written), deleted, or a conflict (refused).</param>
/// <param name="Message">Why, for a conflict.</param>
public sealed record FileWrite(string Path, FileWriteKind Kind, string Message);
