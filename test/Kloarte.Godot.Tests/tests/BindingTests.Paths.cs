using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// BindingTests: folders as the project box shows them (<see cref="FixturePanel.FolderText"/>,
/// <see cref="UiFit.FitPathStart"/>). Explored 11 Oct 2026: long paths were cut at the end
/// ("C:\proj\Kloarte\test\Kloarte.G...", "Documents\MagicQ\show\Ris..."), hiding the folders that tell where it is.
/// </summary>
public partial class BindingTests
{
    private void PathsTest()
    {
        string documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        string text = FixturePanel.FolderText(System.IO.Path.Combine(documents, "Kloarte", "Kruimels"));
        Check(text == @"Documents\Kloarte\Kruimels", $"Documents folder by name: {text}");
        text = FixturePanel.FolderText("C:/proj/show");
        Check(text == @"C:\proj\show", $"one kind of slash: {text}");

        // Too wide for the panel: leading folders go, the last ones stay.
        Font font = ThemeDB.FallbackFont;
        const int size = 16;
        string full = @"C:\proj\Kloarte\test\Kloarte.Godot.Tests\Projects\Kruimels";
        float width = font.GetStringSize(@"...\Projects\Kruimels", HorizontalAlignment.Left, -1, size).X + 2;
        text = UiFit.FitPathStart(full, font, size, width);
        Check(text == @"...\Projects\Kruimels", $"long path keeps its end: {text}");
        text = UiFit.FitPathStart(full, font, size, 5);
        Check(text == @"...\Kruimels", $"at least the last folder: {text}");
        text = UiFit.FitPathStart(@"Documents\Kloarte", font, size, 1000);
        Check(text == @"Documents\Kloarte", $"a path that fits stays whole: {text}");
    }
}
