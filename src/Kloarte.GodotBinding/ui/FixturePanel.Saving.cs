using System;
using System.Collections.Generic;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, saving (UI review 2, 3): one Save for the whole project (Ctrl+S: mechanics, blocking and, when it
/// changed, the show as its next numbered version; "Save show as" stays a copy), an unsaved marker ("•" in the window
/// title and the project box, the unsaved parts in its tooltip), and Save / Don't save / Cancel before the window
/// closes or another show or project opens.
/// </summary>
public partial class FixturePanel
{
    /// <summary>How often the unsaved marker is refreshed (s; it fingerprints show, mechanics and blocking).</summary>
    private const double UnsavedPollInterval = 0.5;

    /// <summary>Time to the next refresh of the unsaved marker.</summary>
    private double _unsavedIn;

    /// <summary>The parts with unsaved changes at the last refresh.</summary>
    private IReadOnlyList<string> _unsaved = Array.Empty<string>();

    /// <summary>Asks Save / Don't save / Cancel.</summary>
    private ConfirmationDialog _savePrompt;

    /// <summary>What to do after the prompt was answered with Save or Don't save.</summary>
    private Action _afterPrompt;

    /// <summary>The Save button and "Save show as" in one row.</summary>
    private Control SaveRow()
    {
        var row = new HBoxContainer();
        var save = new Button
        {
            Text = "Save",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Save the project: mechanics, blocking and, when it changed, the show as its next numbered\n" +
                          "version (nothing is overwritten)",
        };
        save.Pressed += () => _status.Text = SaveProject();
        KeyMap.Tooltip(save, "Save the project: mechanics, blocking and, when it changed, the show as its next numbered\n" +
                             "version (nothing is overwritten)", "file_save");
        var saveAs = new Button
        {
            Text = "Export...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Write a copy of the show (.shw + .xhw) somewhere else; the project keeps its own show",
        };
        saveAs.Pressed += ShowSaveDialog;
        KeyMap.Tooltip(saveAs, "Write a copy of the show (.shw + .xhw) somewhere else; the project keeps its own show", "file_save_as");
        row.AddChild(save);
        row.AddChild(saveAs);

        _savePrompt = new ConfirmationDialog { Title = "Unsaved changes", OkButtonText = "Save", CancelButtonText = "Cancel" };
        _savePrompt.AddButton("Don't save", false, "discard");
        _savePrompt.Confirmed += () =>
        {
            _status.Text = SaveProject();
            Continue();
        };
        _savePrompt.CustomAction += action =>
        {
            if (action == "discard")
            {
                _savePrompt.Hide();
                Continue();
            }
        };
        _savePrompt.Canceled += () => _afterPrompt = null;
        AddChild(_savePrompt);
        return row;
    }

    /// <summary>Does what waited for the prompt.</summary>
    private void Continue()
    {
        var next = _afterPrompt;
        _afterPrompt = null;
        next?.Invoke();
    }

    /// <summary>Saves the whole project of the current show (<see cref="ShowBuilder.SaveProject"/>).</summary>
    private string SaveProject()
    {
        string result = CurrentBuilder?.SaveProject() ?? "No show loaded";
        RefreshUnsaved();
        UpdateProjectBox();   // also when nothing was unsaved: a first save writes the project ("not saved yet" goes)
        return result;
    }

    /// <summary>
    /// Runs `proceed` at once when everything is saved, else after the user chose Save (saved first) or Don't save;
    /// Cancel drops it. `doing` completes "Save the changes before ...?".
    /// </summary>
    private void AskToSave(string doing, Action proceed)
    {
        RefreshUnsaved();
        if (_unsaved.Count == 0)
        {
            proceed();
            return;
        }
        _afterPrompt = proceed;
        _savePrompt.DialogText = $"Project {CurrentBuilder?.ProjectName} has unsaved changes ({string.Join(", ", _unsaved)}).\n" +
                                 $"Save them before {doing}?";
        _savePrompt.PopupCentered();
    }

    /// <summary>Refreshes the unsaved marker now and then (called every frame).</summary>
    private void PollUnsaved(double delta)
    {
        _unsavedIn -= delta;
        if (_unsavedIn > 0)
            return;
        _unsavedIn = UnsavedPollInterval;
        _statusBar?.SetInfo(StatusInfo());
        // An edit has settled when no mouse button is held (a whole drag is one undo step).
        if (Input.GetMouseButtonMask() == 0 && CurrentBuilder is { Model: not null } builder)
        {
            builder.Checkpoint();
            PollProjectFiles();   // after the scene's edits are in the model: a file's change is not overwritten by them
        }
        RefreshUnsaved();
        RefreshShowInfo();
        UpdateInspectorStrip();
        _list?.UpdateLevels();
    }

    /// <summary>The status bar's right side: project (unsaved marker), MagicQ's folder watched, live Art-Net.</summary>
    private string StatusInfo()
    {
        var parts = new List<string>();
        if (CurrentBuilder?.Model != null)
            parts.Add($"{CurrentBuilder.ProjectName}{(_unsaved.Count > 0 ? " \u2022 unsaved" : "")}");
        if (_artNet?.Running == true)
            parts.Add($"Art-Net: live ({_artNet.Packets} packets)");
        return string.Join("   \u00b7   ", parts);
    }

    /// <summary>Finds the unsaved parts and shows the marker when they changed.</summary>
    private void RefreshUnsaved()
    {
        var builder = CurrentBuilder;
        var unsaved = builder?.Model != null ? builder.UnsavedChanges() : Array.Empty<string>();
        if (string.Join(",", unsaved) == string.Join(",", _unsaved))
            return;
        _unsaved = unsaved;
        UpdateProjectBox();
    }
}
