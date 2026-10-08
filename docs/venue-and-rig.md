# Materials, rig and venue

Moved out of CLAUDE.md (verbatim, detail on demand). CLAUDE.md has the summary.

## Materials (realistic values)
- black molton: albedo ~0.02, roughness 1.0; black stage floor: albedo ~0.08, roughness ~0.55
- cyc: albedo ~0.8 (venue JSON), roughness ~0.95; seats: red velvet. Ambient light almost black.

## Rig (available equipment) — see `data/fixture_catalog.json`
Conventional: 12x ADB C203 2 kW PC, 28x ADB C103 1 kW PC, 12x ADB F101 1 kW fresnel, barndoors 12x 2 kW / 30x 1 kW;
profiles (iris + shutters) 4x DN204 2 kW 10-20°, 6x DS204 2 kW 13-36°, 9x DS104 1 kW 15-31°, 5x DW104 1 kW 15-38°,
8x ETC Source Four 25-50°; gobo holders 10x A, 2x B; 41x PAR64 long nose, 6x PAR64 floor can, 6x PAR36 pinspot
(CP60/61/62); 15x ADB ACP1001 1 kW asymmetric; 2x blacklight 400 W.
Moving / LED: 12x Robe LEDBeam 150 (GDTF RGBW, 2288 lm), 4x Robe T1 Profile (GDTF, 10075 lm), 4x FOS100 LED bars 15°.
Other: Panasonic PT-RZ990 (ET-DLE150), Swefog Ultimate 2000 hazer (DMX), Swefog Live T26 smoke. Console MagicQ MQ250M.

## Venue: CC De Brouckere, Torhout (tech sheet 28/02/2023) — `venue/ccdebrouckere.venue.yaml`
- Proscenium theatre with a straight forestage; depth curtain -> back cloth 9 m, incl. forestage 11.5 m; playing
  area max 12.5 m wide; manteau 9-12.5 m; cyc 15 x 7.5 m on T22, back cloth on T21.
- Hoist plan (from the datum, 0.45 m upstage of the show's proscenium object): T1 0.90 ... T22 9.15 (see JSON).
  Max height: light bars 7.5 m, scenery bars 7.9-8 m, portal bridge 6.1 m, under the border 6 m.
- Auditorium: rows A-U (A-S 20 seats, T 18, U 17, alternating half-seat offset, even numbers house left), 415 seats;
  12 removable control seats N-O 15-20 ("Regie"). Row A 1 m below the stage, 0.5 m from the stage edge (user);
  hall 13 m wide, 22 m deep, height difference 5.5 m (rake 0.27 m / row, pitch 1.0 m: derived).
- House stands: floor stands, Manfrotto 070U / 111U / 087U, T-bars, ballet towers. Gel stock list in the tech sheet.

