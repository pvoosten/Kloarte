# Before selling Kloarte

Nothing here is needed while Kloarte is for your own use and shows. It is the list of what to settle **if you ever sell
or publicly distribute it**. Written 13 Oct 2026 from a talk about ChamSys and Robe. This is a work list, not legal
advice: have a lawyer look at the licences in step 0 before money changes hands.

How to use it: work through the steps in order. Each step has a prompt to paste into Claude Code (it assumes nothing
about this conversation), questions Claude should ask you first, and what "done" means.

## Status of the risk

| Item | Where | Risk |
|---|---|---|
| ChamSys colour table | `src/Kloarte.Host/data/allcols.csv` | ChamSys' own file, shipped in the app |
| MagicQ show templates | `data/new_show.shw`, `data/magicq_templates.shw` | made from MagicQ shows: ChamSys personalities and MagicVis objects |
| Robe GDTF files | `data/gdtf/*.gdtf` | Robe's descriptions, wheel images, 3D models; redistribution terms unchecked |
| Robe wording in the notices | `data/third_party_notices.txt` | says the files come via GDTF Share: written from assumption, never verified |
| CC De Brouckere venue | `venue/ccdebrouckere.venue.yaml` | facts from a tech sheet; the venue has not been asked |
| Names and trademarks | UI, README, catalogue | MagicQ, Robe, Robin, ADB, ETC, Swefog, Art-Net: descriptive use only |
| Godot, .NET, YamlDotNet | Help > About | MIT: fine, as long as the notices ship |

