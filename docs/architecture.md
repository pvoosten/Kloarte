# Technical decisions

Moved out of CLAUDE.md; the per-project file listing was dropped (code and XML docs say it). Dated history is still here.

## Technical decisions
- **Godot 4 .NET, C#** for all game code. Shaders in Godot shading language.
- **Desktop only, Forward+ renderer.** No web export (Compatibility renderer has no volumetric fog).
- DMX input: Art-Net on UDP 6454 via `UdpClient` on a background thread (`dmx/ArtNetReceiver.cs`); buffered per
  universe, applied on the main thread in `_Process`.
- Fixture types: catalogue `data/fixture_catalog.json` (house rig: photometrics, zoom range, lamps, accessories);
  moving heads from GDTF (`data/gdtf/`, Robe LEDBeam 150 RGBW + T1 Profile, supplied by the user). Conventional
  fixtures get focus, shutters, barndoors, iris and gobo from the mechanics JSON, not from DMX — like hand focus.
- Beam shaping: one mask texture per fixture (shutters + iris + gobo + barndoors + PAR oval + focus blur), rendered
  by `beam_mask.gdshader` into a SubViewport (UpdateMode.Once, only on change) -> read back after
  `RenderingServer.FramePostDraw` with `GetImage()` -> **new** `ImageTexture` each change -> `SpotLight3D.LightProjector`
  and `beam_volume.gdshader` (haze). Intensity / colour / output changes only update the light (`ChangeLight`), not
  the mask (an intensity change per frame for 72 lamps re-rendering masks took the show to 3 fps).
- Asymmetric floods: no mask shader; a CPU-made projector image of `FloodDistribution` (89° cone).
- Light strength: catalogue lumens -> peak candela = lumens / (solid angle x (1 + falloff) / 2): narrower = brighter;
  PARs: candela per lamp; inverse-square attenuation (`SpotAttenuation = 2`); `ProfileFixture.CandelaToEnergy` 0.075.
- Beam in haze: custom raymarched beam volume per fixture, sampling the same mask; that light's
  `LightVolumetricFogEnergy` = 0. Godot's volumetric fog stays for ambience / floods.
