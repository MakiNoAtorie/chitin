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
zoom/pan camera, a background image to trace over, clipboard copy/paste, and a hot-key overlay.

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
  MainWindow.Sheet.cs     tray, title block, notes, Find
  MainWindow.Inspector.cs detailed-mode inspector
  SheetControls.cs        Ruler, Hatch, Bubble, chrome colours
src/Dialogs/              Resize / Export / Reference / About windows
src/Viewport/
  ViewportControl.cs      input handling, camera, command table, clipboard
  Commands.cs             Command record, Find filter, F1 rows
  Renderer.cs             drawing (port of WWorld.Draw + WVanishingPoint.Draw + centring marks + help)
  Prim.cs                 GuideBuilder primitives shared by Renderer and SvgWriter
  MouseMode.cs            base + Camera / VanishingPoint / TraceLine modes
  ConstructionMode.cs     box / subdivision / ellipse tools
  Styles.cs               every pen and brush for one palette at one zoom
src/Model/
  World.cs                document, view (zoom, camera) and the live Styles
  WorldState.cs           undo snapshot, project schema, UndoStack
  Palette.cs              curated palettes (atelier / ink / charcoal)
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
  Sheet.cs                ruler ticks, index tab placement, camera anchor on resize
assets/app.ico            original icon
assets/fonts/             B612 Mono regular and bold, OFL.txt
docs/SPEC.md              behavioural contract for the port
docs/ui-proposals/        UI overhaul proposals (HTML mockups)
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
`Interactive`, `OpaqueExport` (the clipboard/export render: plane fill, no chrome/help/overlay or
reference image) or `TransparentExport` (no plane fill). Every colour below comes from the active
`Palette` (§8); Help tints are derived from its ink and paper. Every vanishing-point colour in
every built-in palette has a contrast ratio of at least 3:1 against that palette's paper
(`CHECK palette_contrast`, WCAG 2 formula).

1. If `mode == Interactive`: fill the viewport with the palette `void` colour.
2. Push transform (see §5).
3. **Under**: fill the plane frame rect expanded by 8 px in the palette `border` colour; fill the
   plane with the palette `paper` colour; then if a reference image exists and is not hidden, draw
   it clipped to the plane with `PushOpacity`, using `translate(offset) * scale * flip` about the
   plane centre. Exports fill the paper (`OpaqueExport`) or nothing (`TransparentExport`) and never
   draw the reference image.
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
9. **Mouse mode overlay** (`DrawOver`: the dashed in-progress trace line in the palette prompt
   colour at alpha 192, width `2/Zoom`, dash-dot-dot pattern).
10. Pop the transform.
11. **Centring marks** (interactive only): a 3 × 12 px tick in the palette prompt colour at the
    middle of each viewport edge. Readouts are not drawn over the plane; they live in the sheet
    frame around the viewport (§6.1).
12. **Help overlay** if `ShowHelp && mode == Interactive`: fill `(0, 0, 350, viewportH)` with
    palette ink at alpha 128; then the hot-key rows, alternating row background = ink at alpha 32,
    first column = palette prompt (11 pt B612 Mono), `:` muted, description = palette paper (9 pt),
    sub-rows indented 32 px. A row that would cross the viewport bottom starts a new 350 px column at
    the top. The rows are the pointer gestures and construction-tool keys, then every command
    with a key, grouped as in the command table (`Commands.HelpRows`).

## 6.1 Sheet frame (window chrome)

The window is a drawing sheet around the viewport (`MainWindow.axaml`, `MainWindow.Sheet.cs`,
`SheetControls.cs`; pure geometry in `Geometry/Sheet.cs`). Nothing in it reaches an export
(`CHECK export_unchanged`: the export and shapes PNG hashes equal the ones from before the frame).

- Pen weights: 2 px ink around the window and the viewport, 1 px ink between panels, 1 px hairline
  (ink at 28 % over paper) between cells. Square corners; no blur, shadow or gradient.
- Menu bar (28 px): the menus, then **Find** at the right end (§9).
- Tool tray (left, 40 px): square cells lettered `P T B D E` (points, trace, box, subdivide,
  ellipse). The active tool is inverted (paper on ink). `P` runs Escape; `B D E` run the tool
  commands; `T` only names the right-drag gesture.
