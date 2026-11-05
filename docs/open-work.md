# Open work and lasting findings

- **Beam occlusion by scenery is on** (Godot finding 12): +5.5 % on the Kruimels rig, +13 % in a worst-case bench (74 lamps,
  49 beams cut). If a larger rig gets too slow: one texture fetch per step (mask and depth in one texture, a 2D combine
  pass on change), fewer steps for cut beams, or a preference / per-lamp switch (`BeamOcclusion.Enabled`). Known: a lamp
  shows an uncut beam for a few frames until its map's turn; scenery closer than 5 cm to a lens doesn't cut.
- **Movie export resolution (resolved 5 Nov 2026; Godot finding 13).** Movie Maker takes its output dimensions from the
  project viewport settings at startup; `--resolution` alone does not override them. `FixturePanel.StartMovies` writes a
  temporary `override.cfg` for the selected size before launching the render process. The render startup removes it after
  Godot has loaded the settings; an existing user `override.cfg` is never overwritten.
- **Show names**: MagicQ's Save of a show from a subfolder writes into the show root with the folder as prefix and raises the
  number (`Kloarte\Kruimels_001` -> `Kloarte_Kruimels_002`), and writes spaces as underscores. So a project's show lives in the
  show root, every Kloarte save is the next number, MagicQ's numbered saves become the project's show; project data is keyed
  by project (`KloarteProject`, `ShowVersions`, `ProjectLibrary`). Verified in MagicQ PC: `Kruimels_001` -> `Kruimels_002`.
- **Solution structure**: a plain library (`Microsoft.NET.Sdk` + `GodotSharp`) can use the Godot API but its own Node subclasses
  get **no callbacks**; a `Godot.NET.Sdk` library's nodes created in code keep them, but scripts *attached in scenes* must be
  files under the host's res:// (hence the empty `host/Host*.cs` subclasses; repeat `[Tool]` where the base has it). Only the
  host may have `GodotPlugins.Game.Main` (`GodotDisabledSourceGenerators=GodotPluginsInitializer` in the binding). The namespace
  is `Kloarte.GodotBinding`, not `Kloarte.Godot` (it would hide Godot's `Godot` namespace). The Godot test project copies the
  host's files with an inline RoslynCodeTaskFactory task (MSBuild item lists would split `project.godot`'s `;` comments).
