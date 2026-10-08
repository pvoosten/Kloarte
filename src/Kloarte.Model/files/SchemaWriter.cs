using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>
/// Writes the JSON Schema of a project file type from its properties and their <see cref="FileFieldAttribute"/>s
/// (description, unit, range, values): what VS Code uses for completion, hover and warnings. Unknown keys are not allowed
/// by the schema (Kloarte reads them with a warning). Deterministic: the same type gives the same text.
/// </summary>
public static class SchemaWriter
{
    /// <summary>The JSON options: indented, and the units ° and · not escaped.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The schema of `root` as JSON text (titled `title`); the types it uses are in "$defs".</summary>
    public static string Write(Type root, string title)
    {
        var defs = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var schema = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = title,
        };
        foreach (var (key, value) in Object(root, defs))   // the file's own type is the top level, not a reference
            schema[key] = value;
        if (defs.Count > 0)
            schema["$defs"] = defs;
        return JsonSerializer.Serialize(schema, Json).Replace("\r\n", "\n") + "\n";   // LF, as Kloarte's other files
    }

    /// <summary>The schema of one type; a class is a reference into the definitions (added once).</summary>
    private static Dictionary<string, object> Describe(Type type, SortedDictionary<string, object> defs, FileFieldAttribute field)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        var schema = new Dictionary<string, object>();
        if (type.GetCustomAttribute<FileScalarAttribute>() is { } scalar)
        {
            schema["type"] = new[] { "number", "string" };
            if (field == null)
                schema["description"] = scalar.Description;
        }
        else if (type == typeof(string))
        {
            schema["type"] = "string";
            if (field is { Values.Length: > 0 } && field.Values is { } values)
                schema["enum"] = values.Split(',').Select(v => v.Trim()).ToArray();
        }
        else if (type == typeof(bool))
            schema["type"] = "boolean";
        else if (type == typeof(int) || type == typeof(long))
        {
            schema["type"] = "integer";
            Range(schema, field);
        }
        else if (type == typeof(double) || type == typeof(float))
        {
            schema["type"] = "number";
            Range(schema, field);
        }
        else if (type == typeof(DateTime))
        {
            schema["type"] = "string";
            schema["format"] = "date-time";
        }
        else if (DictionaryValue(type) is { } valueType)
        {
            schema["type"] = "object";
            schema["additionalProperties"] = Describe(valueType, defs, null);
        }
        else if (ElementOf(type) is { } element)
        {
            schema["type"] = "array";
            schema["items"] = Describe(element, defs, null);
        }
        else
        {
            string name = type.Name;
            if (!defs.ContainsKey(name))
            {
                defs[name] = null;   // a placeholder: a type that refers to itself
                defs[name] = Object(type, defs);
            }
            schema["$ref"] = "#/$defs/" + name;
        }
        return schema;
    }

    /// <summary>The object schema of a class: its writable properties, camelCase, closed (no unknown keys).</summary>
    private static Dictionary<string, object> Object(Type type, SortedDictionary<string, object> defs)
    {
        var properties = new Dictionary<string, object>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite))
        {
            var field = property.GetCustomAttribute<FileFieldAttribute>();
            var schema = Describe(property.PropertyType, defs, field);
            if (field != null)
                schema["description"] = field.Unit.Length > 0 ? $"{field.Description} ({field.Unit})" : field.Description;
            properties[FileKeys.Of(property.Name)] = schema;
        }
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };
    }

    /// <summary>The minimum and maximum of a number field (when it has them).</summary>
    private static void Range(Dictionary<string, object> schema, FileFieldAttribute field)
    {
        if (field == null)
            return;
        if (!double.IsNaN(field.Min))
            schema["minimum"] = field.Min;
        if (!double.IsNaN(field.Max))
            schema["maximum"] = field.Max;
    }

    /// <summary>The value type of a Dictionary&lt;string, T&gt; (or an interface of it), or null.</summary>
    private static Type DictionaryValue(Type type)
    {
        var dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        return dictionary?.GetGenericArguments()[1];
    }

    /// <summary>The element type of an array or list, or null.</summary>
    private static Type ElementOf(Type type) =>
        type.IsArray ? type.GetElementType()
        : type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType ? type.GetGenericArguments()[0]
        : null;
}
