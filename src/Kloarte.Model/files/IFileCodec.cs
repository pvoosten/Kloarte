using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// Reads and writes the project files (YAML). The model defines what a file holds (<see cref="LampFile"/>,
/// <see cref="ActorsFile"/>, ...); the implementation is in <c>Kloarte.Yaml</c>, set by the host (<see cref="FileCodec"/>),
/// so the model itself depends on no YAML library.
/// </summary>
public interface IFileCodec
{
    /// <summary>
    /// The text of a file holding `value`: the modeline for `schema` (a path relative to the file; null = none), the
    /// header lines (each a full comment line, "# ...") and then the value. Line ends "\n".
    /// </summary>
    string Write(object value, string schema, IEnumerable<string> header);

    /// <summary>
    /// Reads a file's text into `T`. The value is null when there is an error (see the problems, each with its
    /// location); unknown keys and out-of-range values are warnings and the value is still read. `path` names the file
    /// in the problems.
    /// </summary>
    (T Value, List<FileProblem> Problems) Read<T>(string path, string text) where T : class;
}
