using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Kloarte.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Kloarte.Yaml;

/// <summary>
/// Reads a YAML project file into its file type, with every problem located (line:column): a syntax error or a value of
/// the wrong kind is an error (the file is not applied); a key Kloarte doesn't know or a number outside its
/// <see cref="FileFieldAttribute"/> range is a warning (the rest is applied). User decision: a hand-edited file must
/// never take the show down; one broken file doesn't stop the others.
/// </summary>
internal static class YamlFileReader
{
    /// <summary>Reads a file's text as `T`; the value is null when there is an error (see the problems).</summary>
    public static (T Value, List<FileProblem> Problems) Read<T>(string path, string text) where T : class
    {
        var problems = new List<FileProblem>();
        try
        {
            var stream = new YamlStream();
            stream.Load(new System.IO.StringReader(text));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is YamlScalarNode { Value: "" or null })
            {
                problems.Add(new FileProblem(path, 0, 0, "the file is empty", true));
                return (null, problems);
            }
            Check(stream.Documents[0].RootNode, typeof(T), path, problems);
            var value = YamlFormat.Deserializer.Deserialize<T>(text);
            if (value == null)
                problems.Add(new FileProblem(path, 0, 0, "the file is empty", true));
            return (problems.Any(p => p.IsError) ? null : value, problems);
        }
        catch (YamlException e)
        {
            problems.Add(new FileProblem(path, (int)e.Start.Line, (int)e.Start.Column, Message(e), true));
            return (null, problems);
        }
    }

    /// <summary>The innermost message (YamlDotNet wraps the reason in "Exception during deserialization").</summary>
    private static string Message(Exception e)
    {
        while (e.InnerException != null)
            e = e.InnerException;
        return e is YamlException y ? y.Message.Split(new[] { "): " }, 2, StringSplitOptions.None).Last() : e.Message;
    }

    /// <summary>Warnings for unknown keys and numbers out of range, walking the node against the file type.</summary>
    private static void Check(YamlNode node, Type type, string path, List<FileProblem> problems)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (node is YamlMappingNode map && IsDictionary(type, out var valueType))
        {
            foreach (var (_, value) in map.Children)
                Check(value, valueType, path, problems);
        }
        else if (node is YamlMappingNode record && type.IsClass && type != typeof(string))
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite).ToDictionary(p => FileKeys.Of(p.Name));
            foreach (var (key, value) in record.Children)
            {
                string name = (key as YamlScalarNode)?.Value ?? "";
                if (!properties.TryGetValue(name, out var property))
                {
                    var known = string.Join(", ", properties.Keys);
                    problems.Add(new FileProblem(path, (int)key.Start.Line, (int)key.Start.Column,
                        $"unknown key '{name}' (ignored; known here: {known})", false));
                    continue;
                }
                CheckRange(value, property, path, problems);
                Check(value, property.PropertyType, path, problems);
            }
        }
        else if (node is YamlSequenceNode list && ElementOf(type) is { } element)
        {
            foreach (var item in list.Children)
                Check(item, element, path, problems);
        }
    }

    /// <summary>A number outside the property's range, or a text not among its values: a warning.</summary>
    private static void CheckRange(YamlNode value, PropertyInfo property, string path, List<FileProblem> problems)
    {
        if (property.GetCustomAttribute<FileFieldAttribute>() is not { } field || value is not YamlScalarNode { Value: { } text })
            return;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && (v < field.Min || v > field.Max))
            problems.Add(new FileProblem(path, (int)value.Start.Line, (int)value.Start.Column,
                $"{FileKeys.Of(property.Name)} {text} is outside {Range(field)}", false));
        else if (field.Values != "" && property.PropertyType == typeof(string)
                 && !field.Values.Split(',').Select(s => s.Trim()).Contains(text))
            problems.Add(new FileProblem(path, (int)value.Start.Line, (int)value.Start.Column,
                $"{FileKeys.Of(property.Name)} '{text}' is not one of {field.Values}", false));
    }

    /// <summary>"0 .. 1" (with the unit), "≥ 0", "≤ 2".</summary>
    private static string Range(FileFieldAttribute field)
    {
        string unit = field.Unit == "" ? "" : " " + field.Unit;
        string N(double d) => d.ToString(CultureInfo.InvariantCulture);
        return double.IsNaN(field.Min) ? $"≤ {N(field.Max)}{unit}"
            : double.IsNaN(field.Max) ? $"≥ {N(field.Min)}{unit}"
            : $"{N(field.Min)} .. {N(field.Max)}{unit}";
    }

    /// <summary>True for Dictionary&lt;string, T&gt; (any key allowed); the value type.</summary>
    private static bool IsDictionary(Type type, out Type valueType)
    {
        valueType = null;
        var dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary == null)
            return false;
        valueType = dictionary.GetGenericArguments()[1];
        return true;
    }

    /// <summary>The element type of an array or list, or null.</summary>
    private static Type ElementOf(Type type) =>
        type.IsArray ? type.GetElementType()
        : type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType ? type.GetGenericArguments()[0]
        : null;
}
