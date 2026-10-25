using System.Linq;
using System.Text.Json.Nodes;

namespace Kloarte.Application;

/// <summary>JSON Schemas of the remote tools' arguments, built in code.</summary>
internal static class ToolSchema
{
    /// <summary>An object of these properties (the required ones listed as required); no other properties.</summary>
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema, _) in properties)
            props[name] = schema;
        var result = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
        var required = properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
        if (required.Length > 0)
            result["required"] = new JsonArray(required);
        return result;
    }

    /// <summary>A required property.</summary>
    public static (string, JsonObject, bool) Required(string name, JsonObject schema) => (name, schema, true);

    /// <summary>An optional property.</summary>
    public static (string, JsonObject, bool) Optional(string name, JsonObject schema) => (name, schema, false);

    /// <summary>A text.</summary>
    public static JsonObject String(string description) => new() { ["type"] = "string", ["description"] = description };

    /// <summary>A number, optionally limited.</summary>
    public static JsonObject Number(string description, double? min = null, double? max = null)
    {
        var schema = new JsonObject { ["type"] = "number", ["description"] = description };
        if (min != null)
            schema["minimum"] = min;
        if (max != null)
            schema["maximum"] = max;
        return schema;
    }

    /// <summary>A whole number.</summary>
    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };

    /// <summary>True or false.</summary>
    public static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };

    /// <summary>A text or a number (cue numbers: "12.5" or 12.5).</summary>
    public static JsonObject TextOrNumber(string description) =>
        new() { ["type"] = new JsonArray("string", "number"), ["description"] = description };

    /// <summary>A list of texts or numbers.</summary>
    public static JsonObject List(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = new JsonArray("string", "number") },
        ["description"] = description,
    };

    /// <summary>A point [x, y, z] (metres).</summary>
    public static JsonObject Point(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "number" },
        ["minItems"] = 3,
        ["maxItems"] = 3,
        ["description"] = description,
    };

    /// <summary>A list of objects of one schema.</summary>
    public static JsonObject ListOf(JsonObject item, string description) =>
        new() { ["type"] = "array", ["items"] = item, ["description"] = description };

    /// <summary>A free object (its own description says what it holds).</summary>
    public static JsonObject Map(string description) => new() { ["type"] = "object", ["description"] = description };
}
