using System.Linq;
using Godot;

// Smoke test for the FixturePanel and rigging:
//  - group and cue dropdowns filter the list;
//  - a head is moved to another truss and repositioned, saved as a MagicQ show (tests/output) and read back;
//  - another show is opened through the file dialog's signal. Screenshots in tests/output.
// Run:  Godot_console.exe --path . res://tests/panel_capture.tscn
public partial class PanelCapture : Node
{
    [Export] public string OtherShow = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/la comedie.shw";
    private int _frame;
    private Node _scene, _panel;

    public override void _Ready()
    {
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode("FixturePanel");
    }

    private T[] FindAll<T>() where T : Node =>
        _panel.FindChildren("*", typeof(T).Name, true, false).Cast<T>().ToArray();

    private static void Choose(OptionButton button, int index)
    {
        button.Select(index);
        button.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 30)
        {
            var list = FindAll<ItemList>()[0];
            var (groups, cues) = (FindAll<OptionButton>()[0], FindAll<OptionButton>()[1]);
            GD.Print($"list items: {list.ItemCount}, group items: {groups.ItemCount}, cue items: {cues.ItemCount}");
            for (int i = 0; i < Mathf.Min(cues.ItemCount, 14); i++)
                GD.Print($"  cue item {i}: {(cues.IsItemSeparator(i) ? "--- " : "")}{cues.GetItemText(i)}");
            int cue = Enumerable.Range(0, cues.ItemCount).First(i => cues.GetItemText(i).StartsWith("1  start"));
            Choose(cues, cue);
            GD.Print($"cue '{cues.GetItemText(cue)}': {list.ItemCount} fixtures");
            for (int i = 0; i < list.ItemCount; i++)
                GD.Print($"    {list.GetItemText(i)}");
        }
        if (_frame == 120)
        {
            GD.Print($"lit with cue filter: {LitCount()} of {FindAll<ItemList>()[0].ItemCount} listed");
            Save("panel_cue.png");
            Choose(FindAll<OptionButton>()[1], 0);
            GD.Print($"lit without filter: {LitCount()}");
        }
        if (_frame == 130)
            RiggingTest();
        if (_frame == 160)
        {
            var tabs = FindAll<TabContainer>()[0];
            tabs.CurrentTab = 1;
            _panel.GetNode<CuePlayer>("CuePlayer").Jump(4);
        }
        if (_frame == 180)
        {
            Save("panel_playback.png");
            FindAll<TabContainer>()[0].CurrentTab = 2;
            var actors = FindAll<TabContainer>()[0].GetNode("Actors").GetChildren().OfType<ItemList>().First();
            actors.Select(0);
            actors.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        }
        if (_frame == 200)
        {
            Save("panel_actors.png");
            // Edit X, then Y of the selected actor through the panel's fields: both must stay.
            var spins = FindAll<SpinBox>().Where(sp => sp.Suffix == "m" && sp.IsVisibleInTree()).ToList();
            var (x, y) = (spins[^3], spins[^2]);   // ... X, Y, Z are the last three metre fields
            x.Value = 1.5;
            y.Value = 0.4;
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var actor = builder.Actors.Values.First();
            GD.Print($"actor after editing X then Y: {actor.Position} " +
                     (Mathf.IsEqualApprox(actor.Position.X, 1.5f) && Mathf.IsEqualApprox(actor.Position.Y, 0.4f) ? "OK" : "FAIL"));
            FindAll<TabContainer>()[0].CurrentTab = 0;
        }
        if (_frame == 220)
        {
            Save("panel.png");
            FindAll<FileDialog>()[0].EmitSignal(FileDialog.SignalName.FileSelected, OtherShow);
        }
        if (_frame == 340)
        {
            GD.Print($"after open: list items: {FindAll<ItemList>()[0].ItemCount}");
            Save("panel_open.png");
            GetTree().Quit();
        }
    }

    // Head 150 ("Binnen T", on LT 11 at z 4.20) -> truss "T 1" (z 8.40), then 1 m to the right; save + reload.
    private void RiggingTest()
    {
        var builder = _scene.GetNode<ShowBuilder>("Show");
        var f = builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>().First(x => x.DmxAddress == 150);
        var list = FindAll<ItemList>()[0];
        int index = Enumerable.Range(0, list.ItemCount).First(i => list.GetItemText(i).StartsWith("1_150"));
        list.Select(index);
        list.EmitSignal(ItemList.SignalName.ItemSelected, index);

        var t1 = builder.RigObjects.First(o => o.Name == "T 1");
        GD.Print($"before: parent {builder.GetParent(f)}, pos {f.Position}");
        builder.SetParent(f, t1.Id);
        GD.Print($"on T 1: parent {builder.GetParent(f)}, pos {f.Position}");
        builder.MoveFixture(f, f.Position + Vector3.Right);
        GD.Print($"moved: pos {f.Position}, focus {f.FocusPoint}");

        string path = ProjectSettings.GlobalizePath("res://tests/output/roundtrip/rigged.shw");
        GD.Print(builder.SaveShowAs(path));
        var reread = MagicQShow.Load(path);
        var vis = reread.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture && v.Address == 150);
        bool ok = vis.ParentId == t1.Id && Mathf.Abs(vis.Z - 8.40f + 0f) < 0.011f && Mathf.Abs(vis.X - f.Position.X) < 0.011f;
        GD.Print($"re-read: parent {vis.ParentId:x}, x {vis.X}, y {vis.Y}, z {vis.Z}, rx {vis.RotX}, ry {vis.RotY}: {(ok ? "OK" : "FAIL")}");
    }

    private int LitCount() =>
        _scene.GetNode("Show/Fixtures").GetChildren().OfType<ProfileFixture>().Count(f => f.Intensity > 0f);

    private void Save(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }
}
