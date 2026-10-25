using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Kloarte.Application;

/// <summary>
/// The arguments of a remote tool call (a JSON object) as the tools read them: a missing or null argument is absent, a
/// wrong one throws <see cref="ArgumentException"/> naming it (the client sees the message).
/// </summary>
internal sealed class ToolArgs
{
    /// <summary>The arguments object.</summary>
    private readonly JsonElement _args;

    /// <summary>The arguments of a call.</summary>
    public ToolArgs(JsonElement args) => _args = args;

    /// <summary>The argument, or null when it is missing or null.</summary>
    public JsonElement? Get(string name) =>
        _args.ValueKind == JsonValueKind.Object && _args.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    /// <summary>True when the argument is given.</summary>
    public bool Has(string name) => Get(name) != null;

    /// <summary>A text argument (a number is taken as its text), or null.</summary>
    public string OptionalString(string name) => Get(name) is { } value ? TextOf(value, name) : null;

    /// <summary>A text argument that must be given.</summary>
    public string String(string name) => OptionalString(name) ?? throw Missing(name);

    /// <summary>A number argument (a number in a text too), or null.</summary>
    public double? Number(string name)
    {
        if (Get(name) is not { } value)
            return null;
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        throw new ArgumentException($"argument '{name}' must be a number");
    }

    /// <summary>A whole number argument, or null.</summary>
    public int? Int(string name) => Number(name) is { } n
        ? n == Math.Floor(n) ? (int)n : throw new ArgumentException($"argument '{name}' must be a whole number")
        : null;

    /// <summary>A true / false argument; `fallback` when it is missing.</summary>
    public bool Bool(string name, bool fallback = false) => Get(name) is { } value
        ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"argument '{name}' must be true or false"),
        }
        : fallback;

    /// <summary>A list argument of texts / numbers (one value alone counts as a list of one); empty when missing.</summary>
    public IReadOnlyList<string> Strings(string name)
    {
        if (Get(name) is not { } value)
            return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            return new[] { TextOf(value, name) };
        return value.EnumerateArray().Where(v => v.ValueKind != JsonValueKind.Null).Select(v => TextOf(v, name)).ToList();
    }

    /// <summary>A point argument ([x, y, z], metres), or null.</summary>
    public System.Numerics.Vector3? Point(string name)
    {
        if (Get(name) is not { } value)
            return null;
        var numbers = value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number)
            ? value.EnumerateArray().Select(v => (float)v.GetDouble()).ToList()
            : new List<float>();
        if (numbers.Count != 3)
            throw new ArgumentException($"argument '{name}' must be [x, y, z] (metres, stage coordinates)");
        return new System.Numerics.Vector3(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>A list argument of objects, each as arguments of its own; empty when missing.</summary>
    public IReadOnlyList<ToolArgs> Objects(string name)
    {
        if (Get(name) is not { } value)
            return Array.Empty<ToolArgs>();
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Object))
            throw new ArgumentException($"argument '{name}' must be a list of objects");
        return value.EnumerateArray().Select(v => new ToolArgs(v)).ToList();
    }

    /// <summary>An object argument, or null.</summary>
    public JsonElement? Object(string name) => Get(name) is { } value
        ? value.ValueKind == JsonValueKind.Object ? value : throw new ArgumentException($"argument '{name}' must be an object")
        : null;

    /// <summary>The error of a missing argument.</summary>
    public static ArgumentException Missing(string name) => new($"argument '{name}' is missing");

    /// <summary>A text or number element as text.</summary>
    private static string TextOf(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => throw new ArgumentException($"argument '{name}' must be a text or a number"),
    };
}
