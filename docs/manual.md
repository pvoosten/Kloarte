# Kloarte manual

Moved out of the README. Using the app, workspace by workspace.


**Start screen**: when no show could be opened, the view area lists the recent projects (click to open) with New
project, New project from show, Open project folder (a folder dialog: a project anywhere) and Import MagicQ show.

**Menu bar**: *File* (projects, Save, Import / Export MagicQ show, Export (stills, movie), Preferences, Quit), *Edit* (undo / redo), *View* (workspaces, the
views, side panels, Present mode), *Playback* (GO, new / delete cue, Open in MagicQ PC), *Tools* (cue table, palettes, gobo editor, key map), *Help* (key map, command palette,
About); every item shows its keys. **Ctrl+Shift+P** opens the command
palette: type a few letters of any command (or a project's name) and press Enter.

**Transport bar** (under the menu bar, always there): cue stack, Reset, Back, GO, the current and the next cue,
Auto with its hold. **Status bar** (bottom): the last message (errors in red; click it for the last 50), and on the
right the project, unsaved changes and what is connected (live Art-Net). Modes that
change what the stage shows or what a click does are **chips** left of the message, each with an ✕ that ends it: the
group / cue filter (the unlisted lamps are off), Solo, live Art-Net, placing lamps or stands in the plan, Aim (lamps,
or moving heads waiting for a click on the stage). A running export (a movie) shows its progress there with Cancel; when it is done, a click on
the message opens the folder.

**Left panel**: the project box (see Projects); tabs *Fixtures* (a table: head, name, type, gel, level in %; click a column title to sort, again to reverse;
details in the row's tooltip; filtered by MagicQ group or cue; Solo = only the selected fixture, at 70 % until Solo
goes off or it is deselected),
*Playback* (the cue list, insert a BO cue or a cue without changes after the current cue, *Cue table...*, the smoke of
the current cue, live Art-Net, *Open in MagicQ PC*, *Export...*, *Re-import...*), *Actors* (actors and their movement per cue);
**Save** (Ctrl+S: the whole project: the show's `rig.yaml`, `palettes.yaml`, `cues.yaml`, the mechanics and the
blocking) and *Export...* (a MagicQ show). Unsaved changes show as `•` in the window title and the
project box (its tooltip names them); closing the window or opening another show or project asks Save / Don't save /
Cancel.

**Several lamps**: Ctrl / Shift + click in the fixture list, Ctrl + click in the 3D view (again: take it out), box
select in the plan. With several lamps selected the settings set the intensity and the gel of all of them ("—" while
they differ); mechanics are set per lamp. **Gel**: type the code in the gel field ("L201", "201" = Lee, "R80", "O/W")
and pick from the matching gels; the gel is per head (as on the console), so every lamp on that head gets it.

**Present mode** (F11, View > Present mode): for rehearsals with the director. Only the audience view, full screen,
with the current and next cue at the bottom (C hides it) and the seat at the top left. Space / Backspace = GO / Back,
1-9 = the venue's named viewpoints (1 = Regie), [ ] and PgUp / PgDn move seat and row. F11 or Esc goes back.

**Actors** wear a shirt in a colour of their own (Actors tab: Shirt) with their name printed on chest and back.

**Right-click menus**: on a lamp (3D view: a right click without dragging; plan; fixture list): select its MagicQ
group, Patch / gel (head, DMX, gel), Rename head (the head's name in the patch, 15 characters at most like MagicQ; every lamp on the head gets it; undoable), Solo, Aim (the next click on the stage), Remove. On an actor (Actors list):
rename, remove, clear its movement in this cue. On a cue (Playback list): go to, insert a BO or no-change cue after it.
In the timeline: add / delete a step, set the hold after a cue.

**Undo / redo** (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z, Edit menu): every change to the show, the mechanics and the blocking
is undoable: rig, patch, lamps, stands, truss heights, cues, every cue value, palettes, a re-import, lamp settings, actors. A slider drag
or a lamp drag is one step; the Edit menu names the step ("Undo Remove lamp"). The selected lamps and the cue stay.
In the gobo editor's window the same keys undo the drawing. Resetting all keys and restarting a running MagicQ PC still ask
first.

**Smoke per cue** (Playback tab, *Smoke in cue N*): set the *Amount* (0-100 % of the densest haze, the scale of the
Look's Haze slider) and *Builds in* (seconds the hazer takes; default 20 s); changing either ticks *This cue sets the
smoke* (untick it to take the cue's smoke out again). From that
cue's GO the haze in the room builds or thins to the amount, eased, taking that time; a cue that doesn't set smoke leaves it
as it is (it tracks, like the lights); cues that do set it show "smoke 60 %" in the cue list. The haze before the first
smoke cue is the project's (Look > Haze). GO, Auto, Play, scrubbing the timeline, stills and the movie all follow it; Back,
Reset and a jump to a cue show the settled amount. It is Kloarte's own data (saved with the project in
`blocking/steps.yaml`, undoable): MagicQ cannot store it, so the real hazer is still programmed in MagicQ, and these
amounts are for the picture.

**Scenery** (set pieces; *Scenery* tab of the left panel, Scenery menu): blocks, pyramids, tables and curtains of the
project's own (Kloarte's data in `blocking/scenery.yaml`, not exported to MagicQ). *+ Piece* adds one of the chosen
shape (Ctrl+Shift+N adds a block), *Duplicate* (Ctrl+D) copies the selected piece with what the cues do with it,
*Remove* (Shift+Delete, or Delete in the plan) takes it out of the project. Click a piece in the list, the plan or the
3D view to select it; the inspector edits its name, shape, place (x, y = height of its bottom, z) and rotation
(counter-clockwise seen from above), the sizes of its shape (a table's length, depth, height, top and legs; a curtain's
width, height, fullness, *Split* in two halves and how far it is open at the start), colour and material (painted,
wood, molton, metal, gauze). In the plan drag a piece to move it (5 cm steps; Alt = freely), drag it with Shift held to
turn it (15° steps); in the 3D view drag it over the floor, a right drag that starts on the selected piece turns it.
Per cue (inspector, *Cue N*, for the selected piece): *In this cue* Unchanged / In / Out (at the cue's GO, no fade) and
for a split curtain *Open* % and *Opens in* s; what the cue doesn't say tracks from the cues before (the line under it
says from which cue), *Clear this cue's change* takes it out. The plan draws a piece that is out at the current cue as
a dashed outline, and the plan PDF has the pieces. Every edit is one undo step (a whole drag too).

**Values in cues** (decided 14 Oct 2026: Kloarte has no programmer; a value set by hand goes into the current cue,
undoable). Cues *track*: a cue holds only what it changes, every other value stays as the cues before left it. A value
is shown **white** when the cue sets it, **blue** when it comes from a palette (`@name`: edit the palette and every cue
that uses it follows), **grey** when it tracks from an earlier cue (its tooltip names the cue). Conventional lamps
change only their intensity per cue: shutters, barndoors, iris, gobo and focus are their hand focus (*Mechanics*, the
whole show). Moving heads change everything per cue, in their own units (from their GDTF): intensity %, pan / tilt °,
cyan / magenta / yellow or the emitters, CTO in K, colour wheel, zoom °, focus, iris, frost, gobo and animation wheel
(slots by name), rotations, prism, blades; the rest under *Other channels*. A colour wheel that goes from a preset
to *Raw DMX* (the LEDBeam's virtual colour wheel) sets the red / green / blue / white values too, so the colour stays
the preset's (the same hue; a saturated preset as bright as the emitters can make it); then move them from there.
Claude's tools do the same, unless they set the red / green / blue / white values themselves.

| How | What changes |
|---|---|
| The lamp's settings, *Cue n* (inspector) | its head's values in the current cue (several selected lamps: their heads); *Use palette...* takes a palette's values, *Track all* takes the head out of the cue, × one value, @ one value from a palette |
| *Aim at the stage...* (moving heads), or Shift + click with lamps selected | pan / tilt into the current cue (conventionals: their focus point) |
| Ctrl + wheel in 3D | the selected lamps' heads' intensity in the current cue |
| The cue table (F9) | any cue: type a level in a head's cell (empty = track, `@Front` = the palette), the cue's number, text and times; the chosen cell's head in full beside it |
| `cues.yaml` in VS Code | everything, as text (see *The show's files*) |

**Cue table** (F9, Tools > Cue table, *Cue table...* in Playback): a row per cue (number, text, In, Out, Delay) and a
column per head with its intensity (white / blue / grey as above; a dot = the cue also sets other values of that head).
*All heads* / a group, *Only heads in cues*. Choosing a row plays that cue (no fade); choosing a head's cell shows its
values in that cue on the right. Cells are edited in place: a number of %, empty = tracks, `@name` = the palette; the
cue number (any free number: the cues stay in number order), text, In / Out / Delay in seconds. Right click on a row:
*New cue after it* (no changes: the look tracks), *Copy after it*, *Delete cue*. *+ Cue* adds one after the chosen cue.
Insert = a new cue after the current one, Ctrl+Delete deletes the current cue (both in the Playback menu).

**Palettes** (F10, Tools > Palettes): the project's palettes; a palette holds values per head, of any kind (intensity,
colour, position as pan / tilt angles, beam: *combined*). *From the cue*: select lamps, choose a cue, type a name: the
values the cue gives those heads become the palette (new, or added to the palette of that name) and the cue refers to
it. Choose a palette to edit its values per head; *Rename* (the cues follow), *Delete* (the cues that used it keep its
values). On export a palette becomes one MagicQ palette per type of value it holds (intensity, position, colour, beam).

**The show's files** (in the project folder, VS Code with completion and hover from the schemas): `rig.yaml` (fixture
types, the patch: heads with number, name, type, address, channels, gel, beam angle, curve; groups; dimmer curves;
lamps and objects with id, kind, parent, position, rotation, size), `palettes.yaml` and `cues.yaml`:

```yaml
cues:
  - cue: "12.5"
    id: 31
    text: Opening
    fadeIn: 3
    fadeOut: 5
    heads:
      "150": {intensity: 80}
      "201": {intensity: 100, Pan: -12.5, Tilt: 40, ColorSub_C: "@Front", Gobo1: Stars, Zoom: 18}
```

A value is a number (in the channel's unit), a slot or set name (`Stars`, `Open`), a function and its value
(`Shutter1Strobe 5`), an exact DMX value (`dmx 37`, or after a readable value: `Stars (dmx 37)`, written when the
readable part alone wouldn't give the same DMX value) or a palette (`"@Front"`). Keys: `intensity`, the GDTF attributes
of moving heads, the channel names of other heads, `#12` for channel 12 of the head alone. `dmx: {"513": 255}` holds
values of addresses of no head (from an imported show). An edit while Kloarte runs is one undo step ("Edited outside
Kloarte: cues.yaml"); a value that means nothing for its head (a gobo the head hasn't) is a warning and left out; a file
that doesn't read is not applied and never overwritten.

**MagicQ** (the console runs the show from a MagicQ show file; Kloarte is for design and display):

- *Import*: a MagicQ show becomes a project (File > New project from show, the start screen). Exact: after every cue the
  DMX is MagicQ's. Its main cue stack (the one with the most cues) is the cue list; the other stacks, FX, curves and
  settings stay in the MagicQ show.
- *Export* (File > Export MagicQ show, *Export...*): written into a copy of the show the project came from (a new
  project: the house venue's), changing only what differs; an unchanged show is written byte-identical. After writing,
  the file is read back and compared with Kloarte (a difference is a warning). The status says what was written and
  what to **check in MagicQ PC** (kinds of records not yet verified there: new palettes, changed cue channels and
  times, cue steps put in or taken out, new heads, groups and MagicVis items, floor stands). Not exported: dimmer curves
  (a warning; edit them in MagicQ), cue delays. Never over an existing file.
- *Open in MagicQ PC* (Playback): exports the show as the project's next numbered MagicQ show and starts MagicQ PC with
  it, with live Art-Net and the look check.
- *Re-import* (Playback, or File > Import MagicQ show with a show of this project): after editing in MagicQ (the
  technical rehearsal), choose the MagicQ show (the newest one of the project is proposed): Kloarte lists what differs
  (cues added, removed, renumbered, changed; palettes; the patch; the rig) and takes it as one undo step. Mechanics,
  blocking, smoke and cue delays stay Kloarte's.

**Claude / MCP** (an MCP server inside the running app): Claude Code (or another MCP client on this PC) reads the open
show, edits it, plays it and looks at it. While Kloarte runs, connect once:

```sh
claude mcp add --transport http kloarte http://localhost:8462/mcp
```

or open Claude Code in the Kloarte folder: its `.mcp.json` names the same server. The server listens on this PC only
(not the network) and refuses requests from web pages of other sites. Port: default 8462; start Kloarte with
`-- --mcp-port=N` or set `KLOARTE_MCP_PORT=N` for another one (then use that port in the client), `-- --no-mcp` (or port
0) for no server. When the port is taken (a second Kloarte) that Kloarte runs without a server.

Every edit is **one undo step** (Ctrl+Z undoes it like your own, the Edit menu names it) and shows on the status bar
("MCP: ..."); **nothing is saved** until `save_project` (or your Save); `export_magicq` writes the project's next
numbered MagicQ show and **never overwrites**. A call waits while a dialog is open (an error after 2 minutes). Cues and
palettes need a native show (a project of before: save it once).

| Tool | What it does |
|---|---|
| `get_status` | what is open: project, files, unsaved parts, undo / redo, current cue, haze, counts, views, selected lamps |
| `get_problems` | errors and warnings in the project's YAML files (file, line, column) |
| `undo` / `redo` | as Ctrl+Z / Ctrl+Y, also for edits made in the UI |
| `save_project` | as File > Save |
| `export_magicq` | the next numbered MagicQ show; says what was written and what to check in MagicQ PC |
| `list_heads` | the patch: heads (number, name, type, address, gel, lamps) and groups |
| `get_head` | a head's channel keys, the values they take and their value after a cue |
| `list_lamps` | the lamps: rig item id, head, model, position on stage, which mechanics they have |
| `get_lamp` | a lamp's mechanics (field angle, iris, barndoors, shutters, gobo, focus point) |
| `set_lamp_mechanics` | changes a lamp's mechanics (merged in; the beam is rebuilt) |
| `list_cues` | every cue: number, text, times, heads set, smoke, scenery changes, the current one |
| `get_cue` | what a cue sets; with `tracked` the full state after it |
| `set_cue_values` | values in a cue, per head, or one set for heads / groups |
| `add_cue` | a new cue (copy of another, blackout, times, values) |
| `update_cue` | a cue's number, text or times |
| `delete_cue` | deletes a cue (later cues track from the one before) |
| `set_cue_smoke` | the smoke amount and build time from a cue on |
| `list_palettes` | every palette: heads, channel keys, cues that use it |
| `get_palette` | a palette's values per head |
| `set_palette_values` | values in a palette (made when new); cues that use it follow |
| `delete_palette` | deletes a palette no cue uses |
| `go_to_cue` | shows a cue's end state at once (or "before") |
| `go` | GO: the next cue with its fades |
| `set_look` | the room's haze (%) and stray light |
| `select_lamps` | selects lamps (ids, heads or groups) so you see what Claude means |
| `screenshot` | a picture of the window or of one view ("3D", "Audience", ...) |
| `list_actors` | the actors at the end of a cue: on stage, pose, position, facing, height, the face point aimed at |
| `aim_moving_heads` | **moving-head aiming**: pan / tilt in a cue (later cues track it) at an actor's face, the middle of several actors or a stage point; per head or one target for many |
| `focus_lamps` | **mechanical aiming**: a conventional lamp's focus point in its mechanics, the same in **every** cue (as focused by hand), at an actor (where they are at a cue) or a point |
| `list_scenery` | the set pieces: id, name, shape, position, rotation, sizes, colour, material, and at a cue whether each is on stage and how far a split curtain is open |
| `add_scenery` | a set piece (block, pyramid, table, curtain) with the fields of `scenery.yaml`; what isn't given is the shape's default; answers its id |
| `update_scenery` | moves, turns, resizes, recolours or renames a set piece (the id stays) |
| `remove_scenery` | removes set pieces and what the cues change of them |
| `set_cue_scenery` | per cue and piece: `visible` false / true (out / in at the GO), `open` % and `time` s for a split curtain; null or `"clear"` removes the cue's change (`list_cues` shows them) |
| `list_fixture_types` | the fixture types of the house catalogue that can be hung (id, name, kind, watts, zoom) |
| `list_supports` | the trusses, pipes and stands: name, start and end on stage, length, the lamps on them |
| `add_lamps` | hangs lamps of a type on a support (by name, e.g. "LT 5") at x positions or spread over it, each on a head of its own (the next free head and address, or from a given one on); they start dark |
| `remove_lamps` | takes lamps down, **only when their intensity is 0 in every cue** (palettes counted; otherwise refused with the cues that use them); a head no lamp is left on is unpatched and leaves the cues, palettes and groups |

Values are the ones of `cues.yaml` (see *The show's files*): intensity %, pan / tilt °, slot names, `dmx 37`, `"@Front"`.
Aiming: a moving head is aimed per cue; a conventional lamp is focused once for the show. Each tool refuses the other
kind of lamp. An actor is aimed at their face for their pose (or `height` metres above the floor at them).

**Inspector** (right panel, always there so the views keep their width; » / Ctrl+I collapses it to a strip that
names the selection, drag its left edge to resize; both remembered): with nothing selected the show's info and the
*Look* (stray light and haze, saved with the project); else the settings of the selected lamp — type, rigging, *Cue n* (its head's values in the current cue, see *Values in
cues*) and *Mechanics (whole show)* — or of the selected actor.
In *Rigging* a lamp moves only where its support lets it: on a **truss or track** the X / Y / Z fields slide it along
the truss (it keeps its drop and focus point and stops at the ends; on a truss along the stage only X is free, along
the depth only Z, on a diagonal truss X and Z move together), its height follows the truss (change that in Truss
heights); on a **floor stand** only the height can change (move the stand to move the lamp); with **(not attached)**
all three are free. The fields of the directions it can't move in are greyed out, with the reason in their tooltip;
one edit is one undo step ("Move C103 head 150 along T 1").

Mouse wheel over a slider or number field changes it (Shift = fine, Ctrl = coarse); over the labels it scrolls the
panel. Intensities are in % (the DMX value in the tooltip). Ctrl+B hides the side panels.

**Preferences** (File > Preferences, Ctrl+,): UI scale (Automatic follows the screen's scaling), the plan's PDF paper,
the movie size, the Art-Net universe, MagicQ's show folder (shown; click to open), the key map.

**Export** (File > Export..., Ctrl+E / Ctrl+R, or the timeline's Stills / Movie buttons): stills (PNG + index.csv),
movie (rendered in a second window at 30 fps; MP4 when ffmpeg is found) or contact sheet (PDF, nine cues per A4
page); the viewpoint (the 3D view's camera, the audience view's seat, a named viewpoint such as Regie), the first and
last cue, cue captions, the size, and *One per actor*: an export for each person on stage, with only their name on
their shirt and in the caption. Default folder: `exports` in the project folder.

**Workspaces** (bar at the top, View menu, Ctrl+1 / 2 / 3): *Rig* (plan, with 3D and the truss heights as small
views; the Fixtures list), *Focus* (3D, with the audience view and the plan; Fixtures), *Blocking* (the timeline with
a lane per actor, with 3D, the audience view and the actor view; the Actors list) and *Present* (Present mode). In the timeline's
lane header *+ Actor* adds an actor; click a name to select the actor, double-click to rename, right-click to
rename, remove or clear its movement in the cue; the inspector shows the selected step and when it waits, walks and
arrives. **Views**: the main view's toolbar is in the strip under the view bar; click a view's tab to make it the main view; right-click for small / hidden; click a small view
(card) to swap it with the main view; H hides the small views. **Docked timeline**: the timeline's *Dock* button, the
right-click menu of its tab, View > Dock / undock the timeline or Shift+F5 put the timeline under the main view while
that is the 3D, audience or actor view (under the plan it is a card as usual); drag the bar between them for its height;
its toolbar joins the main view's in the strip; click in it for its keys (Shift+Space play, Delete step). F5 still
makes it the main view, F1 / F2 / F8 dock it again; *Undock* makes it a card. Each workspace remembers what you changed in it
(also the docked timeline); View > Reset workspace brings back its default.

**Tools** (Tools menu; each in a window of its own, the same key closes it): *Cue table* (F9), *Palettes* (F10), *Gobo editor* (F7): 1 = select,
2 = line, 3 = arc; Ctrl+Z / Ctrl+Y undo the drawing; save, use on the selected lamp. *Key map* (F6): click a key,
press the new key; reset. Keys the tool doesn't use work as in the main window (Space = GO, Ctrl+S = Save).

| View | Use |
|---|---|
| 3D | right mouse = look, WASD / Q / E = move (Shift faster), wheel = forward/back (Shift faster), Ctrl + wheel = the selected lamps' heads' intensity in the current cue (Ctrl + Shift coarse, Ctrl + Alt 1 DMX; saved with Save), middle mouse = pan, 1/2/3 = presets; click a lamp to select it, Shift + click = aim the selected lamps at that spot (floor or back wall), click the stage to place an actor |
| Audience | choose a seat or a named viewpoint ("Regie"); `[` `]` = seat, PgUp / PgDn = row; right mouse = look around |
| Plan | wheel = zoom, middle mouse = pan, F = fit; drag lamps (Alt = no snapping), box select, add fixtures (choose the type, click near a truss or stand; Esc stops; also in a show without any lamps yet), Remove lamp / Delete, add stands, patch box, Save PDF (A3 / A4, the plan as large as fits, scale bar and a compact legend) |
| Truss heights | drag a truss up or down, or type the height |
| Actor | the stage through the eyes of the actor chosen in the toolbar (or selected elsewhere), standing, seated or lying; right mouse drag sideways = **turn the actor** (its facing is recorded in the actor's step of the current cue, like the Facing slider; one undo step per drag; the actor becomes the selected one), up / down = look up / down, wheel = zoom, middle mouse = straight ahead. The bottom line shows its facing and its next entrance / exit is shown at the bottom ("Next: AF cour, cue 12 (in 8.5 s)"), a banner when GO / Play passes it. Side: cour = the audience's right, jardin = left; near the centre line lointain (upstage) or face (downstage) |
| Plan, set pieces | click = select, drag = move (5 cm steps, Alt = freely), Shift + drag = turn (15° steps), Delete = remove |
| Timeline | drag on the ruler = scrub; drag step edges and hold handles; double-click = add a step; Shift+Space = play; Stills / Movie open the export dialog |

Default keys: Ctrl+1–3 workspaces, F1–F5 and F8 (actor) views, F6 key map, F7 gobo editor, F9 cue table, F10 palettes, Tab next view, H small views, Shift+F5 dock the timeline, Ctrl+B side panels, Ctrl+I inspector, Space GO, Backspace Back,
Home reset, P auto, Insert new cue, Ctrl+Delete delete cue, Ctrl+Shift+N add a set piece, Ctrl+D duplicate it, Shift+Delete delete it, L solo, Esc deselect, Ctrl+O import a MagicQ show, Ctrl+Shift+O open a project folder, Ctrl+S save, Ctrl+Shift+S export a MagicQ show, Ctrl+E export,
Ctrl+R movie, Ctrl+, preferences. All of them can be changed in the key map.
