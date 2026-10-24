# Chitin — cross-platform clone (C# / Avalonia)

Source of truth for behaviour: `reference/original-decompiled/` — the C# decompiled from
`Carapace.exe` (Epic's "Carapace V1.0", .NET Framework 4.0 WinForms, x86). Port it faithfully:
same features, same shortcuts, same drawing rules, same numbers. Where this document and the
reference disagree, the reference wins — say so in the commit message.

## 1. What the program is

A perspective-drawing aid. A white "picture plane" (the sheet) sits in a dark viewport. The user
drops up to nine numbered vanishing points; each one fires a fan of construction lines across the
plane, clipped to it. Two "trace lines" (infinite ruler lines) can be dragged; where they cross you
can drop a vanishing point. There is a horizon line derived from VP1/VP2, a configurable grid, a
zoom/pan camera, a background image to trace over, clipboard copy/paste, and a keys and gestures
list (`F1`).

## 2. Stack and scope

- .NET 8, Avalonia 11.3.22 (`Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Headless` —
  the last one only for the self-test). `Avalonia` and `Avalonia.Skia` (used by the headless
  render) are referenced transitively. Pin these exact versions.
- One project (`Chitin.csproj`), assembly name `Chitin`, window title `Chitin`.
- No other NuGet packages. No MVVM framework, no DI container, no config files, no logging
  framework. All UI drawing is custom; only the menu and two small dialogs use real Avalonia
  controls.
- Targets: Windows, Linux, macOS (`RuntimeIdentifiers` may stay default; publishing per-RID is the
  packager's job, not code).

## 3. Build / run

```
dotnet build -c Release
dotnet run -c Release                     # interactive
dotnet run -c Release -- --selftest out/selftest.png
```

`dotnet build` must be warning-clean (`<TreatWarningsAsErrors>` not required, but no warnings).

## 4. File layout

```
Chitin.csproj
src/App/
  Program.cs              entry point and command line
  SelfTest.cs             --selftest: headless renders and every CHECK
  App.axaml / .cs
  MainWindow.axaml / .cs  sheet frame, menu built from the command table
  MainWindow.Sheet.cs     tray, points panel, foot, Find, keys dialog
  SheetControls.cs        Ruler, chrome colours
src/Dialogs/              Resize / Export / About windows
src/Viewport/
  ViewportControl.cs      input handling, camera, command table, clipboard
  Commands.cs             Command record, Find filter, F1 rows
  Renderer.cs             drawing (port of WWorld.Draw + WVanishingPoint.Draw)
  Prim.cs                 GuideBuilder primitives shared by Renderer and SvgWriter
  MouseMode.cs            base + Camera / VanishingPoint / TraceLine / Reference modes
  ConstructionMode.cs     box / subdivision / ellipse tools
  Styles.cs               every pen and brush for one palette at one zoom
src/Model/
  World.cs                document, view (zoom, camera) and the live Styles
  WorldState.cs           undo snapshot, project schema, UndoStack
  Palette.cs              curated palettes (ink / charcoal)
  VanishingPoint.cs / TraceLine.cs
src/IO/
  ProjectFile.cs          .chitin.json read/write
  SvgWriter.cs            SVG export from the same GuideBuilder primitives
src/Geometry/
  Vec2.cs                 vector maths (port of WVector)
  Geom.cs                 segment intersection (port of WGeom)
  BoxGeometry.cs          box vertices, faces and edges
  Homography.cs           unit-square projectivity and perspective edge division
  ShapeGeometry.cs        subdivision lines and ellipse sampling
  Optics.cs               station point, FOV and cone of vision
  Sheet.cs                ruler ticks, camera anchor on resize
assets/app.ico            original icon
assets/fonts/             B612 Mono regular and bold, OFL.txt
docs/SPEC.md              behavioural contract for the port
docs/ui-proposals/        UI overhaul proposals (HTML mockups)
docs/UX-PASS.md           UX pass findings, carried out in the commits after it
dev-docs/                 wave briefs, plan, reviews, verify.sh gate
reference/                decompiled original - local only, gitignored, NOT distributed
```

Each class keeps the behaviour of its reference counterpart; method names may drop the Hungarian
`W` prefix but the logic must stay recognisably the same (this is a port, not a redesign).

## 5. Coordinate and transform contract

- World space: origin at the centre of the picture plane. Plane occupies the world rect
  `(-W/2, -H/2, W, H)` where W,H are the document size (default 640 x 480).
- `Zoom` (double) in `[0.05, 5]`, default fitted so the plane fits the viewport.
- `Camera` is a world-space offset.
- **screen = (world + Camera) * Zoom**; inverse **world = screen / Zoom - Camera**.
- Draw with one transform push: `Matrix.CreateTranslation(Camera.X, Camera.Y) * Matrix.CreateScale(Zoom)`.
  (Avalonia matrices are row-vector: `p * T * S == (p + Camera) * Zoom`.)
- Centre camera: `Zoom = min(1, viewportW / W, viewportH / H)`; `Camera = (viewportW/2/Zoom, viewportH/2/Zoom)`.
  The reference computes the fit with a branch — the `min` form is the same result; keep the branch
  shape if you prefer, either is acceptable.
- `CrazyFar = max(W, H) * 32` — the "infinite" length used for the horizon and trace lines.
- Zoom to cursor: capture `worldBefore` under the cursor, change `Zoom`, recompute `worldAfter`,
  then `Camera += worldAfter - worldBefore`, then rebuild pens (they are `1/Zoom` wide). Pens live in a `Styles` object built from
  (palette, offset, zoom); PNG and SVG export build their own `Styles` and camera and never change
  the live zoom, camera or pens (`CHECK copy_preserves_view`).
- All pen widths are in **world units** and scale with zoom: `1/Zoom`, `2/Zoom`, circle radius
  `32/Zoom`, the VP number label `16/Zoom`. Grid/VP densities do not scale.

## 6. Rendering pipeline (order matters)

`Renderer.Draw(DrawingContext ctx, World world, Size viewport, RenderMode mode)`, where `mode` is
`Interactive`, `OpaqueExport` (the clipboard/export render: plane fill, no chrome, overlay or
reference image) or `TransparentExport` (no plane fill). Every colour below comes from the active
`Palette` (§8). Every vanishing-point colour in every built-in palette has a contrast ratio of at
least 3:1 against that palette's paper (`CHECK palette_contrast`, WCAG 2 formula).

1. If `mode == Interactive`: fill the viewport with the palette `void` colour.
2. Push transform (see §5).
3. **Under**: fill the plane frame rect expanded by 8 px in the palette `border` colour; fill the
   plane with the palette `paper` colour; then if a reference image exists and is not hidden, draw
   it clipped to the plane with `PushOpacity`, into the largest rect of the image's aspect ratio
   centred in the plane (scale 1 = fit, `Renderer.ReferenceRect`), using
   `translate(offset) * scale * flip` about the plane centre. Exports fill the paper
   (`OpaqueExport`) or nothing (`TransparentExport`) and never draw the reference image.
4. **Grid** if `ShowGrid`, clipped to the plane rect: pen = palette `grid` at alpha 128, width
   `1/Zoom`. `style 0` = both axes, `1` = vertical only, `2` = horizontal only. Vertical lines at
   `x = -W/2 + k*GridSz` for `k = 1..` while the loop counter `< max(W,H)`; same for horizontal.
   `GridSz` default 32, `+` doubles it, `-` halves it, clamped to `[2, 4096]`.
5. **Horizon** if VP[0] and VP[1] exist: direction = normalize(VP1.Pos - VP0.Pos); draw from
   `VP0.Pos - dir*CrazyFar` to `VP0.Pos + dir*CrazyFar`, clipped to the plane, with the palette
   `horizon` colour, width `1/Zoom`; then, clipped to the *complement* of the plane rect, the same
   line dashed (`DashStyle` with dash pattern ≈ 4,3), in the palette horizon colour at alpha 200,
   width `1/Zoom`.
6. **Cone of vision** if `ShowCone` and VP0/VP1 sit on opposite sides of the principal point (see
   §7.4). Drawn before the vanishing points so the guides sit underneath them.
7. **Vanishing points**, indices 0..8 in order (see §7).
8. **Trace lines** (finished ones only).
9. **Hover ring** (interactive only): the point under the pointer, or the one being dragged, gets
   a ring of radius `20/Zoom` in the palette prompt colour, `2/Zoom` wide.
10. **Mouse mode overlay** (`DrawOver`: the dashed in-progress trace line in the palette prompt
    colour at alpha 192, width `2/Zoom`, dash-dot-dot pattern; the tools' previews).
11. Pop the transform.
12. **Empty-plane hint** (interactive only, no point placed, no construction tool):
    `Click to place point 1, or pick a preset in the Camera menu`, 12 px B612 Mono in muted ink (ink
    at 70 % over paper), centred on the plane and wrapped to its width less 12 px each side. A plane
    narrower than 120 px, or too short for the wrapped text, shows no hint: the text never reaches
    the void (`CHECK empty_hint_fits`).

## 6.1 Sheet frame (window chrome)

The window is one layout with no modes (`MainWindow.axaml`, `MainWindow.Sheet.cs`,
`SheetControls.cs`; pure geometry in `Geometry/Sheet.cs`). Nothing in it reaches an export
(`CHECK export_unchanged`).

- Pen weights: 1 px ink between panels, 1 px hairline (ink at 28 % over paper) between cells, a
  2 px rule along the viewport edge of each ruler. Square corners; no blur, shadow or gradient. The
  operating system frames the window.
- Menu bar: File, Edit, View, Tools, Points, Reference, Camera, Help (§9).
- Tool tray (left, 40 px): square cells lettered `P T B D E` (points, trace, box, subdivide,
  ellipse). The active tool is inverted (paper on ink); a cell under the pointer takes the hairline
  fill. Each cell has a tooltip with the tool's name, key and gestures. `P` runs Leave tool /
  unselect, the others run their tool commands.
- Rulers (20 px, top and left of the viewport), in document units (plane top-left = 0), following
  zoom and pan. The minor step is the first of 1, 2, 5, 10, 25, 50, 100, 250, … that is at least
  7 px; the major step is 5 minor steps if that is at least 48 px, else 10; majors carry a label
  (`Sheet.Ticks`, `CHECK ruler_ticks_follow_zoom`). The cursor is a prompt-coloured hairline on
  each ruler; when VP 1 and VP 2 are level, a filled triangle marks the horizon on the left ruler.
  The corner square between the rulers is empty.
- Points panel (right, 252 px; `Ctrl+K` or View > Points panel hides and shows it; view state, not
  saved, not undoable, shown on launch; `CHECK ctrl_k_toggles_points`): one row per placed point:
  a number cell with the point's colour chip (inverted for the selected point; a click runs Select
  vanishing point), X and Y in document units, density `-` / value (`auto` or the step count) /
  `+`, and `×` (Delete vanishing point). A typed X or Y applies on Enter or when the field loses
  focus, as one `Move vanishing point` undo step; Escape drops the edit; an unedited field moves
  nothing (`CHECK points_panel_edits`). With no points the panel says how to place one.
- Foot (28 px): on the left, the status message (prompt or error, after a square in the prompt
  colour; an error is bold) or else "Trace lines cross" when the two trace lines intersect, then
  the active tool's hint, trimmed; on the right, the selected point (`VP n  x, y`, and its density
  when not automatic), the cone readout when the cone is on (`cone FOV° · d D`, or that it needs
  points 1 and 2 either side of the centre), then `-`, the zoom percentage and `+`: the outer cells
  run Zoom out and Zoom in, the percentage runs Centre view.
- Find (`Ctrl+F`): a field and the matching commands in a 440 px box over the top of the viewport
  (§9).
- Keys and gestures (`F1`, Help menu): a dialog listing `Commands.HelpRows`: the pointer gestures,
  the tool keys, then every command with a key, grouped as in the command table.
- Type: **B612 Mono** regular and bold, bundled in `assets/fonts` with its `OFL.txt` and set on
  every window (`CHECK font_loaded`). Hierarchy from size and weight; 9 px spaced capitals for the
  panel labels.
- Colour: every text/background pair the chrome uses reaches WCAG 4.5:1 in both palettes
  (`Chrome.TextPairs`, `CHECK chrome_contrast`).
- **Camera anchor.** On any viewport resize (panel or window) the camera shifts by half the size
  change, so the world point at the viewport centre stays put (`Sheet.KeepCentre`,
  `CHECK resize_keeps_centre`: < 0.5 px across a 300 px width change). The first render still
  fits the plane (`Home`).

## 7. Vanishing point drawing

- Clip to the plane rect. Skip the fan when its bounding box does not intersect the plane.
  `n = Density == 0 ? min(max((int)(|Pos| / 32), 4), 96) : Density`.
  `step = 360 / (n * 4)` degrees. Draw `2n` lines through `Pos` at angles `0, step, 2*step, …`,
  each extending `±dir * CrazyFar`, with the VP's own pen (the active palette's colour *i*,
  width `1/Zoom`). Nine pens exist; VP *i* always uses pen *i*.
