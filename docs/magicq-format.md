# Show model and MagicQ files (findings, MagicQ 1.9.7)

Moved out of CLAUDE.md (verbatim, detail on demand). CLAUDE.md has the summary.

## Show model and MagicQ files (findings, MagicQ 1.9.7)
Shows: `C:\Users\pvoos_nnvpp5d\OneDrive\Documents\MagicQ\show\` (e.g. `Risquons-Tout\RT2025_031.shw`). Read-only for us:
tests write to `tests/output/`, never over the originals. Everything MagicQ stores is kept **lossless**
(unchanged records byte-identical; verified on all 64 shows by `magicq_roundtrip`).
- `.shw` is ASCII (CRLF). Lines starting with `\` are comments. Records = comma-separated fields, first field is the
  record type, terminated by a `;` token, may span lines; most lines end with `,` (separator, not an empty field).
  Numbers are mostly hex (head numbers too: `96` = head 150).
- `P` = personality: name, manufacturer, mode; channel list from field 16, 3 fields per channel: name, flags,
  attribute; attribute 0 = dimmer. `IntensityChannel` (LEDBeam 150 Mode 1: 20; T1 Mode 1: 47; generic: 0) equals the
  GDTF dimmer offset. T1 channel names "Cyan/Red" -> GDTF "Mode 1 - CMY" by default.
- `L` = patch, one record **per DMX channel**: f1 address (hex, 1-based absolute), f2 head name, f4 personality,
  f5 channel in head, f10 head number, f11 gel id (allcols.csv: 1000+n Lee, 2000+n Rosco), f21 shape id, f22 beam
  angle (whole degrees, 0 = default; wins over mechanics). Short L records lack f21/f22. f9 = dimmer curve:
  4 = User 1 (U record 1), user curve n = f9 - 3 (RT2025_028+ channel 159: f3 1 -> 1001, f9 0 -> 4; MagicQ PC output
  222 -> 196 = the U 1 table); 1..3 probably Limit / Offset / Offset No Wrap (unverified).
- `U` = user curve (Curve Editor; also usable in FX): 1 id, 2 name, f6 table length (00ff), then the sampled table
  (16-bit outputs for inputs 0..254; 255 = full), then the points (count, x / 16-bit y pairs, spline flags), 0, `;`.
  MagicQ outputs the curved value: `WithCurves` in `DmxAfter` / `PlaybackTimeline.DmxAt` (fades run before it).
- Lamps sharing a channel: RT2025_021 "Weverij T" heads 400-403 = one L record each with six MagicVis fixtures ->
  lamp = H item, head = patch; their intensity is the same and so is the gel (MagicQ stores it per head).
  Repatching in MagicQ moves the cue values to the new addresses (RT2025_005 -> 006); `PatchEditor.MoveHead` does
  the same for cues and palettes. MagicQ: separate heads with the same number stay separate (virtual dimmer only with
  exactly 2), so the plan never creates duplicate head numbers. Plan patch rules: an address / number of another head
  = join it; the only lamp on a head takes the head along (move / renumber); a lamp sharing a head and given a new
  address gets its own head (given number or a free one); conflicts are refused.
- `G` = groups: `G,<num>,"<name>",<count hex>,<head numbers hex...>`.
- `C` = cue stack: 1 number, 2 name, 4 step count; a 17-field line; then 7 fields per step, one line each: next step,
  previous step (circular list in record order), cue id (-> `S`), ?, flags (00006000; 0000c000 mark cue), cue number
  (float), text (max 15 chars). Then a notes flag (00000001: two strings per step follow, the second is the step note),
  a 5-field line, one 8-digit field per step, and a tail that doesn't depend on the steps. In all 727 stacks of the
  show folder the steps are in cue-number order and linked in record order. `CueStack.InsertStep` adds a step at its
  place in all three per-step sections and relinks; `CueStack.NumberBetween` picks the new cue number (fewest
  decimals: 1|2 -> 1.1, 1|4 -> 2).
- `S` = cue: 1 id, 2 name, 3 pair count, f6..10 In / Out / Pos / Col / Beam (units of 1/33.3 s), pairs from f16 on
  **one line** (address, value), sorted by address; later lines (FX per channel, timing) refer to channels by
  address: a header line `0002,<count>,...` then per channel `addr,waveform?,speed?,size,group,phase offset,flags`
  (RT2025_031 cues 29.1-30.2: an FX on 61 channels with a phase spread; not simulated). Value = `palette id << 8 | DMX value` when it refers to a palette (the low byte is a cached copy).
  `Cue.SetChannel` inserts pairs in address order and updates f3. Empty cues (0 pairs) exist (RT S0001).
  `MagicQShow.AddCue` copies the simplest cue's record (incl. its fade times; the fields after the pairs are not
  understood), next free id; `InsertCue(stack, number, text, blackout)` = BO (every intensity channel of the patch at 0)
  or a cue without changes (no channels: the look tracks). Playback tab: "+ BO cue" / "+ No change" after the current
  cue.
- `F` = palettes (manual `<MagicQ PC>\manual\magicq\palettes\palletes.html`): id = type << 12 | entry (`9` intensity,
  `a` position, `b` colour, `c` beam), name, count, triplets (address, value, link = 0 or another palette's id), then
  a trailer (meaning unknown; new palettes copy it from the simplest palette of the same type). A position palette
  can hold colour / beam too (MagicQ's record mask) = combined palette (RT2025_044 "Strobo accident").
  Re-recording merges and refreshes the cached bytes in the cues (`RecordPalette`). 65185 cue references verified.
  **To verify in MagicQ PC:** a written palette (trailer) and inserted cue pairs.
- `H` = MagicVis: 1 id, 2 kind (1 fixture, 2 object), 3 object type, 8 parent id, 13-19 obfuscated numbers (x, y, z,
  rx, ry, rz, extra); fixtures 20 address, 21 head, 22 personality; objects 20 code, 21-23 size, 24 name, 26 model.
  Object types 1 proscenium, 2 truss / pipe (length along local +X), 4 box, 6 person, 7 podium, 8 model, 9 border.
  Floor stands are written as type-2 pipes with rz = 90 (new H records copied from a truss; **verify in MagicQ**).
  Numbers: "%.2f", per char `x = ((c >= 0x60 ? c - 0x20 : c) ^ 0x64) - (3 - i)`, digits 9-complement, 0x3b '.',
  0x3c '-'. Euler order Y-X-Z; fixtures point down at rotation 0.
- `T` = console settings (loaded only with SHIFT + LOAD SHOW+CON): f1 show path, f8 universe count, then one word
  per universe (DMX I/O): top byte 0x80 enabled | out type (0 None, 1 Art-Net), next byte in type << 4, low byte
  out / in universe nibbles. Default `0110_00nn` = disabled, Art-Net out + in (seen in View DMX I/O; `8011_00nn` gave
  Enabled / Out None). `make_magicq_test_show` sets universes 1 and 2 to `8110_00nn`. Next line: f0 = MagicQ's IP
  (little-endian hex: `8ec80902` = 2.9.200.142), f2 = netmask (`000000ff` = 255.0.0.0).
  `MagicQ\show\status.dat` = MagicQ's own state: the same V + T records; T f1 = the show MagicQ opens at start
  (command-line arguments are ignored). Rewritten when MagicQ quits, so edit it only while MagicQ is closed.
  MagicQ PC cannot output DMX to a (virtual) COM port: its USB devices use FTDI D2XX, out types are network or
  ChamSys hardware only.
- People objects (type 6) codes 60001..60004 (man / woman standing / seated: a GUESS, verify in MagicVis).
- `lastshow.shw` (MagicQ show folder) = MagicQ's copy of the loaded show (unsaved changes included), written when
  MagicQ quits (seen missing while it starts); header `\ Show saved as '<real show>.sbk'`. At start Kloarte opens the show in
  status.dat T f1 (user: preferred; the newer of its .shw and .sbk, `MagicQPc.Existing`; an unreadable status.dat
  counts as missing), else lastshow.shw (`ShowBuilder.OpenMagicQLastShow`, only when the show scene
  is the main scene, so not in tests); `MagicQPc.SourceOf` gives the real show (its project, dialogs, MagicQ launch);
  `MagicQShow.Load` takes the real show's .xhw.
- `.xhw` = ustar tar (some gzip) with icons and `.mvm` models; MagicQ's headers: mode `000644 `, uid/gid `000000 `,
  mtime 0, devmajor/devminor `000000 `, magic `ustar\0` `00`.
- Risquons-Tout: hoist plan positions match (all trusses 0.45 m further from the proscenium object than the house
  plan); all trusses at the MagicVis default 6.00 m; the 8 "Horizon" ACPs hang at 3.98 m, 0.5 m in front of the cyc
  (physically they can't fill the cloth there; hung at the top of the cyc (7 m, 1 m in front) they cover 86 %).


## Writing records for the export (15 Oct 2026, `MagicQExporter`)
- Record order of a show (first occurrence): V T Y Z U P L H F G W S X C B Q R J. New records go after the last of their
  type, else after the types before it (`MagicQShow.AddRecordFrom`, order up to C).
- New records are copies of a record MagicQ wrote (the show's own, else `data/magicq_templates.shw`: lamps P / L / H,
  and a cue S, cue stack C, group G, palette F of each type 9 / a / b / c, the smallest of the Risquons-Tout shows; the
  intensity palette is from `demos/ChauvetDemo2024.shw`), with the fields Kloarte understands set.
- `CueStack.RemoveStep` is the inverse of `InsertStep`: the step line, its two note strings, its per-step field, the
  count and the relinking; a renumbered step that changes the order is removed and put in again (its note is lost).
- `Group.SetHeads` rewrites the member list on the count's line (hex, as MagicQ writes them); `Palette.SetValues`
  replaces all triplets (links dropped); cue times: only the units of In / Out (f6 / f7) are set, not the millisecond
  copies further on (`00000b9b` = 2971 for 99 units): **to verify in MagicQ PC**.
- Not written: user curves (U), FX lines of cues, other cue stacks (kept byte-identical).
