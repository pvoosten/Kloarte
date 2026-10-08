using System;

namespace Kloarte.Model;

/// <summary>
/// Marks a file type that is written in flow style (<c>{depth: 0.35, angle: -5}</c>) instead of one key per line.
/// Read by the codec (<c>Kloarte.Yaml</c>); the schema is the same either way.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FileFlowAttribute : Attribute
{
}