- Unclip. If `mode != Interactive`, stop.
- If `Pos` lies outside the plane rect, draw two "pointer" lines from `Pos` to the pair of plane
  corners picked by the same eight-branch table as the reference `WVanishingPoint.Draw` (copy that
  table verbatim: it selects the corners of the quadrant the point is in, diagonal cases included).
  Pen: palette paper when this VP is selected, else palette ink; width `2/Zoom`.
- Always: filled circle at `Pos`, radius `32/Zoom` (i.e. `DrawEllipse(fill, pen, Pos, 16/Zoom, 16/Zoom)`),
  fill = palette ink at alpha 160 unselected / palette prompt selected, outline = palette ink (1) /
  palette paper (2) — `2/Zoom` wide.
- Centred label `{index+1}` at `Pos`, serif `16/Zoom`, palette paper.

## 7.1 Box tool

`B` toggles box mode; `Escape` leaves it. Building is three press-drag-release gestures: the first
sets `p1`, the second `p2`, the third the height. A press without a drag sets nothing and says
nothing.

- The first axis is chosen by the drag angle against VP0/VP1; `1`/`2` force VP0/VP1 while
  dragging. `p1` is the pointer projected onto the ray from `p0` toward that axis's point, `p2` onto
  the ray toward the other axis's point; an axis without a point takes the pointer as it is.
