using System;

namespace Kloarte.Model;

/// <summary>
/// Marks a type that is one value in the project files, written as a number or a text (e.g. <see cref="LookValue"/>:
/// <c>12.5</c>, <c>Stars</c>, <c>"@Front"</c>). The type has a public static <c>Parse(string)</c> and writes itself with
/// <c>ToString()</c>; the codec (<c>Kloarte.Yaml</c>) uses both, the schema allows a number or a text.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FileScalarAttribute : Attribute
{
    /// <summary>Describes the values of the type (shown on hover in VS Code).</summary>
    public FileScalarAttribute(string description) => Description = description;

    /// <summary>What a value of the type can be.</summary>
    public string Description { get; }
}
