using System;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// One key (physical key, so the same place on AZERTY and QWERTY keyboards) or mouse button / wheel, with
/// modifiers. Text form as stored in the key map JSON: "Ctrl+Shift+S", "F5", "Space", "Mouse:WheelUp", "Ctrl+Mouse:WheelDown".
/// </summary>
public sealed record KeyBinding(Key Key, MouseButton Mouse, bool Ctrl, bool Shift, bool Alt)
{
    /// <summary>A key (physical keycode) with modifiers.</summary>
    public static KeyBinding Of(Key key, bool ctrl = false, bool shift = false, bool alt = false) => new(key, MouseButton.None, ctrl, shift, alt);

    /// <summary>A mouse button or wheel direction with modifiers.</summary>
    public static KeyBinding OfMouse(MouseButton button, bool ctrl = false, bool shift = false, bool alt = false) => new(Key.None, button, ctrl, shift, alt);

    /// <summary>True for a binding that is only a modifier key (e.g. Shift for "fast" while held).</summary>
    public bool IsModifierOnly => Key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;

    /// <summary>As shown to the user: like <see cref="ToString"/>, mouse buttons and the wheel in words.</summary>
    public string Display
    {
        get
        {
            string main = Mouse switch
            {
                MouseButton.None => KeyCap(Key),
                MouseButton.WheelUp => "Wheel up",
                MouseButton.WheelDown => "Wheel down",
                MouseButton.Left => "Click",
                MouseButton.Right => "Right click",
                MouseButton.Middle => "Middle click",
                _ => "Mouse " + (int)Mouse,
            };
            return (Ctrl && Key != Key.Ctrl ? "Ctrl+" : "") + (Shift && Key != Key.Shift ? "Shift+" : "")
                   + (Alt && Key != Key.Alt ? "Alt+" : "") + main;
        }
    }

    /// <summary>A key as it reads on the key cap ("[", "PgUp", "Esc"), else Godot's name ("F11", "Space").</summary>
    private static string KeyCap(Key key) => key switch
    {
        Key.Bracketleft => "[",
        Key.Bracketright => "]",
        Key.Pageup => "PgUp",
        Key.Pagedown => "PgDn",
        Key.Escape => "Esc",
        Key.Equal => "=",
        Key.Minus => "-",
        Key.Delete => "Del",
        _ => OS.GetKeycodeString(key),
    };

    /// <summary>Text form, e.g. "Ctrl+Shift+S".</summary>
    public override string ToString()
    {
        string main = Mouse != MouseButton.None ? "Mouse:" + Mouse : OS.GetKeycodeString(Key);
        return (Ctrl && Key != Key.Ctrl ? "Ctrl+" : "") + (Shift && Key != Key.Shift ? "Shift+" : "")
               + (Alt && Key != Key.Alt ? "Alt+" : "") + main;
    }

    /// <summary>Parses the text form; null when it is not a valid binding.</summary>
    public static KeyBinding Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        // "+" itself can be a key ("Ctrl++"): split on '+' but keep a trailing "+" as the key.
        string t = text.Trim();
        string last;
        string[] mods;
        if (t.EndsWith("++") || t == "+")
        {
            last = "+";
            mods = t.Length > 2 ? t[..^2].Split('+') : Array.Empty<string>();
        }
        else
        {
            var parts = t.Split('+');
            last = parts[^1];
            mods = parts[..^1];
        }
        bool ctrl = mods.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
        bool shift = mods.Contains("Shift", StringComparer.OrdinalIgnoreCase);
        bool alt = mods.Contains("Alt", StringComparer.OrdinalIgnoreCase);
        if (last.StartsWith("Mouse:", StringComparison.OrdinalIgnoreCase))
            return Enum.TryParse(last[6..], true, out MouseButton mb) ? OfMouse(mb, ctrl, shift, alt) : null;
        var key = OS.FindKeycodeFromString(last);
        return key == Key.None ? null : Of(key, ctrl, shift, alt);
    }

    /// <summary>The binding of an input event (key press or mouse button), or null.</summary>
    public static KeyBinding FromEvent(InputEvent e) => e switch
    {
        InputEventKey k => Of(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode,
                              k.CtrlPressed && k.PhysicalKeycode != Key.Ctrl, k.ShiftPressed && k.PhysicalKeycode != Key.Shift,
                              k.AltPressed && k.PhysicalKeycode != Key.Alt),
        InputEventMouseButton m => OfMouse(m.ButtonIndex, m.CtrlPressed, m.ShiftPressed, m.AltPressed),
        _ => null,
    };

    /// <summary>
    /// The event a menu shows as the item's key: a logical key, so its text is "Ctrl+O" (a physical key's text says
    /// "Ctrl+O - Physical"). Only for display: the key map handles the keys. Null for mouse bindings.
    /// </summary>
    public InputEventKey ToDisplayEvent() => Mouse != MouseButton.None ? null : new InputEventKey
    {
        Keycode = Key,
        CtrlPressed = Ctrl && Key != Key.Ctrl,
        ShiftPressed = Shift && Key != Key.Shift,
        AltPressed = Alt && Key != Key.Alt,
    };

    /// <summary>The Godot input event for InputMap.</summary>
    public InputEvent ToEvent()
    {
        if (Mouse != MouseButton.None)
            return new InputEventMouseButton { ButtonIndex = Mouse, CtrlPressed = Ctrl, ShiftPressed = Shift, AltPressed = Alt };
        return new InputEventKey { PhysicalKeycode = Key, CtrlPressed = Ctrl, ShiftPressed = Shift, AltPressed = Alt };
    }
}