- The height runs along the line from `p0` toward VP2 when it exists, else straight up or down.
- An axis with no point falls back to a direction: horizontal, except the second axis when neither
  VP0 nor VP1 exists (vertical). So with VP0 alone the box is in 1-point perspective.
- `p3` is the intersection of the line through `p1` toward the second axis's point and the line
  through `p2` toward the first axis's point; when those lines are parallel, `p3 = p1 + (p2 - p0)`.
- `Backspace` cancels an in-progress build first, then (when idle) deletes the last box.

**Following.** A box built this way stores `p0`, the point each edge aims at (`follow`: width,
depth, height, `-1` for none) and the signed edge lengths from `p0` toward them, plus the corners
and height as built. It is rebuilt from the live points every draw (`BoxGeometry.Corners`): each
linked edge keeps its length and re-aims at its point; with a height point the top corners lie on
the rays from the base corners to VP2 and the top edges converge on VP0/VP1. An edge whose point
was deleted, or had none, keeps its stored direction. A box from an older file has no links and is
drawn as stored (`p0..p3` plus `h` at every corner). `CHECK boxes_follow_points`.

The eight vertices are `p0, p1, p3, p2` and `q0, q1, q3, q2` above them. `p0` (the first click) is
taken as the near corner, so the two side faces meeting at `p0` face the viewer. Every other face
faces the viewer when its projected outward winding matches theirs: a box below the horizon shows
its top, a box above it shows its underside. Front faces are filled at 8 % ink; an edge is solid
when either face it joins is in front, otherwise dashed at 40 % alpha (`CHECK box_visibility`).
Boxes render through the same `Prim` list in the viewport, PNG export and SVG export.

