# Kloarte up de plankn — theatre lighting visualizer

Real-time lighting design tool and visualizer for theatre, built with Godot 4 (.NET / C#). Design the lighting of a
play in Kloarte itself (rig, patch, cues, palettes, every value of every lamp), then export it as a ChamSys MagicQ show
for the console; import MagicQ shows, and take back what was changed in MagicQ. It shows what the MagicQ visualiser
can't: framing shutters, iris, gobos and barndoors, beams in haze, gel colours from the patch, moving heads from their
GDTF, and actors moving through the cues.

## Features

- **A show of its own**: every project holds its rig, patch, palettes and one cue list in its own YAML files
  (`rig.yaml`, `palettes.yaml`, `cues.yaml`), with every value in physical units: intensity in %, pan / tilt in
  degrees, colour, zoom, focus, iris, blades, gobo / colour wheel / prism slots by name. Cues track (a cue holds only
  what it changes); palettes are combined (intensity, colour, position and beam in one) and per project.
- **Cue table** (F9): every cue with the heads' levels (set here, from a palette, or tracked), edited in place; the
  chosen head's values in full beside it. New, copied, renumbered and deleted cues, fade and delay times. **Palettes**
  (F10): made from the current cue, edited, renamed, deleted. A lamp's settings show and edit its values in the
  current cue; moving heads are aimed at a point clicked on the stage. Everything is undoable.
- **MagicQ show files (.shw / .xhw)**: a MagicQ show is **imported** (exactly: the DMX after every cue is MagicQ's),
  **exported** into a copy of the show it came from (only what changed is written; an unchanged show exports
  byte-identical; checked by reading it back), and **re-imported** after editing in MagicQ, with a list of what
  differs, as one undo step.
- **Fixture types** from a catalogue of the house rig: profiles (zoom, focus, shutters, iris, gobo), PCs and fresnels
  (spot-flood, barndoors), PARs (CP60/61/62 oval), asymmetric cyc floods (ACP1001), LED bars and moving heads. Each
  type only shows the settings it has.
- **Physical light strength**: lumens and zoom give the intensity (narrower = brighter), inverse-square falloff,
  stray light that follows the light on stage.
- **Moving heads** (Robe LEDBeam 150, T1 Profile) from their GDTF: pan/tilt, colour, zoom, focus, iris, frost,
  gobos, framing blades, set per cue in their own units.
- **Workspaces**: Rig (plan), Focus (3D), Blocking (timeline with actor lanes) and Present, each with its main view
  and small views; views: 3D, from a seat in the audience, plan, truss heights, timeline, through an actor's eyes. **Tools** in windows of
  their own: cue table, palettes, key map, gobo editor.
- **Plan**: lighting plot with symbols per type, gel colours and codes, head numbers, DMX addresses and a legend;
  add fixtures of the house types; drag lamps onto trusses and floor stands; set head number and DMX address; save as
  PDF.
- **Truss heights**: fly trusses (within the venue's limits); lamps move along and stay focused on the same spot.
- **Cue playback and timeline**: GO / Back / Auto with the cues' fade and delay times; actors that appear, walk, sit and lie down;
  an interactive timeline of cues and actor steps (scrub, drag waits, walking times and holds). Entrances and exits
  are marked with their side ("OP cour", "AF jardin") and announced on the status bar while the cues run. **Smoke per
  cue**: each cue can set the amount of haze in the room and how long the hazer takes to get there.
- **Exports**: stills (a PNG of every key moment), a movie, or a contact sheet (PDF, a picture per cue); from the 3D
  camera, a seat or a named viewpoint (Regie), a cue range, with cue captions; also one per actor (only their name on
  their shirt) to send to the cast.
- **Live Art-Net** input from MagicQ; **Open in MagicQ PC** exports the show, starts MagicQ PC with it and checks every
  look it outputs against the app's cues (MagicQ user dimmer curves included).
- **Claude / MCP**: an MCP server inside the app (`http://localhost:8462/mcp`) lets Claude Code read the open show, set
  cue and palette values, change lamp mechanics, play cues and take screenshots of the views; every edit is one undo
  step, nothing is saved until you (or `save_project`) save. Connecting and the tools: [manual](docs/manual.md),
  *Claude / MCP*.
- **Key map**: every function can get its own keys; changes are saved.

## Requirements

- [Godot 4.7 .NET](https://godotengine.org/download) (the "mono" build), Forward+ renderer
- .NET 8 SDK
- Windows desktop with a Vulkan/D3D12 capable GPU
- Optional: ChamSys MagicQ PC (to run the exported shows; its gel table `show/heads/allcols.csv`, and `ffmpeg.exe`
  for MP4 movies). Kloarte designs shows without it.

## Getting started

```sh
dotnet build Kloarte.sln
```

Open `src/Kloarte.Host` (the Godot project) in the Godot .NET editor, click **Build** (hammer icon, top right) and run
`shows/risquons_tout.tscn` (F6 in that scene). It starts without a project: open one from the File menu (or start with `-- --project=<folder>`, see `run.ps1 -Project`).
Set `OpenMagicQLastShow` on the `Show` node to open the show MagicQ PC had loaded last instead (the show named in MagicQ's
`status.dat`, else `lastshow.shw`), with the project that show belongs to; otherwise the node's `ShowFile` opens.

### Projects

A project is one production: a folder `<projects>\<name>\` with `<name>.kloarte.yaml` and its show and data, all
YAML, so they can be read and edited in VS Code (completion, hover and warnings come from the JSON Schemas Kloarte
copies to `.schemas\`):

- `rig.yaml`: the fixture types, the patch (heads: number, name, type, DMX address, gel, beam angle), groups, dimmer
  curves and every lamp and object of the rig;
- `palettes.yaml`: the palettes, values per head;
- `cues.yaml`: the cue list, in cue order, a cue holding the values it changes per head (`"201": {intensity: 80,
  Pan: -12.5, Gobo1: Stars}`; `"@Front"` = from the palette Front; `dmx 37` = an exact DMX value);
- `mechanics\`: one file per lamp (type, zoom, shutters, iris, gobo, barndoors, focus: the hand focus of the whole
  show; the lamp is the `rigItem` id inside the file);
- `blocking\actors.yaml`, `blocking\steps.yaml` (actors, their movement per cue, holds, smoke per cue);
- `venue.yaml`, the project's own venue.

Kloarte writes a file only when its content changed; comments you add are kept until Kloarte changes that file. A
change made in another program is taken in as one undo step (Edit > Undo: "Edited outside Kloarte: ..."). When
Kloarte has unsaved changes for the same file, it asks which one to keep. A file with an error is not applied and
never overwritten: the last good state stays, the error goes to the status bar in red with its line, a click opens the
file there in VS Code (otherwise Explorer). An edit of `venue.yaml` rebuilds the scene with the new venue.

The project box at the top of the left panel names the project and its folder (click it to see it in Explorer). A
MagicQ show is not part of a project: it is only imported and exported. The project items of the **File** menu:

- **New project...**: the house venue (CC De Brouckere) and no lamps.
- **New project from show...**: a MagicQ show imported as a new project; the show itself is never changed, it is the
  one exports are written into a copy of. Mechanics and venue of the show's project come along, the blocking doesn't.
- **Open project**: the projects, most recently saved first. **Open project folder...** (Ctrl+Shift+O): a project's
  folder anywhere, chosen in a folder dialog (a folder with a `.kloarte.yaml`, or just a show's `rig.yaml`; a copied or
  moved project opens its own show). **Import MagicQ show (.shw)...**: a MagicQ show of the open
  project is re-imported (what differs is listed first; one undo step); another show opens its project, or a new one.
- **Export MagicQ show...**: writes the show as a MagicQ show; **Open in MagicQ PC** (Playback) exports it as the next
  numbered show (`Kruimels_002.shw`, `Kruimels_003.shw`, ...; nothing is overwritten) and starts MagicQ PC with it.

A project made before Kloarte had its own show (its show was a MagicQ `.shw`) is imported when it opens and becomes a
native project at its first **Save** (its `cues.yaml` of before is replaced by the cue list).

### Distribution

```powershell
powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1                    # templates already installed
powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1 -InstallTemplates  # first time: downloads them (~1 GB)
```

This exports a release build into `build\Kloarte` and zips it as `dist\Kloarte-<date>.zip`. Unzip anywhere and run
`Kloarte.exe`: it starts the show scene. The app files it reads (`data\`, the venue, built-in gobos, fixture configs) sit
next to the exe. An exported Kloarte keeps its project data in `Documents\Kloarte`; copy `Projects\` there to take
your mechanics along.

## Using the app

The manual (menus, panels, views, cue table, palettes, cues.yaml, MagicQ import / export, smoke, export, default keys) is in [docs/manual.md](docs/manual.md).

## Where things are stored

| What | Where |
|---|---|
| Rig and patch: fixture types, heads, groups, gels, beam angles, lamp and object positions, truss heights, floor stands | `Projects/<project>/rig.yaml` |
| Palettes | `Projects/<project>/palettes.yaml` |
| The cue list: cue numbers, texts, fade and delay times, every value per head | `Projects/<project>/cues.yaml` |
| The MagicQ show for the console | exported to MagicQ's show folder (`Documents\MagicQ\show`), the next numbered show |
| Mechanics per lamp: type, zoom, shutters, iris, gobo, barndoors, PAR lamp, focus | `Projects/<project>/mechanics/*.yaml`, one file per lamp (not in git) |
| Blocking: actors, their movement per cue, holds | `Projects/<project>/blocking/actors.yaml`, `steps.yaml` (not in git) |
| Fixture types of the house rig | `data/fixture_catalog.json` |
| Venue: stage, cyc, hoist plan, stands, auditorium seats | `Projects/<project>/venue.yaml` (every project holds its own: Save writes a copy of the house venue `venue/<venue>.venue.yaml` the first time; yours to edit) |
| Moving head definitions | `data/gdtf/*.gdtf`; the catalogue's fixture types (channels as MagicQ's personalities) `data/fixture_types.yaml` |
| Look: stray light, haze | `Projects/<project>/<project>.kloarte.yaml` |
| Smoke per cue (amount, build time) | `Projects/<project>/blocking/steps.yaml` (`smoke`, `smokeTime`; not in git) |
| Gobos | new ones from the gobo editor in `Projects/<project>/gobos/<name>.png` (+ `.gobo.json`); the shared library `Projects/gobos/` (made before); built-in ones in `gobos/` |
| Preferences (UI scale, PDF paper, movie size, Art-Net universe) | `user://preferences.json` |
| Key map changes, workspaces | `user://keymap.json`, `user://views.json` (Godot's user data folder) |

`Projects/` is in `src/Kloarte.Host` when Kloarte runs from the Godot project; an exported (distributed) Kloarte
keeps the same files in `Documents\Kloarte` instead. The paths `data/`, `venue/`, `gobos/` in the table above are in
`src/Kloarte.Host` too. Files of before projects (`Projects/<show>.mechanics.json`, ...) are copied into the project
folder the first time the show opens; the old files stay.

## Project layout

`Kloarte.sln` holds the projects; `src/` is what runs the app, `test/` the automated tests.

| Project | Contents |
|---|---|
| `src/Kloarte.Model` | the model, without Godot or MagicQ: the native show (`DesignShow`: rig, palettes, cues in physical units, projected to DMX), show model and mechanics, fixture catalogue, photometrics, GDTF, rig and stage frame, venue, plan and its PDF writer, playback timeline, gobo documents, and the interfaces the rest implements |
| `src/Kloarte.MagicQ` | everything that touches MagicQ: show files (.shw / .xhw), import and export of native shows, `status.dat` and MagicQ PC, Art-Net input |
| `src/Kloarte.GodotBinding` | everything Godot: the lamps (`ProfileFixture`, moving heads), the scene builder, views, plan view, cue table, palettes, panel, key map |
| `src/Kloarte.Application` | the open show (files, loading, saving, re-import), rig and patch edits, playback, the round trip with MagicQ PC, the MCP tools; it uses MagicQ, Godot and the MCP server only through the bindings' interfaces |
| `src/Kloarte.Mcp` | the MCP server in the app (the protocol, .NET only; the tools are the application's) |
| `src/Kloarte.Yaml` | the codec of the project files (YAML, on YamlDotNet); the host sets it, so the model stays free of it |
| `src/Kloarte.Host` | the Godot project: `project.godot`, scenes, shaders, data, venue, gobos, the start-up code that puts it all together, the movie renderer |
| `test/Kloarte.Architecture.Tests` | the dependency rules between the projects (checked on the compiled assemblies) |
| `test/Kloarte.Model.Tests`, `test/Kloarte.MagicQ.Tests`, `test/Kloarte.Mcp.Tests`, `test/Kloarte.Application.Tests` | xUnit tests (no Godot) |
| `test/Kloarte.Godot.Tests` | Godot tests: captures of the real scenes, diagnostics |

## Limitations

- The MagicQ file format is not documented; it was reverse engineered from MagicQ 1.9.7 shows. What an export writes
  of kinds not yet verified in MagicQ PC (new palettes, changed cue channels and times, cue steps put in or taken out,
  new heads, groups and MagicVis items, floor stands) is listed after the export: check it in MagicQ before relying on it.
- Not exported: dimmer curves (edit them in MagicQ) and cue delays (Kloarte's playback only).
- Only one cue list: an imported show's other cue stacks stay in the MagicQ show (exports keep them).
- Light output of most fixture types is estimated (no datasheets yet).
- Not simulated: animation wheel, prism, strobe effects, MagicQ FX, parallel playbacks; moving-head transitions jump
  (transitions are handled in MagicQ).
- MagicVis custom models (`.mvm`) are shown as placeholder boxes.

Not affiliated with ChamSys or Robe. MagicQ and MagicVis are products of ChamSys Ltd.
