using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The running app as a remote client (the MCP server's tools, the application's <c>RemoteTools</c>) uses it: the open
/// show, the playback's cue, the room's look, edits (one undo step each, the stage shows the result), saving and the
/// export, and pictures of the views. Implemented by the UI (<see cref="FixturePanel"/>); call it on the main thread only.
/// </summary>
public interface IRemoteControl
{
    /// <summary>The open show's files and model, or null without a show.</summary>
    IShowFiles Files { get; }

    /// <summary>The open native show, or null (no show, or not a native one).</summary>
    DesignShow Design { get; }

    /// <summary>The current cue's id (<see cref="DesignCue.Id"/>, the played step's cue), or null before the first cue.</summary>
    int? CueId { get; }

    /// <summary>True while a cue fades (after a GO).</summary>
    bool Running { get; }

    /// <summary>
    /// Shows the end state of the cue with this id (null = before the first cue), without fades. Throws when the cue list
    /// has no such cue.
    /// </summary>
    void GoToCue(int? cueId);

    /// <summary>The next cue with its fades (as GO on the console).</summary>
    void Go();

    /// <summary>The room's haze density (0..<see cref="ProjectLook.MaxHaze"/>), saved with the project.</summary>
    float Haze { get; set; }

    /// <summary>The room's stray light (0..2), saved with the project.</summary>
    float StrayLight { get; set; }

    /// <summary>The rig item ids of the selected lamps.</summary>
    IReadOnlyList<int> SelectedLamps { get; }

    /// <summary>Selects the lamps with these rig item ids (none: nothing selected). Returns how many were found.</summary>
    int SelectLamps(IReadOnlyCollection<int> lampIds);

    /// <summary>
    /// Changes the native show (`change` edits its lists) as one undo step named `name`; the stage and the views show the
    /// result. Returns the undo entry's name, or null when nothing changed. Throws when there is no native show.
    /// </summary>
    string EditShow(string name, Action<DesignShow> change);

    /// <summary>
    /// Changes the loaded model (mechanics, blocking) as one undo step named `name`; `rebuild`: the scene is built from the
    /// model again (mechanics), else only the playback follows (blocking, smoke). Returns the undo entry's name, or null.
    /// </summary>
    Task<string> EditModel(string name, Action<IShowModel> change, bool rebuild);

    /// <summary>
    /// Moving-head aiming: the pan and tilt (degrees, in the head's GDTF range) that point the moving head of head number
    /// `head` at a stage point (stage coordinates, metres), as "Aim at the stage" computes them from the rigged head.
    /// Nothing changes; the caller puts the angles into a cue. Null when the head has no moving head on stage.
    /// </summary>
    (float Pan, float Tilt)? AimAngles(int head, System.Numerics.Vector3 stagePoint);

    /// <summary>
    /// Mechanical aiming: focuses conventional lamps (rig item id -&gt; stage point) on their points, as rigged and focused
    /// by hand: the mechanics' focus point, for the whole show, not per cue. One undo step named `name`; returns its name.
    /// Throws for a lamp that isn't on stage or is a moving head (those are aimed with pan / tilt in a cue).
    /// </summary>
    string FocusLamps(string name, IReadOnlyDictionary<int, System.Numerics.Vector3> points);

    /// <summary>Undoes the last edit (the scene is built from the model again). Returns its name, or null.</summary>
    Task<string> Undo();

    /// <summary>Redoes the last undone edit. Returns its name, or null.</summary>
    Task<string> Redo();

    /// <summary>Saves the project (as File > Save). Returns the status text.</summary>
    string Save();

    /// <summary>Exports the show as the project's next numbered console show (never an overwrite). Returns the status text.</summary>
    string ExportConsoleShow();

    /// <summary>The titles of the views (3D, Audience, Plan, Cue table, ...).</summary>
    IReadOnlyList<string> Views { get; }

    /// <summary>The title of the view in the main place.</summary>
    string MainView { get; }

    /// <summary>
    /// A PNG of a view (by title; null or "": the whole window), at most `maxWidth` pixels wide (0: as rendered). A 3D view
    /// is rendered for it even when it isn't shown. Throws for an unknown view or one that isn't shown (2D views).
    /// </summary>
    Task<byte[]> Capture(string view, int maxWidth);

    /// <summary>Shows a message on the status bar (what a remote client did).</summary>
    void Report(string text);
}