## 7.2 Perspective subdivision

`D` toggles subdivision mode. Click a box face (the smallest face under the cursor wins) or click
four corners: the subdivision is added at once with the current cell counts (2 × 2 to start).
`2`–`9` then set the cells of the one just added on both axes, `Shift`+digit the second axis only,
`+`/`-` adjust both (with `Shift`, the second); each change is one undo step, and the counts carry to
the next pick. `Backspace` cancels clicked corners first, then deletes the last subdivision.

A subdivision picked from a box face stores the box and face index and draws from that face, so it
follows the box (§7.1); removing the box (Backspace in the box tool) leaves it where it is.

Face division uses the homography from the unit square to the quad; interior lines are drawn at
`u = i/N` and `v = j/M`, so `N` cells by `M` cells means `N-1 + M-1` interior lines. Older files
may hold edge subdivisions (`edge: true`): they still render with the one-dimensional projective
map, `d_i = d_A * N*r / (N*r + i*(1-r))`, `r = d_B/d_A`, about the vanishing point stored in
`quad[2]` (uniform when it is missing), as ticks perpendicular to the edge at 8 screen pixels. The
tool no longer makes them.

## 7.3 Ellipse in a plane (Wave 2)

`E` toggles ellipse mode. Click a box face or four corners, then drag from the centre to set the
radius; `Backspace` cancels a pending selection first, then deletes the last ellipse; `Escape`
leaves the tool. The ellipse is stored as
the face quad plus a centre and radius in unit-square coordinates, so it survives save/load and
resizes. Drawing samples the inscribed unit circle at 128 parameters and maps each sample through
the same homography as the subdivision, emitting a closed polyline. For a screen-parallel square
this is a circle; for a projected square it is the perspective image of the inscribed circle (the
tangent points are the images of the square's edge midpoints, which are not generally the screen
midpoints of the projected edges).

## 7.4 Station point, FOV and cone of vision (Wave 3)

`V` toggles the overlay (persisted as `showCone`). The principal point `P` is the picture-plane
centre `(0, 0)`. Project VP0 and VP1 onto the horizon direction `normalize(VP1 - VP0)`; their
signed distances from `P` must have opposite signs, otherwise the overlay draws nothing and the
foot's cone readout says it needs points 1 and 2 either side of the centre. With `d = sqrt(|x0| * |x1|)`:

- station point `S = P + (0, d)` in world units (below the principal point);
- field of view `FOV = 2 * atan((W / 2) / d)`, shown rounded to whole degrees;
- cone of vision = circle about `P` of radius `d * tan(30°)` (`≈ 0.57735 d`);
- a small screen-space warning tick beside any existing VP whose distance from `P` exceeds the cone
  radius.

The overlay is drawn dashed and renders in the viewport and both exports. Degenerate cases (a VP on
`P`, coincident VPs, same-side VPs, `d <= 0`) report unavailable and draw nothing.

## 7.5 Camera presets

Presets use world coordinates and the current document size. The horizon sits at 60 % of the height
from the top (`y = 0.1 * H`):

- 1-point: VP0 at `(0, 0.1H)`, all other slots null.
- 2-point: VP0 at `(-0.35W, 0.1H)`, VP1 at `(0.35W, 0.1H)`, rest null.
- 3-point: as 2-point plus VP2 at `(0, -0.8H)`, rest null.

Unused slots are null and every density is 0. On a 640x480 document this is `(0, 48)`,
`(-224, 48)`/`(224, 48)` and `(0, -384)`. Applying a preset pushes one undo snapshot.

## 8. Data and constants

| Item | Value |
|---|---|
| Document default | 640 x 480 |
| `CrazyFar` | `max(W,H) * 32` |
| Zoom range | 0.05 … 5, wheel step ×1.1 / ÷1.1 |
| VPs | fixed array of 9, each `null` or `{ Pos, Density }`; `Density` steps of 4, clamped to 0–384 (shown as 0–96) |
| Trace lines | list, at most 2; a new one evicts index 1 then appends |
| Grid | `ShowGrid` false, `GridSz` 32; both axes; view state (§9) |
| Reference image | image or null; opacity 1, flip H/V false, scale 1, offset (0, 0), hidden false |
| Palette | `ink` (new documents, and any unknown or removed name such as `atelier`), `charcoal`; `Ctrl+Shift+P` toggles; each assigns its nine colours in order; view state (`CHECK default_palette_ink`) |
| Cone of vision | `showCone` false by default; `V` toggles; radius `d * tan(30°)`; view state |

`Vec2.Length()` returns `sqrt(x²+y²)`. The reference named it `GetSizeSquared()` although it
returns the length; the port renamed it (polish stage, R1). `DistFrom` is a real distance.

## 9. Input

Mouse (on the render surface):
- Left press, points tool: pick the **nearest** VP within `32/Zoom` of the cursor → drag it (one
  undo step on the first real move). A press-release without a move on empty plane places the next
  free point (lowest empty slot) at the cursor, unselected, when no point was selected; with a point
  selected it only unselects. With all nine placed, a prompt says so (`CHECK click_places_points`).
- Hover: the VP the press would pick gets a ring (§6) and a hand cursor (`CHECK pointer_navigation`).
- Left drag, trace tool (`T`): pull a trace line. Right press/drag pulls one in every tool. While
  dragging, `Ctrl` snaps the end to 5° increments and `Shift` to 15°, only when held **alone**. On
  release, if the mouse moved, push the line (max 2; a new one evicts index 1).
