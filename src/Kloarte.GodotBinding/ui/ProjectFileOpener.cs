using System;
using System.Diagnostics;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Opens a project file for a person to fix: in VS Code at the line and column (<c>code -g file:line:column</c>), else
/// the file shown in Explorer (user decision of 11 Oct 2026).
/// </summary>
internal static class ProjectFileOpener
{
    /// <summary>Opens `path` at `line` and `column` (from 1; 0 = the start), or shows it in Explorer.</summary>
    internal static void Open(string path, int line, int column)
    {
        try
        {
            Process.Start(new ProcessStartInfo("code", $"-g \"{path}:{Math.Max(line, 1)}:{Math.Max(column, 1)}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception e)
            {
                GD.PrintErr($"Could not open {path}: {e.Message}");
            }
        }
    }
}