- Rulers (20 px, top and left of the viewport), in document units (plane top-left = 0), following
  zoom and pan. The minor step is the first of 1, 2, 5, 10, 25, 50, 100, 250, … that is at least
  7 px; the major step is 5 minor steps if that is at least 48 px, else 10; majors carry a label
  (`Sheet.Ticks`, `CHECK ruler_ticks_follow_zoom`). The cursor is a prompt-coloured hairline on
  each ruler; when VP 1 and VP 2 are level, a filled triangle marks the horizon on the left ruler.
- Index tabs: each placed vanishing point has a notched tab on each ruler at its viewport
  position: paper body, ink figure, a 4 px band in the point's guide colour at the viewport edge
  (text never sits on the point colour). The selected point's tab is inverted. A tab that would
  overlap the previous one steps along, then tabs step back so the last one stays on the ruler; a
  pinned tab keeps room for its arrow and distance, so several points past the same edge stay
  readable side by side. A point past a viewport edge has its tab pinned to that
  ruler end with an arrow, and on the top ruler its distance past the edge in document units
  (`Sheet.PlaceTabs`, `CHECK vp_tab_onscreen`, `CHECK vp_tab_pinned_offscreen`). Clicking a tab
  runs Select vanishing point for it.
- Foot (66 px): numbered notes on the left. `NOTE 1` is the active tool's hint; later notes are
  prompts with a square in the prompt colour: the status message, then "Trace lines cross" when
  the two trace lines intersect. The title block on the right (ISO 7200 layout, small spaced
  labels over values): zoom and point set large; density, cone (`FOV · d`, `needs two points` or
  `off`), tool, sheet (file name or `untitled`), size, palette.
- The corner square between the rulers is hatched at 45°.
- Type: **B612 Mono** regular and bold, bundled in `assets/fonts` with its `OFL.txt` and set on
  every window (`CHECK font_loaded`: the bundled face resolves, not a fallback). Hierarchy comes
  from size and tracking: 9 px spaced capitals for labels, 17 px bold for the values that matter.
- Colour: every text/background pair the chrome uses reaches WCAG 4.5:1 in all three palettes
  (`Chrome.TextPairs`, `CHECK chrome_contrast`).

**Modes.** `Ctrl+K` (View > Detailed panels, or the `CTRL K` cell at the right of the menu bar)
switches between compact and detailed. The mode is view state on `MainWindow`: not in `World`,
not undoable, not saved; every launch starts compact (`CHECK ctrl_k_toggles_mode`). The panel
change is a linear 100 ms width change: tray 40 → 184 px, inspector 0 → 304 px.

- Detailed tray: each cell adds the tool name and gesture; the active cell adds its options.
- Detailed title block: zoom is a field (percent, Enter applies about the viewport centre, Escape
  drops the edit, leaving it unedited keeps the exact zoom; view state, no undo;
  `CHECK zoom_field_edits_only`); density gains `-` and `+` cells that run Lower density / Raise density.
- Inspector (right, rebuilt from `World.Changed`): ruled tables, each section opening with a detail
  bubble (split circle: letter over sheet number). A Vanishing points: all nine slots (chip,
  number, document position, density; the selected row inverted; empty slots hatched).
  B Guides: grid, style, size, cone, level 1–2. C Camera: presets, FOV and station distance
  (hatched while the cone is off or cannot be built), saved rigs (click recalls), save rig.
  D Reference: show, opacity (hatched with no image), load. E Palette: the three palettes with
  their swatches. F Revisions: the last five undo entries, number and command name, newest first
  (`CHECK revisions_name_commands`). Every inspector edit runs the command its shortcut runs, so
  it pushes the same named undo step (`CHECK inspector_edit_undoes`).
- **Camera anchor.** On any viewport resize (panels or window) the camera shifts by half the size
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

## 7.1 Box tool (Wave 2)

`B` toggles box mode; `Escape` leaves it. A box is stored as four screen-space base points
`p0..p3` plus a height vector `h`; all geometry is derived at draw time. `p0` is the near corner,
`p1` lies on the first axis, `p2` on the second, and `p3` is the intersection of the line through
`p1` toward the second axis's vanishing point and the line through `p2` toward the first axis's
vanishing point. When those lines are parallel, `p3 = p1 + (p2 - p0)`.