- `Space` + left drag and middle drag pan the camera by `delta / Zoom`, in every tool.
- Wheel and trackpad scroll pan by 48 px per notch (`Shift` + vertical wheel pans sideways);
  `Ctrl` + wheel (a trackpad pinch) zooms about the cursor by `1.1^delta` (§5). Pan and zoom are
  view state: no undo.
- Without a wheel (a stylus): `Ctrl+=` (also `Ctrl++` and keypad `Ctrl++`) zooms in by 1.25 and
  `Ctrl+-` out, about the viewport centre; `Ctrl+0` shows 100 %. `Ctrl` + `Space` + left drag zooms
  about the press point by `e^((dx - dy) / 100)` from the zoom at the press (right or up zooms in),
  in every tool; it places no point and pushes no undo step (`CHECK stylus_zoom`).
- `Alt` + left drag moves the reference image (one undo step per drag), `Alt` + wheel scales it by
  `1.1^delta` about its centre (§10), when an image is shown.
- Cursor: cross; hand over a point; move cursor while panning; a horizontal-resize cursor while
  drag-zooming.

Keyboard (surface focused):
- Every command is one entry in one table (`ViewportControl.BuildCommandTable`, type `Command`:
  name, group, gesture, keys, action). The key handler, the menus, `F1`, Find and the points panel
  all read it (`CHECK commands_unique_gestures`: no key is bound twice, names are unique, a command
  without a key is in a menu; `CHECK help_matches_commands`). Keys are looked up by key and exact
  modifiers; the active construction tool sees each key first. Every undo snapshot carries the name
  of the command that pushed it, and Edit > Undo / Redo show it (`Undo Move vanishing point`).
  `Shift` is also accepted for `+`, `-`, `[` and `]`. Any other modifier combination does nothing.
- A command that needs a selected point (density, delete selected, level) says so in a prompt when
  there is none (`CHECK view_state_outside_undo`).
- Errors (save, export, open, copy, an image refused, a command or key that throws) stay in the
  foot until the next command or click; prompts clear after 5 s (`CHECK error_persists`).
- `Escape` leaves any tool (trace included); with no tool, unselects.
- `Home` centre camera: fit the plane (zoom at most 1, clamped to 0.05–5); a viewport with no size
  (for example a minimised window) keeps the current view.
- `Ctrl+N` new document. `Ctrl+Shift+R` resize dialog (number fields bounded to `[1, 8192]`,
  prefilled with W/H; OK applies, keeps the reference image fitted to the new plane, and re-centres).
  Edit > Clear document… asks (Clear / Cancel), then removes points, trace lines, shapes and the
  reference image, keeping size, view, reference settings and file.
- `1`…`9` drop/move VP at the mouse; `Ctrl+1…9` select an existing VP; `Alt+1…9` delete one;
  `Delete` deletes the selected one; `Ctrl+Shift+1…9` drop a VP at the intersection of the two
  trace lines (only if they intersect inside the extended segments) and clear them; otherwise a
  note says the lines do not cross. These digit commands are listed by F1, not in menus or Find.
  Points > Place next point places the next free point at the plane centre.
- `Up`/`Down` change the selected point's `Density` by ±4 (clamped to 0–384, shown as 0–96). Every
  `VanishingPoint` clamps its density when created, so a project file cannot ask for more.
- `G` toggle grid, `+`/`-` grid size ×2 / ÷2 (2–4096). `V` toggle the cone of vision.
  `Ctrl+Shift+P` toggle the dark palette (ink / charcoal). Grid, grid size, cone and palette are
  view state: saved in the file, but they push no undo step, do not mark the document unsaved,
  and undo/redo keep them (`CHECK view_state_outside_undo`).
- `H` (no modifier) set VP[0] and VP[1] `Pos.Y` to the cursor's world Y. `H` and `Up`/`Down` do
  nothing when another modifier is held (`CHECK key_modifiers`).
- `I` toggle the reference image, `Alt+I` remove it, `Ctrl+I` open-file dialog (image filter) and
  load, `Ctrl+V` paste an image from the clipboard. A new image keeps the document size: it is
  fitted inside the plane with its aspect ratio, with scale 1 and no offset or flip (opacity kept),
  and a prompt names the Alt gestures (`CHECK reference_direct`).
- `[` / `]` decrease/increase reference opacity by 10 % (clamped to 0–100 %). Reference > Flip
  horizontally, Flip vertically and Fit to plane (scale 1, no offset). A run of the same reference
  change (wheel ticks, `[` `]` presses, flips) is one undo step.
- `Ctrl+Shift+C` copy the plane at zoom 1 as a W×H bitmap (no chrome, no viewport background).
- `Ctrl+Z` undo, `Ctrl+Shift+Z` / `Ctrl+Y` redo; the history is 50 deep and redo clears on a new
  action. A pointer drag, key command, dialog OK and menu command each push exactly one snapshot
  before mutating. A click that only selects pushes nothing (`CHECK pointer_undo`).
