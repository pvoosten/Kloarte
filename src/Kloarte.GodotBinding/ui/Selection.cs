using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.GodotBinding;

namespace Kloarte.GodotBinding;

/// <summary>
/// What is selected, shared by all views (fixture list, 3D views, plan, truss heights, timeline): one or more lamps,
/// or one actor (with the movement step being edited). Views subscribe to <see cref="Changed"/> and unsubscribe when
/// they leave the tree.
/// </summary>
public static class Selection
{
    private static readonly List<ProfileFixture> _fixtures = new();

    /// <summary>Raised after the selection changed.</summary>
    public static event Action Changed;

    /// <summary>The selected lamps (several when box-selected in the plan).</summary>
    public static IReadOnlyList<ProfileFixture> Fixtures => _fixtures;

    /// <summary>The first selected lamp, or null.</summary>
    public static ProfileFixture Fixture => _fixtures.FirstOrDefault(Godot.GodotObject.IsInstanceValid);

    /// <summary>The selected actor id, or null.</summary>
    public static string Actor { get; private set; }

    /// <summary>The selected actor's movement step in the current cue (timeline / actor editor).</summary>
    public static int ActorStep { get; private set; }

    /// <summary>Selects one lamp (null clears).</summary>
    public static void Select(ProfileFixture fixture) => Select(fixture == null ? Array.Empty<ProfileFixture>() : new[] { fixture });

    /// <summary>Selects several lamps.</summary>
    public static void Select(IEnumerable<ProfileFixture> fixtures)
    {
        var list = fixtures.Where(f => f != null).Distinct().ToList();
        if (Actor == null && list.SequenceEqual(_fixtures))
            return;
        _fixtures.Clear();
        _fixtures.AddRange(list);
        Actor = null;
        Changed?.Invoke();
    }

    /// <summary>Selects an actor (and the movement step to edit).</summary>
    public static void SelectActor(string actorId, int step = 0)
    {
        if (Actor == actorId && ActorStep == step && _fixtures.Count == 0)
            return;
        _fixtures.Clear();
        Actor = actorId;
        ActorStep = step;
        Changed?.Invoke();
    }

    /// <summary>Tells the views that the selected things changed (moved, repatched) without a new selection.</summary>
    public static void Touch() => Changed?.Invoke();

    /// <summary>Clears the selection.</summary>
    public static void Clear()
    {
        if (_fixtures.Count == 0 && Actor == null)
            return;
        _fixtures.Clear();
        Actor = null;
        Changed?.Invoke();
    }
}
