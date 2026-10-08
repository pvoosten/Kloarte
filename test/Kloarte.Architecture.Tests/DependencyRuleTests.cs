using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The dependency rules between the projects under <c>src</c> (CLAUDE.md "Dependency rules"; user: important).
/// Reads the compiled assemblies' metadata (assembly and type references), so every use counts, also one that a
/// <c>using</c> or a fully qualified name would hide, and nothing is loaded (no Godot needed).
/// </summary>
public sealed class DependencyRuleTests
{
    /// <summary>The model.</summary>
    private const string Model = "Kloarte.Model";

    /// <summary>The MagicQ binding (files, MagicQ PC, Art-Net).</summary>
    private const string MagicQ = "Kloarte.MagicQ";

    /// <summary>The Godot binding.</summary>
    private const string GodotBinding = "Kloarte.GodotBinding";

    /// <summary>The application.</summary>
    private const string Application = "Kloarte.Application";

    /// <summary>The project files' codec (YamlDotNet): the only project that uses YAML; the host sets it.</summary>
    private const string Yaml = "Kloarte.Yaml";

    /// <summary>Godot's C# API.</summary>
    private const string Godot = "GodotSharp";

    /// <summary>The model depends on nothing but .NET: no Godot, no MagicQ binding, no application.</summary>
    [Fact]
    public void ModelUsesOnlyDotNet() => AssertReferences(Model);

    /// <summary>The MagicQ binding uses the model; it knows nothing of Godot or the application.</summary>
    [Fact]
    public void MagicQBindingUsesOnlyTheModel() => AssertReferences(MagicQ, Model);

    /// <summary>The Godot binding uses the model and Godot; it knows nothing of MagicQ or the application.</summary>
    [Fact]
    public void GodotBindingUsesOnlyTheModelAndGodot() => AssertReferences(GodotBinding, Model, Godot);

    /// <summary>The YAML codec uses the model and YamlDotNet only (the model defines the files; the codec writes them).</summary>
    [Fact]
    public void YamlCodecUsesOnlyTheModelAndYamlDotNet() => AssertReferences(Yaml, Model, "YamlDotNet");

    /// <summary>The application never uses Godot itself, only the model and the two bindings.</summary>
    [Fact]
    public void ApplicationDoesNotUseGodot() => AssertReferences(Application, Model, MagicQ, GodotBinding);

    /// <summary>
    /// The application acts on MagicQ and Godot only through the bindings' interfaces: every type it names from a
    /// binding is an interface (the host, the composition root, hands it the implementations).
    /// </summary>
    [Fact]
    public void ApplicationUsesOnlyTheBindingsInterfaces()
    {
        using var application = Open(Application);
        var bindings = new[] { MagicQ, GodotBinding }.ToDictionary(n => n, Open);
        try
        {
            var notInterfaces = new List<string>();
            foreach (var handle in application.Reader.TypeReferences)
            {
                var reference = application.Reader.GetTypeReference(handle);
                var assembly = ScopeAssembly(application.Reader, reference);
                if (assembly is null || !bindings.TryGetValue(assembly, out var binding))
                    continue;
                var name = FullName(application.Reader, reference);
                if (!binding.IsInterface(name))
                    notInterfaces.Add($"{assembly}: {name}");
            }
            Assert.True(notInterfaces.Count == 0,
                "Kloarte.Application uses binding types that are not interfaces:\n  " +
                string.Join("\n  ", notInterfaces.Distinct().Order()));
        }
        finally
        {
            foreach (var binding in bindings.Values)
                binding.Dispose();
        }
    }

