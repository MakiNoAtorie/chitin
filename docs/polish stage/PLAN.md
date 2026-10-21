# Chitin — polish stage plan

Direction chosen by Maki on 2026-10-20: Proposal B (canvas-first, `docs/ui-proposals/b-canvas.html`)
with these changes.

1. It must not look generated. No default choices: every visual decision comes from the domain
   (perspective drawing, drafting sheets) or from the app's own palettes, not from what a typical
   app or mockup would do. It may be stylish; it may not be generic.
2. `Ctrl+K` does not open a command palette. It switches the window from the compact canvas (B) to
   the detailed panels (A), and back. One frame, two widths.
3. Font: **B612 Mono** (P0 review).
4. Palette: **ink** becomes the default for new documents (P0 review).
5. The menu bar shows in both modes, and carries a **Find** box for commands.
6. The mode is not remembered between launches.

Baseline: `main` @ `79b650f`. Gate: `dev-docs/verify.sh`. The working rules in `dev-docs/PLAN.md`
(SPEC updated per item, one commit per item, `CHECK <name> PASS|FAIL` in the self-test, undo before
every mutation, pure geometry helpers, no new packages except the one font) still apply.

Mockup: `docs/polish stage/mockup.html` (serve `docs/` over HTTP; it loads `../ui-proposals/scene.js`).

---

## 1. Why B looked generated, and the rule that replaces each tell

| Tell in the B mockup | Rule for the polish stage |
|---|---|
| Frosted-glass panels with blur and drop shadows | No blur, no shadows, no gradients, no texture. Flat fills from the active palette. |
| One border weight everywhere | Three pen weights, as on a drawing: hairline for cells, medium for panel edges, heavy for the sheet frame. |
| Rounded pills and 10 px radii | Square corners. Round shapes only where the drawing is round (vanishing points, cone of vision, detail bubbles). |
| One accent colour for "selected" | Selection inverts ink and paper. The prompt colour marks attention only: prompts, focus, the camera centre. |
| `system-ui` / Segoe text | B612 Mono, bundled. Hierarchy from size and tracking: small spaced capitals for labels, large figures for the values that matter. |
| Floating chips in the corners | Readouts live in the sheet margin: rulers, index tabs, title block. Nothing floats over the plane. |
| Centred toast for hints | Numbered drawing notes in the bottom margin (`NOTE 1`, `NOTE 2`). |
| Icon-set glyphs and keycaps | Tools are lettered (`P T B D E`). No icon font, no keycap styling. |
| Eased slide-in animations | Panel change is a linear 100 ms width change. |

## 2. Visual identity: the drawing sheet

References: ISO 5457 sheet frames (frame weights, centring marks), ISO 7200 title blocks
(ruled fields, small labels over large values, revision table), architectural detail callouts
(the split circle), section hatching at 45°, and B612's own source, the Airbus cockpit
display (scales, tabular figures, no decoration). Each element below also does a job.

### 2.1 Frame and line weights

- Heavy rule (2 px) around the whole window and the viewport; medium (1 px ink) between panels;
  hairline (1 px at reduced ink) between table cells.
- Centring marks: a short heavy tick at the middle of each viewport edge, in the prompt colour.
  They mark the viewport centre, which is the point the camera keeps when panels open (§3).

### 2.2 Rulers (top and left margins)

- 20 px rulers ticked in document units, following zoom and pan. Labels at major ticks.
- The cursor is a hairline on each ruler. The horizon (VP1–VP2, when level) is a filled
  triangle on the left ruler.

### 2.3 Vanishing-point index tabs

- Each placed VP has a notched index tab on each ruler at its projected position. The tab is
  paper with ink figures and a 4 px band in the VP colour. Text never sits on the VP colour,
  which removes the contrast failure found in P0 (ink palette VP numbers at 4.2:1).
- Selected VP: tab inverted, band kept.
- A VP outside the viewport has its tab pinned to the ruler end with a direction arrow and its
  distance past the edge in document units.

### 2.4 Title block (bottom-right)

- ISO 7200 layout: small spaced labels over values. Zoom and the selected VP are set large;
  the rest at body size. Fields: sheet, size, zoom, palette, point, density, cone, tool.
- In detailed mode, zoom and density are editable in place.

### 2.5 Notes (bottom-left)

- `NOTE 1` is the current tool's hint. `NOTE 2` appears for prompts (trace lines cross), with a
  square in the prompt colour.

### 2.6 Tool tray

- Square cells lettered `P T B D E`, large. Active tool inverted. Detailed mode adds the name, the
  gesture and the tool options.

### 2.7 Inspector sections and hatching

- Each inspector section opens with a detail bubble (split circle: letter over sheet number),
  the way a drawing calls out a detail.
- Empty or unavailable cells (empty VP slots, FOV with the cone off) are hatched at 45°, not greyed.

