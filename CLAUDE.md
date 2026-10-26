# Kloarte up de plankn — theatre lighting visualizer (Godot .NET)

App name: "Kloarte up de plankn" (West Flemish: light on stage), short "Kloarte". First used for the play "Kruimels". Tests based on a previous play "Risquons-Tout"

## Goal
A realistic real-time visualizer for theatre lighting, that can partially control a ChamSys MagicQ PC application (show files,
and live over Art-Net). It must show what the MagicQ visualiser can't:
- barndoors (flappen) cutting the beam of fresnels and PCs
- framing shutters (messen), iris and gobos on profiles
- all functions of the Robe T1 Profile (framing shutters, animation wheel, prism, CMY/CTO, zoom, focus, iris)
- beams visible in haze/smoke
- simulate haze/smoke machines

The user is an experienced theatre technician (knows DMX, patching, console programming) and a .NET expert.
The actual performance will be done with a MagicQ console, so the shw file must be perfect.

## Code style (user)
- **One type per file**: every top-level class/record/enum/struct in its own file named after the type
  (nested types only when they are private helpers of their outer type; public nested types such as the
  `MagicQShow` record views go in their own partial file, e.g. `MagicQShow.Cue.cs`).
- **Comments are XML doc comments where possible**: `/// <summary>` on types and members (incl. private fields
  and properties); `//` only inside method bodies or where XML docs can't go (e.g. JSON / shader files).
- **Document every public type and every public member** of each type (the build has
  `GenerateDocumentationFile`, so a missing doc comment is warning CS1591).
- **Minimize visibility**: `private` / `internal` / `protected` wherever possible; `public` only where another
  type needs it (Godot needs `public` for `[Export]` / `[Signal]` members and for node script classes).
- Model code (show, rig, venue, timeline, palettes, patch, GDTF, gobo documents, plan drawing, PDF) has **no Godot
  dependencies** (`src/Kloarte.Model`) and is tested with xUnit (`test/Kloarte.Model.Tests`, no Godot).

## Dependency rules (IMPORTANT, user)
The projects under `src/` depend on each other only in this direction; never break a rule to make something easier,
change the design (an interface in the right project, a factory set by the host) instead.
1. **Model** (`Kloarte.Model`): .NET only. No Godot, no MagicQ, no application.
2. **MagicQ binding** (`Kloarte.MagicQ`: show files, MagicQ PC, Art-Net): the model only. Knows nothing of Godot or
   the application.
3. **Godot binding** (`Kloarte.GodotBinding`): the model and Godot. Knows nothing of MagicQ or the application; what
   it needs from them it gets through its own interfaces (`IShowServices`, `ILiveInput`, ...) or the model's.
4. **MCP binding** (`Kloarte.Mcp`: the MCP protocol of the server in the app): .NET only. Knows nothing of the model,
   Godot, MagicQ or the application; the tools are the application's (`IMcpTool`).
5. **Application** (`Kloarte.Application`): the model and the three bindings, but **never acts on MagicQ, Godot or the
   MCP server directly**: no Godot API at all, and from the bindings **only their interfaces** (no classes, records,
   enums, static helpers). Generic over the show type (`ShowSession<TShow>`); scenes are built from model data only.
6. **Host** (`Kloarte.Host`, the Godot project) is the only composition root: it picks the implementations
   (`host/Startup.cs`) and holds the scripts scenes attach.
7. **Codec** (`Kloarte.Yaml`, YamlDotNet): the model and YamlDotNet only. The only project that uses YAML: the model
   defines the project files and the codec interface (`IFileCodec`); the host sets `FileCodec.Current` (the codec
   reads and writes the files; Model, Application and bindings never name YamlDotNet).
8. Namespaces = project names; every type of a project is in its project's namespace.
Enforced by `test/Kloarte.Architecture.Tests` (`DependencyRuleTests`, part of `dotnet test Kloarte.sln`): it reads the
compiled assemblies' metadata, so a fully qualified name or a new project reference can't slip past. A failing rule
test is a design problem to fix, not a test to relax (ask the user before changing a rule).


## Coordinate system (metres)
- Origin = the show's **proscenium line** (MagicVis proscenium object z, Risquons-Tout 9.75) on the stage floor; the
  forestage reaches `Venue.Forestage` (2.15 m in CC De Brouckere) further downstage (the stage edge).
- +X = audience's right (Jardin = -X), +Y = up, -Z = upstage, +Z = towards the audience. MagicVis axes are the same.
- Plan (2D): plan x = stage x, plan y = stage z (audience at the bottom).