- `Ctrl+S` save, `Ctrl+Shift+S` save as, `Ctrl+O` open a `.chitin.json` project. The window title
  is `<file name> - Chitin` (`Untitled` before the first save) and gains ` *` while there are
  unsaved changes (`CHECK window_title`). Closing, New and Open ask first when there are unsaved
  changes: Save (default) / Don't save / Cancel. Save runs Save and continues only when the
  document is then clean, so a cancelled save picker keeps the work (`CHECK confirm_discard`). Open
  clears the undo history. Save and Export write a local file as `<name>.tmp` beside it, then
  rename it over the target; a non-local storage item is written in place.
- `Ctrl+E` opens the export dialog: PNG or SVG, pixel width/height (defaults to the document size),
  and a transparent-background checkbox for PNG. Width and height are linked to the plane's aspect
  ratio (the longer side is capped at 8192; `CHECK export_aspect`).
- `B`, `D`, `E` toggle the box, subdivision and ellipse tools (§7.1–7.3), `T` the trace tool.
  `E` (ellipse): click a face or four corners, then drag from the centre to set the radius; an
  ellipse picked from a box face follows the box; `Backspace` cancels a pending selection first,
  then deletes the last ellipse.
- Four clicked corners must form a convex shape in click order; a bow-tie or dented shape is
  refused with a status message and the corners are cleared (`CHECK tool_pick_quad`).
- A tool takes only its own unmodified keys (and `Shift` + digit / `+` / `-` in the subdivision
  tool). Keys with `Ctrl` or `Alt`, and box-tool `1`/`2` outside the first-axis drag, reach the
  viewport, so point selection, deletion and `Alt+Backspace` work while a tool is active.
- `Backspace` delete the last trace line, `Alt+Backspace` all. `F1` shows Keys and gestures.
- **Find** (`Ctrl+F`, or View > Find a command): a field over the top of the viewport and the
  matching commands under it (name, group, gesture). Matching is per word, case-insensitive, over
  name and group (`Commands.Find`, `CHECK find_matches_words`: "grid lar" finds only Larger grid;
  digit commands are left out). Up/Down move, `Enter` or a click runs, `Escape` or leaving the field
  closes it and returns focus to the viewport (`CHECK find_closes`). While a text field (Find, a
  point coordinate) has focus, only `Ctrl+K` and `Ctrl+F` pass to the command table
  (`CHECK text_field_keys`), and moving the pointer over the viewport does not take the focus away.
- Menus: File, Edit, View, Tools, Points, Reference, Camera, Help, built from the command table in
  table order; each item shows the command's first key and runs the command
  (`ViewportControl.RunCommandAsync`, `CHECK menu_gestures`). Undo and Redo are disabled when
  there is nothing to undo or redo. While the menu has focus, a bound key with Ctrl or Alt still
  reaches the viewport; other keys stay with the menu. `CHECK window_keys_once` (real key presses
  on the main window run once; a menu click and a forwarded key work) and `CHECK menu_state`
  (Undo/Redo enabled state and names; the Points menu exists).

## 10. Clipboard / export / project file

- Copy: render into `RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96))` via
  `CreateDrawingContext()`, save/convert to `Avalonia.Media.Imaging.Bitmap`, then
  `await clipboard.SetBitmapAsync(bitmap)` (extension in `Avalonia.Input.Platform.ClipboardExtensions`).
- Paste: `var bmp = await clipboard.TryGetBitmapAsync()`.
- Project file: one self-contained JSON file, `.chitin.json`, written with `System.Text.Json`.
  Schema (version 1): `version`, `doc` (`w`, `h`), `zoom`, `camera` (`[x, y]`), `vps` (nine entries,
  each `{x, y, density}` or `null`), `traceLines` (`{x0, y0, x1, y1}`), `grid` (`show`, `size`), `boxes`,
  `subdivisions`, `ellipses`, `reference` (`opacity`, `flipH`, `flipV`, `scale`, `offset`, `hidden`),
  `palette`, `showCone`,
  `backgroundPng` (base64 PNG or `null`). A missing `version` reads as 1; a `version` above 1 is
  refused with a status message, because this build would drop fields it does not know. Unknown
  keys are ignored (older files may carry `rigs`, `paletteOffset` and `grid.style`; they are dropped);
  malformed JSON leaves the current document untouched and shows the error in the status bar. The
  background image is embedded in the project and is part of each undo snapshot (in memory, by
  reference): undo after removing, clearing, resizing or replacing it brings back the previous
  image. A bitmap is disposed only when it is neither current nor held by an undo or redo snapshot.
  Files whose encoded background exceeds 64 MB, or whose image header gives more than 40
  megapixels, are refused with a status message. Ctrl+I and project load read the size from the
  header (`World.DecodeImage`, SkiaSharp `SKCodec`) before the full decode. Box entries are
  `{"p0":[x,y],"p1":[...],"p2":[...],"p3":[...],"h":[x,y],"follow":[0,1,2],"lengths":[w,d,h]}`;
  `follow` and `lengths` are absent on older boxes, and a malformed pair (not three entries, an
  index outside -1..8, a non-finite length) loads as an ordinary box. Subdivision entries are
  `{"quad":[[x,y],[x,y],[x,y],[x,y]],"n":N,"m":M,"edge":false,"box":B,"face":F}`; `box` is -1 (or
  absent) for four clicked corners, and an index outside the loaded boxes, a face outside 0..5 or
  an edge entry loads as -1. Older edge entries set `quad[2]` to the vanishing point (and `m` is
  1). Ellipse entries are `{"quad":[[x,y],...],"cx":Cx,"cy":Cy,"r":R,"box":B,"face":F}` with the
  centre and radius in unit-square coordinates; `quad` is always the face as picked, so a
  shape whose box is gone still draws.
