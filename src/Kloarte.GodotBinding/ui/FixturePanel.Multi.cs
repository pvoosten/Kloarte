using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, several lamps at once (UI review 8; user: intensity and gel, both per head as in MagicQ): the fixture
/// list selects several (Ctrl / Shift + click), Ctrl + click in the 3D view adds a lamp (key map <c>select_add</c>), the
/// plan box-selects. With several lamps selected the settings show the intensity and the gel for all of them ("—"
/// when they differ; mechanics stay per lamp). The gel field (also for one lamp) takes a code as typed ("L201", "201",
/// "R80", "O/W") with matching gels from the console's colour table as you type; it sets the gel of the lamps' heads.
/// </summary>
public partial class FixturePanel
{
    /// <summary>Most gels listed under the gel field.</summary>
    private const int GelSuggestions = 8;

    /// <summary>The list shows the shared selection (all its lamps that are listed).</summary>
    private void ShowSelectionInList() => _list.ShowSelected(Selection.Fixtures);

    /// <summary>Ctrl + click on a lamp in the 3D view: added to the selection, or taken out when it is in it.</summary>
    private bool ToggleInSelection(ProfileFixture lamp)
    {
        if (!KeyMap.Held("select_add", GetViewport()))
            return false;
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        if (!lamps.Remove(lamp))
            lamps.Add(lamp);
        Selection.Select(lamps);
        return true;
    }

    /// <summary>The settings of several selected lamps: intensity (per head) and gel (per head).</summary>
    private void BuildMultiParams(List<ProfileFixture> lamps)
    {
        ClearParams(string.Join(",", lamps.Select(l => l.GetInstanceId())));
        var builder = BuilderOf(lamps[0]);
        var heads = lamps.Select(l => l.HeadNumber).Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
        _params.AddChild(new Label { Text = $"{lamps.Count} lamps selected", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _params.AddChild(new Label
        {
            Text = heads.Count == 0 ? "Not patched"
                : $"Head{(heads.Count == 1 ? "" : "s")} {string.Join(", ", heads.Take(10))}{(heads.Count > 10 ? ", ..." : "")}",
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (builder != null)
            GelField(_params, lamps, builder, () => CallDeferred(MethodName.BuildParams));

        Header(_player.Stack != null && _player.Step >= 0 ? $"Cue {_player.Stack.Steps[_player.Step].CueNumberText}" : "This cue");
        if (builder != null)
            CueValues(lamps[0], builder);
        else
            MultiIntensitySlider(lamps.Select(l => l.Intensity).ToList(), v =>
            {
                foreach (var lamp in lamps.Where(IsInstanceValid))
                    lamp.Intensity = v;
            });
        _params.AddChild(new Label
        {
            Text = "Mechanics are set per lamp: select one lamp to edit them.",
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(200, 0),
        });
    }

    /// <summary>The intensity (in %) of several lamps: shows "—" while they differ, sets all when moved (`set`: 0..1).</summary>
    private void MultiIntensitySlider(List<float> values, System.Action<float> set)
    {
        bool same = values.All(v => Mathf.IsEqualApprox(v, values[0], 0.005f));
        bool moved = false;
        var (row, _) = SliderRow("Intensity",
            same ? "Level set by hand (the programmer) for all selected lamps"
                 : "The selected lamps differ; moving the slider sets them all",
            0, 100, 1, (same ? values[0] : values.Average()) * 100f,
            v => same || moved ? $"{v:0} %" : "\u2014", v => $"DMX {Mathf.RoundToInt(v * 2.55):0}",
            v =>
            {
                moved = true;
                set((float)(v / 100.0));
            });
        _params.AddChild(row);
    }

    /// <summary>
    /// The gel of the lamps' heads, added to `parent`: a swatch, a field with the current code ("—" when they differ,
    /// "O/W" without gel) and, while typing, the matching gels to click. Enter takes the typed gel (or the first match);
    /// `applied` runs after a gel was set.
    /// </summary>
    private void GelField(Container parent, List<ProfileFixture> lamps, ShowBuilder builder, System.Action applied)
    {
        var gels = builder.Gels;
        var ids = lamps.Select(GelOf).Distinct().ToList();
        var row = new HBoxContainer();
        var swatch = new ColorRect
        {
            Color = ids.Count == 1 ? lamps[0].GelColor : new Color(0.5f, 0.5f, 0.5f),
            CustomMinimumSize = new Vector2(18, 18),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var field = new LineEdit
        {
            Text = ids.Count > 1 ? "—" : ids[0] == 0 ? GelCodes.OpenWhite : GelCodes.Code(ids[0]),
            PlaceholderText = "L201, R80, O/W",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SelectAllOnFocus = true,
            TooltipText = ids.Count == 1 && gels?[ids[0]] is { } gel ? gel.Name + "\nThe gel is set per head (MagicQ's patch)"
                : "The gel is set per head (MagicQ's patch)",
        };
        row.AddChild(new Label { Text = "Gel", CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(swatch);
        row.AddChild(field);
        parent.AddChild(row);

        var matches = new ItemList
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 150),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var shown = new List<int>();
        parent.AddChild(matches);
        if (gels == null)
        {
            field.Editable = false;
            return;
        }

        void Apply(int gelId)
        {
            _status.Text = builder.SetGel(lamps.Where(IsInstanceValid), gelId);
            FillList();
            matches.Visible = false;
            field.Text = gelId == 0 ? GelCodes.OpenWhite : GelCodes.Code(gelId);
            swatch.Color = lamps.FirstOrDefault(IsInstanceValid)?.GelColor ?? swatch.Color;
            applied?.Invoke();
        }
        field.TextChanged += text =>
        {
            matches.Clear();
            shown.Clear();
            foreach (int id in GelCodes.Search(gels, text, GelSuggestions))
            {
                matches.AddItem(gels[id].Name, Swatch(gels[id]));
                shown.Add(id);
            }
            matches.Visible = shown.Count > 0;
        };
        field.TextSubmitted += text =>
        {
            if (GelCodes.Parse(text, gels) is { } id)
                Apply(id);
            else if (shown.Count > 0)
                Apply(shown[0]);
            else
                _status.Text = $"'{text}' is not a gel of MagicQ's colour table (e.g. L201, R80, O/W)";
        };
        matches.ItemClicked += (index, _, _) => Apply(shown[(int)index]);
    }

    /// <summary>The gel (colour table id, 0 = none) of a lamp's head.</summary>
    private static int GelOf(ProfileFixture lamp) => lamp.HasMeta("gelId") ? lamp.GetMeta("gelId").AsInt32() : 0;

    /// <summary>A small colour swatch of a gel for the match list.</summary>
    private static ImageTexture Swatch(IGel gel)
    {
        var image = Image.CreateEmpty(14, 14, false, Image.Format.Rgb8);
        image.Fill(new Color(gel.R, gel.G, gel.B));
        return ImageTexture.CreateFromImage(image);
    }
}