Building is three press-drag-release gestures: the first sets `p1`, the second sets `p2`, the third
sets `h` (vertical, or along the line to VP3 when it exists). The first axis is chosen by the drag
angle against VP0/VP1; `1`/`2` force VP0/VP1 while dragging. `Backspace` cancels an in-progress
build first, then (when idle) deletes the last box.

The eight vertices are `p0, p1, p3, p2` and the same four translated by `h`. `p0` (the first
click) is taken as the near corner, so the two side faces meeting at `p0` face the viewer. Every
other face faces the viewer when its projected outward winding matches theirs: a box below the
horizon shows its top, a box above it shows its underside. Front faces are filled at 8 % ink; an
edge is solid when either face it joins is in front, otherwise dashed at 40 % alpha
(`CHECK box_visibility`). Boxes render
through the same `Prim` list in the viewport, PNG export and SVG export.

## 7.2 Perspective subdivision (Wave 2)

`D` toggles subdivision mode. Click a box face (the smallest face under the cursor wins) or click
four corners; then `2`–`9` set the cells per axis (`Shift`+digit sets the second axis, `+`/`-`
adjust the first, `Shift`+`+`/`-` the second), and `Enter` commits. `Backspace` cancels a pending
selection first, then deletes the last subdivision. With `Shift` held at the first click the tool
switches to edge mode: click the near
and far ends of one receding edge (before the second click, `1`/`2` force VP0/VP1), choose the
count, and `Enter`.

Face division uses the homography from the unit square to the quad; interior lines are drawn at
`u = i/N` and `v = j/M`, so `N` cells by `M` cells means `N-1 + M-1` interior lines. Edge division
uses the one-dimensional projective map: with `d_A`, `d_B` the distances from the edge's vanishing
point, division *i* sits at `d_i = d_A * N*r / (N*r + i*(1-r))`, `r = d_B/d_A`. A missing or
infinite vanishing point falls back to uniform spacing. Edge mode stores the chosen vanishing point
in `quad[2]` so the saved result does not change when a VP is moved later; ticks are drawn
perpendicular to the edge at 8 screen pixels.

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
signed distances from `P` must have opposite signs, otherwise the overlay draws nothing and the title block
cone field shows `needs two points`. With `d = sqrt(|x0| * |x1|)`:

- station point `S = P + (0, d)` in world units (below the principal point);
- field of view `FOV = 2 * atan((W / 2) / d)`, shown rounded to whole degrees;
- cone of vision = circle about `P` of radius `d * tan(30°)` (`≈ 0.57735 d`);
- a small screen-space warning tick beside any existing VP whose distance from `P` exceeds the cone
  radius.

The overlay is drawn dashed and renders in the viewport and both exports. Degenerate cases (a VP on
`P`, coincident VPs, same-side VPs, `d <= 0`) report unavailable and draw nothing.

## 7.5 Camera presets and saved rigs (Wave 3)

Presets use world coordinates and the current document size. The horizon sits at 60 % of the height
from the top (`y = 0.1 * H`):

- 1-point: VP0 at `(0, 0.1H)`, all other slots null.
- 2-point: VP0 at `(-0.35W, 0.1H)`, VP1 at `(0.35W, 0.1H)`, rest null.
- 3-point: as 2-point plus VP2 at `(0, -0.8H)`, rest null.

Unused slots are null and every density is 0. On a 640x480 document this is `(0, 48)`,
`(-224, 48)`/`(224, 48)` and `(0, -384)`. Applying a preset, saving a rig and recalling a rig each
push one undo snapshot. Saved rigs store a name and the nine `(position, density)` slots in
`World.Rigs` and the project file; saving over an existing name (case-insensitive) replaces it.
Cancelling the name prompt saves nothing and shows no message; an empty name shows
`Rig name cannot be empty.` The Camera menu shows each name as plain text, underscores included.
Each rig in the Camera menu has a submenu: Recall, Rename… (same prompt; a name another rig
already uses is refused) and Delete. Rename and Delete each push one undo snapshot
(`CHECK rig_manage`).

## 8. Data and constants