- SVG export (`SvgWriter`) consumes the same `GuideBuilder` primitives as the screen at zoom 1,
  skips `ScreenOnly` primitives, honours each primitive's clip rect, emits stroke/fill opacity from
  the ARGB alpha, dash arrays from the pen, and text as `<text>` with the same sizes and a serif or
  sans-serif family. The viewBox is the document rect in world units. PNG export uses
  `RenderTargetBitmap` at the requested size with `Zoom = width / documentWidth` and a centred camera;
  transparent mode omits the plane fill. Both exports contain guides only (no chrome, hover ring, hint or
  reference image).

## 11. Self-test (mandatory, this is the acceptance gate)

`--selftest <path>`: initialise the headless platform
(`AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting()`),
build this exact scene, and write three PNGs (`<path>`, a `-export` sibling and a `-shapes` sibling
for the construction scene):

- document 640 x 480, zoom 1.25, camera `(560, 360)` (the viewport is centred on the document);
- VPs: 0 at `(-420, -140)` density 0, 1 at `(380, -150)` density 0, 2 at `(60, -900)` density 8,
  4 at `(1500, 60)` density 6 (this one exercises the off-screen pointer lines);
- trace lines `(-500, 300) → (200, -250)` and `(0, -400) → (300, 350)`;
- grid on, size 40;
- viewport render `1400 x 900`, export render `W x H` with `RenderMode.OpaqueExport`;
- selection index 2 (selected VP must be gold);
- print `CHECK <name> PASS|FAIL` for every named assertion, then `SELFTEST_OK <path1> <path2>
  <path3>` and exit 0; on failure or a failed check, exit non-zero.
- The gate scene and the shapes scene pin the `ink` palette. Their export hashes were re-recorded
  when `atelier` was removed, and equal what the earlier renderer gives for the same scenes in
  `ink`; there is no RNG in the render path.
