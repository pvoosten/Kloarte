# Theatre lighting visualizer (Godot .NET)

## Goal
A free, realistic real-time visualizer for theatre lighting, driven live by a ChamSys MagicQ MQ250M
console over Art-Net/sACN. It must show what the MagicQ visualiser can't:
- barndoors (flappen) cutting the beam of fresnels and PCs
- framing shutters (messen), iris and gobos on profiles
- all functions of the Robe T1 Profile (framing shutters, animation wheel, prism, CMY/CTO, zoom, focus, iris)
- beams visible in haze/smoke

The user is an experienced theatre technician (knows DMX, patching, console programming) and a .NET expert.
Don't explain lighting or DMX basics; do explain Godot specifics when relevant.

## Technical decisions
- **Godot 4 .NET, C#** for all game code. Shaders in Godot shading language.
- **Desktop only, Forward+ renderer.** No web export (Compatibility renderer has no volumetric fog).
- Multiple PCs are available; several instances can receive the same Art-Net stream (e.g. audience view + plan view).
- DMX input: Art-Net on UDP 6454 via `UdpClient` on a background thread; buffer universes, apply on the main thread in `_Process`.
- Fixture definitions: GDTF where available (Robe publishes GDTF for T1 Profile and LEDBeam 150). Conventional fixtures
  (dimmer only) get their focus, shutters, barndoors, iris and gobo from a config file, not from DMX — like real hand focus.
- Beam shaping technique: one mask texture per fixture (shutters + iris + gobo + rotation + focus blur), generated
  from parameters and projected through the light. Barndoors = soft version of the same mask.
- Mask pipeline (proven in the PoC): `beam_mask.gdshader` renders into a SubViewport (UpdateMode.Once, only on change)
  -> after `RenderingServer.FramePostDraw` read back with `GetImage()` -> **new** `ImageTexture` each change
  -> `SpotLight3D.LightProjector` (surfaces) and `beam_volume.gdshader` (haze).
- Beam in haze: custom raymarched beam volume per shaped fixture, sampling the same mask; that light's
  `LightVolumetricFogEnergy` = 0. Godot's volumetric fog stays for ambience / unshaped lights.
- Haze level: global shader uniform `haze_density` (project.godot `[shader_globals]`), used by the beam volumes.
  Environment `volumetric_fog_density` is set separately for now (their brightness scales differ, see below).

## Technical questions
1. ANSWERED (Godot 4.7.2): `ViewportTexture` as `light_projector` is refused by the engine
   ("ViewportTexture cannot be used as a Light3D projector texture"). Workaround that works: read the viewport
   back with `GetImage()` into an `ImageTexture`. **`ImageTexture.Update()` does NOT refresh the projector**
   (the projector atlas only copies a texture when it is added), so create a new `ImageTexture` per change.
   Cost per change: GPU readback stall + projector atlas rebuild. Fine for conventionals (static) and occasional
   changes; must be measured for the T1 with DMX-driven shutters/gobo changing every frame
   (fallbacks: throttle mask updates to e.g. 15 Hz, or apply the mask in custom surface shaders).
   Orientation verified: mask image top = beam top, image left = beam left, seen from behind the fixture.
2. ANSWERED: No. Godot's volumetric fog ignores `light_projector`: the beam in fog is the full cone.
   Plan B implemented and working: `beam_volume.gdshader` (ray/frustum clip, scene-depth clip, 48 jittered steps,
   Henyey-Greenstein phase). Shutter cuts, iris and gobo are visible in haze.
   Known gaps: no occlusion by scenery inside the beam (e.g. a border cutting a beam in haze; Godot's fog does
   this via shadows), no extinction, brightness scale differs from Godot's fog (custom beam ~4x dimmer at equal
   density; use `FogEnergy` / `haze_density` to match).
3. OPEN: Shadow atlas budget: ~170 fixtures. Decide which fixtures cast shadows and at what resolution.
4. OPEN: occlusion in the custom beam (see 2). Options: per-fixture depth map from a light camera (only for
   selected fixtures), or accept it.

## Coordinate system (metres)
- Origin = downstage centre, on the stage floor
- +X = audience's right, +Y = up, -Z = upstage, +Z = towards the audience