| Item | Value |
|---|---|
| Document default | 640 x 480 |
| `CrazyFar` | `max(W,H) * 32` |
| Zoom range | 0.05 … 5, wheel step ×1.1 / ÷1.1 |
| VPs | fixed array of 9, each `null` or `{ Pos, Density }`; `Density` steps of 4, clamped to 0–384 (shown as 0–96) |
| Trace lines | list, at most 2; a new one evicts index 1 then appends |
| Grid | `ShowGrid` false, `GridStyle` 0, `GridSz` 32 |
| Reference image | image or null; opacity 1, flip H/V false, scale 1, offset (0, 0), hidden false |
| Palette | `ink` (new documents), `atelier` (fallback for an unknown name and for a project file that names none, so older files keep their look), `charcoal`; `S` rotates the nine assignments, `Ctrl+Shift+P` cycles (`CHECK default_palette_ink`) |
| Cone of vision | `showCone` false by default; `V` toggles; radius `d * tan(30°)` |
| Rigs | named nine-slot camera snapshots; empty until the user saves one |

`Vec2.Length()` returns `sqrt(x²+y²)`. The reference named it `GetSizeSquared()` although it
returns the length; the port renamed it (polish stage, R1). `DistFrom` is a real distance.

## 9. Input

Mouse (on the render surface):
- Left press: pick the **first** VP whose distance to the cursor is `< 32/Zoom` (iterate 0..8,
  break at first match) → drag it. If none, selection becomes -1.
- Middle press/drag: pan the camera by `delta / Zoom`.
- Right press/drag: start a trace line at the cursor; while dragging, `Ctrl` snaps the end to 5°
  increments and `Shift` to 15° — but only when that modifier is held **alone** (the reference
  compares the full `ModifierKeys` mask). On release, if the mouse moved, push the line (max 2).
- Wheel: zoom about the cursor (§5).
- Cursor: cross.

Keyboard (surface focused):
- Every command is one entry in one table (`ViewportControl.BuildCommandTable`, type `Command`:
  name, group, gesture, keys, action). The key handler, the menus, the `F1` overlay, Find and the
  inspector all read it, so they cannot drift apart (`CHECK commands_unique_gestures`: no key is
  bound twice, names are unique, a command without a key is in a menu; `CHECK help_matches_commands`).
  Keys are looked up by key and exact modifiers; the active construction tool sees each key first.
  Every undo snapshot carries the name of the command that pushed it. `Shift` is also accepted for `+`, `-`, `[` and
  `]`, which need it on many layouts. Any other modifier combination does nothing.
- `Escape` cancel mode, deselect all.
- `Home` centre camera: fit the plane (zoom at most 1, clamped to 0.05–5); a viewport with no size
  (for example a minimised window) keeps the current view. `Ctrl+R` clear document (VPs, trace lines, background, grid off; size kept).
- `Ctrl+N` new document (asks first when there are unsaved changes). `Ctrl+Alt+C` resize dialog
  (fields prefilled with current W/H; values validated in `[1, 8192]`; OK
  applies and clears the background image, then re-centres).
- `1`…`9` drop/move VP at the mouse; `Ctrl+1…9` select an existing VP; `Alt+1…9` delete one;
  `Ctrl+Shift+1…9` drop a VP at the intersection of the two trace lines (only if they intersect
  inside the extended segments) and clear them; otherwise a note says the lines do not cross.
- `Ctrl+arrows` (no other modifier) mirror the selected VP across an axis (`Up/Down` → `Pos.Y *= -1`,
  `Left/Right` → `Pos.X *= -1`); plain `Up/Down` change `Density` by ±4 (clamped to 0–384, so the title block shows at most 96). Every
  `VanishingPoint` clamps its density when created, so a project file cannot ask for more.
- `G` toggle grid, `Ctrl+G` cycle style, `+`/`-` grid size ×2 / ÷2 (floor 2).
- `H` (no modifier) set VP[0] and VP[1] `Pos.Y` to the cursor's world Y. `H`, `Up`/`Down` and
  `Ctrl+arrows` do nothing when another modifier is held.
- `I` toggle background, `Alt+I` remove it, `Ctrl+I` open-file dialog (image filter) and load —
  loading sets the document size to the image size and re-centres. `Ctrl+V` paste an image from
  the clipboard with the same effect.
