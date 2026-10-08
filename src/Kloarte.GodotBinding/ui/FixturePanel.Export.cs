using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: exports of the show (UI review 16, 32; the dialog in FixturePanel.ExportDialog.cs). Stills: a PNG of the
/// stage at every key moment of the timeline (each GO, the end of each fade, the start and end of each actor step) plus
/// index.csv, rendered off screen at the chosen size from the chosen viewpoint. Contact sheet: a PDF with the picture of
/// every cue when its fades are done (FixturePanel.ContactSheet.cs). Movie: the app saves the current state and starts
/// a second Godot process that renders the timeline with Movie Maker at a fixed frame rate (smooth whatever the render
/// speed); its progress (a file the process writes) is a task in the status bar with Cancel, and the AVI is converted
/// to MP4 when ffmpeg is found (MagicQ PC ships one). Per actor: one export per person, only their name on their shirt.
/// </summary>
public partial class FixturePanel
{
    /// <summary>Frame rate of the movie.</summary>
    public const int MovieFps = 30;

    /// <summary>Seconds of the initial state before the first GO in a movie from the start (the render's lead-in).</summary>
    private const double MovieLeadIn = 1.0;

    /// <summary>A movie to render: the AVI, the process arguments, its progress file and what the status bar calls it.</summary>
    private sealed record MovieJob(string Avi, string[] Args, string Progress, string Label);

    /// <summary>Movies waiting to be rendered (per actor: one after the other).</summary>
    private readonly Queue<MovieJob> _movieJobs = new();

    /// <summary>The movie being rendered and its process, the ffmpeg conversion running, the movies done.</summary>
    private MovieJob _movieJob;
    private int _moviePid = -1;
    private readonly List<string> _moviesDone = new();
    private double _moviePoll;

    /// <summary>True while a movie is rendered or waiting to be.</summary>
    public bool RecordingMovie => _movieJob != null || _movieJobs.Count > 0;

    /// <summary>
    /// Saves a PNG of the stage at every key frame of the timeline into `folder` (nnn_t0012.3s_cue5.png) and index.csv
    /// (file, time, cue, what), from the 3D view's camera at the movie size of the preferences. Returns a status message.
    /// </summary>
    public Task<string> ExportKeyFrames(string folder)
    {
        int last = (_player.Stack?.Steps.Count ?? 0) - 1;
        return Export(new ExportOptions(ExportKind.Stills, "3d", -1, last, false, PreferredSize(), false, folder));
    }

    /// <summary>
    /// Starts the movie render of the whole stack from the 3D view's camera (`end` = the last second, else the end of
    /// the stack). Returns a status message.
    /// </summary>
    public string StartMovie(string aviPath, string resolution = "1920x1080", double? end = null)
    {
        int last = (_player.Stack?.Steps.Count ?? 0) - 1;
        return StartMovies(new ExportOptions(ExportKind.Movie, "3d", -1, last, false, ParseSize(resolution), false, aviPath), end);
    }

