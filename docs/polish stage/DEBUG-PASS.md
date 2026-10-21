# Chitin — debug pass

Date: 2026-10-20. Baseline: `main` @ `9cd567d`. Scope: every file under `src/` (about 5,800 lines of C#).

Method: static reading only. This Windows host has no .NET SDK (`dotnet build` fails with
"sdk-not-found"), so nothing below was built or run. Each finding names the code path that causes it.
Severity: **High** = data loss or a wrong action the user can hit in normal use. **Medium** = wrong
behaviour on a common path, or a crash or hang caused by bad input. **Low** = an edge case, polish,
or code quality.

The work is divided into five stages. Each stage lists the finding IDs it closes; the findings
themselves are in §1–§5 below. Every ID is in exactly one stage, except G14, which is out of scope.
Do the stages in order: each one depends on the one before it. Every stage ends with
`dev-docs/verify.sh` passing.

---

## Stages

### Stage 1 — Data safety

Goal: no command loses or overwrites the user's work.

| ID | Item |
|---|---|
| B1 | Set `e.Handled` before the first `await` in `OnKeyDown`, so a Ctrl shortcut runs once. |
| B2 | Put the reference bitmap in the undo snapshot; dispose it only when no snapshot holds it. |
| B3 | Keep the file binding consistent with undo (or clear history on Open). |
| B4 | Ask before New and Open discard a dirty document. |
| G3 | Save through a temp file and rename. |

Done when: the self-test has a `CHECK` for each of B1–B4 (part of G13), and a key that opens a
dialog opens exactly one.

Status: **done** (`6be2ab2`, `c03a107`, `da738a8`, `ac62303`). New checks: `key_handled_before_await`,
`undo_reference_image`, `open_clears_history`, `confirm_discard`, `save_atomic`. Not tested by
hand in the running app on a desktop yet.

### Stage 2 — Input and validation

Goal: input does what the user expects, and bad files or images cannot hang or crash the app.

| ID | Item |
|---|---|
| B5 | Push undo on the first real move, not on press; leave pan out of undo. |
| B6 | Tools handle only the keys they use; modified keys pass through. |
| B7 | Clamp VP density in `Restore`, the Up key and `FanDensity`. |
| B8 | Check image size from the header before the full decode. |
| B9 | Ignore wheel events with `Delta.Y == 0`. |
| B10 | No message when Save Rig is cancelled. |
| B11 | Show underscores in rig names. |
| B13 | Guard `CenterCamera` against empty bounds; clamp zoom. |
| B14 | Reference dialog Cancel leaves the dirty flag as it was. |
| B15 | `H` and Up/Down check modifiers; message when trace lines do not cross. |
| G4 | Read and check `version` on load. |
| G13 | Self-test checks for B1–B7 (the Stage 1 checks plus B5, B6, B7). |

Done when: each Medium bug in this stage has a self-test `CHECK`.

Status: **done** (`49d269d` … `7926d4b`, one commit per item; B5+B9 and B10+B11 share one each).
New checks: `pointer_undo`, `tool_keys_pass`, `density_clamp`, `image_header_guard`,
`rig_cancel_silent`, `center_camera_empty`, `reference_cancel_clean`, `key_modifiers`,
`project_version`. B11 (underscores in the Camera menu) has no check; it needs the real menu.
Not tested by hand in the running app on a desktop yet.

### Stage 3 — Cleanup and performance

Goal: less code before the polish-stage UI work starts. No behaviour change, apart from S2 (what
Clear keeps) and P2.

| ID | Item |
|---|---|
| S1–S13 | All simplifications in §4. Do S3 first: plan item 4 (ink default) depends on it. |
| R5 | One layer of error handling for async commands. |
| P1 | Raise the text cache limit or cache the help layout. |
| P2 | Redraw on pointer move only when an overlay is active. |
| G10 | Show unhandled errors in the status line or a log file. |
| G12 | Make `alloc_render` relative, or a warning instead of a gate failure. |

Done when: `verify.sh` passes with the same `CHECK` list, and the line count of `src/` is lower.

Status: **done** (`2d3a2e5` … `093ef99`). `src/` went from 7,387 to 6,989 lines, including one new
check (`tool_pick_quad`, which covers the S8 helpers). `alloc_render` is now relative (G12).
Deviations:
- S4 keeps the 40-megapixel check in the paste path. Without it, a refused paste would leave an
  empty undo step.
- G10 shows errors in the status line but writes no log file.
- `verify.sh` also needed a fix: its `rg` call had no path, so it read stdin and hung when the gate
  ran from a non-interactive shell.

G10 has no check (it needs the desktop lifetime). Not tested by hand in the running app yet.

### Stage 4 — Structural refactor

Goal: give the polish-stage features (rulers, index tabs, title block, Find box) a base they do not
have to work around.

| ID | Item |
|---|---|
| R1 | Rename `Vec2.GetSizeSquared()` to `Length()`. |
| R2 | `Vec2` operators and `record struct` equality. |
| R3 | Key-command table shared by the keyboard, the menu and the Find box. |
| R4 | `Styles` object from `(palette, zoom)`; exports stop changing `World.Zoom`. |
| P3 | Covered by R4: keep pens that do not depend on zoom. |

Done when: `RenderPlane` and `SvgWriter` no longer save and restore the zoom, and `OnKeyDown` is a
table lookup.

Status: **done** (`ced6085` R1, `c46b4d7` R2, `0f823e4` R4, `2fd2863` R3). `src/` went from
6,989 to 6,811 lines, and the render hashes are unchanged through all four commits.
`copy_preserves_view` now asserts that an export leaves the live `Styles` object untouched.
Notes:
- **P3 not done.** A zoom step still builds a new `Styles` object (about 30 small allocations).
  Keeping the pens that do not depend on zoom would need a second object, which costs more than it
  saves.
- **R3 table entries have no names yet.** Add the names when the Find box is built.
- **Behaviour change in R3:** keys now need exact modifiers. Ctrl+Shift+N, Ctrl+Shift+O,
  Ctrl+Shift+E and Ctrl+Shift+Y no longer act, and neither do Ctrl or Alt with `+`, `-`, `[` or `]`.

Not tested by hand in the running app yet.

### Stage 5 — Feature gaps

Goal: close the gaps that change what the user sees. Do the menu items together with the
polish-stage menu bar and Find box (plan item 5), not before.

| ID | Item |
|---|---|
| G1 | Box face and edge visibility from the box's real orientation. |
| G2 | Refuse a self-crossing quad. |
| G5 | Menu shortcut text, Undo/Redo enabled state, keyboard-only commands in the menu. |
| G6 | Ctrl+N for New; move Resize to another shortcut. |
| G7 | Tool keys in the help overlay; make the overlay fit or scroll. |
| G8 | Delete and rename rigs. |
| G9 | File name in the title bar. |
| G11 | Darker ink palette VP colours for slots 7 and 8. |
| B12 | One aspect rule for PNG and SVG export. |

Out of scope: G14 (screen-space construction shapes), which needs its own design.

Status: **done** (`8a85ae7` … `79cfaa3`; G5+G6 share one commit). New checks: `box_visibility`,
`palette_contrast`, `export_aspect`, `menu_gestures`, `rig_manage`, `window_title`;
`tool_pick_quad` also covers G2. The gate passes with 64 checks.
Notes:
- **Render hash:** only the interactive PNG changed, because the help rows changed. The export and
  shapes renders are unchanged.
- **Menu timing:** G5 and G6 were done on the current Avalonia menu, not with the polish-stage menu
  bar. The menu items run entries of the key table, so the redesign can keep them as they are.
- **G6 shortcuts:** Resize moved to Ctrl+Alt+C (Photoshop's Canvas Size key).
- **Not checked:** the rig submenu and the Undo/Redo enabled state are not covered by a check.

Not tested by hand in the running app yet.

---

# Findings

## 1. Bugs

### B1 — High: Ctrl+S / O / E / N / I can run twice (two dialogs, two writes)

`ViewportControl.OnKeyDown` is `async void` (`src/Viewport/ViewportControl.cs:167`). It sets
`e.Handled = true` only at the end (`:451`), after it awaits the picker or dialog. The handler gives
control back at its first `await`, so the KeyDown event bubbles to `MainWindow.Window_KeyDown`
(`src/App/MainWindow.axaml.cs:115`) with `Handled == false`. For Ctrl+S, O, E, Z, Y, N, I and P, the
window then calls `Viewport.HandleGlobalKey(e)`, which runs `OnKeyDown` a second time.

- Ctrl+S with no file open: two Save pickers. Ctrl+S with a file open: two `OpenWriteAsync` calls
  write the same file at the same time, which can corrupt it or raise an IOException.
- Ctrl+O, Ctrl+E, Ctrl+N, Ctrl+I and Ctrl+Alt+I: two pickers or two modal dialogs.

Fix: decide `handled` before the first `await`. Set `e.Handled = true` at once for every key that
opens a dialog, then await. The simplest form: give the async commands their own method and call it
without awaiting it after the handled flag is set.

### B2 — High: the reference image is not in undo, so undo loses it or misplaces it

`World.Capture` / `Restore` (`src/Model/World.cs:144`, `:194`) do not include `BackgroundImg`, and
`SetReferenceImage` disposes the old bitmap (`World.cs:316`).

- `Alt+I` and **Reference > Remove** (`ViewportControl.cs:348`, `:867`) do not call `PushUndo`. The
  image is disposed and cannot come back.
- `Ctrl+R` (Clear), `Ctrl+N` (Resize) and **File > New** remove the image. Undo restores the points
  but not the image.
- After `Ctrl+I` / `Ctrl+V` (load or paste an image), undo restores the old document size but keeps
  the new image, stretched to the old plane.

Fix: keep the bitmap reference in `WorldState` (in memory only, not serialised). Do not dispose a
bitmap while an undo or redo snapshot still refers to it. Dispose it when the snapshot leaves the
50-step stack.

### B3 — Medium: Undo after Open, then Save, overwrites the opened file with the old document

`OpenProjectAsync` sets `_projectFile` to the new file and pushes the previous document onto the undo
stack (`ViewportControl.cs:984-988`). Undo restores the previous document but leaves `_projectFile`
unchanged. The next Ctrl+S writes the old document into the file the user just opened, with no
prompt. Fix: record the file binding in the snapshot, or clear the undo history on Open (most editors
do this).

### B4 — Medium: New and Open discard unsaved work without asking

Only window close asks for confirmation (`MainWindow.axaml.cs:32`). **File > New** and **Open** both
replace a dirty document. Undo can bring the document back, but not its file binding or its image
(see B2, B3). Fix: reuse `ConfirmDiscardAsync` before New and Open.

### B5 — Medium: pan and plain clicks add undo steps and clear redo

- Middle-drag pan calls `PushUndo` (`ViewportControl.cs:106`). Each pan adds an undo step and clears
  the redo stack. Undo also moves the view back, because `Zoom` and `Camera` are part of the snapshot.
- A left click on a vanishing point that does not drag (`:96-99`), or a right click that does not drag
  (`:111`), also adds an undo step and clears redo. Example: undo, then click a point to select it,
  and redo is gone.

Fix: push the snapshot on the first real move (`HasMoved`), not on press. Leave camera-only changes
out of undo, or restore the current view after an undo.

### B6 — Medium: construction tools swallow keys that have modifiers

`BoxTool.HandleKey` (`src/Viewport/ConstructionMode.cs:141`) and `SubdivisionTool.HandleKey` (`:492`)
do not check modifiers, and `OnKeyDown` asks the tool before it handles points.

- In the box tool, `1`, `2`, `Ctrl+1/2`, `Alt+1/2` and `Ctrl+Shift+1/2` do nothing, even at stage 0
  where nothing is being forced.
- In the subdivision tool, `Ctrl` / `Alt` / `Ctrl+Shift` with `2`–`9` and `+` / `-` (grid size) are
  used by the tool.
- `Alt+Backspace` (clear trace lines) in any tool runs the tool's Backspace action.

Fix: let a tool handle only unmodified keys (Shift where the tool uses it). Return `false` for
everything else.

### B7 — Medium: a project file can hang the app through VP density

`FanDensity` returns `point.Density` with no cap when it is not 0 (`src/Viewport/Prim.cs:82`). The
automatic value has a cap of 96, but a set value does not. `World.Restore` does not clamp vanishing
point density (`World.cs:200-207`), and only rigs reject negative values. A file with
`"density": 100000000` builds 200 million lines on load. The Up key also has no upper limit
(`ViewportControl.cs:266`). Fix: clamp density in `Restore`, in the Up key handler and in
`FanDensity` (for example 0 to 4 × 96).

### B8 — Medium: images are decoded before the 40-megapixel check

`new Bitmap(stream)` decodes the full image first (`ViewportControl.cs:1091`,
`src/IO/ProjectFile.cs:98`). The pixel limit is checked after that. A small, very compressible PNG
(for example 60,000 × 60,000) passes the 64 MB file check, then needs gigabytes during decode, which
can stop the process with an out-of-memory error. Fix: read the size from the header first (PNG IHDR,
or a decode with `Bitmap.DecodeToWidth`) and refuse the image before the full decode.

### B9 — Low: horizontal scroll zooms out

`OnPointerWheelChanged` uses `e.Delta.Y > 0 ? 1.1 : 1/1.1` (`ViewportControl.cs:153`). A horizontal
trackpad swipe has `Delta.Y == 0`, so it zooms out. Fix: return when `Delta.Y == 0`.

### B10 — Low: cancelling Save Rig shows "Rig name cannot be empty."

`PromptForNameAsync` returns `null` on Cancel, and `SaveRigAsync` treats that as an empty name
(`ViewportControl.cs:691`). Fix: return without a message when the result is `null`.

### B11 — Low: underscores disappear from rig names in the Camera menu

`new MenuItem { Header = rig.Name }` (`MainWindow.axaml.cs:222`). Avalonia reads `_` as an access-key
marker, so "front_left" shows as "frontleft". Fix: use a `TextBlock` as the header, or replace `_`
with `__`.

### B12 — Low: PNG and SVG export behave differently when the aspect ratio changes

The export dialog lets the user set width and height separately (`src/Dialogs/ExportDialog.axaml`).
`RenderPlane` scales by width only and centres the height (`ViewportControl.cs:1202-1205`), so a
different aspect ratio crops the plane or leaves an unfilled band. `SvgWriter` uses the plane as its
`viewBox` (`src/IO/SvgWriter.cs:30`), so the browser letterboxes it. Fix: lock the aspect ratio in
the dialog (one size field and a scale), or use the same fit in both exporters.

### B13 — Low: `CenterCamera` with zero bounds

`CenterCamera` (`ViewportControl.cs:46`) divides by `zoom`. That value is 0 when `Bounds` is empty
(for example Home while the window is minimised), which gives an infinite camera. The method also
does not apply the 0.05 minimum zoom, so an 8192 px document in a small window can open below the
minimum, and the first wheel step then jumps. Fix: return when the bounds are empty, and clamp zoom.

### B14 — Low: the Reference dialog marks the document dirty on Cancel

Each slider change calls `SetReferenceSettings`, which calls `BumpRevision()` and sets dirty. On
Cancel, the settings are restored but the dirty flag stays set, so the close prompt shows for a
document that did not change.

### B15 — Low: keys that ignore modifiers

`H` (`ViewportControl.cs:319`) and Up/Down density (`:262`, `!control` only) also run with Shift or
Alt held. `Ctrl+Shift+(1-9)` with trace lines that do not cross does nothing and shows no message.

---

## 2. Gaps

| # | Gap | Where |
|---|---|---|
| G1 | Box faces and hidden edges are fixed (`VisibleFaceIndices = {1,2,3}`; hidden = the edges at `p3`). A box drawn downward, or above the horizon, shows the wrong faces as solid and the wrong edges dashed. | `src/Geometry/BoxGeometry.cs:19`, `:68` |
| G2 | A four-click subdivision or ellipse quad can cross itself (a "Z" click order). The homography is still invertible, so the result is a bow-tie grid. `ContainsQuad` already has the sign test that can reject it. | `ConstructionMode.cs:294`, `:484` |
| G3 | Save writes to the target file directly (truncate, then write). An I/O failure partway through leaves a damaged project. Write to a temp file and rename where a local path is available. | `ViewportControl.cs:788` |
| G4 | `version` is written to the project file but never read. A future format change cannot be detected. | `ProjectFile.cs:31` |
| G5 | The menu shows no shortcut text, Undo and Redo are always enabled, and Resize, Copy, Paste, the three tools and grid size are keyboard-only. | `src/App/MainWindow.axaml` |
| G6 | Ctrl+N opens **Resize**, but **File > New** is a different command with no shortcut. Most apps use Ctrl+N for New. | `ViewportControl.cs:242`, `MainWindow.axaml:14` |
| G7 | The help overlay has no tool keys (box `1`/`2`/Backspace, subdivision `2`–`9`/Shift/Enter, ellipse) and no Ctrl+Shift+(1-9). It does not scroll, so in a window shorter than about 720 px its last rows go under the bottom bar. | `src/Viewport/Renderer.cs:231` |
| G8 | Saved rigs cannot be deleted or renamed. | `World.cs:583` |
| G9 | The title bar does not show the file name, only "Chitin *". | `MainWindow.axaml.cs:27` |
| G10 | `Dispatcher.UIThread.UnhandledException` writes to stderr and marks the error as handled. A Windows `WinExe` has no console, so the error is lost. Show it in the status line or write a log file. | `src/App/App.axaml.cs:17` |
| G11 | Ink palette (the planned default): VP slots 7 and 8 (`#B2B2B2`, `#C6C6C6`) on paper `#F7F7F4` have a contrast ratio below 2:1, so their guide lines are almost invisible. | `src/Model/Palette.cs:33` |
| G12 | The `alloc_render` gate compares against a fixed byte count measured on one Linux runtime (`BaselineAllocBytes = 207_086`). It can fail on Windows, macOS or a newer .NET with no change to the code. | `src/App/Program.cs:16`, `:419` |
| G13 | The self-test has no check for B1 to B7 (key double dispatch, image undo, file binding after undo, pan undo, tool key capture, density cap). | `Program.cs` |
| G14 | Known limitation already in the README: boxes, subdivisions and ellipses use screen-space points and do not follow a moved vanishing point. Listed here only so the polish stage keeps it in view. | `README.md` |

---

## 3. Code that is hard to read or maintain

- **R1 — `Vec2.GetSizeSquared()` returns the length** (`src/Geometry/Vec2.cs:15`). A comment says the
  name is "kept on purpose", but every caller has to remember this. Rename it to `Length()`. The port
  is already validated by the self-test; it does not need the old name.
- **R2 — `Vec2` has no operators.** The code builds a vector from component arithmetic (`new Vec2(a.X - b.X, a.Y - b.Y)`) about 40 times.
  Add `+`, `-`, `* double` and use `record struct` to get equality.
- **R3 — `OnKeyDown` is a 300-line `if`/`switch`** with a `documentChanged` flag that each branch sets by
  hand (`ViewportControl.cs:167-463`), and it has the wrong indentation inside `try`. Most branches
  repeat `PushUndo(); <change>;`. A table from key + modifiers to a command (sync or async), with an
  undo flag, removes the flag and B1 together. The menu and the future **Find** box can use the same
  table.
- **R4 — The `World` class mixes four jobs**: document data, view (zoom, camera, selection, overlay),
  about 35 pen and brush fields, and the status message. `RenderPlane` and `SvgWriter` change
  `World.Zoom`, call `ZoomChanged()` to rebuild every pen, then restore it (`ViewportControl.cs:1200`,
  `SvgWriter.cs:93`). A `Styles` object built from `(palette, zoom)` and passed to
  `GuideBuilder.Build` removes that save-and-restore code and lets an export run without a change to
  the live view.
- **R5 — Error handling is repeated.** `MainWindow` has eight `async void` handlers with the same
  try/catch (`MainWindow.axaml.cs:179-325`). Each one calls a `ViewportControl` method that already
  has its own try/catch, so the outer catch never runs. Keep one layer.

---

## 4. Simplification and deletion

| # | Change | Saves |
|---|---|---|
| S1 | Delete `WorldState.ContentEquals` and the five per-type `ContentEquals` (about 200 lines, `src/Model/WorldState.cs`). Only the self-test uses them, except `ReferenceSettings.ContentEquals`. Compare `ProjectFile.Write` output strings in the test, or use records. | ~190 lines |
| S2 | `NewDocument`, `ClearDocument` and private `ResetDocument` (`ViewportControl.cs:541-586`) are three versions of a reset that keep different things (Clear keeps rigs, grid style, palette and cone; New resets them). `NewDocument` calls `BumpRevision` about eight times. Use `World.Restore(new WorldState())` plus `SetReferenceImage(null)` for New, and decide what Clear keeps. | ~35 lines |
| S3 | `"atelier"` as the default appears in five places (`World.cs:77`, `:271`, `:339`, `WorldState.cs:296`, `ViewportControl.cs:579`, and the `Palette.ByName` fallback). Make one `Palette.Default`. Plan item 4 (ink default) then changes one line. | — |
| S4 | The 40-megapixel check is in `World.SetReferenceImage` and also in both callers (`ViewportControl.cs:1092`, `:1136`). The 64 MB check is done twice in `LoadBackgroundAsync` (`:1076`, `:1084`). Keep one of each. | ~20 lines |
| S5 | Delete `LoadReferenceAsync` (a one-line wrapper, `:873`) and `ProcessKey` / `HandleGlobalKey` duplication (`:1002-1011`). | ~12 lines |
| S6 | Delete `World.HideBackground`, which only renames `Reference.Hidden` (`World.cs:72`). | ~5 lines |
| S7 | `World.CurrentStatus` checks the 5 s time limit, and `SetStatus` also starts a timer that clears the message after 5 s (`World.cs:99`, `:284`). Keep the timer only; drop `StatusMessageAt`. | ~8 lines |
| S8 | `EllipseTool` and `SubdivisionTool` repeat the same four-corner pick, corner-marker drawing and `Point(Vec2)` helper (`ConstructionMode.cs:283-301`, `:392-408`, `:478-489`, `:622-637`). `BoxTool` has a third `Point` helper. Move these into `ConstructionMode`. | ~45 lines |
| S9 | Dead code: `ToolKind.None` and the `_ => null` / "not available yet" branch in `ToggleTool` (`ViewportControl.cs:526-532`); the `_edge && _edgeNear == null` branch in `SubdivisionTool.Begin` (`ConstructionMode.cs:459`), because `_edgeNear` is always set together with `_edge`; the second `_forced` check in `BoxTool.ChooseAxis` (`:228`); the unused array `points` allocated before the early return in `PerspectiveEdge.Divide` (`Homography.cs:160`). | ~15 lines |
| S10 | `CameraMenu_Opened` searches for the separator in a loop (`MainWindow.axaml.cs:199-207`). Use `menu.Items.IndexOf(RigSeparator)`. | ~8 lines |
| S11 | `CopyPlaneAsync` → `RenderPlane` encodes to PNG and decodes again only to get a `Bitmap` (`ViewportControl.cs:1217-1229`). `RenderTargetBitmap` is already a `Bitmap`; return it (and the grayscale `WriteableBitmap`) directly. | one encode and one decode per copy |
| S12 | Move the 900-line self-test out of `Program.cs` into `src/App/SelfTest.cs`. `Program.cs` then has only the entry point. | readability |
| S13 | `ConfirmDiscardAsync` and `PromptForNameAsync` build small dialogs in code, while the other dialogs use XAML. One `MessageDialog` helper can serve both. | ~40 lines |

---

## 5. Performance

- **P1 — Text cache thrash while help is shown.** `Renderer.Format` clears its cache when it holds
  64 entries (`Renderer.cs:322`). The help overlay alone needs about 120 strings, so the cache is
  cleared and every `FormattedText` is rebuilt on each frame. Each mouse move causes a frame. Raise
  the limit to about 256, or keep the help text layout in its own cache.
- **P2 — Every pointer move redraws and refocuses.** `OnPointerMoved` calls `Focus()` and
  `InvalidateVisual()` on each move, also when there is no overlay and nothing changes on screen
  (`ViewportControl.cs:128`, `:133`). Redraw only when `World.Overlay != null`. Focus on press
  (already done) or on pointer enter.
- **P3 — Each zoom step rebuilds every pen and brush** (`World.ZoomChanged`, about 30 allocations),
  clears the text cache, and the next frame rebuilds all guide primitives. This is acceptable now.
  The `Styles` split in §3 lets pens that do not depend on zoom stay in memory.

