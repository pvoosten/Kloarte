using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.Yaml;

/// <summary>
/// The project files' codec (<see cref="IFileCodec"/>) on YamlDotNet: <see cref="YamlFormat"/> writes, and
/// <see cref="YamlFileReader"/> reads with every problem located. The host sets it as <see cref="FileCodec.Current"/>.
/// </summary>
public sealed class YamlFileCodec : IFileCodec
{
    /// <inheritdoc />
    public string Write(object value, string schema, IEnumerable<string> header) => YamlFormat.Write(value, schema, header);

    /// <inheritdoc />
    public (T Value, List<FileProblem> Problems) Read<T>(string path, string text) where T : class =>
        YamlFileReader.Read<T>(path, text);
}
