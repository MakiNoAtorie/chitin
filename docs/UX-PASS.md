# Chitin — UX pass

Date: 2026-10-23. Baseline: `main` @ `db28f2f`. Goal stated by Maki: *simple, fluid, good user experience*; some features do not work well, and the app feels bloated.

Method: full read of `src/` (every UI, input and model file), the headless window renders in `.verify/screens/`, `docs/SPEC.md` and the polish-stage plan. `dotnet build -c Release` is clean and `--selftest` passes on this host. The window was **not** driven by hand here (no desktop automation in this session), so every behavioural finding names the code path that produces it. Findings marked **(verify)** are worth a two-minute check in the running app before acting.

Severity: **A** breaks or blocks a normal workflow · **B** friction on a common path · **C** bloat: UI or code that costs attention without paying for it.

---

## 0. What the app is for

The core loop is short: drop two or three vanishing points, nudge them until the horizon and angles feel right, pull a trace line or two, build a few boxes, copy or export the plane. Everything in the window should make that loop faster or get out of the way. Most of the findings below are places where the chrome, the mode system or an inherited Carapace convention interrupts that loop.

---

## 1. Fluidity: things that don't work well

### A1. Pan needs a middle button; zoom needs a wheel
`ViewportControl.cs:129` pans only on middle-drag; `ViewportControl.cs:190` turns every wheel tick into zoom and ignores horizontal delta. On a laptop trackpad (two-finger scroll, no middle button) there is no way to pan at all, and two-finger scroll zooms instead of panning. This is the single biggest "doesn't feel fluid" item.
- Fix: `Space`+left-drag (or `Alt`+drag) pans; plain wheel/trackpad scroll pans, `Ctrl`+wheel zooms (pinch arrives as Ctrl+wheel on Windows/macOS). Keep middle-drag.

### A2. Boxes, subdivisions and ellipses do not follow the vanishing points
`BoxState` stores screen-space corners (`WorldState.cs:75-95`). Move VP1 after building a box and the box is simply wrong; the README lists it as a known limitation, but for the user it reads as "the box tool is broken" because the natural workflow is *rough in VPs → build a box → adjust the horizon*. Everything else in the scene (fans, horizon, cone) re-projects live, so the box stands out.
- Fix: store the box as `p0` + lengths along the three VP directions and rebuild at draw time; or, cheaper, when a VP moves, re-solve `p1/p2/p3/h` by intersecting the original rays with the new VP directions. If neither is affordable, at least show the boxes dashed/muted while a VP is being dragged so it is obvious they are stale.

### A3. Reference image: no direct manipulation, and loading one resizes the document
Moving or scaling the reference means opening `Ctrl+Alt+I` and typing offsets into `NumericUpDown` fields (`ReferenceDialog.axaml:26-30`). Loading or pasting an image silently resets the document size to the image size and recentres (`ViewportControl.cs:1218`, `:1258`); resizing the document silently removes the image (`:1153`). Both are surprising, both are inherited from Carapace, neither is necessary.
- Fix: with the reference shown, `Alt`+drag moves it, `Alt`+wheel scales it; `[`/`]` keep opacity. Load/paste should fit the image to the current plane (scale = min(W/w, H/h)) instead of changing the document; Resize should keep the image. The dialog then only needs flip H/V, which can be two menu items.

### A4. "Unsaved changes" offers Discard / Cancel but no Save
`ViewportControl.cs:1120`. The dialog people expect is *Save · Don't save · Cancel*. Today the user has to Cancel, remember Ctrl+S, then close again.

### A5. Errors vanish after five seconds
Every failure (save, export, open, image refused, command failed) goes through `World.SetStatus`, which clears itself after 5 s (`World.cs:259`) and shows as `NOTE 2` in the foot, far from where the user is looking. A failed save that the user did not notice is data loss.
- Fix: distinguish *prompts* (auto-clear) from *errors* (stay until the next command, and show them where the action happened: a line under the menu bar or a small modal for save/export failures).

