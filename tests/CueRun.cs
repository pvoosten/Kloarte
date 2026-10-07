using System.Linq;
using Godot;

// Plays every cue of a cue stack in order (with tracking) and saves one screenshot per cue plus a contact sheet;
// prints per cue how many fixtures are lit and how dark the picture is (share of near-black pixels in the stage area).
// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/cue_run.tscn
public partial class CueRun : Node
{
    [Export] public string StackName = "show";
    private const int FramesPerCue = 40;
    private const int ThumbW = 320, ThumbH = 180, Columns = 6;

    private Node _scene;
    private ShowBuilder _builder;
    private MagicQShow.CueStack _stack;
    private int _frame, _step = -1;
    private Image _sheet;
    private string _outDir;

    public override void _Ready()
    {
        _outDir = ProjectSettings.GlobalizePath("res://tests/output/cues");
        DirAccess.MakeDirRecursiveAbsolute(_outDir);
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _scene.GetNode<CanvasLayer>("FixturePanel").Visible = false;
        _builder = _scene.GetNode<ShowBuilder>("Show");
    }

    public override void _Process(double delta)
    {
        if (_stack == null)
        {
            _stack = _builder.Model.MagicQ.CueStacks.First(s => s.Name == StackName);
            int rows = (_stack.Steps.Count + Columns - 1) / Columns;
            _sheet = Image.CreateEmpty(ThumbW * Columns, ThumbH * rows, false, Image.Format.Rgb8);
            // Front-of-house seat in the first rows: the stage fills the frame.
            var cam = _scene.GetNode<FlyCamera>("Camera");
            cam.GlobalPosition = new Vector3(0, 1.8f, 6.5f);
            cam.LookAt(new Vector3(0, 1.4f, -4.5f));
            NextCue();
            return;
        }
        if (++_frame < FramesPerCue)
            return;

        var step = _stack.Steps[_step];
        var image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgb8);
        string file = $"{_outDir}/{_step + 1:00}_cue_{step.CueNumberText}.png";
        image.SavePng(file);

        // Darkness: share of near-black pixels in the stage area (between the masking, floor to borders).
        int dark = 0, total = 0;
        double sum = 0;
        for (int y = image.GetHeight() * 3 / 10; y < image.GetHeight() * 3 / 4; y += 4)
            for (int x = image.GetWidth() / 6; x < image.GetWidth() * 5 / 6; x += 4)
            {
                float l = image.GetPixel(x, y).Luminance;
                sum += l;
                total++;
                if (l < 0.03f)
                    dark++;
            }
        int lit = _builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>().Count(f => f.Intensity > 0f);
        GD.Print($"cue {step.CueNumberText,6} {step.Text,-16} lit {lit,2}  mean {sum / total:0.000}  black {100.0 * dark / total:0}%");

        image.Resize(ThumbW, ThumbH);
        _sheet.BlitRect(image, new Rect2I(0, 0, ThumbW, ThumbH), new Vector2I(_step % Columns * ThumbW, _step / Columns * ThumbH));
        NextCue();
    }

    private void NextCue()
    {
        _frame = 0;
        if (++_step >= _stack.Steps.Count)
        {
            _sheet.SavePng($"{_outDir}/contact_sheet.png");
            GD.Print($"saved {_outDir}/contact_sheet.png");
            GetTree().Quit();
            return;
        }
        _builder.ApplyCueStack(_stack, _step);
    }
}