- `Ctrl+C` copy the plane at zoom 1 as a W×H bitmap (no chrome, no help, no viewport background);
  `Ctrl+Shift+C` the same in grayscale (0.299/0.587/0.114 row sums).
- `Ctrl+Z` undo, `Ctrl+Shift+Z` / `Ctrl+Y` redo; the history is 50 deep and redo clears on a new
  action. A pointer drag, key command, dialog OK and menu command each push exactly one snapshot
  before mutating. A left or right click that does not move pushes nothing. Pan and zoom are view
  state: they push nothing, and undo/redo keep the current zoom and pan. A wheel event with no
  vertical delta (a sideways trackpad swipe) does not zoom.
- `Ctrl+S` save, `Ctrl+Shift+S` save as, `Ctrl+O` open a `.chitin.json` project. The window title
  is `<file name> - Chitin` (`Untitled` before the first save) and gains ` *` while there are
  unsaved changes (`CHECK window_title`); closing, File > New and Open each ask before
  discarding unsaved changes (Discard / Cancel). Open clears the undo history, so an undo can never
  put the previous document back under the opened file's name. Save and Export write a local file
  as `<name>.tmp` beside it, then rename it over the target, so a failed write leaves the old file
  intact; a non-local storage item is written in place.
- `Ctrl+E` opens the export dialog: PNG or SVG, pixel width/height (defaults to the document size),
  and a transparent-background checkbox for PNG. Width and height are linked to the plane's aspect
  ratio (editing one sets the other; the longer side is capped at 8192), so PNG and SVG cover the
  same area (`CHECK export_aspect`).
- `[` / `]` decrease/increase reference-image opacity by 10 % (clamped to 0–100 %), `Ctrl+Alt+I`
  opens the reference dialog (opacity, scale 25–400 %, horizontal/vertical flip, offset X/Y, Fit to
  Plane, Reset). Dialog changes apply live through `World.SetReferenceSettings`, which bumps the
  revision and reports a status message when a value is clamped. Cancel puts the previous settings
  back and, if the document was saved before the dialog opened, leaves it marked as saved.
- `B` toggles the box tool: three left-drags set the first axis, the second axis and the height;
  `1`/`2` force VP0/VP1 for the first axis while dragging; `Backspace` deletes the last box;
  `Escape` leaves the tool.
- `D` toggles the subdivision tool: click a face or four corners, `2`–`9` set the cells per axis,
  `+`/`-` adjust them, `Enter` commits; hold `Shift` at the first click for edge mode;
  `Backspace` deletes the last subdivision; `Escape` leaves the tool.
- `E` toggles the ellipse tool: click a face or four corners, then drag from the centre to set the
  radius; `Backspace` deletes the last ellipse; `Escape` leaves the tool.
- Four clicked corners must form a convex shape in click order; a bow-tie or dented shape is
  refused with a status message and the corners are cleared (`CHECK tool_pick_quad`).
- A tool takes only its own unmodified keys (and `Shift` + digit / `+` / `-` in the subdivision
  tool). Keys with `Ctrl` or `Alt`, and box-tool `1`/`2` outside the first-axis drag, reach the
  viewport, so point selection, deletion and `Alt+Backspace` work while a tool is active.
- `S` rotate the palette assignment by one (no randomness); `Ctrl+Shift+P` cycle palettes.
- `V` toggle the cone-of-vision / station-point overlay.
- `Backspace` delete the last trace line, `Alt+Backspace` all. `F1` toggle the help overlay.
- **Find** (`Ctrl+F`, or click the field in the menu bar): typing lists the matching commands
  under the field as a ruled table (name, group, gesture). Matching is per word,
  case-insensitive, over name and group (`Commands.Find`, `CHECK find_matches_words`: "grid sty"
  finds only Cycle grid style). Up/Down move, `Enter` runs, `Escape` closes and returns focus to
  the viewport; the list stays closed until the next keystroke in the field (`CHECK find_closes`). Find reads the command table, so it lists nothing the menus or keys lack. A
  1–9 command run from Find only names its keys in a note.
  While a text field (Find, zoom) has focus, only `Ctrl+K` and `Ctrl+F` pass to the command table,
  and moving the pointer over the viewport does not take the focus away.