### B1. No hover feedback on vanishing points, and the lowest index always wins
The cursor is always a cross (`ViewportControl.cs:44`); nothing highlights a VP under the pointer, so the 32 px grab radius is invisible until you press. When two points overlap (VP1 on VP3, common after a preset), the loop at `:118` always picks the lower index, so the upper one cannot be grabbed by mouse.
- Fix: track the hovered VP in `OnPointerMoved` and draw its ring in the prompt colour; pick the *nearest* VP, not the first; switch to a hand/move cursor while hovering.

### B2. Dropping points is keyboard-only and invisible to a new user
A new document is an empty white plane. The only way to place a point is to hold the mouse somewhere and press `1`–`9`; the hint for that lives in 12 px text in the bottom-left foot. The `P` tray cell looks like a tool but only runs Escape. **(verify)** First-run friction is real: nothing on the plane says what to do.
- Fix: make `P` a real *Points* tool: click drops the next free point (1, 2, 3 …), `1`–`9` still drop a specific index. Show a short empty-state hint centred on the plane when there are no points ("Click to place VP 1, or Camera › 2-point"). Consider starting new documents on the 2-point preset.

### B3. Keys with silent no-ops
`Up`/`Down` (density), `H` (level), `Ctrl+arrows` (flip) do nothing without a selected point and say nothing (`ViewportControl.cs:454`, `:509`). The user presses a key, nothing happens, and has no idea why.
- Fix: one status prompt, "Select a point first (Ctrl+1–9 or click one)". Or make `Up`/`Down` act on the point under the cursor when none is selected.

### B4. Subdivision needs `Enter`; a box tool click produces a scolding
Subdivision shows a live 2×2 preview the moment a face is picked but does not exist until `Enter` (`ConstructionMode.cs`, `SubdivisionTool.HandleKey`). Clicking (not dragging) in the box tool prints "Box cancelled: drag to set the first axis." (`ConstructionMode.cs:149`), which punishes a harmless click.
- Fix: commit the subdivision on face pick with the current N×M; digits/`+`/`-` then edit the *last* subdivision. Drop the box-cancel message (a click that does nothing needs no note).

### B5. `S` recolours the whole drawing
`S` rotates the palette assignment (`ViewportControl.cs:329`), is an undoable edit, and marks the document dirty. It sits next to `D`/`E` on the keyboard and one key away from the brain's "Ctrl+S". Palette, palette offset, grid and cone toggles are all document state that trip the "unsaved changes" dialog on close (`:629`, `:637`).
- Fix: move colour rotation to the menu only; treat palette, grid and cone as *view* state (persist them in the file, but do not dirty or push undo for them). Undo should be for geometry.

### B6. Shortcut collisions with user expectation
`Ctrl+C` copies a bitmap of the plane while `Ctrl+V` pastes a *reference image* (`:309`, `:360`): a user who presses Ctrl+C/Ctrl+V expecting a round-trip gets their own drawing as a background. `Ctrl+Alt+C` resizes the document (`:312`), `Ctrl+R` wipes it. These are Carapace legacies and the port is not obliged to keep them.
- Fix: `Ctrl+Shift+C` export-to-clipboard stays; `Ctrl+V` only pastes when the clipboard holds an image *and* asks nothing else; resize goes on `Ctrl+Shift+R`; clear stays in the menu with confirmation.

### B7. Compact ↔ detailed is a mode the user has to learn
`Ctrl+K` doubles the UI: the tray grows from 40 to 184 px, an inspector slides in, the zoom readout becomes a text box, density gains steppers, tool cells gain option text (`MainWindow.Inspector.cs:21-30`, `MainWindow.Sheet.cs:242-300`). Every launch resets to compact, so the detailed layout is rediscovered every session. The inspector itself is read-mostly: VP positions cannot be typed in, only displayed.
- Fix: one layout. Keep the compact sheet; replace the inspector with one small, always-available *Points* table (number, x, y editable, density) that can be hidden with `Ctrl+K`. Delete the rest of detailed mode (see §2).

