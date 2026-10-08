using System.Collections.Generic;
using System.Numerics;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Rig, plan and patch changes the user makes in the views (plan, truss heights, settings, clicks on the stage).
/// Lamps and supports by rig item id; plan positions in plan coordinates (stage x, stage z), heights and positions in
/// stage coordinates. A refused change throws <c>Kloarte.Model.ShowEditException</c> with the reason.
/// </summary>
public interface IRigCommands
{
    /// <summary>
    /// Sets the gel (colour table id, 0 = open white) of the heads of these lamps: the console keeps it per head, so
    /// every lamp on those heads gets it. Returns a status line.
    /// </summary>
    string SetGel(IEnumerable<int> lampIds, int gelId);

    /// <summary>
    /// Names the head of a lamp (the console keeps the name per head, so every lamp on the head gets it; at most 15
    /// characters). One undo step. Returns a status line.
    /// </summary>
    string RenameHead(int lampId, string name);

    /// <summary>
    /// Moves a lamp in the plan; with `snap` onto the nearest truss / stand within the snap distance (keeping its drop
    /// below the truss), else unattached at its height. It stays focused on its focus point. Returns the support it
    /// snapped to (0 = none).
    /// </summary>
    int MoveLampInPlan(int lampId, Vector2 plan, bool snap);

    /// <summary>
    /// Flies a truss to a height above the stage floor (or sets a stand's height): its lamps move along and are
    /// re-aimed at their unchanged focus points.
    /// </summary>
    void SetSupportHeight(int supportId, float stageHeight);

    /// <summary>Adds a floor stand of a house type at a plan position, `stageHeight` high. Returns its id.</summary>
    int AddStand(string type, Vector2 plan, float stageHeight);

    /// <summary>
    /// Adds a lamp of a catalogue type at a plan position (on the nearest truss, or on the nearest stand aimed at centre
    /// stage) on a head of its own. Returns its id.
    /// </summary>
    int AddLamp(string modelId, Vector2 plan);

    /// <summary>Removes a lamp and its head's patch when nothing else uses the head. Returns a status text.</summary>
    string RemoveLamp(int lampId);

    /// <summary>Removes a stand; lamps on it stay where they are, unattached.</summary>
    void RemoveSupport(int supportId);

    /// <summary>Patches a lamp to a head number and DMX address (plan rules). Returns what was done.</summary>
    string ApplyLampPatch(int lampId, int head, int address);

    /// <summary>Hangs a lamp on another object; on a truss it moves onto it, keeping its drop below the truss.</summary>
    void SetParent(int lampId, int parentId);

    /// <summary>Moves a lamp (stage coordinates); it stays focused on its focus point.</summary>
    void MoveLamp(int lampId, Vector3 stagePosition);

    /// <summary>
    /// Where a lamp can move now (<see cref="LampMotion"/>): along its truss, up and down its stand, or
    /// freely. The settings disable the fields of the axes it can't move in.
    /// </summary>
    LampMotion MotionOf(int lampId);

    /// <summary>
    /// Sets one coordinate of a lamp's position (stage coordinates, from its settings): a lamp on a truss slides along it
    /// until that axis has the value and stops at the truss's ends; the fixed axes are refused. One named undo step. The
    /// lamp stays focused on its focus point. Returns null, or what happened: why nothing moved, or that it stopped.
    /// </summary>
    string MoveLampAxis(int lampId, MotionAxis axis, float stageValue);

    /// <summary>Focuses a conventional lamp on a stage point (moving heads are aimed with pan / tilt).</summary>
    void FocusAt(int lampId, Vector3 stagePoint);

    /// <summary>Sets a lamp's beam angle in the console's patch (whole degrees) and on the lamps of its head.</summary>
    void SetBeamAngle(int lampId, float degrees);
}
