# Chitin — feature plan (everything except the bigger bets)

Scope fixed by Maki: foundation, construction, and identity work. **Excluded:** reference-image
homography warp, CLI batch/headless project rendering.

Baseline: `master` @ `7ff7038`. Build with `dotnet build -c Release`; prove with
`dotnet run -c Release -- --selftest .verify/selftest.png`; smoke-test the real window under
`Xvfb :99` and screenshot with `ffmpeg -f x11grab`.

## Working rules for every item

1. `SPEC.md` still describes the original's behaviour; this plan adds to it. Update `SPEC.md`'s
   behaviour sections as each item lands — the repo must stay readable without git archaeology.
2. One commit per item, message names the item and what was verified (`ITEM 3: reference opacity/flip
   — build clean, CHECK reference_opacity PASS, screenshot`).
3. Every item extends the self-test with named assertions printed as `CHECK <name> PASS|FAIL`
   before the final `SELFTEST_OK`. A failing check must exit non-zero. No item is "done" on a
   screenshot alone where a number can be asserted.
4. **Undo is cross-cutting**: every mutating command (key, mouse drag commit, dialog OK, menu action)
   pushes a snapshot *before* mutating. New features must not bypass it.
5. All geometry that decides pixels lives in pure static helpers (`Vec2` in, `Vec2` out) so the
   self-test can assert exact values without a screen.
6. Ponytail is active: no new packages, no MVVM, no service layer. Dialog controls come from
   `Avalonia.Themes.Fluent`, already referenced.
7. **One session per wave, review included.** A wave is one OpenCode session: implement every item,
   then — in that same session, before reporting the wave closed — review the wave's own diff for
   bugs, gaps and plan mismatches, fix what the review finds, rebuild, re-run the gate, and only then
   report. A review opened as a separate session is a process error; the reviewer has no memory of
   what was deliberately chosen.

---

# Wave 0 — defects, gaps, performance and housekeeping (gate for everything else)

Source: my read of every file plus a `/ponytail-audit` pass, both at `63f8ab6`. Each item lists what
was observed, the fix, and the check that proves it. Nothing here is speculative: the bugs were read
from the code or reproduced from the CLI.

## W0-1 `async void` handlers can kill the process (bug)

`ViewportControl.OnKeyDown`, `MainWindow.About_Click` and the resize path are `async void`. Any
exception thrown after an `await` — a failing clipboard write, a dialog that closes under the
handler — escapes to the dispatcher and terminates the app with no message. The suite has no global
handler either.

Fix: wrap each handler body in `try/catch`, route the message to a HUD status line (new
`World.StatusMessage` + timestamp, cleared after 5 s), and install
`Dispatcher.UIThread.UnhandledException` in `App` that logs to stderr and keeps the process alive.
**Check:** `CHECK no_unguarded_async_void` (source scan in the verify script: every `async void`
must be followed by a `try` within three lines) plus a manual forced-throw test under Xvfb.

## W0-2 Bitmap lifetime, load failures and image bombs (bug + breach)

- Loading or pasting a reference image assigns `World.BackgroundImg` without disposing the previous
  `Bitmap` → one leaked native image per load. `Alt+I`, `Ctrl+R` and `ResetDocument` leak the same way.
- `LoadBackgroundAsync` swallows every failure (`catch { BackgroundImg = null; }`) and silently drops
  the user's image.
- Nothing limits the decoded size: a 200 MP PNG or a 64-bit "pixel bomb" decodes straight into RAM.

Fix: one `World.SetReferenceImage(Bitmap?)` that disposes the previous instance and is used by every
path; failures keep the old image and set `StatusMessage`; reject encoded files > 64 MB before
decode and decoded images > 40 MP after decode (reject = dispose + message).
**Check:** `CHECK bitmap_dispose` (a counter on the world: 100 replacements → 99 disposals) and
`CHECK image_size_guard` (an 8000×6000 synthetic bitmap is refused with a message, the previous image
survives).

## W0-3 Unbounded document and grid sizes (bug)