## Files
- `main.tscn` — main scene: WorldEnvironment, Venue (VenueBuilder), Camera (FlyCamera), Fixtures/FOH_DS104 (PoC profile).
- `environment/theatre_environment.tres` — black background, ambient ~black, AgX tonemap, light glow,
  volumetric fog (density 0.015, anisotropy 0.6, temporal reprojection). Fog volume 128x128x128 (project.godot).
- `VenueBuilder.cs` — [Tool] script on a Node3D, generates the venue from Inspector parameters
  (stage floor, auditorium, walls, grid, proscenium, legs, borders, cyc). Generated nodes are not saved in the scene.
- `camera/FlyCamera.cs` — RMB look, WASD/QE move, Shift fast, wheel = forward/back (Shift x4), middle-drag = pan;
  keys 1/2/3 = audience / side / plan view. WASD is ignored while a text field has focus.
- `ui/FixturePanel.cs` — runtime CanvasLayer (in main.tscn and the show scenes), left side has tabs Fixtures /
  Playback / Actors (the latter two in FixturePanel.Playback.cs). "Open show (.shw)..." (native
  file dialog, starts in the MagicQ show folder; loads the .xhw next to it; uses the scene's ShowBuilder or adds
  one; mechanics per show in `res://shows/<show name>.mechanics.json`). Fixture list filtered by **dropdowns only**
  (user's wish: groups and cues are edited in MagicQ): MagicQ group, or cue (grouped per cue stack with separators;
  list shows each head's level in the cue for single-channel heads). Group and cue filter are mutually exclusive.
  **Only the listed (filtered) fixtures can light up** (user's wish); the others are set to intensity 0 and get
  their own intensity back when the filter changes (`ApplyLit`). Solo narrows the lit set to the selected fixture.
  Listed fixtures keep their preview intensity — cue levels are shown, not applied.
  Solo, stray-light slider, settings panel: rigging ("Hangs on" = MagicVis parent object, X/Y/Z position spinboxes in stage
  coordinates), lamp, shutters, iris, gobo, focus point. "Save mechanics" (ShowBuilder.SaveMechanics) and
  "Save show as (.shw)..." (ShowBuilder.SaveShowAs, default name `<show>_kruimels.shw`; rigging changes live in
  the MagicQ part). Orange marker on the selected fixture.
  Generated fixtures don't appear in the editor's Scene dock (not owned); use this panel or the Remote tree.
- `fixtures/ProfileFixture.cs` — [Tool] conventional profile: Intensity, Energy, FieldAngle, Softness (focus),
  4 shutters (depth 0..2, tilt), shutter rotation, iris, gobo (+rotation, size), FocusPoint (aims the lamp),
  CastShadows, CustomBeam, LampColor, GelColor (light colour = LampColor x GelColor; the gel comes from the MagicQ
  patch and is never saved in the mechanics). Shutters are named after the beam edge they cut, seen from behind the fixture.
  Generates children (mask SubViewport, SpotLight3D, beam volume, body); not saved in the scene.
- `fixtures/beam_mask.gdshader` — canvas_item shader that draws the mask.
- `fixtures/beam_volume.gdshader` — raymarched beam in haze (see question 2).
- `gobos/window.png` — test gobo (window; the top-left pane has a marker for orientation checks).
- `tests/projector_capture.tscn` + `ProjectorCapture.cs` — steps the PoC through looks and saves screenshots
  (and the mask) to `tests/output/` (has `.gdignore`, so Godot doesn't import them).
- `magicq/ShwDocument.cs`, `XhwArchive.cs`, `MagicQShow.cs`, `ShowModel.cs` — the show model (see "Show model").
- `magicq/ShowBuilder.cs` — [Tool] builds a show from the ShowModel: scenery from MagicVis objects, one
  ProfileFixture per MagicVis fixture (aimed by its rotation, focus = where the beam hits the floor, or the saved
  focus point), mechanics from the mechanics JSON or else the kind presets, gel from the patch (`PatchChannel.Gel`
  -> colour table -> GelColor = colour normalised to max 1 x brightness), MagicQ groups -> node groups
  `mq:<group name>`. Adds an assumed venue (stage floor, auditorium floor, back wall) — not in the show file.
  Inspector buttons "Rebuild show" and "Save mechanics"; `ExportMagicQ(path)` writes .shw/.xhw.
- `shows/risquons_tout.tscn` — the Risquons-Tout show (RT2025_031.shw): 72 fixtures, 60 objects, 32 groups.
- `shows/risquons-tout.fixtures.json` — kind presets (profile, fresnel, pc, par64, flood, ledbeam) + lamp type per
  head name / address, used only for fixtures that have no saved mechanics. Mapping is a GUESS, to be corrected.
- `shows/risquons-tout.mechanics.json` — saved mechanics per fixture (created by "Save mechanics"; not yet present).
- `tests/show_capture.tscn` + `ShowCapture.cs` — renders a show scene from the 3 camera presets plus a close-up of
  the people on stage (show_view1..4.png, with fps).
- `scenery/Mannequin.cs` — people figures for MagicVis people objects (see MagicQ section).
- `magicq/Blocking.cs` — **blocking** (user: cue stack playback with timing + moving actors as one whole): actors
  (`ActorDef`: id, name, female, height (0 = default 1.76 m man / 1.66 m woman; mannequins scale), initial
  `ActorState`) and per MagicQ cue id per actor a LIST of movement steps (user: several movements of actors within
  one light cue) that run one after the other (`ActorState`: visible, pose standing/seated/lying, position x/y/z
  stage coords, facing °, and per step Delay (wait first) and MoveTime (0 = walking speed); all nullable =
  tracking like MagicQ). `StateAfter(actor, cueIds)`, `Steps`, `Record(cue, actor, step, change)`, `RemoveStep`. Part of `ShowModel` (Godot-free), saved as
  `res://shows/<show file name>.blocking.json`. Without a saved blocking the MagicVis people become the actors.
- `scenery/Actor.cs` — actor node: standing / seated (mannequin on a stool; "sitting down" = swap) / lying (standing
  mannequin on its back, head upstage at facing 0) children, Visible, Facing.
- `playback/CuePlayer.cs` — plays a cue stack: GO fades every fixture to the next cue's tracked level (rising with
  the cue's In time, falling with Out); per actor its movement steps of the cue run in sequence: wait Delay, walk
  (MoveTime, else 1.2 m/s) facing the way it goes, turn (0.6 s) to the step's facing, take the step's pose;
  appearing = at the start of the step (at its place), disappearing = at the end of the step (last step also waits
  for the Out fade). Back/Jump snap. Auto: GO again after Hold s when nothing runs.
  A GO while running completes the running cue first.
- `ui/FixturePanel.Playback.cs` — tabs Playback (stack dropdown, preferring "show"; cue list with In/Out times;
  Reset / Back / GO (also Space); Auto + hold) and Actors (list, + Man / + Woman / Remove, Save blocking; right panel:
  name, height, and in the current cue a movement-step picker with Add/Delete step, Wait first, Move time, visible,
  pose, position, facing for the edited step — or the initial state before the first cue —; the actor shows the
  state after the edited step; left click on the stage places the selected actor for that step). Leaving the Fixtures tab clears group/cue filter and Solo.
- `tests/playback_test.tscn` + `PlaybackTest.cs` — programs blocking on stack "show" (two movement steps in one
  cue: walk, wait 0.5 s, walk on, sit, face 90°; 1.95 m tall actor; lie down, appear, disappear, 4 s move), plays with GO, checks mid-fade/mid-walk and end states, Back; "PLAYBACK OK".
- `environment/StrayLight.cs` — node in main.tscn and the show scenes. User: "in reality there is always some
  scatter; once you light something the rest never goes fully dark". Sets the environment's ambient light to
  base + Amount x 1e-4 x sum(fixture energy x intensity), in the mixed light colour. Panel slider "Stray light"
  (0..2, default 0.5). Tested alternatives (tests/gi_capture.tscn): SSIL adds little here (black molton/floor);
  SDFGI gives subtle physical bounce but costs ~5 fps AND replaces the ambient light, which kills the stray light —
  so SDFGI/SSIL are configured in the environment but disabled.
- `tests/panel_capture.tscn` + `PanelCapture.cs` — smoke test of the FixturePanel: cue filter ("1 start" -> 7
  heads with levels), rigging (head 150 from LT 11 to T 1, +1 m, Save show as -> tests/output/roundtrip/rigged.shw,
  re-read OK), Playback/Actors tab screenshots, editing an actor's X then Y through the panel fields keeps both
  (regression: fields used a stale position copy), then open "la comedie.shw" through the dialog signal; screenshots panel.png / panel_open.png.
  Note: `StageFrontZ` (9.75) is tuned for Risquons-Tout; other shows may need their own value.
- `tests/magicq_roundtrip.tscn` + `MagicQRoundTrip.cs` — headless reader/writer tests (see "Show model").
- `SketchUpPostImport.cs` — mentioned in earlier notes but NOT present in the project. (SketchUp Free only exports
  SKP/STL; STL has no materials, so venue geometry is preferably generated with VenueBuilder.)

## Show model (internal structure) — the base of the application
Everything the app knows about a show lives in `ShowModel` (no Godot dependencies, plain .NET):
- `ShowModel.MagicQ` (`MagicQShow`) = everything MagicQ stores, **lossless**:
  - `ShwDocument`: every line/record/field/line ending of the .shw, also record types we don't understand
    (V, T, Y, U, X, B, Z, C, J, Q, r, u, E1...). `ShwRecord` gives field access across lines (raw token,
    GetString/SetString, GetHex/SetHex keeping width, GetFloat/SetFloat). `AddRecord`/`RemoveRecord` exist but
    new-record layout is untested in MagicQ.
  - `XhwArchive`: the .xhw entries (name + bytes). Plain or gzip tar. Unchanged -> original bytes written back;
    changed -> rebuilt with MagicQ-identical ustar headers, re-gzipped if the original was.
  - Typed views that read/write the underlying fields: `Personalities` (P), `Patch` (L), `Groups` (G), `Vis` (H,
    with decoded/encoded MagicVis numbers), `CueStacks` (C, with `Steps`), `Cues` (S, with `Channels`),
    `Palettes` (F), `Effects` (W), plus the
    derived `Heads` (patch grouped per head). Edit through a view, then `Save`.
  - `MagicQShow.Save(shwPath)` writes .shw + .xhw: the "write back to MagicQ" method.
- `ShowModel.Mechanics`: per fixture (key = absolute DMX address) what MagicQ can't store: `FixtureMechanics` =
  kind, field angle, softness, field falloff, energy, colour, `Shutters` (4 blades depth/angle + rotation), iris,
  `Gobo` (path, rotation, size), `Barndoors` (4 flaps 0 open..1 closed + rotation; not rendered yet), focus point.
  Saved as JSON (`SaveMechanics`), because MagicQ files have no place for it.
- Verified by `tests/magicq_roundtrip.tscn` (headless, ~20 s): all 63 .shw/.xhw in the MagicQ folder round-trip
  byte-identical (incl. 3 gzip xhw), 50015 MagicVis numbers re-encode exactly, typed edits change only their lines
  and survive a reload, regenerated xhw headers equal MagicQ's, mechanics JSON round-trips.
  Not yet tested: loading a written-back show in MagicQ itself (do this before trusting edits).
  Test show for that: `tests/make_magicq_test_show.tscn` writes `<MagicQ show>/kruimels-test/kruimels_edit.shw`
  (RT2025_031 with head 150 renamed "KRUIMELS TEST", its MagicVis fixture moved +2 m in x and tilted 45°,
  group "Water T" renamed "KRUIMELS GROEP"). MagicQ PC is installed at `C:\Program Files\ChamSys Ltd\MagicQ PC`
  (mqqt.exe) and may be used for testing; it has no command-line show loading and computer use was not available,
  so the user loads the show by hand (Setup > File Manager).

## MagicQ show files (findings, MagicQ 1.9.7)
Shows: `C:\Users\pvoos_nnvpp5d\OneDrive\Documents\MagicQ\show\` (e.g. `Risquons-Tout\RT2025_031.shw`). Read-only for us:
tests write to `tests/output/roundtrip/`, never over the originals.
- `.shw` is ASCII (CRLF). Lines starting with `\` are comments. Records = comma-separated fields, first field is the
  record type, terminated by a `;` token, may span lines; most lines end with `,` (separator, not an empty field).
  Numbers are mostly hex (head numbers too: `96` = head 150).
- `P` = personality (head type) in plain text: name, manufacturer, mode, channel definitions (e.g. "Robe_Rob150LEDBeam_Mode 1").
- `L` = patch, one record **per DMX channel**: `L,<abs address hex, 1-based>,"<head name>",<attr>,<personality>,
  <channel in head>,...,<head number hex at field 10>,<gel id hex at field 11>,...`. Generic dimmers: address = head
  number. Field 11 = id in MagicQ's colour table: 0 no colour, 1 wheel, 2 mix, 1000+n = Lee n (e.g. `47b` = 1147 =
  L147 Apricot), 2000+n = Rosco n, others Apollo/GAM/web colours. The last field (e.g. `787ff`) is a BGR display
  colour of the head (not the gel) — not used.
- Colour table: `<MagicQ>\show\heads\allcols.csv` (843 entries, 284 Lee): name, description, id, colour hex in
  **BGR**, CIE x, CIE y, brightness 0..1. Read by `magicq/MagicQColours.cs`; fallback copy in `data/allcols.csv`
  (`data/` has `.gdignore`, otherwise Godot imports the CSV as a translation).
- `G` = groups: `G,<num>,"<name>",<count hex>,<head numbers hex...>,...`.
- `C` = cue stack: 1 number, 2 name, 4 step count (hex); then a 17-field line; then 7 fields per step: step index,
  link, cue id (-> `S`), ?, flags, cue number (float, e.g. 8.110001 = 8.11), text. Sort steps by cue number.
- `S` = cue (recorded data): 1 id, 2 name, 3 number of channel/value pairs (hex), 10 fields, then the pairs:
  absolute DMX address, value (low byte = DMX level 0..255; upper bits are flags, e.g. `a009e2` on LEDBeam channels).
  Further lines (per-head attribute data for moving heads, timing) are not interpreted yet.
  Cue -> fixtures: address -> patch head number (L field 10) -> fixtures (`ShowBuilder.FixturesInCue`).
- People objects (H type 2, object type 6): field 20 = variant 60001..60004. Rendered by `scenery/Mannequin.cs`
  (primitive mannequin, male/female, standing/seated on a stool); mapping in `Mannequin.MagicVisCodes` is a GUESS:
  60001 man standing, 60002 woman standing, 60003 man seated (used for a "Drummer" in the Chauvet demo),
  60004 woman seated. User: MagicVis has standing and seated figures — verify mapping in MagicVis.
- L fields 21/22 = Patch > View Heads "shape" and "beam angle" for generic heads (manual: MagicVis chapter
  "Modifying Beam Angle and Head Shape"). Field 22 = beam angle in whole degrees, hex, 0 = default from personality
  (la comedie: ACP 90°, PAR 39°/20°, Front 40°, Front2 61°). **Used:** `PatchChannel.BeamAngle` -> fixture
  FieldAngle, wins over mechanics/presets; the panel's "Beam ° (MagicQ)" slider writes it back (`ShowBuilder.SetBeamAngle`,
  saved with Save show as). Risquons-Tout: 48 of 72 heads have one, la comedie 57 of 65. Field angles are clamped to
  120° (`ProfileFixture.MaxFieldAngle`; Horizon heads ask 170°).
  Field 21 = shape id (0 = default; MavForce moving head = 1 = "Yoke Spot"). Generic ids seen: 0x10 (2 KW, HAZER),
  0x12 (PARs), 0x13 (Front, band specials), 0x18 (ACP floods), 0x1c (Front2). The shape-name list in mqqt.exe
  ("None, Yoke Spot, ..., Profile, String, Par64 Long Nose, ..., Fresnel 12") doesn't map onto these ids in string
  order, so names are unknown; stored as fixture meta "shape", not used for the lamp kind yet.
  Short L records (some "Dimmer" channels) lack fields 21/22: `Shape`/`BeamAngle`/`GelOrNone` read 0 there.
- `P` channel list: from field 16, 3 fields per channel: name, flags, attribute; attribute 0 = Dimmer (intensity).
  `Personality.IntensityChannel` = offset of the first attribute-0 channel (LEDBeam 150 Mode 1: 20 of 22;
  T1 Profile Mode 1: 47 of 49; generic dimmer: 0).
- Cue times: S fields 6..10 = In, Out, Pos, Col, Beam in units of 1/33.3 s (0x63 = 99 = 3 s, 0x42 = 2 s, 0x21 = 1 s,
  0x3e7 = 30 s); the same times appear later in the record in ms (0xb9b = 2971). `Cue.FadeIn` / `FadeOut`.
  Field 11 (e.g. 0x53, 0x42, 0x31 in some RT cues) unknown (wait/delay?). Every step halts (GO per cue); C step
  flags 00006000 vs 00004000 not understood (auto-follow?).
- Cue playback (`ShowBuilder.ApplyCueStack(stack, upTo)`, also applies the blocking; `CueLevels` for fades): MagicQ tracks, so the `S` cues of steps 0..upTo (cue
  number order) are accumulated per DMX address (low byte = level); each fixture's intensity = value at
  DMX address + IntensityChannel / 255. Only the stack itself — other playbacks running in parallel in the real
  show (e.g. "autostrade" chase stacks) are not simulated. S fields 6..15 look like fade times (0063 = 0.99 s?,
  03e7 on the long fade to black of 30.4).
- Risquons-Tout stack "show" (39 cues): real blackouts at 0.1 "FO", 27.1 "handgeknoopt" and 30.4 "V zit" (all
  channels 0). `tests/cue_run.tscn` plays the whole stack, saves `tests/output/cues/NN_cue_X.png` + contact_sheet.png
  and prints lit fixtures / mean / % black on stage per cue (full-stage looks 0-6 % black; night scenes 50-74 %).
- Real heads are typed by personality (`byPersonality` in the fixtures JSON: "RobinT1Profile" -> t1profile,
  "LEDBeam" -> ledbeam), before byName. T1s are in Risquons-Tout_RT2025_039..044 (Mode 1, 49 ch, heads 709/70a/70b
  replacing the LEDBeams of RT2025_031) and in la comedie (Mode 3, 53 ch). Without DMX they point straight down
  (MagicVis rotation 0).
- Venue fit (`ShowBuilder.FitVenue`, AutoVenue): stage front = proscenium object z (Risquons-Tout 9.75), else a
  box named podium/stage/scene/toneel (front edge + top; la comedie: podium box 1 m high, front z 1.8, origin y
  shifted by 1); back wall 0.5 m behind the most upstage MagicVis item. ToStage = (x, y - floorY, z - frontZ).
- `R` = unknown (earlier guessed "cues"; wrong). `F` = palettes ("centre", channel/value triplets), `W` = FX ("Dim Chase").
- Rigging edits (`ShowBuilder.SetParent` / `MoveFixture`): H field 8 = parent object id; choosing a truss snaps the
  head onto the truss line (nearest point, keeps its drop below the truss, default 0.33 m); moving keeps the focus
  point and rewrites the MagicVis rotation (rx, ry from the beam direction, rz = 0).
- `H` = MagicVis 3D scene. Fields: 1 id, 2 kind (1 fixture, 2 object), 3 object type, 8 parent id (the truss a
  fixture hangs on), 13-19 seven **obfuscated numbers**: x, y, z, rx, ry, rz, extra.
  Fixtures: field 20 address, 21 head number, 22 personality. Objects: 21-23 size, 24 name, 26 model file.
  Object types: 1 proscenium, 2 truss/pipe (length along local +X from the position), 4 box (size = depth, width,
  height; bottom-centre), 6 person, 7 podium, 8 custom model (.mvm), 9 border.
- **Number decoding (cracked):** text is "%.2f"; per char i: `x = ((c >= 0x60 ? c - 0x20 : c) ^ 0x64) - (3 - i)`;
  x = '0'..'9' is the 9-complement of the digit, 0x3b = '.', 0x3c = '-'. Verified on rotations (0/90/-90/180/270)
  and truss heights. Encoding is the inverse (+0x20 when the result is below 0x50).
  Implemented in `MagicQShow.DecodeNumber` / `EncodeNumber`.
- MagicVis axes = ours (+X audience right, Jardin = -X; +Y up; +Z towards audience); fixtures point down (-Y) at
  zero rotation, Euler order Y-X-Z (pan, tilt, roll). Origin differs: `ShowBuilder.StageFrontZ` (MagicVis z of our z=0).
- `.xhw` is a ustar tar archive (older/demo shows: gzip-compressed tar) with icons (`icons/*.mc2`) and MagicVis
  models (`models/*.mvm`, binary/compressed, format unknown; rendered as placeholder boxes). No patch data.
  MagicQ's headers: mode `000644 `, uid/gid `000000 `, mtime 0, devmajor/devminor `000000 `, magic `ustar\0` `00`.

## Build / run
- Godot 4.7.2 mono: `C:\Users\pvoos_nnvpp5d\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\`
  (use the `_console.exe` for log output).
- `dotnet build`, then `<godot>_console.exe --path . --resolution 1280x720 res://tests/projector_capture.tscn`
  renders the test screenshots. After adding assets run `<godot>_console.exe --headless --path . --import` once.
- MagicQ reader/writer tests: `<godot>_console.exe --headless --path . res://tests/magicq_roundtrip.tscn` -> "ALL OK".
- Python heredocs in Git Bash: avoid `\0`/`\n` escapes in non-raw strings (they broke patch scripts twice).

## Materials (realistic values)
- black molton: albedo ~0.02, roughness 1.0
- black stage floor: albedo ~0.08, roughness ~0.55
- cyc: albedo ~0.85, roughness ~0.95
- Ambient light almost black, like a real theatre.

## Rig (available equipment)
Conventional (dimmer channel only; shaping set in config):
- Pebble convex (PC): 12x ADB C203 2 kW, 28x ADB C103 1 kW
- Fresnel: 12x ADB F101 1 kW
- Barndoors: 12x ADB 2 kW, 30x ADB 1 kW
- Profiles (all with iris and shutters):
  4x ADB DN204 2 kW (10°–20°), 6x ADB DS204 2 kW (13°–36°),
  9x ADB DS104 1 kW (15°–31°), 5x ADB DW104 1 kW (15°–38°),
  8x ETC Source Four 25°–50°
  Gobo holders: 10x size A, 2x size B
- PAR: 41x PAR64 longnose, 6x PAR64 floorcan shortnose, 6x PAR36 pinspot; PAR64 lamps CP60/61/62
- Floods: 15x ADB ACP1001 1 kW asymmetric
- Blacklight: 2x 400 W

Moving / LED:
- 12x Robe LEDBeam 150
- 4x Robe T1 Profile
- 4x FOS100 Power Solo IP20 spot lens 15° LED bars

Other:
- Projector: Panasonic PT-RZ990, lens ET-DLE150
- Haze: Swefog Ultimate 2000 (DMX), C fluid
- Smoke: Swefog Live T26 X-power HD 2600 W, Neutral-Pro HD fluid

Console: ChamSys MagicQ MQ250M.

## Next steps
1. DONE: WorldEnvironment (near-black ambient, volumetric fog) + fly camera with preset views.
2. DONE: PoC ADB profile (DS104) with 4 shutters, iris, gobo, focus, in haze; questions 1 and 2 answered.
3. Art-Net receiver; drive the PoC profile's intensity from MagicQ.
4. Robe T1 Profile from GDTF (measure the cost of per-frame mask changes, question 1).
5. Stress test with ~100+ spots for shadow/performance budget (incl. ~100 beam volumes).

Risquons-Tout show (built, needs checking by the user): correct lamp types in the JSON; confirm StageFrontZ
(9.75 = proscenium line is a guess; front-light pools spill into the auditorium); real venue dimensions;
ProfileFixture is used for every kind (no barndoors, no PAR oval, no moving-head pan/tilt yet).
Show model: check a written-back .shw in MagicQ; render barndoors from `FixtureMechanics.Barndoors`;
UI for barndoors and lamp kind in the FixturePanel.
Performance: 72 fixtures (shadows + beam volumes) = 33-50 fps at 1280x720 on this PC.

Later / polish: beam occlusion by scenery (question 4); match beam and Godot-fog brightness; haze controller
(Swefog DMX -> `haze_density` + environment fog); better gobo defocus (the 9-tap blur shows a double edge);
realistic energy units (physical light units / lumens).
