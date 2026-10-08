using System.Collections.Generic;

namespace Kloarte.GodotBinding;

/// <summary>
/// A function of the application that can be bound to keys in the key map (ui/KeyMap.cs). Context decides where it
/// works: "global" everywhere, otherwise only while that view has the focus ("3d", "plan", "timeline", "gobo"); the same
/// key can mean different things in different views.
/// </summary>
public sealed class AppAction
{
    /// <summary>InputMap action name, e.g. "playback_go".</summary>
    public string Id { get; }

    /// <summary>Name shown in the key map view.</summary>
    public string Name { get; }

    /// <summary>Group in the key map view (Camera, Views, Playback...).</summary>
    public string Category { get; }

    /// <summary>"global" or the view where the action works.</summary>
    public string Context { get; }

    /// <summary>Default bindings (up to two).</summary>
    public IReadOnlyList<KeyBinding> Defaults { get; }

    /// <summary>Creates an action definition.</summary>
    public AppAction(string id, string name, string category, string context, params KeyBinding[] defaults)
    {
        Id = id;
        Name = name;
        Category = category;
        Context = context;
        Defaults = defaults;
    }
}