- Menus: File, Edit, View, Tools, Reference, Camera, Help, built from the command table in table
  order; each item shows the command's first key and runs the command
  (`ViewportControl.RunCommandAsync`) (`CHECK menu_gestures`: every shown shortcut is bound). Undo and Redo are
  disabled when there is nothing to undo or redo. Camera menu: 1-Point / 2-Point / 3-Point presets,
  Save Rig…, and the saved rigs. While the menu has focus, a bound key with Ctrl or Alt still
  reaches the viewport; other keys stay with the menu. `CHECK window_keys_once` (real key presses on
  the main window run once; a menu click and a forwarded key work) and `CHECK menu_state` (Undo/Redo
  enabled state; the rig submenu lists and deletes a rig).

## 10. Clipboard / export / project file

- Copy: render into `RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96))` via
  `CreateDrawingContext()`, save/convert to `Avalonia.Media.Imaging.Bitmap`, then
  `await clipboard.SetBitmapAsync(bitmap)` (extension in `Avalonia.Input.Platform.ClipboardExtensions`).
- Paste: `var bmp = await clipboard.TryGetBitmapAsync()`.
- Grayscale: apply the colour matrix by drawing through a `ColorMatrix`-equivalent — simplest is
  per-pixel `unsafe`-free work on the `WriteableBitmap`/`Bitmap` via `CopyPixels`/`Lock()`, or draw
  with a `ColorMatrixFilter`-style effect if available in 11.3. Do not add an imaging package:
  `Bitmap.Lock()` + `ILockedFramebuffer` is the way.
- Project file: one self-contained JSON file, `.chitin.json`, written with `System.Text.Json`.
  Schema (version 1): `version`, `doc` (`w`, `h`), `zoom`, `camera` (`[x, y]`), `vps` (nine entries,
  each `{x, y, density}` or `null`), `traceLines` (`{x0, y0, x1, y1}`), `grid` (`show`, `style`,
  `size`), `boxes`, `subdivisions`, `ellipses`, `reference` (`opacity`, `flipH`, `flipV`, `scale`,
  `offset`, `hidden`), `rigs` (`{name, vps}` entries), `palette`, `paletteOffset`, `showCone`,
  `backgroundPng` (base64 PNG or `null`). A missing `version` reads as 1; a `version` above 1 is
  refused with a status message, because this build would drop fields it does not know. Unknown
  keys are ignored;
  malformed JSON leaves the current document untouched and shows the error in the status bar. The
  background image is embedded in the project and is part of each undo snapshot (in memory, by
  reference): undo after removing, clearing, resizing or replacing it brings back the previous
  image. A bitmap is disposed only when it is neither current nor held by an undo or redo snapshot.
  Files whose encoded background exceeds 64 MB, or whose image header gives more than 40
  megapixels, are refused with a status message. Ctrl+I and project load read the size from the
  header (`World.DecodeImage`, SkiaSharp `SKCodec`) before the full decode. Box entries are
  `{"p0":[x,y],"p1":[...],"p2":[...],"p3":[...],"h":[x,y]}`. Subdivision entries are
  `{"quad":[[x,y],[x,y],[x,y],[x,y]],"n":N,"m":M,"edge":false}`; edge mode sets `quad[2]` to the
  vanishing point (and `m` is 1). Ellipse entries are
  `{"quad":[[x,y],[x,y],[x,y],[x,y]],"cx":Cx,"cy":Cy,"r":R}` with the centre and radius in
  unit-square coordinates. Rig entries are `{"name":"Bedroom","vps":[{"x":0,"y":48,"density":0},
  null, …]}` with exactly nine slots.
- SVG export (`SvgWriter`) consumes the same `GuideBuilder` primitives as the screen at zoom 1,
  skips `ScreenOnly` primitives, honours each primitive's clip rect, emits stroke/fill opacity from
  the ARGB alpha, dash arrays from the pen, and text as `<text>` with the same sizes and a serif or
  sans-serif family. The viewBox is the document rect in world units. PNG export uses
  `RenderTargetBitmap` at the requested size with `Zoom = width / documentWidth` and a centred camera;
  transparent mode omits the plane fill. Both exports contain guides only (no chrome, help or reference
  image).

## 11. Self-test (mandatory, this is the acceptance gate)

`--selftest <path>`: initialise the headless platform
(`AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting()`),
build this exact scene, and write three PNGs (`<path>`, a `-export` sibling and a `-shapes` sibling
for the construction scene):

