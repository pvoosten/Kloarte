# Open work and lasting findings

- **Beam occlusion by scenery is on** (Godot finding 12): +5.5 % on the Kruimels rig, +13 % in a worst-case bench (74 lamps,
  49 beams cut). If a larger rig gets too slow: one texture fetch per step (mask and depth in one texture, a 2D combine
  pass on change), fewer steps for cut beams, or a preference / per-lamp switch (`BeamOcclusion.Enabled`). Known: a lamp
  shows an uncut beam for a few frames until its map's turn; scenery closer than 5 cm to a lens doesn't cut.
- **Open (26 Oct 2026): movie size ignored.** Every movie is 1152x648 (the project's default window size) whatever the export
  dialog / preferences ask: Movie Maker ignores the `--resolution WxH` the app passes (`FixturePanel.StartMovies`), both from
  the project and in the exported build (checked in the AVI header). To find: how Godot 4.7 sets the Movie Maker size
  (window size override in the render process, `display/window/size/*`, or resizing the window before the first frame).
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
