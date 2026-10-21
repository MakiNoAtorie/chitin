# Wave 3 — implementation brief (optics, palette, camera presets)

One session, per `dev-docs/PLAN.md` Working rule 7: implement items 6, 9 and 8, then review your own
diff, fix what the review finds, re-run the gate, and only then report the wave closed.

`dev-docs/PLAN.md` **Wave 3** is the spec. Two decisions already settled:

- **Item 8 is palette only.** No UI overhaul: no new panels, no moved chrome, no restyled dialogs. The
  visible change is the colour of guides, paper and ink. `Ctrl+Shift+P` cycles palettes.
- The help overlay must finally gain rows for every shortcut added since wave 0 (`B`, `D`, `E`, `V`,
  `Ctrl+E`, `Ctrl+Z/Y`, `Ctrl+S/O`, `[`/`]`, `Ctrl+Alt+I`, `Ctrl+Shift+P`). Adding them — like the
  palette change itself — deliberately moves the interactive PNG hash. That is expected this time:
  accept it, record the new hash in the report, and keep `gate-export.png` behaviour explicit (the
  export follows the same palette, so its hash moves too).

## Item 6 — station point, FOV, cone of vision

- Principal point = picture-plane centre. Require VPs 0 and 1 to exist on opposite sides of it;
  otherwise the overlay draws nothing and the HUD says `cone: needs two points`.
- `d = sqrt(|x0| * |x1|)` where `x` is the signed distance along the horizon from the principal point.
  FOV `= 2*atan((pictureWidth/2)/d)`. Cone radius `= d * tan(30°)`.
- Overlay (`V` toggles, persisted in the project): dashed cone circle about the principal point, station
  marker at `P + (0, d)` in world units, dashed legs to V0 and V1, and a small warning tick on any VP
  outside the cone. HUD right side adds `· FOV 77° · d 400`.
- New `CHECK station_distance`: VPs at (−400, 0) and (400, 0) on a 640-wide document → `d = 400 ± 1e-6`,
  FOV `= 77.3196° ± 0.001`, cone `= 230.9401 ± 0.001`. Add a case with both VPs on the same side →
  overlay reported unavailable.

## Item 9 — camera presets and saved rigs

- Camera menu: 1-Point, 2-Point, 3-Point. World coordinates, 640×480 document, horizon at 60 % height
  (`y = 48`): 1-point `VP0 (0, 48)`; 2-point `VP0 (−224, 48)`, `VP1 (224, 48)`; 3-point adds
  `VP2 (0, −384)`. Unused slots null, densities 0. Applying a preset pushes undo.
- "Save Rig…" asks for a name and stores the nine points (position + density) in `World.Rigs` and the
  project file (the schema already has `rigs`); the menu lists rigs for one-click recall. Saving over an
  existing name replaces it.
- New `CHECK preset_three_point` (exact nine-point array) and `CHECK rig_roundtrip` (save → serialise →
  load → identical).

## Item 8 — curated palette (colours only)

- `Palette` record: nine VP colours, grid colour, ink, paper (plane fill), border, void/horizon, prompt.
  Built-ins: `atelier` (default, warm off-white paper, terracotta/ochre/sage/plum guides), `ink` (near
  monochrome, one ochre accent), `charcoal` (dark paper, chalk guides). **No blue anywhere.**
- Delete `ShuffleVPColors`' RNG. `S` rotates the assignment by one; `Ctrl+Shift+P` cycles palettes.
  Palette name is saved and restored; `ZoomChanged` rebuilds pens from the active palette.
- Every colour the renderer uses must come from the palette — no stray literal colours in `Renderer` or
  `Prim`. Derive the HUD/help tints from palette ink/paper rather than hardcoding.
- New `CHECK palette_stable` (two builds of the same scene are byte-identical) and `CHECK palette_no_random`
  (the nine pen colours equal the palette's nine, in order).

## Closing phase (same session)

1. Review your own diff for defects and gaps: optics degenerate cases (same-side VPs, zero distance,
   VPs exactly on the principal point), preset arithmetic, rig serialisation, palette coverage (any
   hardcoded colour left?), help-overlay accuracy against the real key handler, and plan compliance.
2. Fix what you find, or refuse with a reason.
3. Run `dev-docs/verify.sh` until `GATE_OK`. Report the new interactive and export hashes as the wave 4
   baseline, and confirm the shapes hash only moves if the palette legitimately changes it.
4. Commit `Wave 3 review: <n> findings fixed`, then report: commits, findings fixed/refused, gate
   output, new hashes, and anything left open.
