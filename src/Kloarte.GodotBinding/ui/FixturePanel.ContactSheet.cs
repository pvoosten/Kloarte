using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the contact sheet (UI review 32): a picture of every cue of the range when its fades are done (and of
/// the start when the range begins there), nine per A4 page with "Cue 5  text" under each (<see cref="ContactSheetPdf"/>);
/// per actor one PDF per person.
/// </summary>
public partial class FixturePanel
{
    /// <summary>JPEG quality of the pictures in the PDF.</summary>
    private const float ContactSheetQuality = 0.85f;

    /// <summary>Writes the contact sheet(s) into the target folder; returns a status message.</summary>
    private async Task<string> ExportContactSheets(ExportOptions options)
    {
        var tl = _player.Timeline;
        if (tl == null || tl.Segments.Count == 0)
            return "No cue stack chosen (Playback tab)";
        var moments = new List<(double Time, string Caption)>();
        if (options.FromStep < 0)
            moments.Add((-0.5, "Before the first cue"));
        for (int i = System.Math.Max(0, options.FromStep); i <= options.ToStep && i < tl.Segments.Count; i++)
        {
            var seg = tl.Segments[i];
            moments.Add((seg.ActiveEnd - 0.001, $"Cue {seg.CueNumber}  {seg.Text}".TrimEnd()));
        }
        Directory.CreateDirectory(options.Target);
        var actors = ExportActors(options);
        double restore = _player.Time;
        string project = CurrentBuilder.ProjectName.Length > 0 ? CurrentBuilder.ProjectName : "show";
        var files = new List<string>();
        // The captions go under the pictures, not into them.
        using var shooter = new ExportShooter(this, options.Size, ExportCamera(options.Viewpoint), tl, captions: false);
        try
        {
            foreach (var actor in actors)
            {
                shooter.ForActor(CurrentBuilder, actor);
                var frames = new List<ContactSheetFrame>();
                for (int i = 0; i < moments.Count; i++)
                {
                    var (t, caption) = moments[i];
                    _player.Seek(t);
                    var image = await shooter.Shoot(t);
                    var jpeg = image.SaveJpgToBuffer(ContactSheetQuality);
                    frames.Add(new ContactSheetFrame(new PdfImage(jpeg, image.GetWidth(), image.GetHeight()),
                        options.Captions ? caption : ""));
                    StatusBar.ShowTask($"Contact sheet{(actor == null ? "" : $" ({actor.Name})")}: {i + 1} / {moments.Count}",
                        (double)(i + 1) / moments.Count, null);
                }
                string name = actor == null ? $"{FileSafe(project)}_contact_sheet.pdf" : $"{FileSafe(project)}_{FileSafe(actor.Name)}.pdf";
                string path = Path.Combine(options.Target, name);
                string title = $"{project} - {_player.Stack?.Name}{(actor == null ? "" : $" - {actor.Name}")}";
                ContactSheetPdf.Export(path, title, frames);
                files.Add(path);
            }
        }
        finally
        {
            shooter.ForActor(CurrentBuilder, null);
            StatusBar.EndTask();
            _player.Seek(restore);
        }
        string done = files.Count == 1 ? $"Contact sheet saved: {files[0]}" : $"{files.Count} contact sheets saved in {options.Target}";
        PostWithFolder(done, options.Target);
        return done;
    }
}