### 2.8 Colour

- All chrome takes its colours from the active `Palette`; no colour outside `Palette.cs`.
  Switching palette re-themes the whole window.
- Text pairs reach WCAG 4.5:1 in all three palettes (CHECK in P4).

## 3. Modes, menu bar and Find

`Ctrl+K` is free today (no `Key.K` handler in `ViewportControl`). It toggles one flag on
`MainWindow`. The flag is view state: not in `World`, not undoable, not saved, compact on launch.

| | Compact (default) | Detailed (`Ctrl+K`) |
|---|---|---|
| Menu bar | Shown, with Find | Shown, with Find |
| Left margin | Ruler + lettered tool tray | Tray widens: names, gestures, tool options |
| Right margin | Ruler only | Inspector: points (all nine slots, density, level 1–2), guides, camera (preset, rigs, FOV, distance), reference, palette, revisions |
| Title block | Readouts | Zoom and density editable |

Find (`Ctrl+F`, free today):

- A field at the right end of the menu bar. Typing lists matching commands under it as a ruled
  table: name, group, gesture. Arrow keys move, `Enter` runs, `Escape` closes and returns focus to
  the viewport. Matching is per word, case-insensitive, over name and group.
- It reads the command table from P1, so it can never list a command the menus or keys lack.

Revisions (detailed inspector, last five entries):

- The undo stack shown as an ISO 7200 revision table: number and command name, newest first.
  Needs each undo push to carry the command name (P1 gives every command a name).

Other rules:

- The inspector is ruled tables, not cards, sliders or switches. Every inspector edit runs the
  same command as its shortcut, so undo holds without new code paths.
- On any viewport resize the camera shifts by half the size change so the point at the viewport
  centre stays put. Today the camera is anchored top-left (`ViewportControl.CenterCamera`), so
  toggling panels would jump the plane. Verified in the mockup: centre world point identical
  across three toggles.

## 4. Work items

### P0 — Mockup and review — closed

- Direction approved; font B612 Mono, palette ink. The revised mockup (§2–3) gets a last look
  before P1.

### P1 — One command table

- A single list of commands (name, group, gesture, action) read by the key handler, the menu,
  the `F1` overlay, Find, the inspector and the revision table. Today the same list is written
  three times (`ViewportControl` key switch, `MainWindow.axaml`, `Renderer.DrawHelp`).
- `PushUndo` takes the command name.
- CHECK `commands_unique_gestures`, CHECK `help_matches_commands`,
  CHECK `find_matches_words` (pure filter: "grid sty" finds only "Cycle grid style").

### P2 — Compact frame

- Menu bar with Find, rulers, index tabs, centring marks, title block, notes, tool tray. Remove the
  HUD bars from `Renderer` (interactive mode only).
- Tab placement and off-screen pinning are a pure helper (`Vec2` in, edge position out).
- CHECK `vp_tab_onscreen`, CHECK `vp_tab_pinned_offscreen`, CHECK `ruler_ticks_follow_zoom`.
- CHECK `export_unchanged`: `gate-export.png` and `gate-shapes.png` hashes equal the pre-P2 hashes.
  Chrome must never reach an export. `gate.png` (interactive) changes by design.

### P3 — Detailed mode

- `Ctrl+K` toggle, wide tray, inspector bound to `World` through a `Changed` event, revision table.
- Camera anchor on resize (§3).
- CHECK `ctrl_k_toggles_mode`, CHECK `resize_keeps_centre` (world point at viewport centre before
  and after a 300 px width change differs by < 0.5 px), CHECK `inspector_edit_undoes`,
  CHECK `revisions_name_commands`.

### P4 — Palette-driven chrome, font, ink default

- Chrome brushes built from `Palette`; B612 Mono regular and bold embedded as `AvaloniaResource`
  with its `OFL.txt`.
- New documents start on ink (`World.PaletteName` default, and `Ctrl+R` reset). Files that name a
  palette keep it; `Palette.ByName`'s fallback for unknown names stays atelier so old files look
  the same. Check whether any self-test assertion assumes atelier on a fresh `World`.
- CHECK `chrome_contrast` for every text/background pair in all three palettes (≥ 4.5:1).
- CHECK `font_loaded`: the bundled face resolves, not a fallback.
- CHECK `default_palette_ink`.

### P5 — Review and screenshots

- Same-session review of the stage diff (rule 7 in `dev-docs/PLAN.md`), fixes, gate re-run.
- Screenshots: 3 palettes × 2 modes, Find open, and one 2-point scene with both VPs off-screen.
- Update `docs/SPEC.md` (input, rendering order, layout) and `README.md` (controls).

## 5. Answered questions

1. Command search: yes, as Find in the menu bar (§3).
2. Remember the mode across launches: no.
3. Menu bar in compact mode: yes, both modes.
