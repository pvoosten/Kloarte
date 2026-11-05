using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Kloarte.MagicQ;

namespace Kloarte.MagicQVerify;

/// <summary>
/// One verification: copies the show, starts MagicQ PC on the copy, plays every cue of a stack and compares the Art-Net
/// output with what the show file says. status.dat is put back afterwards, and MagicQ is left closed.
/// </summary>
internal sealed class ShowRun
{
    /// <summary>The output counts as settled after this many seconds without a change.</summary>
    private const double QuietSeconds = 1.6;

    /// <summary>Seconds to go on waiting for a fade after its expected end (an FX never settles).</summary>
    private const double SettleLimit = 6;

    /// <summary>
    /// Seconds after which a Go that moved nothing counts as lost. Well over the idle Art-Net rate (about 1 packet a
    /// second per universe): an instant cue can show up only with the next packet.
    /// </summary>
    private const double RetrySeconds = 3;

    /// <summary>Time between looks at the output, in ms.</summary>
    private const int PollMilliseconds = 15;

    /// <summary>The command line.</summary>
    private readonly VerifyOptions _options;

    /// <summary>The report so far.</summary>
    private readonly StringBuilder _report = new();

    /// <summary>Creates a run.</summary>
    public ShowRun(VerifyOptions options) => _options = options;

    /// <summary>Runs the verification; true when every cue passed.</summary>
    public bool Run()
    {
        Directory.CreateDirectory(_options.Output);
        string copy = CopyShow();
        string sourceHash = Hash(_options.Show);
        var show = MagicQShow.Load(_options.Expected ?? copy);
        var stack = show.CueStacks.FirstOrDefault(s => s.Name == _options.Stack)
            ?? throw new ArgumentException($"no cue stack '{_options.Stack}' in the show");

        byte[] status = File.ReadAllBytes(MagicQPc.StatusFile);
        // A killed run can't restore it: this copy can.
        File.WriteAllBytes(Path.Combine(_options.Output, "status.dat.original"), status);
        var before = FolderState.Read(MagicQPc.ShowFolder);
        var results = new List<StepResult>();
        using var artNet = new ArtNetReceiver { UniverseOffset = _options.UniverseOffset };
        try
        {
            artNet.Start();
            Line(MagicQPc.Launch(copy, show.Patch.Select(p => (p.Address - 1) / 512 + 1).Distinct()));
            var window = MagicQWindow.Wait(TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("MagicQ's window did not appear");
            WaitForOutput(artNet);
            Thread.Sleep(3000);
            window.RaiseFader(_options.Playback);
            Thread.Sleep(1000);
            window.Capture(Path.Combine(_options.Output, "start.png"));
            // Open before the first Go, it shows the top of the stack (later it follows the current cue).
            if (_options.CueStack)
                window.CaptureCueStack(_options.Playback, Path.Combine(_options.Output, "cue_stack_start.png"));
            results = PlayStack(show, stack, window, artNet);
            if (_options.CueStack)
                window.CaptureCueStack(_options.Playback, Path.Combine(_options.Output, "cue_stack_end.png"));
            if (_options.Palettes)
                window.CapturePalettes(_options.Output);
            if (_options.Vis)
                window.CaptureVis(_options.Output);
        }
        finally
        {
            // MagicQ rewrites status.dat as it runs, so it has to be gone first.
            foreach (var process in Process.GetProcessesByName("mqqt"))
            {
                process.Kill();
                process.WaitForExit(10000);
            }
            File.WriteAllBytes(MagicQPc.StatusFile, status);
        }

        foreach (var r in results)
            Line(Describe(r));
        int passed = results.Count(r => r.Passed);
        Line($"{passed} of {results.Count} cues pass");
        bool untouched = Hash(_options.Show) == sourceHash;
        if (!untouched)
            Line($"FAILED: {_options.Show} changed during the run");
        Line("Changed in MagicQ's show folder (status.dat is restored): "
            + string.Join(", ", FolderState.Changed(MagicQPc.ShowFolder, before)));
        File.WriteAllText(Path.Combine(_options.Output, "report.txt"), _report.ToString());
        Console.WriteLine($"Report and screenshots: {_options.Output}");
        return untouched && results.Count > 0 && passed == results.Count;
    }

    /// <summary>Copies the show into the output folder: MagicQ autosaves next to the show it runs.</summary>
    private string CopyShow()
    {
        string copy = Path.Combine(_options.Output, Path.GetFileName(_options.Show));
        File.Copy(_options.Show, copy, true);
        string archive = Path.ChangeExtension(_options.Show, ".xhw");
        if (File.Exists(archive))
            File.Copy(archive, Path.ChangeExtension(copy, ".xhw"), true);
        return copy;
    }

    /// <summary>Waits until Art-Net arrives.</summary>
    private static void WaitForOutput(ArtNetReceiver artNet)
    {
        var clock = Stopwatch.StartNew();
        while (artNet.Packets == 0)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("no Art-Net received: is Kloarte running? It takes the packets");
            Thread.Sleep(100);
        }
    }