## Technical decisions (detail: docs/architecture.md, decisions only)
- **Godot 4 .NET, C#**, shaders in Godot shading language. **Desktop only, Forward+** (Compatibility has no volumetric fog).
- DMX in: Art-Net UDP 6454 on a background thread (`dmx/ArtNetReceiver.cs`), applied on the main thread in `_Process`.
- Fixtures: catalogue `data/fixture_catalog.json` (house rig); moving heads from GDTF (`data/gdtf/`). Conventional lamps
  get focus, shutters, barndoors, iris, gobo from the mechanics files, not from DMX.
- Beam shaping: one mask texture per fixture (`beam_mask.gdshader` -> SubViewport once on change -> `GetImage()` after
  `FramePostDraw` -> **new** `ImageTexture` -> `LightProjector` + `beam_volume.gdshader`). Intensity / colour changes only
  update the light (`ChangeLight`), never re-render masks (72 lamps per frame = 3 fps).
- Haze: custom raymarched beam volume per fixture (that light's `LightVolumetricFogEnergy` = 0); Godot's fog only for
  ambience / floods. Level = global shader uniform `haze_density`.
- Light strength: lumens -> peak candela = lumens / (solid angle x (1 + falloff) / 2); inverse-square (`SpotAttenuation = 2`).
- **Project files are YAML, editable in VS Code** (`Kloarte.Yaml`; model defines the files and `IFileCodec`): per project
  `<name>.kloarte.yaml`, `venue.yaml` (the user's, never rewritten after the first save), `mechanics/<lamp>.yaml` (one per lamp,
  keyed by the rig item id inside the file; only values differing from `FixtureMechanics.DefaultFor`), `blocking/actors.yaml`
  + `steps.yaml` (holds, smoke per cue), the native show's `rig.yaml`, `palettes.yaml`, `cues.yaml`. Stay JSON: `fixture_catalog.json`, `shows/*.fixtures.json`, gobo drawings,
  `user://keymap.json` / `views.json`. Old JSON still loads; the first save writes YAML and keeps the JSON.
  Rules: a file is written only when its content changed (hand comments survive); every file has a JSON-Schema modeline
  (schemas generated from the file attributes, committed copies in `src/Kloarte.Host/data/schemas/`, a test checks them;
  regenerate with `KLOARTE_UPDATE_SCHEMAS=1 dotnet test`); the folder is watched and an outside edit is one undo step
  (conflict with unsaved changes = Take the file's / Keep Kloarte's / Save Kloarte's first); **a broken file is never
  applied nor overwritten** (last good state kept, error on the status bar with file:line:col, other files unaffected);
  unknown keys / out-of-range values are warnings.
- **Standalone design tool (decided 14 Oct 2026, built 14-15 Oct; detail in docs/architecture.md)**: design and display
  only, **no programmer** (a value set by hand goes into the current cue). Every project is a native show `DesignShow`
  (`rig.yaml`, `palettes.yaml`, `cues.yaml` = the file types themselves; one tracking cue list, values in physical units
  `LookValue` per head and channel key (`HeadLayout`: GDTF attributes, `intensity`, `#n`), combined palettes per
  project, position = angles); conventional lamps intensity per cue only. It is an `IShowDocument` (cues projected to
  DMX), so playback, timeline and scene work unchanged; after editing the lists call `DesignShow.Changed()` (UI: through
  `CueEditing.Edit`). MagicQ = `IShowExchange`: import (exact, all shows), export into a copy of the source (only what
  differs; unchanged records byte-identical; read back and compared), re-import with a diff (`DesignDiff`, one undo
  step). A project of before (show = a .shw) is imported at load and becomes native at its first Save.
- **Exports never overwrite**: an export is the project's next numbered MagicQ show (`ShowVersions.NextFile`); a test
  that exports deletes its target first.
- **Smoke per cue** is Kloarte's own data in `blocking/steps.yaml` (`smoke` %, `smokeTime` s), not a MagicQ hazer fixture.
- **Show names**: exported MagicQ shows go to MagicQ's show root, each the next number (`Kruimels_002.shw`), never an
  overwrite; a MagicQ save of the project comes back by re-import. Project data is keyed by project.
- Actor ids are slugs of the name (`ActorIds.For`); renaming keeps the id.
- Undo = snapshots (`EditHistory`), the scene is rebuilt from the model; `ShowSession.Editor` must follow `Model.Show`.
- **MCP server in the app (decided 25 Oct 2026)**: Claude (or any MCP client) reads, edits, plays and looks at the open
  show. Hand-written (`Kloarte.Mcp/McpServer`, `HttpListener`; no ASP.NET Core in the Godot process, no NuGet package):
  Streamable HTTP, POST JSON-RPC to `http://localhost:8462/mcp`, one JSON answer per request (no SSE, no sessions),
  localhost only, a foreign `Origin` is refused (403). Tool calls are queued and run on Godot's main thread
  (`HostFixturePanel._Process` -> `Pump()`). Tools = `RemoteTools` (application) on `IRemoteControl` (UI's own paths:
  `CueEditing.Edit`, undo, Save, export): every edit one undo step, nothing saved until `save_project`, the export is
  the next numbered show. Port: `-- --mcp-port=N`, `KLOARTE_MCP_PORT`, `--no-mcp` / 0 = off; a taken port = no server.

## Godot findings (4.7.2) (detail: docs/godot-findings.md)
- `light_projector` refuses a `ViewportTexture`; `ImageTexture.Update()` doesn't refresh it: make a new texture per change.
- Godot's volumetric fog ignores projectors, hence the custom beam volume. OPEN: no occlusion by scenery inside the beam.
- **A spot with a projector and `ShadowEnabled = false` gives no light**: use `ShadowEnabled = true` + `ShadowCasterMask = 0`.
- Shadow atlas: set it with `ShowBuilder.ConfigureShadowAtlas` on every viewport that renders the stage (default places run out
  at the 88th lamp = dark lamps).
- `UnprojectPosition` gives canvas coordinates, not image pixels. A Control's `_Ready` runs at once when added to a tree.
- SubViewports sharing `World3D` show the same scene; the root has `Disable3D` while the view host shows 3D views.
- **Exit with a native file dialog open hangs**: open every native dialog via `NativeFileDialogs.Popup`; the window close is
  refused while one is open.
- `FileDialog` replaces its `Title` unless `ModeOverridesTitle = false` (set before `FileMode`); a wrapping `Label` in a
  dialog needs a minimum width.
- Native tool windows: set `ForceNative` while hidden; key handlers must be child nodes (`ToolWindowKeys`).
- **An export template refuses a scene on the command line** (`disable_path_overrides`, aborts): the exported build
  starts `boot.tscn` (`HostBoot`), which runs the movie render on `-- --render-movie`, else the show scene.

## Files
Solution `Kloarte.sln`; `src/` runs the app, `test/` tests it. `README.md` is user-facing: keep it in sync with features.
- `src/Kloarte.Model/` — `Abstractions/` (`IShowDocument`, `IRigItem`, `ICue`, `IAppPaths`, ...), `project/` (project folder,
  watcher, `ShowVersions`), `files/` (YAML file types, `CueFiles`), `show/` (`ShowModel<TShow>`, mechanics, blocking),
  `design/` (the native show: `DesignShow`, `LookValue`, `HeadLayout`, `DesignStore`, `DesignShowEditor`, `DesignDiff`),
  `fixtures/`, `gdtf/`, `gobos/`, `rig/`, `venue/`, `plan/` (plan drawing, own PDF writer), `playback/` (`PlaybackTimeline`).
- `src/Kloarte.MagicQ/` — `.shw` format (`ShwDocument`, `MagicQShow` partial per view, `PatchEditor`), `MagicQStore`,
  `MagicQShowEditor`, `MagicQPc`, `design/` (`MagicQImport`, `MagicQExport`, `MagicQExchange`), Art-Net (`dmx/`).
- `src/Kloarte.GodotBinding/` — `binding/` (`IShowServices`, `IStage`, `ILiveInput`, `IRemoteControl`), `show/ShowBuilder`
  (partial), `fixtures/ProfileFixture` + `MovingHead`, `camera/`, `environment/`, `gobos/`, `plan/`, `playback/`,
  `scenery/`, `ui/` (`FixturePanel` partial incl. `FixturePanel.Remote`, `ViewHost`, views, `KeyMap`, `StatusBar`, ...).
- `src/Kloarte.Mcp/` — the MCP binding: `McpServer`, `IMcpServer`, `IMcpTool`, `IMcpReply`, `McpReply`.
- `src/Kloarte.Application/` — `EditHistory<TShow>`, `ProjectLibrary`, `ShowSession<TShow>`, `RigEditor`, `ShowPlayback`,
  `ConsoleLink`, `LiveInput`, `ShowServices<TShow>`, `RemoteTools` (partial per area: Show, Rig, Cues, Palettes, Stage, Aim, Lamps,
  Values; `RemoteTool`, `ToolArgs`, `ToolSchema`).
- `src/Kloarte.Yaml/` — the codec. `src/Kloarte.Host/` — Godot project, `host/Startup.cs` composition root, `host/Host*.cs`
  scripts scenes attach, scenes, shaders, `data/` (`.gdignore`), `Projects/` (git-ignored, one folder per project).
- `test/` — `Kloarte.Architecture.Tests`, `Model.Tests`, `MagicQ.Tests`, `Mcp.Tests` (`McpServerTests`: the protocol over
  HTTP), `Application.Tests` (incl. `RemoteToolsTests` on `FakeRemoteControl`; xUnit, shared in `test/Shared`),
  `Kloarte.Godot.Tests` (Godot project). `tools/make_dist.ps1` builds the distribution.
- UI: workspaces Rig / Focus / Blocking / Present (`ViewHost`), tools (key map F6, gobo editor F7) in native windows, inspector
  right, status bar bottom, every function an `AppAction` in `KeyMap` (never hard-code keys in texts: `KeyMap.Tooltip`, `Hint`).
- UI of the native show: cue table (F9, `CueTableView`), palettes (F10, `PaletteView`), the values of a head in a cue
  (`LookEditor`, also in the lamp settings), shared state and edits in `CueEditing`.
- User manual (menus, views, cue values, cue table, palettes, MagicQ import / export, Claude / MCP, keys): `docs/manual.md`.

## MagicQ files (detail: docs/magicq-format.md)
Shows are in `C:\Users\pvoos_nnvpp5d\OneDrive\Documents\MagicQ\show\`, **read-only for us**. Everything MagicQ stores is
kept **lossless** (unchanged records byte-identical; `MagicQRoundTripTests` checks all 64 shows).
- `.shw` ASCII, CRLF, records of comma-separated fields ending in `;`, mostly hex. Record types: `P` personality, `L` patch
  (one per DMX channel), `U` user curve, `G` group, `C` cue stack, `S` cue, `F` palette, `H` MagicVis item, `T` console settings.
- Cue values = `palette id << 8 | DMX value` when they refer to a palette. Gel and intensity are per head.
- `.xhw` = ustar tar with icons and models. `status.dat` T f1 = the show MagicQ opens (edit only while MagicQ is closed).
- **Unverified in MagicQ PC:** written palettes (trailer), inserted cue pairs, floor stands as pipes with rz = 90, people codes.

## Tests
- `dotnet test Kloarte.sln` = Architecture, Model, MagicQ, Mcp, Application tests. Godot tests need `dotnet build Kloarte.sln` first, then
  `<godot>_console.exe [--headless] --path test/Kloarte.Godot.Tests res://tests/<scene>.tscn` (markers like "BINDING OK",
  "PLAYBACK OK", "PANEL...": see the scene names in test/Kloarte.Godot.Tests/tests).
- Exploratory testing: scene `explore` with `-- --steps="...;shot:a;dump;quit"`; look at the shots with Read. Never press
  "Open in MagicQ PC" there. After each step explore; an odd result gets a test that reproduces it, then the fix.
- **Find controls by title / tooltip / text, never by index.**
- **Tests never hand a path in the user's MagicQ folder to anything that writes**: open copies in `tests/output`, use
  `MagicQPc.ShowFolderOverride` / `TestShowStore`. Afterwards `find <MagicQ folder> -newermt "-15 minutes"` lists nothing.
- Tests must not touch user data: `ViewHost.Persist = false`, `KeyMap.EnsureLoaded("res://tests/output/...")` first in `_Ready`,
  `GoboLibrary.Folder = "res://tests/output/gobos"`.

## Build / run (detail: docs/build-and-release.md)
- Godot 4.7.2 mono: `c:\tools\godot\Godot_v4.7.2-stable_mono_win64[_console].exe`
  (`_console.exe` for logs). `dotnet build Kloarte.sln`, then `--path src/Kloarte.Host` (e.g. `res://shows/risquons_tout.tscn`).
- After adding assets: `<godot>_console.exe --headless --path src/Kloarte.Host --import` (and for the test project).
- Patch scripts: write Python helpers with the Write tool (heredocs and sed mangle `\n`, `\0`, `\\`).
- Distribution: `powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1`. Hung build, release logs: docs/build-and-release.md.

## Rig, venue, materials
Equipment, CC De Brouckere tech sheet and material values: `docs/venue-and-rig.md` and `data/fixture_catalog.json`.
Open work and lasting structure findings (plain library nodes get no callbacks, scripts in scenes must be in the host, namespace
`Kloarte.GodotBinding`): `docs/open-work.md`.
