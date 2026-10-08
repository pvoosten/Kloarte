using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// A view in the view host (3D, audience, plan, truss heights, timeline, key map, gobo editor). The host decides
/// whether it is the main view, a small card or hidden; a small view may simplify itself (<see cref="Compact"/>).
/// </summary>
public partial class ViewBase : Control
{
    /// <summary>Title in the view bar and on its card.</summary>
    public virtual string Title => Name;

    /// <summary>Key map context of the view ("3d", "plan", "timeline", "gobo"; "global" = none of its own).</summary>
    public virtual string Context => KeyMap.Global;

    /// <summary>Key map action that makes this view the main view (e.g. "view_plan").</summary>
    public virtual string ShowAction => "";

    /// <summary>True while the view is a small card (preview).</summary>
    public bool Compact { get; private set; }

    /// <summary>True while the view is the main view.</summary>
    public bool IsMain { get; private set; }

    /// <summary>
    /// The view's toolbar, made in its <c>_Ready</c> and not added to the view: the host shows it in the tool strip under
    /// the view bar while the view is the main view (UI review 29: never over the content). Null = none.
    /// </summary>
    public Control Toolbar { get; protected set; }

    /// <summary>The show the views show (set by the host when a show is (re)built).</summary>
    public ShowBuilder Builder { get; private set; }

    /// <summary>Called by the host when the view becomes main / small.</summary>
    public void SetRole(bool main, bool compact)
    {
        bool changed = main != IsMain || compact != Compact;
        IsMain = main;
        Compact = compact;
        if (changed)
            OnRoleChanged();
    }

    /// <summary>Called by the host when a show is loaded or rebuilt.</summary>
    public void SetBuilder(ShowBuilder builder)
    {
        Builder = builder;
        OnShowChanged();
    }

    /// <summary>The view became main or small.</summary>
    protected virtual void OnRoleChanged() { }

    /// <summary>A show was loaded or rebuilt.</summary>
    protected virtual void OnShowChanged() => QueueRedraw();

    /// <summary>A key map event for this view's context while it is the main view; true when used.</summary>
    public virtual bool HandleKey(InputEvent e) => false;
}