- Additional checks: `CHECK undo_restores` (3 VPs, 3 undos, 3 redos, bit-identical) and
  `CHECK project_roundtrip` (capture, serialise, read, restore, compare the serialised state and the
  render hash), `CHECK svg_well_formed` (`XDocument.Parse`, at least 100 `<line>` elements, every stroke a
  six-digit hex colour),   `CHECK png_transparent` (transparent export: all four corners alpha 0,
  at least 1 % of pixels non-transparent) and `CHECK reference_transform` (a 2×2 red/green/blue/white
  bitmap at opacity 0.5 over white reads ≈ (255, 128, 128) and flipH swaps the red/green samples).
  Construction checks: `CHECK box_geometry` (8 vertices, finite-VP intersection and parallel
  fallback), `CHECK box_render`, `CHECK homography_identity`, `CHECK homography_inverse`,
  `CHECK homography_degenerate`, `CHECK subdivision_centre`, `CHECK subdivision_counts`,
  `CHECK edge_division`, `CHECK ellipse_is_circle`, `CHECK ellipse_perspective`,
  `CHECK project_malformed` and `CHECK shapes_svg`. `CHECK tool_toggle` proves `B`/`D`/`E` select the
  three tools and `Escape` leaves them. Wave 3 adds `CHECK station_distance` (d=400, FOV=77.3196°,
  cone=230.9401, and unavailable for same-side or principal-point VPs), `CHECK cone_overlay` (drawn
  when available, absent when not), `CHECK preset_three_point` (exact nine-slot arrays for 1/2/3
  point), `CHECK palette_stable` (two builds byte-identical) and `CHECK palette_no_random` (pen colours equal
  the palette in order; a palette change leaves the document clean). Polish stage 1 adds `CHECK key_handled_before_await` (Ctrl+Shift+R marks
  the key handled while its dialog is still open, so the window does not run it again) and
  `CHECK undo_reference_image` (undo brings the image back after Remove and Clear; the image is
  disposed once when its last snapshot leaves the 50-step history), `CHECK open_clears_history`
  (a loaded project has no undo and is not dirty) and `CHECK confirm_discard` (New on a dirty
  document asks first; Save is the default and a cancelled save keeps the document, Cancel keeps
  it, Don't save clears it, a clean document skips the dialog) and
  `CHECK save_atomic` (a second write replaces the file and leaves no `.tmp`; a write that fails
  before the rename leaves the previous content). Polish stage 2 adds `CHECK pointer_undo` (headless
  mouse input: clicks and a pan add no undo step, a sideways wheel pans without zooming, a point drag adds
  one, and undo keeps the panned view) and `CHECK tool_keys_pass` (`Ctrl+1`, `Alt+Backspace` and
  `Alt+2` reach the viewport inside tools; plain `2` stays with the subdivision tool and drops a
  point in the box tool) and `CHECK density_clamp` (file densities 1e8 and -5 load as 384 and 0;
  `Up` stops at 384; the fan builder clamps a raw field value) and `CHECK image_header_guard` (a
  2×2 PNG decodes; the same PNG with its IHDR patched to 60000×60000 is refused before decode) and

  `CHECK center_camera_empty` (centring a viewport with empty bounds keeps zoom 1 and camera 0,0)
  and `CHECK key_modifiers` (`Shift+Up`, `Alt+H` and `Ctrl+Shift+Left` change
  nothing; `Ctrl+Shift+3` without crossing lines drops no point and shows the status message) and
  `CHECK project_version` (version 2 is refused; a saved file carries version 1) and `CHECK tool_pick_quad`
  (headless clicks: four corners give one subdivision at once and digits set its cells; one click
  on a box face and a drag give an ellipse on a face of that box). `CHECK alloc_render` is relative: a frame that reuses the cached
  guides allocates at most two thirds of a frame that rebuilds them. An unhandled UI-thread error
  goes to stderr and to the status line as `Error: <message>`; the app keeps running.
  The UX pass adds `CHECK pointer_navigation`, `CHECK stylus_zoom`, `CHECK error_persists`,
  `CHECK ctrl_k_toggles_points`,
  `CHECK points_panel_edits`, `CHECK click_places_points`, `CHECK view_state_outside_undo`,
  `CHECK reference_direct` and `CHECK boxes_follow_points` (each described where its behaviour is).
  The self-test also writes the real window, headless, to `screens/` next to `<path>` (each palette,
  Find open, a following 3-point box after its point moved, and an empty document at fit and zoomed out) for review. The `-shapes` PNG contains stacked boxes, a subdivided face and an
  ellipse; the interactive and export PNGs move when the palette changes.

The operator will inspect all three PNGs. Do not stub rendering, do not catch and swallow errors.

## 12. Avalonia 11.3.22 API notes (verified against the package, use these)

- `using (ctx.PushTransform(matrix))`, `using (ctx.PushClip(rect))`.
- `ctx.DrawLine(IPen, Point, Point)`; `ctx.DrawEllipse(IBrush, IPen, Point center, double rx, double ry)`;
  `ctx.DrawRectangle(IPen, Rect)` and `ctx.FillRectangle(IBrush, Rect)`; `ctx.DrawImage(IImage, Rect)`;
  `ctx.DrawText(FormattedText, Point)`.
- `FormattedText(string, CultureInfo, FlowDirection, Typeface, double emSize, IBrush)` then read
  `.Width` / `.Height`; there is no alignment parameter — offset the draw point yourself
  (Near = as-is, Center = `-w/2`, Far = `-w`) and use `.Baseline`/`.Height` for vertical centering.
- `new Pen(IBrush, double thickness, IDashStyle, PenLineCap, PenLineJoin, double miterLimit)`;
  `new DashStyle(IEnumerable<double> dashes, double offset)` (dash lengths are in multiples of the
  pen thickness).
- `new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96))`, `using (var c = rtb.CreateDrawingContext())`,
  `rtb.Save(path)`.
- `SolidColorBrush(Color.FromArgb(a,r,g,b))`.
- Clipboard: `TopLevel.GetTopLevel(this).Clipboard`; `SetBitmapAsync` / `TryGetBitmapAsync`
  (extension methods, `using Avalonia.Input.Platform;`).
- Rendering happens on the UI thread; finish any `await`ed clipboard work before touching the world.

## 13. Non-goals

- No settings file.
- No reference-image homography warp; the reference controls are affine (opacity, scale, flip,
  offset).
- Palettes re-theme the whole main window: every chrome colour comes from the active palette
  (ink, paper, hairline = ink at 28 % over paper, prompt), including the Fluent menu and text-field
  brushes (`Chrome.FluentColours`); a dark paper selects Fluent's dark variant. Dialogs keep the
  Fluent theme but use the bundled face.
- SVG text uses system font metrics, so label positions can differ from the screen by a pixel or two;
  lines and fills are exact.
- No pixel-identical font metrics — same sizes and alignments, default UI font.
- Boxes from files saved before the UX pass, and shapes from four clicked corners, keep their
  screen points: moving a vanishing point does not re-project them (§7.1).
- The ellipse is exact for square faces and for the projected-square quad the box tool builds; a
  general quadrilateral maps a parameter circle, which is the right shape only when the face is
  square in 3D.

## 14. Deviations from the original

These differences are deliberate and documented; everything else aims to match the reference.

- `Geom.GetLineIntersection` returns `false` for collinear segments; the original returned the
  midpoint of the extended first segment.
- Automatic vanishing-point density is capped at 96, and a VP whose fan bounding box misses the
  plane emits no fan lines; the original never capped or culled.
- Document dimensions are clamped to `[1, 8192]` per axis and grid size to `[2, 4096]`; the
  original accepted any value and could overflow.
- Reference images larger than 64 MB encoded or 40 megapixels decoded are refused; the original
  decoded any size.
- The reference image is drawn over the palette paper colour so opacity blends against the sheet;
  the original drew it directly over the viewport without an underlay. Opacity, scale, flip and
  offset are new controls.
- There is no randomness in the drawing colours: each curated palette assigns its nine colours in
  order, and `Ctrl+Shift+P` toggles light (`ink`) and dark (`charcoal`).
- The station-point/cone overlay and camera presets are new; they do not change any reference
  behaviour when off.
- UX pass (`docs/UX-PASS.md`): the wheel pans and `Ctrl` + wheel zooms (the original zoomed on the
  wheel); a click places the next point; loading a reference keeps the document size (the original
  resized the document to the image); copy is `Ctrl+Shift+C`, resize `Ctrl+Shift+R`, and Clear has
  no key (the original used `Ctrl+C`, `Ctrl+Alt+C` and `Ctrl+R`); boxes follow their points; the
  help overlay became the F1 dialog; grid styles, palette rotation and grayscale copy are gone.
