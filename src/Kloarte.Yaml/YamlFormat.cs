using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Kloarte.Model;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Kloarte.Yaml;

/// <summary>
/// How Kloarte writes and reads its YAML project files (YamlDotNet; user decision of 11 Oct 2026): camelCase keys in the
/// order of the file types' properties, nulls left out, numbers as short as they are (the file types round them), short
/// number lists (<c>focusPoint: [x, y, z]</c>) and the <see cref="FileFlowAttribute"/> types in flow style, colours
/// (text starting with "#") always quoted, and no aliases. Every written file starts with the modeline (the JSON Schema
/// for VS Code) and then the header lines.
/// </summary>
public static class YamlFormat
{
    /// <summary>The serializer (thread-safe, built once).</summary>
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .WithQuotingNecessaryStrings()
        .WithTypeConverter(new NumberConverter())
        .WithTypeConverter(new ScalarConverter())
        .WithTypeInspector(inner => new WritableOnly(inner))
        .WithEventEmitter(next => new FileStyleEmitter(next))
        .WithIndentedSequences()
        .DisableAliases()
        .Build();

    /// <summary>The deserializer: keys Kloarte doesn't know are skipped (<see cref="YamlFileReader"/> warns about them).</summary>
    internal static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new NumberConverter())
        .WithTypeConverter(new ScalarConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// A file's text: the modeline for `schema` (a path relative to the file; null = none), the header lines (each a full
    /// comment line) and then the value. Line ends "\n".
    /// </summary>
    public static string Write(object value, string schema = null, IEnumerable<string> header = null)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(schema))
            text.Append("# yaml-language-server: $schema=").Append(schema).Append('\n');
        foreach (string line in header ?? Enumerable.Empty<string>())
            text.Append(line).Append('\n');
        text.Append(Serializer.Serialize(value).Replace("\r\n", "\n"));
        return text.ToString();
    }

    /// <summary>Doubles as short as they are ("0.1", "6", never "0.10000000000000001"), read with a point.</summary>
    private sealed class NumberConverter : IYamlTypeConverter
    {
        /// <summary>True for double and double? (the file types' numbers).</summary>
        public bool Accepts(Type type) => type == typeof(double) || type == typeof(double?);

        /// <summary>Reads a number with a point; an empty or "~" value of a nullable number is null.</summary>
        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            if (type == typeof(double?) && scalar.Value is "" or "~" or "null")
                return null;
            if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            throw new YamlException(scalar.Start, scalar.End, $"'{scalar.Value}' is not a number (use a point: 1.5)");
        }

        /// <summary>Writes the number in its shortest round-trip form.</summary>
        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer) =>
            emitter.Emit(new Scalar(((double)value!).ToString("R", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The <see cref="FileScalarAttribute"/> types (<see cref="LookValue"/>): one scalar, plain when it is a number,
    /// quoted when YAML needs it; read with the type's <c>Parse(string)</c> (a value it refuses is an error at its place).
    /// </summary>
    private sealed class ScalarConverter : IYamlTypeConverter
    {
        /// <summary>True for a type marked <see cref="FileScalarAttribute"/>.</summary>
        public bool Accepts(Type type) => IsScalar(type);

        /// <summary>Reads the scalar with the type's Parse.</summary>
        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            try
            {
                return type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) })!
                    .Invoke(null, new object[] { scalar.Value });
            }
            catch (TargetInvocationException e) when (e.InnerException is FormatException f)
            {
                throw new YamlException(scalar.Start, scalar.End, f.Message);
            }
        }

        /// <summary>Writes the value's text: plain for a number, else as YAML needs it.</summary>
        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
        {
            string text = value?.ToString() ?? "";
            bool number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            emitter.Emit(new Scalar(null, null, text, number ? ScalarStyle.Plain : ScalarStyle.Any, true, !number));
        }
    }

    /// <summary>
    /// Only the properties a file can set are written (as the JSON Schemas have them): a property without a setter is
    /// something the file type computes.
    /// </summary>
    private sealed class WritableOnly : YamlDotNet.Serialization.TypeInspectors.TypeInspectorSkeleton
    {
        /// <summary>The inspector it filters.</summary>
        private readonly ITypeInspector _inner;

        /// <summary>Filters an inspector.</summary>
        public WritableOnly(ITypeInspector inner) => _inner = inner;

        /// <inheritdoc />
        public override string GetEnumName(Type enumType, string name) => _inner.GetEnumName(enumType, name);

        /// <inheritdoc />
        public override string GetEnumValue(object enumValue) => _inner.GetEnumValue(enumValue);

        /// <inheritdoc />
        public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object container) =>
            _inner.GetProperties(type, container).Where(p => p.CanWrite);
    }

    /// <summary>True for a type marked <see cref="FileScalarAttribute"/>.</summary>
    private static bool IsScalar(Type type) => type.GetCustomAttribute<FileScalarAttribute>() != null;

    /// <summary>Number lists and the <see cref="FileFlowAttribute"/> types in flow style; colours quoted.</summary>
    private sealed class FileStyleEmitter : ChainedEventEmitter
    {
        /// <summary>Wraps the next emitter.</summary>
        public FileStyleEmitter(IEventEmitter next) : base(next) { }

        /// <summary>Number lists in flow style ([x, y, z]).</summary>
        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            var type = eventInfo.Source.Type;
            if (type == typeof(double[]) || type == typeof(float[]) || type == typeof(int[]))
                eventInfo.Style = SequenceStyle.Flow;
            base.Emit(eventInfo, emitter);
        }

        /// <summary>Flow-style types ({depth: 0.35, angle: -5}) in flow style.</summary>
        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            var type = eventInfo.Source.Type;
            if (type.GetCustomAttribute<FileFlowAttribute>() != null
                || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && IsScalar(type.GetGenericArguments()[1]))
                eventInfo.Style = MappingStyle.Flow;
            base.Emit(eventInfo, emitter);
        }

        /// <summary>A text starting with "#" (a colour) is always double quoted: "#rrggbb" (a plain "#" would be a comment).</summary>
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string { Length: > 0 } text && text.StartsWith('#'))
                eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
        }
    }
}
