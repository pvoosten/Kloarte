using System;

namespace Kloarte.Model;

/// <summary>
/// The codec the project files are read and written with. The host sets it at startup (<c>Kloarte.Yaml</c>'s
/// <c>YamlFileCodec</c>); tests set it in their module initializer.
/// </summary>
public static class FileCodec
{
    /// <summary>Backing field of <see cref="Current"/>.</summary>
    private static IFileCodec _current;

    /// <summary>The codec; throws when none was set (the host or the test didn't set one).</summary>
    public static IFileCodec Current
    {
        get => _current ?? throw new InvalidOperationException(
            "No project file codec: the host must set FileCodec.Current (Kloarte.Yaml's YamlFileCodec)");
        set => _current = value;
    }
}
