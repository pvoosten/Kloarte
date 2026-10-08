using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, the project's files (user decisions of 11 Oct 2026): a file changed outside Kloarte is read on the
/// panel's poll and applied at once as one undo step (the scene is rebuilt); when Kloarte has unsaved changes for the
/// same file the user chooses (take the file's / keep Kloarte's / save Kloarte's first); a file with an error is not
/// applied and its error goes to the status bar in red with the file, line and column (a click opens the file).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The conflicts waiting for the user's choice (one dialog at a time).</summary>
    private readonly Queue<ProjectChange> _conflicts = new();

    /// <summary>The load problems last reported (by reference: a new load gives a new list).</summary>
    private object _reportedProblems;

    /// <summary>The choice dialog on screen, or null.</summary>
    private AcceptDialog _choiceDialog;

    /// <summary>The change the choice dialog on screen asks about.</summary>
    private ProjectChange _asked;

    /// <summary>Reads the project files that changed outside Kloarte (called on the poll, after the unsaved check).</summary>
    private void PollProjectFiles()
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || builder.Files == null)
            return;
        ReportLoadProblems(builder.Files);
        if (Input.GetMouseButtonMask() != 0)
            return;   // a drag or a click in progress: the files wait for the next poll
        var changes = builder.Files.PollProjectChanges();
        if (changes.Any(c => c.Applied))
            RebuildAfterFileChange(builder);
        foreach (var change in changes)
        {
            if (change.Conflict)
                _conflicts.Enqueue(change);
            else if (change.HasError)
                ReportProblem(change.Problems.First(p => p.IsError));
            else if (change.Kind == ProjectFileKind.Venue)
                _status.Text = $"Venue changed outside Kloarte: {change.Name} (the scene is rebuilt with it)";
            else if (change.Problems.Count > 0)
                _status.Text = $"Edited outside Kloarte: {change.Name} ({change.Problems[0]})";
            else
                _status.Text = $"Edited outside Kloarte: {change.Name} {KeyMap.Hint("Undo", "edit_undo")}";
        }
        ShowNextConflict();
    }

    /// <summary>The problems of the show's load (once per load): an error in red, a warning in the message.</summary>
    private void ReportLoadProblems(IShowFiles files)
    {
        if (ReferenceEquals(files.LoadProblems, _reportedProblems))
            return;
        _reportedProblems = files.LoadProblems;
        var first = files.LoadProblems.FirstOrDefault(p => p.IsError) ?? files.LoadProblems.FirstOrDefault();
        if (first == null)
            return;
        if (first.IsError)
            ReportProblem(first);
        else
            _status.Text = $"Read with warnings: {first}";
    }

    /// <summary>An error of a project file: the message in red, its click opens the file at the error (the lamp is marked).</summary>
    private void ReportProblem(FileProblem problem)
    {
        _status.Post($"Could not read {problem}", () => ProjectFileOpener.Open(problem.Path, problem.Line, problem.Column));
        FillList();
    }

    /// <summary>True when the lamp's project file has an error (the fixture list marks it; it keeps its last good state).</summary>
    private bool IsBrokenLamp(ProfileFixture fixture)
    {
        var builder = CurrentBuilder;
        var broken = builder?.Model?.BrokenLamps;
        return broken != null && builder.VisOf(fixture) is { } vis && broken.Contains(vis.Id);
    }

    /// <summary>Asks about the next conflict, when no dialog is on screen.</summary>
    private void ShowNextConflict()
    {
        if (_choiceDialog != null || _conflicts.Count == 0)
            return;
        var change = _conflicts.Dequeue();
        var dialog = new AcceptDialog
        {
            Name = "ProjectChoice",
            Title = "Changed outside Kloarte",
            DialogAutowrap = true,
            DialogText = $"{change.Name} was changed outside Kloarte, and Kloarte has unsaved changes for the same file.\n" +
                         "Which one stays?",
        };
        dialog.GetOkButton().Visible = false;
        dialog.AddButton("Take the file's", false, "take");
        dialog.AddButton("Keep Kloarte's", false, "keep");
        dialog.AddButton("Save Kloarte's first", false, "save");
        dialog.CustomAction += action => Decide(change, action.ToString() switch
        {
            "take" => ProjectChoice.TakeFile,
            "save" => ProjectChoice.SaveKloarteFirst,
            _ => ProjectChoice.KeepKloarte,
        });
        dialog.Canceled += () => Decide(change, ProjectChoice.KeepKloarte);
        _choiceDialog = dialog;
        _asked = change;
        AddChild(dialog, true);
        dialog.PopupCentered(new Vector2I(520, 0));
    }

    /// <summary>Settles the change the dialog asked about with the user's choice (closing it without one keeps Kloarte's).</summary>
    private void Decide(ProjectChange change, ProjectChoice choice)
    {
        if (_choiceDialog == null || _asked != change)
            return;
        _choiceDialog.QueueFree();
        _choiceDialog = null;
        var builder = CurrentBuilder;
        if (builder?.Files == null)
            return;
        string message = builder.Files.ResolveProjectChange(change, choice);
        if (choice == ProjectChoice.TakeFile)
            RebuildAfterFileChange(builder);
        _status.Text = message;
        RefreshUnsaved();
        UpdateProjectBox();
        ShowNextConflict();
    }

    /// <summary>The scene is built again from the model after a file changed the model (the view stays as it is).</summary>
    private async void RebuildAfterFileChange(ShowBuilder builder)
    {
        var view = TakeView(builder);
        builder.RebuildFromModel();
        await RestoreView(builder, view);
    }
}
