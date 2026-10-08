namespace Kloarte.Model;

/// <summary>
/// The console's show files for a native show (<see cref="DesignShow"/>): import a console show, export a native show
/// into a console show (a copy of the one it came from), the console's colour table. The console binding implements it
/// (MagicQ); the host sets it.
/// </summary>
public interface IShowExchange
{
    /// <summary>The console's show folder (where shows are imported from and exported to).</summary>
    string ShowFolder { get; }

    /// <summary>The extension of the console's show files (".shw").</summary>
    string Extension { get; }

    /// <summary>The console show a new project starts from (the house venue, no lamps).</summary>
    string NewShow { get; }

    /// <summary>True when a file is a console show (by its extension).</summary>
    bool IsConsoleShow(string path);

    /// <summary>Imports a console show (read only).</summary>
    ShowImport Import(string path, FixtureCatalog catalog);

    /// <summary>
    /// Exports a native show to a new console show file (never over an existing one). New cues and palettes get the
    /// console's ids in the design.
    /// </summary>
    ShowExport Export(DesignShow show, string path, FixtureCatalog catalog);

    /// <summary>The console's colour table (gels by id), or null.</summary>
    IGelTable LoadGels(string showPath);
}