    /// <summary>Every type of a project is in the project's namespace (namespaces = project names).</summary>
    [Fact]
    public void TypesAreInTheirProjectsNamespace()
    {
        var misplaced = new List<string>();
        foreach (var name in new[] { Model, MagicQ, GodotBinding, Application, Yaml })
        {
            using var assembly = Open(name);
            var reader = assembly.Reader;
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if (type.GetDeclaringType().IsNil == false)
                    continue;
                var ns = reader.GetString(type.Namespace);
                var typeName = reader.GetString(type.Name);
                // <Module>, compiler-generated helpers, and the attributes the compiler embeds.
                if (typeName.StartsWith('<') || ns.StartsWith("Microsoft.CodeAnalysis") ||
                    ns == "System.Runtime.CompilerServices")
                    continue;
                if (ns != name && !ns.StartsWith(name + "."))
                    misplaced.Add($"{name}: {ns}.{typeName}");
            }
        }
        Assert.True(misplaced.Count == 0, "Types outside their project's namespace:\n  " + string.Join("\n  ", misplaced));
    }

    /// <summary>
    /// Asserts that <paramref name="name"/> references no assembly but .NET's and <paramref name="allowed"/>.
    /// </summary>
    private static void AssertReferences(string name, params string[] allowed)
    {
        using var assembly = Open(name);
        var reader = assembly.Reader;
        var forbidden = reader.AssemblyReferences
            .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
            .Where(n => !IsDotNet(n) && !allowed.Contains(n))
            .Order()
            .ToList();
        Assert.True(forbidden.Count == 0, $"{name} must not reference {string.Join(", ", forbidden)}" +
            $" (allowed: .NET{string.Concat(allowed.Select(a => ", " + a))})");
    }

    /// <summary>Whether an assembly is part of .NET itself.</summary>
    private static bool IsDotNet(string assembly) =>
        assembly is "mscorlib" or "netstandard" || assembly == "System" || assembly.StartsWith("System.") ||
        assembly.StartsWith("Microsoft.");

    /// <summary>The assembly a type reference points into (through its outermost declaring type), or null.</summary>
    private static string? ScopeAssembly(MetadataReader reader, TypeReference reference)
    {
        var scope = reference.ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
            : null;
    }

    /// <summary>A referenced type's full name (<c>Namespace.Outer+Nested</c>).</summary>
    private static string FullName(MetadataReader reader, TypeReference reference)
    {
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return FullName(reader, reader.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope)) + "+" + name;
        var ns = reader.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>Opens one of the src projects' compiled assemblies.</summary>
    private static CompiledAssembly Open(string name) => new(Path.Combine(ReadFolderOf(name), name + ".dll"));

    /// <summary>
    /// The folder a src assembly is read from: the application's (it holds the copies of the assemblies it references,
    /// the Godot binding's included, which the Godot SDK builds elsewhere), except for the YAML codec the application
    /// doesn't reference, whose own folder is read.
    /// </summary>
    private static string ReadFolderOf(string name) => OutputFolder(name == Yaml ? Yaml : Application);

    /// <summary>
    /// The output folder of a src project (its own assembly is in it), built in the same configuration as this test
    /// project.
    /// </summary>
    private static string OutputFolder(string project)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = output.Name;
        var configuration = output.Parent!.Name;
        var root = output;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kloarte.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root!.FullName, "src", project, "bin", configuration, framework);
    }

    /// <summary>A compiled assembly opened for its metadata.</summary>
    private sealed class CompiledAssembly : IDisposable
    {
        /// <summary>The open file.</summary>
        private readonly PEReader pe;

        /// <summary>The full names of the assembly's interfaces (nested ones as <c>Outer+Nested</c>).</summary>
        private HashSet<string>? interfaces;

        /// <summary>Opens <paramref name="path"/>.</summary>
        public CompiledAssembly(string path)
        {
            Assert.True(File.Exists(path), $"{path} not found: build the solution first");
            pe = new PEReader(File.OpenRead(path));
            Reader = pe.GetMetadataReader();
        }

        /// <summary>The assembly's metadata.</summary>
        public MetadataReader Reader { get; }

        /// <summary>Whether the type with this full name is one of the assembly's interfaces.</summary>
        public bool IsInterface(string fullName)
        {
            interfaces ??= Reader.TypeDefinitions
                .Select(Reader.GetTypeDefinition)
                .Where(t => (t.Attributes & TypeAttributes.Interface) != 0)
                .Select(FullName)
                .ToHashSet();
            return interfaces.Contains(fullName);
        }

        /// <summary>A defined type's full name (<c>Namespace.Outer+Nested</c>).</summary>
        private string FullName(TypeDefinition type)
        {
            var name = Reader.GetString(type.Name);
            var outer = type.GetDeclaringType();
            if (!outer.IsNil)
                return FullName(Reader.GetTypeDefinition(outer)) + "+" + name;
            var ns = Reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        /// <inheritdoc/>
        public void Dispose() => pe.Dispose();
    }
}
