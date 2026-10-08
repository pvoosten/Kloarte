# Open work and lasting findings

- **Open:** occlusion by scenery inside the custom beam volume (Godot finding 2).
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
