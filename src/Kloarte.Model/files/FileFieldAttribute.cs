using System;

namespace Kloarte.Model;

/// <summary>
/// What a field of a project file means: a description, its unit, the allowed range and values. One source for the JSON
/// Schemas VS Code uses (completion, hover) and for the warnings of the codec (Kloarte.Yaml) (a value out of range).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FileFieldAttribute : Attribute
{
    /// <summary>Describes a field.</summary>
    public FileFieldAttribute(string description) => Description = description;

    /// <summary>What the field is (shown on hover in VS Code).</summary>
    public string Description { get; }

    /// <summary>Unit, e.g. "m", "°", "s" ("" = none).</summary>
    public string Unit { get; set; } = "";

    /// <summary>Lowest allowed value (NaN = no limit).</summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>Highest allowed value (NaN = no limit).</summary>
    public double Max { get; set; } = double.NaN;

    /// <summary>The allowed values of a text field, separated by commas ("" = any text).</summary>
    public string Values { get; set; } = "";

    /// <summary>True for a text that is always written in quotes (colours "#rrggbb").</summary>
    public bool Quoted { get; set; }
}
