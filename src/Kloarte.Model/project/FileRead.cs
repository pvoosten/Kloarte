using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// A project file as read: its path, its text, the value (null when it doesn't parse) and the problems found in it
/// (<see cref="ProjectFolder.Read{T}"/>).
/// </summary>
/// <typeparam name="T">The file type.</typeparam>
/// <param name="Path">The file (absolute).</param>
/// <param name="Text">The file's text as it was read.</param>
/// <param name="Value">The file's value, or null when there is an error.</param>
/// <param name="Problems">Errors (the file is not applied) and warnings (applied), each with its location.</param>
public sealed record FileRead<T>(string Path, string Text, T Value, List<FileProblem> Problems) where T : class
{
    /// <summary>True when the file has an error (its value is not to be applied).</summary>
    public bool HasError => Value == null || Problems.Exists(p => p.IsError);
}