- document 640 x 480, zoom 1.25, camera `(560, 360)` (the viewport is centred on the document);
- VPs: 0 at `(-420, -140)` density 0, 1 at `(380, -150)` density 0, 2 at `(60, -900)` density 8,
  4 at `(1500, 60)` density 6 (this one exercises the off-screen pointer lines);
- trace lines `(-500, 300) → (200, -250)` and `(0, -400) → (300, 350)`;
- grid on, style 0, size 40; help overlay on for the viewport render, off for the export;
- viewport render `1400 x 900`, export render `W x H` with `RenderMode.OpaqueExport`;
- selection index 2 (selected VP must be gold);
- print `CHECK <name> PASS|FAIL` for every named assertion, then `SELFTEST_OK <path1> <path2>
  <path3>` and exit 0; on failure or a failed check, exit non-zero.
- The gate scene and the shapes scene pin the `atelier` palette, so their export hashes stay
  comparable across the change of default palette; there is no RNG in the render path.
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
  point), `CHECK rig_roundtrip` (save, serialise, load, replace-by-name), `CHECK palette_stable`
  (two builds byte-identical) and `CHECK palette_no_random` (pen colours equal the palette in order,
  and rotate by one after `S`). Polish stage 1 adds `CHECK key_handled_before_await` (Ctrl+Alt+C marks
  the key handled while its dialog is still open, so the window does not run it again) and
  `CHECK undo_reference_image` (undo brings the image back after Remove and Clear; the image is
  disposed once when its last snapshot leaves the 50-step history), `CHECK open_clears_history`
  (a loaded project has no undo and is not dirty) and `CHECK confirm_discard` (New on a dirty
  document asks first; Cancel keeps it, Discard clears it, a clean document skips the dialog) and
  `CHECK save_atomic` (a second write replaces the file and leaves no `.tmp`; a write that fails
  before the rename leaves the previous content). Polish stage 2 adds `CHECK pointer_undo` (headless
  mouse input: clicks and a pan add no undo step, a sideways wheel does not zoom, a point drag adds
  one, and undo keeps the panned view) and `CHECK tool_keys_pass` (`Ctrl+1`, `Alt+Backspace` and
  `Alt+2` reach the viewport inside tools; plain `2` stays with the subdivision tool and drops a
  point in the box tool) and `CHECK density_clamp` (file densities 1e8 and -5 load as 384 and 0;
  `Up` stops at 384; the fan builder clamps a raw field value) and `CHECK image_header_guard` (a
  2×2 PNG decodes; the same PNG with its IHDR patched to 60000×60000 is refused before decode) and
  `CHECK rig_cancel_silent` (Cancel in Save Rig adds no rig and no status message) and
  `CHECK center_camera_empty` (centring a viewport with empty bounds keeps zoom 1 and camera 0,0)
  and `CHECK reference_cancel_clean` (a live opacity change, then Cancel: opacity back to 100 %, not
  dirty, no undo step) and `CHECK key_modifiers` (`Shift+Up`, `Alt+H` and `Ctrl+Shift+Left` change
  nothing; `Ctrl+Shift+3` without crossing lines drops no point and shows the status message) and
  `CHECK project_version` (version 2 is refused; a saved file carries version 1) and `CHECK tool_pick_quad`
  (headless clicks: four corners and Enter give one subdivision; one click on a box face and a drag
  give an ellipse on a face of that box). `CHECK alloc_render` is relative: a frame that reuses the cached
  guides allocates at most two thirds of a frame that rebuilds them. An unhandled UI-thread error
  goes to stderr and to the status line as `Error: <message>`; the app keeps running.
  The `-shapes` PNG contains stacked boxes, a subdivided face and an
  ellipse; the interactive and export PNGs move when the palette or help rows change.

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
- Boxes, subdivisions and ellipses store screen-space points; moving a vanishing point afterwards
  does not re-project them. Re-projecting would need a live camera model.
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
- `S` rotates the nine palette assignments instead of shuffling random colours, and `Ctrl+Shift+P`
  cycles curated palettes. There is no randomness in the drawing colours.
- The station-point/cone overlay (item 6) and camera presets/rigs (item 9) are new; they do not
  change any reference behaviour when off.