- Haze level: global shader uniform `haze_density` (project.godot `[shader_globals]`); environment fog separately.
- **Project files in YAML, editable in VS Code** (user, decided 11 Oct 2026; **built 11 Oct 2026**, `Kloarte.Yaml` +
  `files/` / `project/` in the model, `ShowSession.Files.cs`, `FixturePanel.ProjectFiles.cs`; tests `YamlFilesTests`,
  `YamlProjectTests`):
  - Scope: the project data (`<name>.kloarte`, mechanics, blocking, cues, venue) and the house venue
    (`ccdebrouckere.venue.yaml`). Stay JSON: `fixture_catalog.json`, `shows/*.fixtures.json`, gobo drawings,
    `user://keymap.json` / `views.json`.
  - Folders per area in the project folder: `<name>.kloarte.yaml`, `venue.yaml`, `mechanics/<lamp>.yaml` (**one file
    per lamp**), `blocking/actors.yaml` (names, shirt colours), `blocking/steps.yaml` (all steps grouped by cue number,
    plus the holds and the smoke per cue: `smoke` %, `smokeTime` s), **`venue.yaml`** (user, 12 Oct 2026: every project holds
    its own venue: `SaveYamlFiles` writes it on the first Save from the old `venue.json`, else a copy of the scene's venue
    (`IShowFiles.DefaultVenueFile`, set by `ShowBuilder`); never rewritten after, it is the user's) and **`cues.yaml`**
    (user, 12 Oct 2026: the show's cues are in the project files and editable there; until 15 Oct 2026 they were
    written into the .shw, now `cues.yaml` is the native show's cue list, with `rig.yaml` and `palettes.yaml`).
  - Lamp identity: the rig item (MagicVis H) id inside the file (`rigItem: 75`) is the key. The file name is readable
    and derived (head number in decimal, model, support, place, e.g. `h012-c103-LX1-3.yaml`), renamed by Kloarte on
    save when those change; a hand-renamed file still loads; two files with the same id = an error for both. A
    generated header comment says what the lamp is (head, DMX, gel, truss, position).
  - Content: only values that differ from `FixtureMechanics.DefaultFor(model)`; fixed key order, rounded numbers
    (metres 3 decimals, degrees 0.1), short lists in flow style (`focusPoint: [x, y, z]`), colours quoted `"#rrggbb"`.
    A file is written only when its content changed (hand-written comments survive until Kloarte changes that file).
    Format version in the project document.
  - Migration: old JSON (project folder and flat files of before projects) keeps loading; the first save writes YAML;
    the JSON files are kept.
  - VS Code: JSON Schemas per file kind (descriptions, units, enums, ranges); every written file starts with a
    `# yaml-language-server: $schema=...` modeline (Red Hat YAML extension).
  - Reload on external edit: the project folder is watched (debounced, Kloarte's own writes ignored by content hash);
    an external change is one undoable step ("Edited outside Kloarte: ..."); with unsaved Kloarte changes in the same
    area it asks as for a MagicQ save (Take the file's / Keep Kloarte's / Save Kloarte's first).
  - Errors in hand-edited files: keep the last good state (a broken file is not applied); per-file isolation (one
    broken lamp file doesn't stop the others, its lamp is marked in the fixture list); never overwrite a file that
    doesn't parse; errors on the status bar in red with file:line:column, a click opens `code -g file:line:col`
    (else Explorer); unknown keys and out-of-range values are warnings, not errors.
  - Decided while building it (11 Oct 2026): YamlDotNet 16.3.0 in its own project `Kloarte.Yaml` (the model stays
    .NET only: the codec rule, now rule 7); schemas generated from the attributes, a copy per project in `.schemas/` (the house venue
    points at the committed copies in `src/Kloarte.Host/data/schemas/`, a test checks they are current); a hand
    comment survives until Kloarte changes that file (the comparison ignores comment lines except the modeline and
    Kloarte's `# Kloarte:` header); the JSON files are kept (no `.json.bak`); the watcher is polled by the window
    (400 ms quiet, Kloarte's own writes ignored by content hash). The project document is rewritten on every save
    (its `saved` stamp) and is not watched. The project's `venue.yaml` is watched (user, 12 Oct 2026): an outside edit
    rebuilds the scene (not model data: no undo step), a broken venue is not applied (the scene keeps its venue, also
    on a later rebuild). The house venue is YAML only (its JSON deleted, user 12 Oct 2026); an old project's
    `venue.json` is still read.
    "Save Kloarte's first" overwrites the file's change, as MagicQ's does.
- **Standalone design tool, native show** (user, decided 14 Oct 2026; **built 14-15 Oct 2026**, see "Built" below): Kloarte is for design and
  display only, not live operation. Every project gets a native show in YAML; the .shw only comes in by import and goes
  out by export.
  - Show: `DesignShow` in `Kloarte.Model` (namespace `Kloarte.Model`): rig (fixtures with model, head, address, mode;
    gel per head; supports), **one cue list**, palettes. It implements `IShowDocument` by projecting to DMX (GDTF and
    the catalogue), so playback, timeline, `CueTracking`, Art-Net and the scene stay as they are.
  - `IShowStore` / `IShowEditor` move from `Kloarte.MagicQ` to `Kloarte.Model` and become console-neutral (no rule
    change).
  - Cues: **tracking** (only what a cue changes; values carry forward; no block flags). Values are **physical
    attributes** per fixture, not DMX: intensity %, pan/tilt degrees, colour (RGB/CMY/CTO), zoom, focus, iris, gobo by
    name and rotation, animation wheel, prism, blades; a value can refer to a palette. Number, text, fade in/out, delay.
    Cues can be deleted and renumbered, in the UI and in `cues.yaml`.
  - **Conventional lamps: intensity per cue only.** Shutters, barndoors, iris, gobo and focus stay hand focus per lamp
    (`FixtureMechanics`); gel per head. Moving heads: every attribute per cue.
  - Palettes: **per project** (not in the venue) and **combined** (any mix of intensity, colour, position, beam); they
    list the fixtures or fixture types they apply to; editing one changes every cue that uses it. **Position palettes
    store angles only** (pan/tilt), no stage points.
  - Editing: cue table (cues x fixtures; cells show the tracked value and mark what the cue itself sets), inspector
    with physical values, palette editor, "aim at a point" (computes pan/tilt), `cues.yaml` in VS Code. **No
    programmer** (a console concept).
  - Files: `rig.yaml`, `palettes.yaml`, `cues.yaml` version 2 (per-fixture attributes); mechanics, blocking, `steps.yaml`
    and `venue.yaml` as before.
  - Import (`Kloarte.MagicQ`): .shw -> `DesignShow`, values decoded via personalities and GDTF; keeps the source file
    and the MagicQ ids (cue ids, palette ids per type) for stable exports. MagicQ palettes of different types merge into
    one combined palette only when they apply to the same heads and nothing is lost; MagicQ's own combined palettes
    stay one.
  - Export: written into a **copy of the imported source .shw**, as the next numbered show (never an overwrite). Rig,
    patch, MagicVis items, groups, palettes and cues are written; every record the design didn't change stays
    byte-identical (FX, user curves, settings). The export is standalone: rig, patch, personalities, palettes, cues. A
    combined palette becomes one MagicQ palette per type it holds (ids kept in `palettes.yaml`).
  - After MagicQ edits (e.g. at the technical rehearsal): **re-import with a diff**, merged as one undo step.
  - Kept of the MagicQ links: Art-Net live input, "Open in MagicQ PC". Removed: the programmer, the MagicQ watcher, the
    MagicQ-backed project path.
  - All projects become native (Kruimels too), **last**: the native path is built next to today's, and the switch comes
    once a native Kruimels exports a show that is right in MagicQ PC.
  - Proof: for all 64 shows the DMX after every cue from the imported native show equals MagicQ's tracking DMX; export
    then import gives the same model; unchanged records byte-identical; written palettes, new cue records and floor
    stands checked by hand in MagicQ PC.
  - Phases: 1 move the interfaces; 2 `DesignShow`, its files, the DMX projection; 3 import + the 64-show DMX test;
    4 export + round-trip tests (user checks in MagicQ PC); 5 cue table, palette editor, aim, inspector; 6 re-import
    with diff; 7 switch all projects, remove the old path, docs.
  - Built (14-15 Oct 2026; user: all remaining steps without asking): `DesignShow` (`design/`: `DesignRig`,
    `DesignPaletteList`, `DesignCueList` are the file types themselves), values `LookValue` (number / set name / "function
    value" / exact `dmx N` / `@palette`; written by the codec as a `FileScalar`), `HeadLayout` / `HeadChannel` per head
    (GDTF channels when the fixture type has a GDTF mode with its channel count, else one key per channel; `intensity`;
    `#n` = one byte; every DMX value reads as a value that gives it back, an exact DMX value added only when needed:
    1.3 % of all T1 / LEDBeam values), `DesignShowEditor` (`IShowEditor`: the plan's patch rules), `DesignStore`
    (`IShowStore<DesignShow>`), `IProjectShow` (the session writes / watches / applies the show's files like the
    others), `IShowExchange` (model) = `MagicQExchange` (`MagicQImport`, `MagicQExport` + `MagicQExporter`), UI
    `CueTableView` (F9), `PaletteView` (F10), `LookEditor`, `CueEditing`. Import is exact on all 75 shows of the show
    folder (1897 cues; `DesignImportTests`); export of an unchanged import is byte-identical for all of them, edits read
    back the same, and every export reads its file back and compares the DMX of every cue (`DesignExportTests`).
    Templates for records a new show lacks (S, C, G, F of each type) in `data/magicq_templates.shw`; the catalogue's
    fixture types in `data/fixture_types.yaml` (generated from the templates by a test). New lamps keep the plan's
    rules; conventional lamps: intensity per cue only.
  - Deviation from the plan: the host switched to native projects **before** a native Kruimels export was checked in
    MagicQ PC (that check can only be done by the user): to do before the next performance. Not exported (warnings):
    dimmer curves, cue delays; the S record's millisecond copies of the times are not changed with them.

## Scenery (user, decided and built 27 Oct 2026)
Set pieces on stage: block, pyramid, table, curtain, each with one colour and a material preset. User manual:
`docs/manual.md`, *Scenery*.
- **Kloarte's own data**, not exported to MagicQ: `blocking/scenery.yaml` (`SceneryFile`; per piece shape, position =
  centre of the bottom, rotation about the vertical, sizes, colour `#rrggbb`, material painted / wood / molton / metal /
  gauze; only what differs from the shape's default is written). Imported MagicVis boxes / borders stay in `rig.yaml`.
- **Per cue, tracking like the smoke**: `steps.yaml` `scenery: {<piece>: {visible, open, time}}`: a piece goes out / comes
  in at the GO (no fade), a split curtain opens / closes over `time` (default 5 s) from where it is. Before the first cue
  every piece is in. A GO's active part lasts until its curtains have finished (`TimelineSegment.SceneryEnd`).
- **Model**: `Blocking.Scenery` + `Blocking.SceneryCues` (in the blocking, so undo, unsaved check and save work as for
  actors; deviation from the prompt, which had its own snapshot), `PlaybackTimeline.SceneryAt` / `SceneryAfter`,
  `SceneryGeometry` (footprints, ray picking: the plan, the PDF and the 3D pick use the model, not the nodes).
- **Scene**: `SetPiece` per piece under "Scenery" (`ShowBuilder.Scenery`: `RebuildScenery`, `RebuildPiece`), opaque pieces
  on `RenderLayers.Occluders` (with the MagicVis boxes / borders / proscenium and the cyc), gauze translucent without
  shadow; `CuePlayer` applies in / out and openness; every change of geometry on that layer goes through
  `ShowBuilder.NotifyOccludersChanged` (invalidates the lamps' occlusion maps, raises `OccludersChanged`).
- **Editing**: Scenery tab + inspector (`FixturePanel.Scenery`, shared `SceneryEditing`), plan (drag, Shift = turn), 3D
  (pick, drag, right-drag turns), key map `scenery_add` / `scenery_duplicate` / `scenery_delete`; MCP `list_scenery`,
  `add_scenery`, `update_scenery`, `remove_scenery`, `set_cue_scenery`. An edit rebuilds only its piece; a drag is one
  undo step.

## MCP server (user, decided and built 25 Oct 2026)
Claude (Claude Code, or any MCP client) works on the show open in the running app: reads it, edits it, plays it and
looks at the result. User manual: `docs/manual.md`, *Claude / MCP*.
- **Layers** (dependency rule 4, approved by the user 25 Oct 2026):
  - `Kloarte.Mcp` (MCP binding, .NET only): the protocol. `McpServer` (`IMcpServer`: `Add(IMcpTool)`, `Instructions`),
    `IMcpTool` (name, title, description, JSON-Schema input, read-only hint, `CallAsync(args, IMcpReply)`), `IMcpReply`
    (text / image content; `McpReply` builds the result). Knows nothing of the show.
  - `Kloarte.Application`: the tools, `RemoteTools` (partial per area: Show, Rig, Cues, Palettes, Stage, Values;
    `RemoteTool` implements `IMcpTool`, `ToolArgs` reads arguments, `ToolSchema` builds the input schemas). It names
    only `IMcpServer` / `IMcpTool` / `IMcpReply` from the binding and acts on the app only through `IRemoteControl`.
  - `Kloarte.GodotBinding`: `IRemoteControl` (files, native show, current cue, GO / go to cue, haze, stray light,
    selection, `EditShow`, `EditModel`, undo / redo, Save, export, view titles, `Capture`, `Report`), implemented by
    `FixturePanel.Remote` through the UI's own paths (cue edits via `CueEditing.Edit`, mechanics / blocking as one
    `EditHistory` step, scene rebuilt where needed).
  - Host: `Startup` makes the server (`McpFactory`: `new McpServer(...)` + `new RemoteTools(ui).AddTo(server)`);
    `HostFixturePanel` starts it in `_Ready`, pumps it in `_Process`, disposes it on `TreeExiting`.
- **Transport**: Streamable HTTP, one endpoint `POST /mcp` with one JSON-RPC message, one `application/json` answer
  (notifications: 202, no body). No SSE stream (GET = 405), no sessions (no `Mcp-Session-Id`), no batches: the spec
  allows a server all three. Methods: `initialize` (versions 2025-11-25, 2025-06-18, 2025-03-26, 2024-11-05; an unknown
  one gets the newest), `ping`, `tools/list` (`listChanged: false`), `tools/call`. Tool annotations: `readOnlyHint` from
  the tool, `destructiveHint` false (every edit is one undo step), `openWorldHint` false.
- **Threading**: `HttpListener` accepts on a background thread; a `tools/call` is queued (`ConcurrentQueue`) and the
  request waits. `Pump()`, called every frame on Godot's main thread, runs the queued calls one at a time there (the
  scene and the model are only touched on the main thread). An async tool (undo, mechanics, screenshot: waits for
  rendered frames) finishes in later frames, resumed by Godot's main thread. A call not finished within `CallTimeout`
  (2 min: a modal dialog open, the app busy) is answered with an error and skipped by a later `Pump`. On exit waiting
  calls get "Kloarte is closing".
- **Security**: listens on `http://localhost:<port>/` only (not the network); a request with an `Origin` header that is
  not localhost / loopback is refused with 403 (a web page of another site using DNS rebinding); no authentication
  (the PC's own user). Nothing is saved without `save_project`; `export_magicq` is the project's next numbered show
  (never an overwrite); the user sees every edit (status bar "MCP: ...") and can undo it.
- **Port**: default 8462 (`McpServer.DefaultPort`); `-- --mcp-port=N`, else `KLOARTE_MCP_PORT`; 0 or `--no-mcp` = no
  server. A taken port (a second Kloarte) = this one runs without a server (logged).
- **Why hand-written, not the C# MCP SDK** (`ModelContextProtocol.AspNetCore`): its HTTP transport needs ASP.NET Core
  (Kestrel, hosting, DI) inside the Godot process, and a NuGet dependency for a few hundred lines of JSON-RPC; the subset Kloarte needs (tools only, JSON answers, no sessions) fits `HttpListener` + `System.Text.Json`.
- **Tests**: `Kloarte.Mcp.Tests` (`McpServerTests` over real HTTP: handshake and tool list, calls run when pumped,
  errors, timeout of an unpumped call, refused requests),
  `Application.Tests/RemoteToolsTests` (the tools on `FakeRemoteControl`), the dependency rules in
  `DependencyRuleTests`.