- `ResetDocument` accepts any input; `Ctrl+N` → 100000×100000 then `Ctrl+C` allocates a render target
  that OOMs the app.
- `GridSz *= 2` overflows after **26 presses** (2⁵ = 32, overflow at 2³¹) into a negative value; the
  grid then silently disappears until enough `-` presses recover it.

Fix: `ResetDocument` clamps to `[1, 8192]` per axis, one `SetGridSize` clamps to `[2, 4096]`, and the
resize dialog validates against the same bounds before closing.
**Check:** `CHECK doc_clamp` (0→1, 99999→8192), `CHECK grid_clamp` (30 doublings → 4096, 30 halvings → 2).

## W0-4 CLI contract is broken (gap)

Reproduced: `Chitin --selftest` with no path falls through to the GUI, which dies with
`System.Exception: XOpenDisplay failed` and exit code 134. `Main` also ignores the exit code from
`StartWithClassicDesktopLifetime`, and the self-test never verifies that the files it "wrote" exist.

Fix: `--selftest` without a path prints usage to stderr and exits 2; `--help` prints usage to
stdout and exits 0; unknown arguments print usage to stderr and exit 2; `Main` returns the
lifetime's exit code; the self-test asserts both files exist and are larger than 1 KB before
printing `SELFTEST_OK`.
**Check:** shell gate — four invocations with expected exit codes 2/2/0/0, the help text and the
`SELFTEST_OK` line.

## W0-5 Geometry edge cases (bug)

- `Geom.GetLineIntersection` returns `true` for collinear lines and reports the midpoint of the
  *extended* first segment. The HUD only uses the boolean, but item 5 (drop a VP at the intersection)
  uses the point: two collinear trace lines would drop a point at a meaningless coordinate. The
  original has the same bug.
- A horizon with `VP0 == VP1` normalises a zero vector and draws a zero-length dashed line
  (harmless on Skia today, fragile).
- `Renderer.DrawHud` dereferences `VanishingPoints[SelectionIdx]!` — safe today, but an
  `is { } point` guard costs nothing.

Fix: collinear → `false` (documented as a deliberate deviation in SPEC), degenerate horizon → skip,
null-guard the HUD.
**Check:** `CHECK geom_collinear`, `CHECK geom_parallel`, `CHECK geom_cross` (known point ±1e-6),
`CHECK horizon_degenerate`.

## W0-6 The copy/export path is not exception-safe (bug)

`CopyPlaneAsync` swaps zoom and camera to the export configuration, renders, and restores them after
the `using` block. Any exception in render or clipboard leaves the viewport at zoom 1 / wrong camera,
and the `RenderTargetBitmap` is handed to the clipboard and then disposed.