### B8. Find lists commands it cannot run
From Find or a menu, an indexed command (drop/select/delete point) only prints "press 1-9 in the viewport" (`ViewportControl.cs:389`). The `Points` group is deliberately kept out of the menus (`:344`), so the only place a new user can learn those keys is `F1`.
- Fix: a `Points` menu with the three digit commands written out, or drop them from Find.

### B9. Ruler clutter at the pinned tabs **(verify)**
In every headless render the pinned tab for an off-screen VP draws its distance (`492`, `866`) over the first ruler labels (`SheetControls.cs:258` onward, `Sheet.Spread`). It reads as noise on a 20 px strip.
- Fix: drop the distance; keep arrow + number. Or drop index tabs entirely (see C3).

### B10. The foot note gets cut off in detailed mode
`NOTE 1` truncates to "…drops a point at the cursor…" when the title block grows (detailed screenshot). The tool hint is the one piece of text the user needs while working.

---

## 2. Bloat: what to cut

Code under `src/` is about 7,300 lines excluding the self-test; roughly a third of it is chrome and mode machinery that duplicates something else on screen. Each item names what the user already has instead.

| # | Cut | Where | Why it's duplicate |
|---|---|---|---|
| C1 | **Detailed mode** (inspector, width transitions, zoom text box, density steppers, tray option text) | `MainWindow.Inspector.cs` (264 lines), `MainWindow.Sheet.cs:242-300`, `MainWindow.axaml:117-128` | Every inspector row runs a command the menu and keyboard already run; positions are read-only; `Revisions` repeats the Edit menu's undo names. Keep only an editable points table (B7). |
| C2 | **Title block** (8 ISO 7200 cells) | `MainWindow.Sheet.cs:200-240` | `SHEET` duplicates the window title; `TOOL` duplicates the inverted tray cell; `PALETTE` is visible by looking at the window; `SIZE` belongs in the resize dialog. Keep zoom, selected point + coords, cone readout, in one line. |
| C3 | **Ruler index tabs** with spread/pin/distance logic | `SheetControls.cs:258-330`, `Sheet.cs:36-80` (~130 lines) | The VP is already a numbered circle on the plane with pointer lines when off-plane; the rulers themselves are fine. |
| C4 | **Centring marks** (four orange ticks at viewport edges) | `Renderer.cs:182-190` | They mark "the point the camera keeps when panels resize" — a concept the user never thinks about. |
| C5 | **Bubble** and **Hatch** controls | `SheetControls.cs:117-190` | Decorative detail callouts and section hatching for empty cells. Empty is fine as empty. |
| C6 | **Three places to discover commands** (menus, Find box pinned in the menu bar, F1 overlay) | `MainWindow.axaml:66-93`, `MainWindow.Sheet.cs:431-520`, `Renderer.cs:192-250`, `Commands.cs:55-95` | Keep the menus (every item shows its key) and a Find palette that opens on `Ctrl+F` *as a popup*, not a permanent text box with `FIND`/`CTRL F`/`CTRL K` labels eating the menu bar. Drop the F1 overlay or reduce it to a one-page cheat sheet in Help. |
| C7 | **Rigs** (save / recall / rename / delete, dynamic Camera submenus) | `ViewportControl.cs:684-790`, `MainWindow.axaml.cs:140-175`, `WorldState.RigState` | Niche; the three presets do the job. If kept, one "Save camera as preset…" with a flat list is enough. |
| C8 | **Palette rotation** (`S`) and three per-palette menu items + cycle | `ViewportControl.cs:323-329` | Two palettes (light, dark) picked from one menu item cover it. |
| C9 | **Flip point H/V** (`Ctrl+arrows`) | `ViewportControl.cs:351-356` | Rarely useful, invisible, four key bindings. |
| C10 | **Grid style cycle** (both / vertical / horizontal) | `ViewportControl.cs:317` | A grid is on or off. |
| C11 | **Copy plane in grayscale** | `ViewportControl.cs:310`, `Grayscale()` (~30 lines) | Ink palette already exports near-grey; any paint app desaturates. |
| C12 | **Edge-mode subdivision** (`Shift`+first click, `1`/`2` force VP) | `SubdivisionTool` (~60 lines) | Hard to discover, hard to explain in one hint line; face subdivision covers the common case. |
| C13 | **`ReferenceDialog`** (sliders, numeric offsets, Fit, Reset) | `Dialogs/ReferenceDialog.*` (135 lines) | Replaced by A3 direct manipulation plus two flip menu items. |
| C14 | **`ResizeDialog`** text validation | `Dialogs/ResizeDialog.*` | Two `NumericUpDown` with min/max do the validation natively; the explanatory text and red error line go. |
| C15 | Window-level **2 px ink frame** and 2 px foot rule | `MainWindow.axaml:61`, `:130` | The OS already frames the window; the double border is heavy on dark palettes. |

