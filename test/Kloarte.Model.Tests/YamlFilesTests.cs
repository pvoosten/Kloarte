using System.IO;
using System.Linq;
using System.Text.Json;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The project files (YAML, user decisions of 11 Oct 2026): the codec's text, the warnings and errors with their location,
/// the write-only-when-changed rule with hand-written comments, the schemas and the mapping of lamps and blocking.
/// </summary>
public sealed class YamlFilesTests : TestBase
{
    /// <summary>Fresh output folder for every test.</summary>
    public YamlFilesTests() : base("model_yaml")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        Directory.CreateDirectory(_out);
    }

    /// <summary>A lamp file with a few values of each kind.</summary>
    private static LampFile SampleLamp() => new()
    {
        RigItem = 75,
        Model = "adb-c103",
        FieldAngle = 25.5,
        Color = "#ff8800",
        FocusPoint = new[] { 1.25, 2, -3 },
        Shutters = new LampFileShutters { Top = new LampFileBlade { Depth = 0.35, Angle = -5 } },
    };

    /// <summary>A lamp file's text is the modeline, Kloarte's header, then the values in the file's key order.</summary>
    [Fact]
    public void LampFileTextHasModelineHeaderAndFileStyles()
    {
        string text = FileCodec.Current.Write(SampleLamp(),
            ProjectSchemas.ModelineFor(ProjectSchemas.LampSchema, ProjectFolder.MechanicsDirectory),
            new[] { FileText.HeaderLine("Head 12 · DMX 1-012") });
        var lines = text.Split('\n');
        Assert.Equal("# yaml-language-server: $schema=../.schemas/lamp.schema.json", lines[0]);
        Assert.Equal("# Kloarte: Head 12 · DMX 1-012", lines[1]);
        Assert.Contains("rigItem: 75", text);
        Assert.Contains("color: \"#ff8800\"", text);                    // colours always quoted
        Assert.Contains("focusPoint: [1.25, 2, -3]", text);            // short number lists in flow style
        Assert.Contains("top: {depth: 0.35, angle: -5}", text);        // a shutter blade in flow style
        Assert.True(text.IndexOf("rigItem") < text.IndexOf("model:"), "keys in the order of the properties");
    }

    /// <summary>What is written reads back to the same values.</summary>
    [Fact]
    public void LampFileRoundTrips()
    {
        string text = FileCodec.Current.Write(SampleLamp(), null, Enumerable.Empty<string>());
        var (value, problems) = FileCodec.Current.Read<LampFile>("lamp.yaml", text);
        Assert.Empty(problems);
        Assert.Equal(75, value.RigItem);
        Assert.Equal("#ff8800", value.Color);
        Assert.Equal(new[] { 1.25, 2, -3 }, value.FocusPoint);
        Assert.Equal(0.35, value.Shutters.Top.Depth);
        Assert.Equal(-5, value.Shutters.Top.Angle);
    }

    /// <summary>A key Kloarte doesn't know and a number out of range are warnings: the file is still applied.</summary>
    [Fact]
    public void UnknownKeysAndOutOfRangeValuesAreWarnings()
    {
        const string text = "rigItem: 7\nfieldAngle: 400\nfocusPont: [1, 2, 3]\n";
        var (value, problems) = FileCodec.Current.Read<LampFile>("lamp.yaml", text);
        Assert.NotNull(value);
        Assert.Equal(7, value.RigItem);
        Assert.Equal(400, value.FieldAngle);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.False(p.IsError));
        Assert.Contains(problems, p => p.Message.Contains("focusPont") && p.Line == 3);
        Assert.Contains(problems, p => p.Message.Contains("fieldAngle 400") && p.Line == 2);
    }

    /// <summary>A syntax error or a value of the wrong kind is an error with its line and column; nothing is applied.</summary>
    [Fact]
    public void BrokenFilesAreErrorsWithTheirLocation()
    {
        var (value, problems) = FileCodec.Current.Read<LampFile>("lamp.yaml", "rigItem: 7\nfieldAngle: [\n");
        Assert.Null(value);
        Assert.Contains(problems, p => p.IsError && p.Line > 0);

        var (wrong, wrongProblems) = FileCodec.Current.Read<LampFile>("lamp.yaml", "rigItem: seven\n");
        Assert.Null(wrong);
        Assert.Contains(wrongProblems, p => p.IsError && p.Line == 1 && p.ToString().StartsWith("lamp.yaml:1:"));

        var (empty, emptyProblems) = FileCodec.Current.Read<LampFile>("lamp.yaml", "# only a comment\n");
        Assert.Null(empty);
        Assert.Contains(emptyProblems, p => p.IsError && p.Message == "the file is empty");
    }

    /// <summary>A hand-written comment doesn't count as a change; Kloarte's header and the modeline do.</summary>
    [Fact]
    public void CommentsAreNotContent()
    {
        const string plain = "# yaml-language-server: $schema=x\n# Kloarte: lamp 1\nrigItem: 1\n";
        const string noted = "# yaml-language-server: $schema=x\n# my note: check the gel\n# Kloarte: lamp 1\nrigItem: 1   \n";
        Assert.Equal(FileText.Content(plain), FileText.Content(noted));
        Assert.NotEqual(FileText.Content(plain), FileText.Content(plain.Replace("lamp 1", "lamp 2")));
        Assert.NotEqual(FileText.Content(plain), FileText.Content(plain.Replace("rigItem: 1", "rigItem: 2")));
    }

    /// <summary>A file is written only when its content changed; a hand-written comment survives an unchanged save.</summary>
    [Fact]
    public void FilesAreWrittenOnlyWhenTheContentChanged()
    {
        var folder = new ProjectFolder(_out);
        string path = folder.PathOf("mechanics/a.yaml");
        string text = FileCodec.Current.Write(SampleLamp(), null, new[] { FileText.HeaderLine("lamp") });

        Assert.Equal(FileWriteKind.Written, folder.Write(path, text).Kind);
        File.WriteAllText(path, File.ReadAllText(path).Replace("rigItem: 75", "# my note\nrigItem: 75"));
        Assert.Equal(FileWriteKind.Unchanged, folder.Write(path, text).Kind);
        Assert.Contains("# my note", File.ReadAllText(path));

        string changed = FileCodec.Current.Write(new LampFile { RigItem = 75, FieldAngle = 30 }, null, new[] { FileText.HeaderLine("lamp") });
        Assert.Equal(FileWriteKind.Written, folder.Write(path, changed).Kind);
        Assert.DoesNotContain("# my note", File.ReadAllText(path));
    }

    /// <summary>A file changed outside Kloarte is not overwritten (a conflict) unless the user chose to (force).</summary>
    [Fact]
    public void ExternalChangesAreNotOverwrittenSilently()
    {
        var folder = new ProjectFolder(_out);
        string path = folder.PathOf("blocking/actors.yaml");
        string text = FileCodec.Current.Write(new ActorsFile(), null, new[] { FileText.HeaderLine("actors") });
        folder.Write(path, text);

        File.WriteAllText(path, "actors:\n  - id: a1\n    name: Edited\n");
        var conflict = folder.Write(path, text + "\n# edited");
        Assert.Equal(FileWriteKind.Conflict, conflict.Kind);
        Assert.Contains("Edited", File.ReadAllText(path));
        Assert.Equal(FileWriteKind.Written, folder.Write(path, text, force: true).Kind);
    }

    /// <summary>A file that doesn't parse, and that Kloarte never read, is never overwritten.</summary>
    [Fact]
    public void AFileThatDoesNotParseIsNeverOverwritten()
    {
        var folder = new ProjectFolder(_out);
        string path = folder.PathOf("mechanics/broken.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "rigItem: [\n");
        var write = folder.Write(path, FileCodec.Current.Write(SampleLamp(), null, Enumerable.Empty<string>()));
        Assert.Equal(FileWriteKind.Conflict, write.Kind);
        Assert.Equal("rigItem: [\n", File.ReadAllText(path));
    }

    /// <summary>The JSON Schemas are valid JSON, close (unknown keys flagged), and describe the fields with their units.</summary>
    [Fact]
    public void SchemasDescribeTheFields()
    {
        var lamp = JsonDocument.Parse(SchemaWriter.Write(typeof(LampFile), "lamp")).RootElement;
        Assert.Equal("object", lamp.GetProperty("type").GetString());
        Assert.False(lamp.GetProperty("additionalProperties").GetBoolean());
        var fieldAngle = lamp.GetProperty("properties").GetProperty("fieldAngle");
        Assert.Equal(1, fieldAngle.GetProperty("minimum").GetDouble());
        Assert.Contains("°", fieldAngle.GetProperty("description").GetString());
        var mounting = lamp.GetProperty("properties").GetProperty("mounting");
        Assert.Contains("top", mounting.GetProperty("description").GetString() ?? "");

        var steps = JsonDocument.Parse(SchemaWriter.Write(typeof(StepsFile), "steps")).RootElement;
        Assert.True(steps.GetProperty("$defs").TryGetProperty("CueEntry", out _));
        Assert.Equal(SchemaWriter.Write(typeof(LampFile), "lamp"), SchemaWriter.Write(typeof(LampFile), "lamp"));
    }

    /// <summary>
    /// The schema copies in the host (data/schemas, the house venue's) are the generated ones. With the environment
    /// variable KLOARTE_UPDATE_SCHEMAS=1 the test writes them instead (after a change of a file class).
    /// </summary>
    [Fact]
    public void CommittedSchemasAreCurrent()
    {
        foreach (var (file, text) in ProjectSchemas.All())
        {
            string path = TestEnvironment.File("res://data/schemas/" + file);
            if (System.Environment.GetEnvironmentVariable("KLOARTE_UPDATE_SCHEMAS") == "1")
                File.WriteAllText(path, text);
            Assert.Equal(text, File.ReadAllText(path));
        }
    }

    /// <summary>
    /// The house venue is YAML only (user: the JSON of before is deleted): it reads without problems, points at the
    /// committed schema, and round trips through the codec.
    /// </summary>
    [Fact]
    public void HouseVenueIsYaml()
    {
        string path = TestEnvironment.File("res://venue/ccdebrouckere.venue.yaml");
        Assert.False(File.Exists(Path.ChangeExtension(path, null) + ".json"), "the house venue JSON is gone");
        string text = File.ReadAllText(path);
        Assert.StartsWith("# yaml-language-server: $schema=../data/schemas/venue.schema.json", text);
        var (venue, problems) = FileCodec.Current.Read<Venue>(path, text);
        Assert.Empty(problems);
        Assert.Equal(2.15f, venue.Forestage);
        Assert.Equal(415, venue.Auditorium.Layout(venue.Forestage).Sum(r => r.Seats.Count));

        string again = FileCodec.Current.Write(venue, null, Enumerable.Empty<string>());
        var (back, _) = FileCodec.Current.Read<Venue>("venue.yaml", again);
        Assert.Equal(JsonSerializer.Serialize(venue, Venue.Json), JsonSerializer.Serialize(back, Venue.Json));
    }

    /// <summary>A lamp's mechanics round trip through its file, rounded as the file rounds them.</summary>
    [Fact]
    public void MechanicsRoundTripThroughTheLampFile()
    {
        var mechanics = new FixtureMechanics { Model = "adb-c103", Kind = FixtureKind.Pc, FieldAngle = 27.04f, Color = "#ff8800" };
        var file = MechanicsDiff.ToFile(75, mechanics, null);
        Assert.Equal(27.0, file.FieldAngle);
        string text = FileCodec.Current.Write(file, null, Enumerable.Empty<string>());
        var (read, problems) = FileCodec.Current.Read<LampFile>("lamp.yaml", text);
        Assert.Empty(problems);
        var back = MechanicsDiff.FromFile(read, null);
        Assert.Equal(27f, back.FieldAngle, 3);
        Assert.Equal("#ff8800", back.Color);
    }

    /// <summary>The actors and steps (holds and movement) round trip through the two blocking files.</summary>
    [Fact]
    public void BlockingRoundTripsThroughItsFiles()
    {
        var blocking = new Blocking
        {
            Actors = { new ActorDef { Id = "a1", Name = "Anna", Female = true, ShirtColor = "ff8800", Height = 1.7f } },
            Holds = { [5] = 2.5f },
            Smoke = { [5] = new CueSmoke { Amount = 42.04, Time = 30f }, [7] = new CueSmoke { Amount = 0 }, [9] = new CueSmoke { Amount = 100, Time = 0f } },
        };
        blocking.Cues[5] = new() { ["a1"] = new() { new ActorState { Delay = 1.25f, Position = new[] { 1f, 0f, 2f }, Visible = true, Pose = "seated" } } };
        var cues = CueIndex.Of(null);
        string actors = FileCodec.Current.Write(BlockingFiles.ActorsOf(blocking), null, Enumerable.Empty<string>());
        string steps = FileCodec.Current.Write(BlockingFiles.StepsOf(blocking, cues), null, Enumerable.Empty<string>());
        var (actorsFile, p1) = FileCodec.Current.Read<ActorsFile>("actors.yaml", actors);
        var (stepsFile, p2) = FileCodec.Current.Read<StepsFile>("steps.yaml", steps);
        Assert.Empty(p1.Concat(p2));

        var problems = new System.Collections.Generic.List<FileProblem>();
        var back = BlockingFiles.FromFiles(actorsFile, stepsFile, null, cues, "steps.yaml", "scenery.yaml", problems);
        Assert.Empty(problems);
        Assert.Equal("Anna", back.Find("a1").Name);
        Assert.Equal("ff8800", back.Find("a1").ShirtColor);
        Assert.Equal(2.5f, back.Holds[5]);
        Assert.Equal(42.0, back.Smoke[5].Amount, 1);   // smoke rounds to a tenth of a percent
        Assert.Equal(30f, back.Smoke[5].Time);
        Assert.Equal(0.0, back.Smoke[7].Amount);       // "no smoke" is a value, not a missing entry
        Assert.Null(back.Smoke[7].Time);
        Assert.Equal(Blocking.DefaultSmokeTime, back.Smoke[7].Seconds);
        Assert.Equal(0f, back.Smoke[9].Time);
        Assert.Equal(3, back.Smoke.Count);
        Assert.Contains("smoke: 42", steps);
        Assert.Contains("smokeTime: 30", steps);
        var step = back.Cues[5]["a1"].Single();
        Assert.Equal(1.3f, step.Delay!.Value, 3);   // steps round to a tenth of a second
        Assert.Equal("seated", step.Pose);
        Assert.Equal(new[] { 1f, 0f, 2f }, step.Position);
    }
}
