namespace Kloarte.Model;

/// <summary>What a project file holds (<see cref="ProjectChange"/>).</summary>
public enum ProjectFileKind
{
    /// <summary>A lamp's mechanics (<c>mechanics/*.yaml</c>, <see cref="LampFile"/>).</summary>
    Lamp,

    /// <summary>The actors (<c>blocking/actors.yaml</c>).</summary>
    Actors,

    /// <summary>The steps: holds and movement per cue (<c>blocking/steps.yaml</c>).</summary>
    Steps,

    /// <summary>A file of a native show (<c>rig.yaml</c>, <c>palettes.yaml</c>, <c>cues.yaml</c>, <see cref="IProjectShow"/>).</summary>
    Show,

    /// <summary>The project's venue (<c>venue.yaml</c>): not model data; the scene is rebuilt from it.</summary>
    Venue,

    /// <summary>The set pieces (<c>blocking/scenery.yaml</c>).</summary>
    Scenery,
}