Not bloat, keep: the command table (`Commands.cs`), the `Prim`/`GuideBuilder` pipeline, undo with named steps, atomic save, cone of vision, camera presets, the rulers themselves, B612 Mono, the three construction tools' core gestures.

---

## 3. Target: the simple version

One layout, no modes:

```
┌──────────────────────────────────────────────────────────────┐
│ File  Edit  View  Points  Tools  Reference  Camera  Help      │
├────┬──────────────────────────────────────────────────┬──────┤
│ P  │ ruler                                            │ VP   │
│ T  ├──────────────────────────────────────────────────┤ 1 x y│
│ B  │                                                  │ 2 x y│
│ D  │ r                 plane                          │ 3 x y│
│ E  │ u                                                │ …    │
│    │ l                                                │(Ctrl │
│    │ e                                                │ K    │
│    │ r                                                │hides)│
├────┴──────────────────────────────────────────────────┴──────┤
│ hint for the active tool · prompt/error     zoom · VP 1 x,y  │
└──────────────────────────────────────────────────────────────┘
```

- **Tray**: `P` is a real tool (click places the next point); `T` is a tool too (left-drag pulls a trace line, so right-drag is no longer required knowledge). `B D E` as today. Tooltip on hover with name + key.
- **Pointer**: left = tool action or drag a VP (hovered VP highlighted, nearest wins); `Space`+drag or middle = pan; wheel = pan, `Ctrl`+wheel = zoom; `Alt`+drag/wheel = move/scale reference.
- **Foot**: one line, left = tool hint or the last prompt (errors persist), right = zoom and selected point. No title block.
- **Points table** (right, `Ctrl+K` hides): 9 rows, x/y editable, density stepper. The one thing detailed mode had that compact needs.
- **Find**: `Ctrl+F` opens a popup palette over the plane; nothing in the menu bar.
- **Document state** = geometry, reference, size. **View state** = zoom, pan, palette, grid, cone, panel visibility (saved in the file, never dirtying, never in undo).

---

## 4. Suggested order

1. **A1 + B1** (pan/zoom/hover): small, isolated to `ViewportControl`, biggest felt improvement.
2. **A4 + A5** (save prompt, persistent errors): data safety.
3. **C1–C6** cuts and the one-layout rebuild of `MainWindow` (§3). Net removal of ~900 lines; the self-test loses the `ctrl_k_*`, `inspector_*`, `vp_tab_*` and `zoom_field_*` checks.
4. **B2 + B4 + B5 + B6** (points tool, tool key cleanups, shortcut sanity): touches the command table only.
5. **A3 + C13** (reference by direct manipulation).
6. **A2** (boxes follow VPs): the one item that needs real geometry work; schedule it last but do not skip it.

Each step leaves the app buildable and the self-test green; steps 1, 2 and 4 are a day or less each.