    /// <summary>Plays the steps one by one.</summary>
    private List<StepResult> PlayStack(MagicQShow show, MagicQShow.CueStack stack, MagicQWindow window, ArtNetReceiver artNet)
    {
        var expected = MagicQVerification.ExpectedLevels(show, stack);
        var intensities = MagicQCheck.IntensityAddresses(show);
        var cues = show.Cues.ToDictionary(c => c.Id);
        var results = new List<StepResult>();
        bool started = window.IsActive(_options.Playback);
        if (started)
            Line("The stack started when its fader went up: no Go for the first cue, whose fade is not measured");
        for (int i = 0; i < stack.Steps.Count; i++)
        {
            var step = stack.Steps[i];
            float fade = cues.TryGetValue(step.CueId, out var cue) ? MagicQVerification.LongestFade(cue) : 0;
            var before = artNet.Snapshot();
            string name = $"{step.CueNumberText} {step.Text}".Trim();
            // Only a cue that has to move the output has a fade: others repeat the look or set a channel's default.
            bool moves = MagicQVerification.Compare(before, expected[i], intensities).Diffs.Count > 0;
            // Some stacks start when the fader goes up: they are on their first cue already.
            bool go = i > 0 || !started;
            var meter = Settle(artNet, before, fade, go ? () => window.Go(_options.Playback) : null,
                go && moves ? () => Again(window, name) : null);
            var (compared, diffs) = MagicQVerification.Compare(artNet.Snapshot(), expected[i], intensities);
            var result = new StepResult(name, compared, diffs.Count, MagicQCheck.FormatDiffs(diffs), fade, meter.Duration,
                !moves || meter.Matches(fade) || i == 0 && started);
            results.Add(result);
            Console.WriteLine(Describe(result));
            window.Capture(Path.Combine(_options.Output, $"cue_{i + 1:000}.png"));
        }
        return results;
    }

    /// <summary>Presses Go again after a click that did nothing; the report shows it.</summary>
    private void Again(MagicQWindow window, string cue)
    {
        Line($"No output change after Go on {cue}: pressed Go again");
        window.Go(_options.Playback);
    }

    /// <summary>
    /// Runs `go` (may be null) and follows the output from `before` until a fade of `fade` seconds has ended and it is
    /// quiet. `again` runs once when the output has not started to change after <see cref="RetrySeconds"/>; null = no
    /// change expected. The first sample is from before the Go: an instant cue is over before the click returns.
    /// </summary>
    private static FadeMeter Settle(ArtNetReceiver artNet, Dictionary<int, int> before, float fade, Action go, Action again)
    {
        var meter = new FadeMeter();
        meter.Add(0, before);
        var clock = Stopwatch.StartNew();
        go?.Invoke();
        double quietFrom = 0;
        long packets = artNet.Packets;
        while (true)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (artNet.Packets != packets)
            {
                packets = artNet.Packets;
                if (meter.Add(now, artNet.Snapshot()))
                    quietFrom = now;
            }
            if (again != null && meter.FirstChange == null && now >= RetrySeconds)
            {
                again();
                again = null;
                clock.Restart();
                continue;
            }
            if (now >= fade + QuietSeconds && now - quietFrom >= QuietSeconds || now >= fade + SettleLimit)
                return meter;
            Thread.Sleep(PollMilliseconds);
        }
    }

    /// <summary>A line of the report for a cue.</summary>
    private static string Describe(StepResult r)
    {
        string state = r.Passed ? "ok  " : "FAIL";
        string fade = string.Format(CultureInfo.InvariantCulture, "fade {0:0.00}s measured {1:0.00}s", r.ExpectedFade, r.MeasuredFade);
        string diffs = r.Differences == 0 ? $"{r.Compared} channels match" : $"{r.Differences} of {r.Compared} differ: {r.DifferenceText}";
        return $"{state} {r.Cue}: {fade}{(r.FadeOk ? "" : " (WRONG)")}; {diffs}";
    }

    /// <summary>Prints a line and adds it to the report.</summary>
    private void Line(string text)
    {
        Console.WriteLine(text);
        _report.AppendLine(text);
    }

    /// <summary>The SHA-256 of a file.</summary>
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
