using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Host;

/// <summary>
/// Renders a show as a movie (run by the app as a second Godot process with Movie Maker:
/// <c>--write-movie out.avi --fixed-fps 30 res://render/show_render.tscn -- --show=... --stack=...</c>). Builds the
/// show from the files the app saved for it (show, mechanics, blocking, venue) and plays the cue stack's timeline
/// deterministically: frame n shows time n / fps - lead-in, so the movie has the timeline's timing whatever the render
/// speed. Quits after the last cue (incl. its hold). User arguments (after "--"):
/// --show, --mechanics, --blocking, --config, --venue (paths), --stack (cue stack number), --hold (default hold, s),
/// --fps (same as --fixed-fps),
/// --camera=x,y,z,tx,ty,tz,fov (stage coordinates of the camera and its target), --start, --end (seconds, optional),
/// --progress=file (written every few frames: "frame total", for the app's progress bar), --caption=1 (the cue as a
/// caption), --actor=id (a movie for one person: only their name on their shirt, their name in the caption).
/// </summary>
public partial class ShowRender : Node3D
{
    /// <summary>Seconds of the initial state before the first GO.</summary>
    public const double LeadIn = 1.0;

    private ShowBuilder _builder;
    private CuePlayer _player;
    private Camera3D _camera;
    private double _start = -LeadIn, _end = double.NaN;
    private int _frame;
    private int _fps = 30;

    /// <summary>The progress file ("frame total"), or null.</summary>
    private string _progress;

    /// <summary>The caption (cue, actor), or null without one.</summary>
    private ExportCaption _caption;

    /// <summary>Builds the scene from the command line.</summary>
    public override void _Ready()
    {
        var args = ParseArgs(OS.GetCmdlineUserArgs());
        // Must match --fixed-fps of the Movie Maker command line (the app passes both).
        _fps = int.TryParse(args.GetValueOrDefault("fps", "30"), out int fps) && fps > 0 ? fps : 30;
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = GD.Load<Environment>("res://environment/theatre_environment.tres") });
        _builder = new ShowBuilder
        {
            Name = "Show",
            ShowFile = args.GetValueOrDefault("show", ""),
            MechanicsFile = args.GetValueOrDefault("mechanics", ""),
            BlockingFile = args.GetValueOrDefault("blocking", ""),
            FixtureConfig = args.GetValueOrDefault("config", ""),
            VenueFile = args.GetValueOrDefault("venue", "res://venue/ccdebrouckere.venue.yaml"),
        };
        AddChild(_builder);
        AddChild(new StrayLight { Name = "StrayLight", WorldEnvironment = "../WorldEnvironment" });
        _camera = new Camera3D { Current = true, Fov = 55f };
        AddChild(_camera);
        var c = (args.GetValueOrDefault("camera", "0,2.5,12,0,1,-4,55")).Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        _camera.Position = _builder.ToGlobal(new Vector3(c[0], c[1], c[2]));
        _camera.LookAt(_builder.ToGlobal(new Vector3(c[3], c[4], c[5])));
        if (c.Length > 6)
            _camera.Fov = c[6];

        _player = new CuePlayer { Builder = _builder };
        AddChild(_player);
        var stack = _builder.Model?.Show.CueStacks.FirstOrDefault(s => s.Number.ToString(CultureInfo.InvariantCulture) == args.GetValueOrDefault("stack", ""))
                    ?? _builder.Model?.Show.CueStacks.FirstOrDefault(s => s.Name == "show");
        if (args.TryGetValue("hold", out var hold))
            _player.Hold = float.Parse(hold, CultureInfo.InvariantCulture);
        _player.SetStack(stack);
        if (args.TryGetValue("start", out var start))
            _start = double.Parse(start, CultureInfo.InvariantCulture);
        _end = args.TryGetValue("end", out var end) ? double.Parse(end, CultureInfo.InvariantCulture)
            : (_player.Timeline?.Length ?? 0) + LeadIn;
        _progress = args.GetValueOrDefault("progress");
        string actorName = null;
        if (args.TryGetValue("actor", out var actorId))
            actorName = ExportCaption.ShowOnlyActor(_builder, actorId);
        if (args.GetValueOrDefault("caption") == "1" || actorName != null)
        {
            var layer = new CanvasLayer();
            AddChild(layer);
            _caption = new ExportCaption { Timeline = _player.Timeline, ActorName = actorName, ShowCue = args.GetValueOrDefault("caption") == "1" };
            layer.AddChild(_caption);
        }
        GD.Print($"ShowRender: {stack?.Name}, {_player.Timeline?.Segments.Count} cues, {_start:0.0} .. {_end:0.0} s at {_fps} fps");
    }

    /// <summary>One movie frame = one step of 1/fps on the timeline.</summary>
    public override void _Process(double delta)
    {
        double t = _start + _frame / (double)_fps;
        _frame++;
        int total = (int)System.Math.Ceiling((_end - _start) * _fps) + 1;
        if (t > _end)
        {
            GD.Print($"ShowRender: done, {_frame - 1} frames");
            WriteProgress(total, total);
            GetTree().Quit();
            return;
        }
        _player.Seek(t);
        _caption?.ShowAt(t);
        if (_frame % 10 == 1)
            WriteProgress(_frame - 1, total);
    }

    /// <summary>Writes "frame total" to the progress file (the app reads it for its progress bar).</summary>
    private void WriteProgress(int frame, int total)
    {
        if (string.IsNullOrEmpty(_progress))
            return;
        try
        {
            System.IO.File.WriteAllText(_progress, $"{frame} {total}");
        }
        catch (System.IO.IOException)
        {
            // the app is reading it: the next one will do
        }
    }

    /// <summary>"--key=value" arguments.</summary>
    private static Dictionary<string, string> ParseArgs(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>();
        foreach (var a in args)
        {
            var s = a.TrimStart('-');
            int eq = s.IndexOf('=');
            if (eq > 0)
                result[s[..eq]] = s[(eq + 1)..];
        }
        return result;
    }
}