    /// <summary>Runs an export; returns a status message (a movie only starts: it reports in the status bar).</summary>
    internal async Task<string> Export(ExportOptions options)
    {
        try
        {
            return options.Kind switch
            {
                ExportKind.Movie => StartMovies(options),
                ExportKind.ContactSheet => await ExportContactSheets(options),
                _ => await ExportStills(options),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"Could not export: {e.Message}";
        }
    }

    // --- Viewpoints and cue range ---

    /// <summary>The viewpoints an export can use: key and name (3D view, audience seat, the venue's named ones).</summary>
    internal List<(string Key, string Name)> ExportViewpoints()
    {
        var list = new List<(string, string)> { ("3d", "3D view (its camera now)") };
        if (_host?.Views.OfType<AudienceView>().FirstOrDefault() is { } audience)
            list.Add(("audience", $"Audience view ({audience.Where})"));
        var viewpoints = CurrentBuilder?.Venue?.Auditorium.Viewpoints;
        for (int i = 0; i < (viewpoints?.Count ?? 0); i++)
            list.Add(($"vp:{i}", viewpoints[i].Name));
        return list;
    }

    /// <summary>The camera of a viewpoint in the show's coordinates: position, a point it looks at, field of view.</summary>
    private (Vector3 Position, Vector3 Target, float Fov) ExportCamera(string viewpoint)
    {
        var builder = CurrentBuilder;
        Camera3D camera = viewpoint == "audience" ? _host?.Views.OfType<AudienceView>().FirstOrDefault()?.Camera : FindCamera(GetTree().Root);
        if (viewpoint.StartsWith("vp:") && int.TryParse(viewpoint[3..], out int index)
            && builder?.Venue?.Auditorium is { } aud && aud.Viewpoints.ElementAtOrDefault(index) is { } vp
            && aud.EyeOf(builder.StageEdge, vp) is { } eye)
            return (new Vector3(eye[0], eye[1], eye[2]), AudienceView.LookAtPoint, AudienceView.SeatFov);
        if (camera == null || builder == null)
            return (new Vector3(0, 2.5f, 12f), new Vector3(0, 1f, -4f), 55f);
        return (builder.ToLocal(camera.GlobalPosition), builder.ToLocal(camera.GlobalPosition - camera.GlobalBasis.Z * 10f), camera.Fov);
    }

    /// <summary>The time range of the cues `from` .. `to` (step indices; -1 = from the start, with the lead-in).</summary>
    private (double Start, double End) ExportRange(PlaybackTimeline tl, int from, int to)
    {
        var segments = tl.Segments;
        double start = from < 0 || segments.Count == 0 ? -MovieLeadIn : segments[Math.Min(from, segments.Count - 1)].Start;
        double end = segments.Count == 0 ? 0 : segments[Math.Clamp(to, 0, segments.Count - 1)].End;
        return (start, Math.Max(end, start));
    }

    /// <summary>The actors an export is made for: each actor (per actor), else one export for everybody (null).</summary>
    private List<ActorDef> ExportActors(ExportOptions options) =>
        options.PerActor && CurrentBuilder?.Model is { } model && model.Blocking.Actors.Count > 0
            ? model.Blocking.Actors.ToList()
            : new List<ActorDef> { null };

    /// <summary>The movie size of the preferences.</summary>
    private static Vector2I PreferredSize() => ParseSize(Preferences.Current.MovieResolution);

    /// <summary>"1920x1080" as a size (1920 x 1080 when it doesn't read).</summary>
    private static Vector2I ParseSize(string text)
    {
        var parts = (text ?? "").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0
            ? new Vector2I(w, h) : new Vector2I(1920, 1080);
    }

    /// <summary>A name that can be part of a file name ("Anna B." -> "Anna_B").</summary>
    private static string FileSafe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ' ' or '.' ? '_' : c)).Trim('_');

    // --- Stills ---

    /// <summary>
    /// Saves a PNG at every key frame of the cue range (per actor: a folder per person) and index.csv. The playhead and
    /// the actors' shirts are put back afterwards. Returns a status message.
    /// </summary>
    private async Task<string> ExportStills(ExportOptions options)
    {
        var tl = _player.Timeline;
        if (tl == null)
            return "No cue stack chosen (Playback tab)";
        var (start, end) = ExportRange(tl, options.FromStep, options.ToStep);
        var keys = tl.KeyFrames().Where(k => k.Time >= start - 1e-6 && k.Time <= end + 1e-6).ToList();
        var actors = ExportActors(options);
        double restore = _player.Time;
        int saved = 0;
        using var shooter = new ExportShooter(this, options.Size, ExportCamera(options.Viewpoint), tl, options.Captions);
        try
        {
            foreach (var actor in actors)
            {
                string folder = actor == null ? options.Target : Path.Combine(options.Target, FileSafe(actor.Name));
                Directory.CreateDirectory(folder);
                shooter.ForActor(CurrentBuilder, actor);
                var csv = new StringBuilder("file,time,cue,what\n");
                for (int i = 0; i < keys.Count; i++)
                {
                    var (t, what) = keys[i];
                    _player.Seek(t + 0.001);
                    var image = await shooter.Shoot(t + 0.001);
                    int seg = tl.SegmentAt(t + 0.001);
                    string cue = seg < 0 ? "pre" : tl.Segments[seg].CueNumber;
                    string file = $"{i + 1:000}_t{t.ToString("0000.0", CultureInfo.InvariantCulture)}s_cue{cue}.png";
                    image.SavePng(Path.Combine(folder, file));
                    csv.Append($"{file},{t.ToString("0.00", CultureInfo.InvariantCulture)},{cue},\"{what.Replace("\"", "'")}\"\n");
                    saved++;
                    StatusBar.ShowTask($"Stills{(actor == null ? "" : $" ({actor.Name})")}: {i + 1} / {keys.Count}",
                        (double)(i + 1) / keys.Count, null);
                }
                File.WriteAllText(Path.Combine(folder, "index.csv"), csv.ToString());
            }
        }
        finally
        {
            shooter.ForActor(CurrentBuilder, null);
            StatusBar.EndTask();
            _player.Seek(restore);
        }
        string done = actors.Count > 1
            ? $"Saved {keys.Count} stills for each of {actors.Count} actors in {options.Target}"
            : $"Saved {keys.Count} key frames and index.csv in {options.Target}";
        PostWithFolder(done, options.Target);
        return done;
    }

    /// <summary>Posts a message whose click opens a folder.</summary>
    private void PostWithFolder(string text, string folder) =>
        _status.Post(text, () => OS.ShellOpen(folder), "Click: open the folder");

    // --- Movie ---

    /// <summary>
    /// Queues the movie render(s) of the options (per actor: one movie per person, named after them) and starts the first;
    /// `end` overrides the end of the range (seconds). Returns a status message.
    /// </summary>
    private string StartMovies(ExportOptions options, double? end = null)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _player.Stack == null || _player.Timeline == null)
            return "No show / cue stack to record";
        if (RecordingMovie)
            return "A movie is being recorded already";
        string folder = ProjectSettings.GlobalizePath("user://render");
        string shw = builder.SaveForRender(folder);
        var (p, target, fov) = ExportCamera(options.Viewpoint);
        var (start, rangeEnd) = ExportRange(_player.Timeline, options.FromStep, options.ToStep);
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        _moviesDone.Clear();
        foreach (var actor in ExportActors(options))
        {
            string avi = actor == null ? options.Target
                : Path.Combine(Path.GetDirectoryName(options.Target) ?? "", $"{Path.GetFileNameWithoutExtension(options.Target)}_{FileSafe(actor.Name)}.avi");
            string progress = Path.Combine(folder, $"progress_{_movieJobs.Count}.txt");
            // From the project the second process needs --path; an exported build runs its own .pck.
            var args = AppPaths.Exported ? new List<string>() : new List<string> { "--path", ProjectSettings.GlobalizePath("res://") };
            args.AddRange(new[]
            {
                "--write-movie", avi,
                "--fixed-fps", MovieFps.ToString(CultureInfo.InvariantCulture),
                "--resolution", $"{options.Size.X}x{options.Size.Y}",
                "res://render/show_render.tscn",
                "--",
                "--show=" + shw,
                "--mechanics=" + Path.Combine(folder, "mechanics.json"),
                "--blocking=" + Path.Combine(folder, "blocking.json"),
                "--config=" + builder.FixtureConfig,
                "--venue=" + builder.VenueFile,
                "--stack=" + _player.Stack.Number.ToString(CultureInfo.InvariantCulture),
                "--hold=" + F(_player.Hold),
                "--fps=" + MovieFps.ToString(CultureInfo.InvariantCulture),
                $"--camera={F(p.X)},{F(p.Y)},{F(p.Z)},{F(target.X)},{F(target.Y)},{F(target.Z)},{F(fov)}",
                "--start=" + F(start),
                "--end=" + F(end ?? rangeEnd),
                "--progress=" + progress,
            });
            if (options.Captions)
                args.Add("--caption=1");
            if (actor != null)
                args.Add("--actor=" + actor.Id);
            _movieJobs.Enqueue(new MovieJob(avi, args.ToArray(), progress, actor?.Name ?? Path.GetFileName(avi)));
        }
        int count = _movieJobs.Count;
        StartNextMovie();
        if (_movieJob == null)
            return "Could not start the movie process";
        return count > 1
            ? $"Recording {count} movies ({(end ?? rangeEnd) - start:0} s each), one after the other..."
            : $"Recording {(end ?? rangeEnd) - start:0} s of show into {Path.GetFileName(options.Target)} (in a separate window)...";
    }

    /// <summary>Starts the next queued movie, if any.</summary>
    private void StartNextMovie()
    {
        _movieJob = null;
        if (_movieJobs.Count == 0)
            return;
        var job = _movieJobs.Dequeue();
        Directory.CreateDirectory(Path.GetDirectoryName(job.Avi) ?? ".");
        try
        {
            File.Delete(job.Progress);
        }
        catch (IOException)
        {
        }
        _moviePid = OS.CreateProcess(OS.GetExecutablePath(), job.Args);
        if (_moviePid <= 0)
        {
            _movieJobs.Clear();
            StatusBar.EndTask();
            return;
        }
        _movieJob = job;
        _moviePoll = 1.0;   // show the task at once
    }

    /// <summary>Stops the movie being rendered and drops the others (the status bar's Cancel).</summary>
    private void CancelMovies()
    {
        _movieJobs.Clear();
        if (_moviePid > 0 && OS.IsProcessRunning(_moviePid))
            OS.Kill(_moviePid);
        string avi = _movieJob?.Avi;
        _movieJob = null;
        _moviePid = -1;
        StatusBar.EndTask();
        _status.Text = $"Movie cancelled{(avi != null ? $" ({Path.GetFileName(avi)} is incomplete)" : "")}";
    }

    /// <summary>
    /// Watches the movie process (every second): its progress in the status bar; when it ended, converts the AVI to MP4
    /// (ffmpeg, when found) and starts the next movie; after the last one a message whose click opens the folder.
    /// </summary>
    private void PollMovie(double delta)
    {
        if (_movieJob == null || (_moviePoll += delta) < 1.0)
            return;
        _moviePoll = 0;
        var job = _movieJob;
        int left = _movieJobs.Count;
        if (OS.IsProcessRunning(_moviePid))
        {
            var (frame, total) = ReadProgress(job.Progress);
            string which = left > 0 || _moviesDone.Count > 0 ? $" {_moviesDone.Count + 1} / {_moviesDone.Count + 1 + left}" : "";
            StatusBar.ShowTask(total > 0 ? $"Movie{which} ({job.Label}): frame {frame} / {total}" : $"Movie{which} ({job.Label}): starting...",
                total > 0 ? (double)frame / total : -1, CancelMovies);
            return;
        }
        _moviePid = -1;
        if (File.Exists(job.Avi))
        {
            _moviesDone.Add(job.Avi);
            ConvertToMp4(job.Avi);
        }
        else
            _status.Text = $"The movie process ended without writing {Path.GetFileName(job.Avi)}";
        StartNextMovie();
        if (_movieJob != null)
            return;
        StatusBar.EndTask();
        if (_moviesDone.Count == 0)
            return;
        string folder = Path.GetDirectoryName(_moviesDone[0]) ?? "";
        string what = _moviesDone.Count == 1 ? $"Movie saved: {_moviesDone[0]}" : $"{_moviesDone.Count} movies saved in {folder}";
        PostWithFolder(what + (FindFfmpeg() != null ? " (converting to MP4)" : ""), folder);
    }

    /// <summary>The progress file's "frame total" (0, 0 when it isn't there yet).</summary>
    private static (int Frame, int Total) ReadProgress(string file)
    {
        try
        {
            var parts = File.Exists(file) ? File.ReadAllText(file).Split(' ') : Array.Empty<string>();
            return parts.Length == 2 && int.TryParse(parts[0], out int f) && int.TryParse(parts[1], out int t) ? (f, t) : (0, 0);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    /// <summary>Converts an AVI to MP4 next to it when ffmpeg is found (in the background).</summary>
    private void ConvertToMp4(string avi)
    {
        string ffmpeg = FindFfmpeg();
        if (ffmpeg == null)
            return;
        string mp4 = Path.ChangeExtension(avi, ".mp4");
        OS.CreateProcess(ffmpeg, new[] { "-y", "-loglevel", "error", "-i", avi, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", mp4 });
    }

    /// <summary>ffmpeg from MagicQ PC or the PATH, or null.</summary>
    private static string FindFfmpeg()
    {
        const string magicQ = "C:/Program Files/ChamSys Ltd/MagicQ PC/ffmpeg.exe";
        if (File.Exists(magicQ))
            return magicQ;
        foreach (string dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string exe = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(exe))
                    return exe;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }
}