Not a problem as far as we know: reading / writing the `.shw` format (EU interoperability rules, you ship no ChamSys
code), `ffmpeg` (taken from the user's own MagicQ install, not shipped), your own code and show data.

---

## 0. Read the terms yourself first (you, not Claude)

- [ ] The MagicQ EULA (in the MagicQ PC install and on chamsys.co.uk): anything about show files, compatible tools,
      reverse engineering, redistribution of personalities?
- [ ] The GDTF Share terms of use (gdtf-share.com) and the licence line of each Robe file you ship (inside the .gdtf:
      `description.xml`, and the share page of the file).
- [ ] Whether a lawyer (or the Flemish "Intellectual property" helpdesk of VLAIO / the Belgian IPR helpdesk) should look
      at it. They answer such questions cheaply for small businesses.

Done when: you know, per item in the table above, whether you may ship it. Write the answers at the bottom of this file.

---

## 1. Replace the ChamSys colour table

**Prompt**

> Read CLAUDE.md and `docs/before-selling.md` (step 1). `src/Kloarte.Host/data/allcols.csv` is ChamSys' own file and must not
> ship. First find every use of it (`MagicQColours`, `MagicQStore`, the "fallback" in CLAUDE.md, the dist script). Then
> ask me the questions below, then replace it with a table Kloarte owns, keeping the MagicQ gel ids it has to read
> (1000 + n Lee, 2000 + n Rosco). Keep reading the user's own `heads/allcols.csv` from their MagicQ folder when it
> exists: that is their file, not shipped by us. Add tests so every gel id of the 64 shows in the show folder still
> resolves to a colour. Update CLAUDE.md, the notices and the dist script.

**Claude asks you**
- Where may the replacement colours come from (own measurements, the public Lee / Rosco swatch data you may reuse, a
  generic approximation)? Is an approximate colour acceptable where no data is free to use?
- Should Kloarte ask the user to point to their MagicQ install for the exact colours instead of shipping any?

Done when: no ChamSys file is in `data/`, tests pass, gels look as before within tolerance (compare the plan PDFs).

---

## 2. Replace the MagicQ show templates

**Prompt**

> Read CLAUDE.md and `docs/before-selling.md` (step 2). `data/new_show.shw` (a new project's show) and
> `data/magicq_templates.shw` (P / L / H records for generic dimmer, LEDBeam 150, T1 Profile) were made from MagicQ
> show files. Find how they are used (`MagicQStore.NewShowTemplate`, `MagicQShow.ImportLamp`,
> `AddLampInPlan`). Ask me the questions below, then change the design so that Kloarte writes the records itself from
> its own definitions (the format is documented in CLAUDE.md under "Show model and MagicQ files") instead of copying
> ChamSys content. Every show written must still open in MagicQ PC; list what I have to verify there myself, and
> make me do it before you call this done. Keep the byte-for-byte round trip of existing shows.

**Claude asks you**
- Must a new project start from a blank MagicQ show that Kloarte writes, or from the user's own show (the "New project
  from show" path already exists)? Is "from your own show" enough for sale?
- Which fixture personalities must be added without a template (the T1 in Mode 3, the LEDBeam, generic dimmer)? Are
  the MagicQ personalities themselves ChamSys content (they are in the user's MagicQ install: should Kloarte copy
  them from there at runtime instead)?

Done when: no `.shw` made from MagicQ content ships, MagicQ PC opens a new show and a show with added lamps, tests pass.

---

## 3. The Robe GDTF files

**Prompt**

> Read CLAUDE.md and `docs/before-selling.md` (step 3). `data/gdtf/*.gdtf` are Robe's files. I have read the terms (my answer
> is in the "Answers" section of docs/before-selling.md). Ask me the questions below, then implement the choice: either
> (a) not bundled: Kloarte finds them in `data/gdtf` of the user's own copy, or downloads them from GDTF Share on first
> use / via a menu item "Get fixture files" with the licence shown and a clear message when a fixture is missing
> (moving heads must then degrade to a plain lamp, not crash); or (b) bundled with Robe's written permission, with the
> exact attribution they ask for in Help > About. Fix the Robe paragraph of `data/third_party_notices.txt` so it says only
> what is true.

**Claude asks you**
- (a) or (b)? If (a): is it OK that a fresh install shows moving heads as plain lamps until the files are fetched?
- Which other manufacturers' GDTF files might come later (so the mechanism is general, not Robe-only)?

Done when: the notices are true, a clean install without the files starts and says what to do, tests pass.

---

## 4. Names, trademarks and "not endorsed" wording

**Prompt**

> Read CLAUDE.md and `docs/before-selling.md` (step 4). Search the app text, README, export presets, window title and
> dist script for ChamSys / MagicQ / Robe / Robin / ADB / ETC / Swefog / Art-Net. Use them only to say what Kloarte
> works with ("opens MagicQ show files"), never as part of the product name, logo or splash, and never in a way that
> suggests endorsement. Add one clear sentence to Help > About and the README ("Kloarte is independent software; it is
> not made, endorsed or supported by ChamSys, Robe or the venue"). Show me the list of changes before editing.

Done when: you have read the list and agree.

---

## 5. The venue (CC De Brouckere)

**Prompt**

> Read CLAUDE.md and `docs/before-selling.md` (step 5). The app ships `venue/ccdebrouckere.venue.yaml` (made from the venue's
> tech sheet of 28/02/2023). Ask me the questions below, then make the shipped venue either a generic example theatre
> or the real venue with the owner's permission, with their name used as I direct. Check that nothing else in the
> repo identifies the venue's staff or private data.

**Claude asks you**
- Do you have (or will you ask for) the venue's permission? Should the venue be named, credited or not mentioned?
- Is a generic demo venue better for the product, with the real one only in your own projects?

---

## 6. Legal basics for selling (interview)

Ask Claude to interview you before any code: "Interview me for `docs/before-selling.md` step 6, one question at a time."

- Who sells it (you as a person, a company, a non-profit / theatre group) and where (Belgium only, EU, worldwide)?
- What is sold: the app, a licence per seat, a subscription, support, shows? Free for amateur theatre?
- Do you want it open source (and under which licence) or closed? (The notices already allow both: Godot, .NET and
  YamlDotNet are MIT.)
- Licence text for users (EULA), disclaimer ("the performance depends on your console; check your show file in MagicQ"),
  privacy (does Kloarte send anything? it should not; keep it that way and say so).
- Support promise and updates; what happens when MagicQ changes its file format.
- VAT / business registration questions: for an accountant, not for Claude.

---

## 7. Ask for endorsement or partnership: **ask Claude to draft the requests**

Do not forget this step: a written yes from the owners removes most of the doubt above, and ChamSys, Robe and the
venue may well say yes.

**Prompt (one request per party; say which)**

> Read `docs/before-selling.md` step 7. Draft a request to **[ChamSys / Robe / CC De Brouckere]** for **[permission /
> endorsement / partnership]**. Interview me first, one question at a time: who I am and what Kloarte does (one
> paragraph and three screenshots: ask me which), what exactly I want from them (see the lists below), what I offer
> back, what I will do if they say no. Then write a short, friendly, concrete email in English (and a Dutch version if
> I ask), with a subject line and a one-line ask at the top. Do not send anything.

What to ask for, per party:

- **ChamSys:** permission to read and write MagicQ show files in a commercial tool and to describe it as "compatible
  with MagicQ"; permission or an alternative for the colour table and the fixture personalities; an answer on the
  EULA question of step 0; a contact for file-format questions (and the offer to keep the show files lossless: the
  round trip test on 64 shows is a good thing to mention); maybe a listing as a third-party tool.
- **Robe:** written permission to include the Robin LEDBeam 150 and T1 Profile GDTF files (or confirmation that
  GDTF Share terms allow it); the attribution they want; maybe test files of new fixtures, and a mention of Kloarte as a
  visualiser that shows the T1's framing shutters and animation wheel (the things MagicQ's own visualiser can't).
- **CC De Brouckere:** permission to ship their venue (stage, hoist plan, auditorium) as a built-in venue and to use
  their name; maybe a pilot there, a quote about "Kruimels", and a link to them in the README.

Keep the answers (emails, dated) in a `legal/` folder outside git, or in a private repo.

---

## Answers (fill in as you find out)

- MagicQ EULA:
- GDTF Share terms / Robe file licences:
- ChamSys reply:
- Robe reply:
- CC De Brouckere reply:
- Lawyer / helpdesk:
