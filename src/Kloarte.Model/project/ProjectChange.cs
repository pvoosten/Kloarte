using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// A project file that changed outside Kloarte, as the session reports it (<see cref="ProjectFileKind"/>). A change
/// without a conflict is applied already (one undo step, <see cref="Applied"/>); a conflict waits for the user's choice
/// (<see cref="ProjectChoice"/>); a file with an error is not applied (<see cref="Problems"/>, the last good state stays).
/// </summary>
/// <param name="Path">The file (absolute).</param>
/// <param name="Kind">What the file holds.</param>
/// <param name="Name">The file's name ("h012-c103-LX1-3.yaml"), for the messages.</param>
/// <param name="RigItem">The lamp's rig item id (lamp files), else null.</param>
/// <param name="Applied">True when the change went into the model (the scene must be rebuilt).</param>
/// <param name="Conflict">True when Kloarte has unsaved changes for the same file: the user chooses.</param>
/// <param name="Problems">Errors and warnings of the file, located.</param>
public sealed record ProjectChange(
    string Path,
    ProjectFileKind Kind,
    string Name,
    int? RigItem,
    bool Applied,
    bool Conflict,
    IReadOnlyList<FileProblem> Problems)
{
    /// <summary>True when the file has an error (not applied).</summary>
    public bool HasError => Problems.Any(p => p.IsError);
}
