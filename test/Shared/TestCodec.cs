using System.Runtime.CompilerServices;
using Kloarte.Model;
using Kloarte.Yaml;

namespace Kloarte.Tests;

/// <summary>
/// The project files' codec for every test (the host sets the same one at startup): the YAML codec of Kloarte.Yaml.
/// </summary>
public static class TestCodec
{
    /// <summary>Sets the YAML codec before any test runs.</summary>
    [ModuleInitializer]
    internal static void SetCodec() => FileCodec.Current = new YamlFileCodec();
}
