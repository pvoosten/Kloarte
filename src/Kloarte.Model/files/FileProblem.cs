namespace Kloarte.Model;

/// <summary>
/// Something wrong in a project file, with where it is (line and column from 1; 0 = the whole file). An error means the
/// file is not applied (Kloarte keeps the last good state); a warning (unknown key, value out of range) is applied.
/// </summary>
/// <param name="Path">The file (absolute).</param>
/// <param name="Line">Line (from 1), 0 = unknown.</param>
/// <param name="Column">Column (from 1), 0 = unknown.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="IsError">True: the file is not applied; false: a warning.</param>
public sealed record FileProblem(string Path, int Line, int Column, string Message, bool IsError)
{
    /// <summary>"file:line:column: message" (the file name only), as on the status bar.</summary>
    public override string ToString() =>
        $"{System.IO.Path.GetFileName(Path)}{(Line > 0 ? $":{Line}:{Column}" : "")}: {(IsError ? "" : "warning: ")}{Message}";
}