Fix: extract `Bitmap RenderPlane()`; restore zoom/camera in `finally`; give the clipboard a bitmap it
owns (encode to a stream and build a detached `Bitmap` from it).
**Check:** `CHECK copy_preserves_view` (zoom, camera and pen widths identical before/after; returned
bitmap has the document's pixel size).

## W0-7 Keyboard focus dies after using a menu (UX bug)

Any menu interaction moves focus off the viewport; `1`–`9`, `G`, `F1` and the rest stop responding
until the user clicks the canvas again. Reproduced during the Xvfb smoke test: after Help > About,
keys were ignored.

Fix: refocus the viewport when a menu closes, and handle the global accelerators (`Ctrl+S/O/E/Z`,
`Ctrl+N`) at window level so they work regardless of focus.
**Check:** Xvfb script — open the menu, close it, press `1`, assert the HUD shows a selection.

## W0-8 Dialog polish (gap)

Resize: invalid input makes OK do nothing with no explanation; bounds are undocumented. About: nothing
closes it with Escape; the two link buttons use three C# handlers where `HyperlinkButton NavigateUri`
does the same with none (ponytail audit #1).

Fix: inline validation message + disabled OK until valid; `IsCancel` on the OK button of About;
`HyperlinkButton` with `NavigateUri`; `AutomationProperties.Name` on the viewport and the dialog
fields.
**Check:** manual screenshots (invalid input state, About closed with Escape).

## W0-9 `Vec2` is a heap class in the hottest loop (performance + practice)

Every fan line allocates two `Vec2` objects and every call to `Dot`/`DistFrom` allocates another; a
default-density VP at `|Pos| = 1500` emits 92 lines per frame, and automatic density has no ceiling —
`|Pos| = 100000` emits 6250 lines per frame, i.e. >12 000 allocations. `Normalize()` also mutates the
receiver, so `new Vec2(...).Normalize()` hides a side effect in an expression.

Fix: `readonly struct Vec2` with `Normalized()` returning a new value; audit every mutation site;
delete the unused `GetSize()` and collapse the two constructors into `Vec2(double x = 0, double y = 0)`
(audit #4, #6).
**Check:** identical PNG hashes, plus `CHECK alloc_render` printing allocated bytes per 1400×900
frame before and after — the after must be at least 50 % lower.

## W0-10 Geometry is rebuilt from scratch every frame (performance)

`Renderer.Draw` re-derives every line, grid row and fan on each `InvalidateVisual` — including on
every pointer move during a drag. Pairs with item 2's `GuideBuilder`.

Fix: build an immutable `Prim[]` keyed by `(World.Revision, Zoom)`; the renderer and the SVG writer
consume the same list; any document mutation bumps `Revision`.
**Check:** `CHECK guide_cache` (two draws without mutation return the same array instance; one
mutation returns a new one) and `CHECK alloc_render` as above.

## W0-11 Text layout is rebuilt per frame (performance)

Every HUD line and every VP number builds a `FormattedText` (a full layout pass) each frame — 10+
layouts per frame at rest.

Fix: cache `FormattedText` by `(text, size, brush)` in a bounded dictionary (64 entries, cleared when
the palette or zoom changes), reusing the same instance across frames.
**Check:** covered by `CHECK alloc_render`.

## W0-12 Automatic fan density has no ceiling (performance)

`density = max((int)(|Pos|/32), 4)` is unbounded; a point dragged far away, or a very tall document,
turns one drag into thousands of lines per frame. Manual density stays free — this only caps the
automatic mode.

Fix: cap automatic density at 96 and skip a VP's fan entirely when its bounding box does not intersect
the plane. Document both as deviations from the reference.
**Check:** `CHECK fan_cap` (|Pos| = 1e6 → density 96, 192 lines), `CHECK fan_cull` (a VP 1e6 away
emits no lines).

## W0-13 Ponytail-audit refactors (accepted subset)

- Merge `VPVoidLines` into `VPOutline` — identical colours and widths, one array (audit #8).
- Merge the two plain `Key.Up` / `Key.Down` density branches into one case with a signed delta (audit #3).
- Drop the direct `Avalonia` `PackageReference`; it is transitive through `Avalonia.Desktop` (audit #9).
- Keep one mode reference: `World.Overlay`, delete `ViewportControl._mouseMode` (audit #5).

**Rejected, with reasons:**

- Audit #2 (replace the 360-entry direction table with `Atan2`): the table reproduces the reference's
  first-maximum tie-break. Accepted only if an equivalence test over all 360 directions and both step
  sizes passes — otherwise keep the table. Do not change the snap behaviour for a line count.
- Audit #7 (delete the parameterless `ResizeDialog` constructor): **false positive** — the XAML
  `x:Class` generation requires it; removing it fails the build with `AVLN3001` (reproduced in the
  previous pass, see `dev-docs/REVIEW-1.md`).

**Check:** build clean, PNG hashes unchanged, `git diff --stat` net delta reported in the commit.

## W0-14 Truth in the repository (breach)

- `SPEC.md` §2 claims no packages beyond the four listed — `Avalonia.Skia` is referenced (needed by the
  headless render).
- `SPEC.md` §11 specifies camera `(0, 0)` for the self-test; the implementation centres the camera,
  which is the only way the scene is visible. Fix the spec, not the code.
- `SPEC.md` §8's note about `GetSize()` must go if W0-9 deletes it.
- Add a **Deviations** section to `SPEC.md` listing every deliberate difference from the original:
  collinear intersection, fan cap and culling, size clamps, `S` semantics (wave 3), palettes (wave 3).
- Add `NOTICE.md`: this is a derivative of Epic's freeware Carapace V1.0, built by decompiling
  `Carapace.exe` (SHA-256 `10dfe06d…e4fcb`) with `ilspycmd`; the icon comes from the same binary;
  personal use, not for redistribution.
- `README.md`: note that running `bin/Release/net8.0/Chitin` directly needs `DOTNET_ROOT` on this
  host, or use `dotnet run`.

**Check:** `rg -n "Avalonia.Skia|0, 0|GetSize\(" SPEC.md` reviewed by hand; `NOTICE.md` present.

# Wave 1 — foundation

## ITEM 1 — Undo/redo + project save/load

**Why:** today `Ctrl+R` or a mis-drag is unrecoverable and closing the window loses everything.

**State model.** Introduce `WorldState` — the complete mutable document: picture-plane size, zoom,
camera, 9 vanishing points (`Pos`, `Density`), trace lines, grid (show/style/size), boxes (item 4),
subdivisions (item 5), ellipses (item 7), reference-image settings (item 3) and the palette name.
`World.Capture()` deep-copies into a `WorldState`; `World.Restore(state)` applies it and rebuilds
zoom-dependent pens. The background bitmap is *not* part of the snapshot (it is an asset, not a
document value) — it is part of the saved file (below) and survives undo.

**Undo stack.** `UndoStack` with `Push(WorldState)` / `Undo()` / `Redo()`, depth 50, redo cleared on
push. Wire `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`. Pointer drags push once on press, not per move.

**Project file.** `Ctrl+S` save, `Ctrl+Shift+S` save-as, `Ctrl+O` open, File menu entries, `.chitin.json`
via `System.Text.Json`, schema:

```json
{ "version": 1, "doc": {"w":640,"h":480}, "zoom":1.0, "camera":[0.0,0.0],
  "vps": [{"x":-400,"y":150,"density":0}, null, null, null, null, null, null, null, null],
  "traceLines": [{"x0":0,"y0":0,"x1":100,"y1":100}],
  "grid": {"show":false,"style":0,"size":32},
  "boxes": [{"p0":[0,0],"p1":[100,0],"p2":[0,100],"p3":[100,100],"h":[0,-50]}],
  "subdivisions": [{"quad":[[0,0],[1,1],[2,2],[3,3]],"n":4,"m":3,"edge":false}],
  "ellipses": [{"quad":[[0,0],[1,1],[2,2],[3,3]],"cx":0.5,"cy":0.5,"r":0.5}],
  "reference": {"opacity":1.0,"flipH":false,"flipV":false,"scale":1.0,"offset":[0,0],"hidden":false},
  "rigs": [{"name":"Bedroom","vps":[{"x":0,"y":48,"density":0}, null, null, null, null, null, null, null, null]}],
  "palette": "atelier",
  "backgroundPng": "<base64 or null>" }
```

Embed the background as base64 PNG so a project is one self-contained file (a 4 MB photo makes a
~5.4 MB project — acceptable; do not invent compression schemes). Unknown keys are ignored on load;
a missing background decodes to null. Never crash on malformed JSON: show the error in the bottom
HUD bar and keep the current document.

**Acceptance.** `--selftest` adds:
- `CHECK undo_restores PASS` — drop 3 VPs, undo 3×, assert all null; redo 3×, assert positions
  bit-identical to the pre-undo capture.
- `CHECK project_roundtrip PASS` — capture → serialize → deserialize → apply → capture; assert
  `WorldState` equality field by field, then assert both renders hash equal.

## ITEM 2 — SVG and transparent-PNG export

**Why:** clipboard export is opaque raster at 100%; artists want vectors and control of size.

**One geometry source.** Extract the world-space guide geometry into
`GuideBuilder.Build(World world) -> IReadOnlyList<Prim>`, where `Prim` is a small struct/record:
`Line(penClass, p0, p1)`, `Circle(penClass, centre, radius)`, `Poly(penClass, points, closed)`,
`FillBrush(brushClass, rect)`, `Text(...)`. `Renderer` iterates this list instead of calling
`DrawingContext` inline; `SvgWriter` serialises the same list. This is the one abstraction the
feature earns: without it the SVG path would silently drift from the screen. Keep `Prim` flat — no
visitor pattern, no interface hierarchy.

SVG rules: line widths are already world units, so emit them as-is with `vector-effect` off and a
viewBox in world units; colours from the pen brushes; `stroke-opacity` from the ARGB alpha; text as
`<text>` with the same font sizes. Skip the HUD and help overlay (they are UI, not artwork).

**PNG rules.** `RenderTargetBitmap` at the requested pixel size; `transparent` skips the plane fill
and the frame. Guides only, always at 1 document unit = 1 pixel of the export at zoom 1, scaled by
the chosen size.

**UI.** `Ctrl+E` opens `ExportDialog` (format radio: SVG / PNG; width field; height field; transparent
checkbox for PNG). The grid follows the document's `G` toggle, as on screen. Defaults: current document
size, PNG, opaque. File menu entry "Export…".

**Acceptance.** `--selftest` adds:
- `CHECK svg_well_formed PASS` — export the self-test scene at 1920×1080, parse with
  `XDocument.Parse` (throws = fail), assert ≥ 100 `<line>` elements and that every `stroke` value is
  a valid hex colour.
- `CHECK png_transparent PASS` — export PNG with transparent on, decode and assert the four corner
  pixels have alpha 0 and that ≥ 1 % of pixels are non-transparent.

## ITEM 3 — Reference-image controls

**Why:** one toggle is not enough to trace a photo. (`Lock` from the earlier suggestion is dropped —
the image has no pointer interaction to lock.)

**State.** `ReferenceSettings { double Opacity = 1; bool FlipH, FlipV; double Scale = 1; Vec2 Offset;
bool Hidden; }` on `World`; `BackgroundImg` remains the image asset and `HideBackground` forwards to
`Hidden`. Drawing: fill the plane white, clip to it, `PushOpacity`, then a transform that applies
flip, scale and offset around the plane centre, then `DrawImage` of the plane rect.

**UI.** `Ctrl+Alt+I` opens `ReferenceDialog`: opacity slider 0–100 %, scale slider 25–400 %, flip H
and V checkboxes, offset X/Y numeric fields, "Fit to plane" and "Reset" buttons. Changes apply live
(dialog holds a reference to the world and invalidates the viewport). `[` / `]` adjust opacity by
10 % without opening the dialog. Loading, pasting and removing keep their existing keys.

**Acceptance.** `--selftest` adds `CHECK reference_transform PASS`: build a 2×2 test bitmap in code
(TL red, TR green, BL blue, BR white), set it as the reference with opacity 0.5, render, then probe
pixels at the four quadrant centres: with `flipH` the red/green sample points must swap; with
opacity 0.5 over white the red quadrant must read ≈ (255,128,128) ± 2.

---

# Wave 2 — construction

## ITEM 4 — Perspective box tool

**Why:** the reason people open a perspective tool is to draw volumes, not only line fans.

**Model.** A box is stored as four screen-space base points `p0..p3` plus a height vector `h`
(a `Vec2`), so it renders identically forever and survives undo/save as plain numbers. All 12 edges
and the 6 face quads are derived at draw time; hidden edges (the three that meet the far bottom
corner) are drawn dashed at 40 % alpha.

**Interaction.** `B` toggles box mode (Esc leaves). In box mode a left-drag is a three-stage build,
all in one continuous gesture — press sets `p0`, the first axis direction is chosen automatically
from the drag angle against each VP axis (nearest wins; `1`/`2` force VP 0 / VP 1 while dragging),
release sets `p1`; the second drag picks the perpendicular axis on release (`p2`); the third drag
sets the height (`p0 + h`), vertical or converging to VP 3 if it exists. Then the corner
`p3 = intersect(line(p1 → VP_y), line(p2 → VP_x))` — if those lines are parallel (VPs at infinity),
fall back to `p1 + (p2 - p0)`. `Backspace` in box mode deletes the last box.

**Draw.** `FillBrush` for the three visible faces at 8 % ink, then the edges. Filling first is what
makes stacks readable.

**Acceptance.** `--selftest` adds `CHECK box_geometry PASS` calling the pure
`BoxGeometry.Build(p0, p1, p2, h, axisDir1, axisDir2)` with fixed inputs: assert the 8 vertices
exactly (the 4th base corner must equal the computed line intersection to 1e-4) and assert the
degenerate case (parallel axes) uses the fallback. Also `CHECK box_render PASS`: a scene with 3 stacked
boxes renders, and the pixel at the projected centre of the top face is not plane-white.

## ITEM 5 — Perspective subdivision

**Why:** dividing a face into equal parts is the most-used perspective construction after the grid.

**Two modes, one primitive.**
- *Face mode* (default): click a box face (hit-test the 6 quads, nearest) or click 4 corners; type
  `2`–`9` for cells per axis or `+`/`-`; Enter commits. Division uses a **homography** from the unit
  square to the quad, evaluated at `i/N, j/M`, then lines drawn through the opposite edges. This is
  exact for any quad that is the projection of a rectangle — which is what the box tool produces.
- *Edge mode* (`Shift` held at the first click): subdivide one receding edge. Click A (near) and B
  (far); the edge's vanishing point is the best-matching VP axis (`1`/`2` override). Division along
  a line through a finite VP is a one-dimensional projective map, so distances from the VP follow
  `d_i = d_A · N·r / (N·r + i·(1−r))`, `r = d_B/d_A`, `i = 0…N`. `i = 0` gives `d_A`, `i = N`
  gives `d_B`; for `N = 2` this reduces to the harmonic mean `2·d_A·d_B/(d_A+d_B)`, which is the
  classic perspective midpoint. No homography needed here; draw division ticks across the edge
  perpendicular to it in screen space.

Store `Subdivision { Vec2 A, B, C, D; int N, M; }` — the recomputation happens at draw time from the
quad and counts, so a saved project re-renders identically.

**Acceptance.** `--selftest` adds:
- `CHECK homography_identity PASS` — mapping the unit square onto itself returns the input points.
- `CHECK subdivision_centre PASS` — for a quad that is a projected square (fixed VP rig), the
  homography midpoint at (0.5, 0.5) equals the quad's diagonal intersection to 1e-6. This is the
  invariant that proves the perspective is right, not just that lines were drawn.
- `CHECK subdivision_counts` — N=4, M=3 produces 3+2 = 5 interior lines and 4×3 = 12 cells. *(Corrected:
  the original "4+3 interior lines and 12 cells" cannot both hold; proven in wave 2.)*
- `CHECK edge_division PASS` — for a fixed edge with `d_A = 300`, `d_B = 150` (so `r = 0.5`) and
  `N = 2`: the midpoint must sit at `d = 200` from the VP (harmonic mean), not at `225` as a screen
  bisector would put it. With `N = 4` the three points must sit at `d = 240, 200, 171.43` (±1e-4).

## ITEM 7 — Ellipse in a perspective plane

*(Done with item 5 because it shares the homography.)*

**Why:** circles on receding faces are the thing everyone freehands badly.

**Approach.** Same homography: sample the inscribed circle of the unit square at 128 parameters and
map through the homography, emit a closed `Poly`. Store
`EllipseInPlane { Vec2[] Quad; double Cx, Cy, R; }` in unit-square parameters so a resized/resaved
project still renders. Tool: `E` toggles ellipse mode; click a box face or 4 corners, then drag from
the centre to set the radius; `Backspace` deletes the last.

**Acceptance.** `--selftest` adds `CHECK ellipse_is_circle PASS` — with a screen-parallel square quad,
the sampled polyline's distance from the centre must stay within 0.5 px of `R` for all 128 points,
- `CHECK ellipse_perspective PASS` — with the projected square, the polyline must stay strictly inside
  the quad and be tangent to all four edges to 1e-4. *(Corrected: the tangency points are the images of
  the unit-square edge midpoints under the homography, not the screen midpoints of the projected edges.)*

---

# Wave 3 — teaching and identity

## ITEM 6 — Station point, FOV and cone of vision

**Why:** the original teaches by construction only. Showing the camera geometry is the clearest
thing Chitin never did.

**Math.** Principal point `P` = picture-plane centre. For VPs 0 and 1 on the horizon,
`d = sqrt(|x0| · |x1|)` where `x` is the signed distance from `P` along the horizon (guard: require
both VPs to exist and to lie on opposite sides; otherwise the overlay says "needs two points").
Station point `S` sits at distance `d` from `P` perpendicular to the picture plane — draw it in the
viewport as a marker below the horizon at `P + (0, d)` in world units, connected to V0 and V1 by
thin dashed lines (the 90° corner). FOV `= 2·atan((pictureWidth/2) / d)`. Cone of vision = dashed
circle of radius `d·tan(30°)` about `P`.

**UI.** `V` toggles the overlay; the HUD right side adds `· FOV 77° · d 400` when it is on. VPs
outside the cone get a small warning tick, and the help overlay gains the three new rows.

**Acceptance.** `--selftest` adds `CHECK station_distance PASS` — VPs at (−400, 0) and (400, 0) give
`d = 400 ± 1e-6`, FOV `= 2·atan(0.8) = 77.3196° ± 0.001`, cone radius `= 230.9401 ± 0.001`.

## ITEM 8 — Curated palette / paper theme

**Why:** colours are currently `Random` triplets, so a session can come out blue. This is the
difference between a port and your tool.

**Palettes.** `Palette` record: nine VP colours, grid colour, ink, paper, plane fill, border, void.
Three built-ins:
- `atelier` (default): warm off-white paper `#F5F0E6`, viewport `#2B2724`, ink `#23201D`, VP colours
  `#8C6A46 #A85A62 #6F8158 #B07A2E #7C5C74 #9A6B4F #6B7A6A #A8563C #7A6A52`, grid `#8C8272`.
- `ink`: near-monochrome — paper `#F7F7F4`, ink `#111`, VP colours as nine greys with one ochre accent.
- `charcoal`: dark paper `#1E1C1A` with chalk-white guides and warm accents, for sketching at night.

No blue anywhere — not in the default palette, not in the alternates.

**Behaviour.** `Ctrl+Shift+P` cycles the palette (menu: View > Palette). `S` now *rotates* the palette
assignment across the nine VPs instead of generating random colours. Persist the palette name in the
project file.

**Scope decision (Maki): palette only — no UI overhaul.** The existing shells, panels and layout stay
exactly as they are. Nothing here adds chrome, moves a panel, or changes a dialog; the only visible
difference is the colour of the guides, the paper and the ink. `Ctrl+Shift+P` cycles palettes, not
application themes.

**Acceptance.** `--selftest` adds `CHECK palette_stable PASS` — two renders of the same palette are
byte-identical (kills the RNG), and `CHECK palette_no_random PASS` — the nine VP colours are exactly
the palette's nine, in order.

## ITEM 9 — Camera presets and saved rigs

**Why:** every session starts by hand-placing the same three points.

**Presets.** Camera menu: 1-Point, 2-Point, 3-Point. All coordinates are **world units relative to
the plane centre** (a 640×480 document spans x ∈ [−320, 320], y ∈ [−240, 240]); the horizon sits at
60 % of the height from the top, i.e. `y = −240 + 0.6·480 = 48`.
- 1-point: VP0 at `(0, 48)`, VPs 1–8 cleared, verticals parallel.
- 2-point: VP0 `(−224, 48)`, VP1 `(224, 48)` (15 % and 85 % of the width), rest cleared.
- 3-point: as 2-point plus VP2 at `(0, −384)` (30 % of the height above the top edge), densities all 0.
Applying a preset pushes undo.

**Rigs.** "Save Rig…" asks for a name, stores the nine points (position + density) in
`World.Rigs` and the project file; the menu lists them for one-click recall. Renaming is out of
scope; deleting a rig is "Save Rig" over the same name.

**Acceptance.** `--selftest` adds `CHECK preset_three_point PASS` — apply on a 640×480 document and
assert the exact nine-point array (VP0 `(−224, 48)`, VP1 `(224, 48)`, VP2 `(0, −384)`, rest null,
all densities 0), and `CHECK rig_roundtrip PASS` — save a rig, serialise, load, assert identical.

---

# Key map (new bindings, conflict-checked against the original set)

| Key | Action | Item |
|---|---|---|
| `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y` | undo / redo | 1 |
| `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+O` | save / save as / open project | 1 |
| `Ctrl+E` | export dialog (SVG / PNG) | 2 |
| `[` / `]` | reference opacity −/+ 10 % | 3 |
| `Ctrl+Alt+I` | reference settings dialog | 3 |
| `B` | box tool on/off (`Backspace` deletes the last box while on) | 4 |
| `D` | subdivision tool on/off | 5 |
| `E` | ellipse tool on/off | 7 |
| `V` | cone of vision / station point overlay | 6 |
| `Ctrl+Shift+P` | cycle theme | 8 |
| `S` | rotate palette assignment (was: shuffle random colours) | 8 |

Existing bindings stay as they are. `S` is the only behaviour change (deterministic palette rotation
instead of random RGB). Menu additions: File (New, Open, Save, Save As, Export…), Edit (Undo, Redo,
Clear Document), View (Grid, Grid Style, Help, Cone of Vision, Theme), Reference (Load, Toggle,
Remove, Settings…), Camera (1-Point, 2-Point, 3-Point, Save Rig…, *rig list*), Help (About).

---

# Execution order and gates

| Wave | Items | Gate before the next wave |
|---|---|---|
| **0** | W0-1…W0-14 — defects, leaks, bounds, CLI, perf, housekeeping | build clean, every new `CHECK` passes, PNG hashes unchanged where behaviour must not move, `alloc_render` at least 50 % lower, `--selftest` exit codes 2/2/0 |
| 1 | 1 undo/save, 2 export, 3 reference controls | build clean, all CHECKs pass, project round-trips by hand in the GUI, screenshot of the new dialogs |
| 2 | 4 boxes, 5 subdivision, 7 ellipse | geometry CHECKs pass, a stacked-box + subdivided face + ellipse scene renders and survives save/load |
| 3 | 6 cone of vision, 8 palette, 9 presets | CHECKs pass, screenshots of all three themes, presets verified against the numeric table |

Order inside wave 0 matters: **W0-9 → W0-10 → W0-13** first (they reshape `Vec2`, the draw path and the
mode reference, and every later item builds on the new shape), then **W0-1** (the status line the later
items report errors through), then **W0-2, W0-3, W0-5, W0-6** (state and geometry), then **W0-4, W0-7,
W0-8, W0-12, W0-11, W0-14** (surfaces, perf caps, documentation).

Estimates, in delegated sessions: W0 medium (one session, mostly mechanical, plus a verification pass);
1 medium; 2 medium; 3 small; 4 large; 5 medium; 6 small; 7 medium; 8 small; 9 small. Wave 0 and Wave 1
together are roughly a day and a half of supervision including verification.

# Known limitations to state, not hide

- Boxes, subdivisions and ellipses store screen-space points: moving a vanishing point afterwards
  does **not** re-project them. Re-projecting would need a live camera model; noted in the README.
- The ellipse is exact for square faces and for the projected-square quad the tool builds; a general
  quadrilateral maps a *parameter* circle, which is the right shape only when the face is square in
  3D.
- SVG export writes text as `<text>` with system font metrics, so label positions vary by machine by
  a pixel or two. Lines and fills are exact.
