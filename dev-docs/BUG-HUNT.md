# Bug hunt (read-only pass, 2026-10-24)

Status: every finding below, minor ones included, is fixed, each with a self-test check.

Scope: every file under `src/` except the self-test, read in full; the self-test was grepped for what
it asserts. Nothing was fixed. Two findings were confirmed with a scratch .NET 8 program (marked
*verified*); the rest come from tracing the code. The app was not driven by hand.

Ordered by impact. Line numbers refer to the working tree at commit `7dec6f5`.

---

## 1. A reference image that loads fine makes the project unsaveable

**Where:** `src/IO/ProjectFile.cs:36-42`, `src/Model/World.cs:35-36`, `src/Viewport/ViewportControl.cs:1247-1256`

Load accepts any image up to 40 megapixels (`MaxDecodedImagePixels`) and any *source file* up to
64 MB. Save re-encodes the bitmap as PNG and throws when the base64 is over 64 MB
(`MaxEncodedImageBytes`). A 30-40 MP photograph is an 8 MB JPEG on disk but 80-120 MB as base64 PNG,
so it passes every load check and then every save fails with "Background image is larger than
64 MB encoded".

Consequences: Ctrl+S fails after the file picker; "Save" in the close/open/new confirmation fails,
so `ConfirmDiscardAsync` returns false and the window cannot close through that path; the error
text does not tell the user to remove the reference. The two limits were never reconciled.

**Repro:** load a 7000×5000 photo (35 MP), Ctrl+S.

---

## 2. Face picking prefers hidden back faces

**Where:** `src/Geometry/BoxGeometry.cs:138-168` (`HitFace`), used by `ConstructionMode.PickQuad`
(`src/Viewport/ConstructionMode.cs:65-73`)

`HitFace` tests all six `Faces` and returns the one with the smallest projected area, ignoring
`VisibleFaces`. In perspective a back face is farther away, so it projects *smaller* than the front
face it overlaps, and the hidden back faces always project inside the box silhouette. Clicking a
visible side face anywhere in the band near its far vertical edge therefore returns the hidden face
behind it, and the subdivision or ellipse is drawn on a face the user cannot see, following the
wrong plane.

The self-test only clicks the exact centre of face 2, where the back face does not reach.

**Repro:** 2-point preset, B, draw a box, E, click the front-left face about a quarter of the way
in from its right edge. The quad outline that appears is the dashed back face.

---

## 3. A new document starts dirty, and some no-op commands dirty a clean one

**Where:** `src/Viewport/ViewportControl.cs:782-790` (`NewDocument`), `src/Model/World.cs:276-278`
and `:558-560` (`Restore` → `ResetDocument` → `BumpRevision()` with `document: true`)

`NewDocument` restores an empty state, which bumps the revision as a document change, and never
calls `MarkSaved`. Right after Ctrl+N the title reads "Untitled *" and closing the window asks to save
an empty document. `LoadProject` gets this right by calling `MarkSaved()` after `Restore`.

Same family:

- `World.SetReferenceImage(null)` with no image (`World.cs:307-318`) still bumps the revision, so
  Alt+I on a document without a reference marks it unsaved.
- `ToggleReference` (`ViewportControl.cs:985-992`) pushes an undo entry and flips `Hidden` even when
  there is no image: a phantom "Show / hide reference" undo step plus a dirty flag.

---

## 4. Clicking to place a point fails on any pointer that jitters

**Where:** `src/Viewport/ViewportControl.cs:246-249`, `src/Viewport/MouseMode.cs:27-33`

Placement requires `VanishingPointMode { HasMoved: false }`, and `HasMoved` is set by *any*
pointer-move event, including one with zero delta. Stylus, touch and high-resolution mice routinely
emit a move between press and release, so the click does nothing. The box and ellipse tools solved
the same problem with a 4 px threshold (`ConstructionMode.cs:126,168`); the points tool did not.

Related: `TraceLineMode.MouseUp` (`MouseMode.cs:150-165`) uses the same flag, so a jittery
right-click adds a zero-length trace line. `AddTraceLine` then normalises a zero vector to zero and
emits an invisible line, which silently consumes one of the two trace-line slots.

---

## 5. Ruler half-major ticks land on the wrong units (*verified*)

**Where:** `src/Geometry/Sheet.cs:17,23`

`major / 2` is integer division. When `major = minor * 5` and `minor` is odd (5 or 25), the "half
major" spacing is 12 or 62 instead of a non-integer that should produce no level-1 ticks at all.
Scratch run of the same arithmetic:

| zoom | minor | major | major/2 | level-1 ticks at |
|------|-------|-------|---------|------------------|
| 2.0  | 5     | 25    | 12      | 60, 120, 180 …   |
| 0.4  | 25    | 125   | 62      | 1550, 3100 …     |

At 200 % the ruler shows medium ticks at 60, 120, 180 and none at 10, 20, 30. The self-test checks
only positions 1 and 5 at zoom 2 (`SelfTest.cs:766-772`), which happen to be right.

---

## 6. Project files can carry infinities into the model (*verified*)

**Where:** `src/Model/World.cs:177-190, 283, 353` (`Restore`), `src/IO/ProjectFile.cs:124-204`

`JsonSerializer` turns `1e999` into `double.PositiveInfinity` rather than failing (checked on
.NET 8). `Restore` validates boxes, subdivisions and ellipses for finiteness but not vanishing
points, trace lines, `Camera` or `Reference.Offset`. A point at infinity gives NaN guide geometry;
an infinite camera gives NaN for every screen-to-world conversion. Worse, the next Save throws,
because `Utf8JsonWriter.WriteNumberValue(Infinity)` is not valid JSON, so the document cannot be
saved to fix it. Only reachable through a hand-edited or corrupted file, but the loader claims to
reject bad files.

---

## 7. "Level points 1 and 2" uses a stale pointer position from the menu and Find

**Where:** `src/Viewport/ViewportControl.cs:584-602, 455`

`LevelHorizon` sets both Y values to `_mouseWorld.Y`, the last pointer position *over the viewport*.
The command is not `Indexed`, so it is also in the Points menu and in Find. Run from there, the
pointer is over the menu or the Find box and the horizon jumps to wherever the cursor last crossed
the plane. The cursor-dependent 1-9 commands were kept out of the menus for exactly this reason.

---

## 8. Guide lines get thicker as you zoom in (probable intent mismatch)

**Where:** `src/Viewport/Styles.cs:84-87`

`PenWidth` returns `Math.Max(1, width / Zoom)` in world units. For zoom ≤ 1 that is a constant
screen width, which is the stated intent. For zoom > 1 the floor wins: at 500 % a "1 px" guide is
5 px on screen, at 200 % it is 2 px. Every other zoom-dependent size in the renderer (`16 / Zoom`,
`20 / Zoom`, hover ring) is held constant on screen, so the floor looks like an accident rather
than a design choice. Flagged as probable, not certain.

---

## 9. A second button mid-drag silently replaces the active drag

**Where:** `src/Viewport/ViewportControl.cs:100-160, 242-257`

`OnPointerPressed` builds a new `MouseMode` for every button press and assigns it to
`World.Overlay`; `OnPointerReleased` does not check which button was released. Pressing the middle
or right button while left-dragging a point discards the `VanishingPointMode` (its undo entry is
already pushed), the first release restores `Overlay = _construction`, and the still-held left
button no longer moves anything. Nothing crashes; the drag just dies and the undo history has an
entry for a half-finished move.

---

## 10. Odd plane sizes export half a pixel off

**Where:** `src/Model/World.cs:656-657`, `src/Viewport/Prim.cs:90-94`,
`src/Viewport/ViewportControl.cs:1347-1359`

`PicturePlaneWidthHalf = width / 2` is integer, so for a 641 px plane the plane rect runs from
−320 to +321. `RenderPlane` centres the camera at `width / 2.0`, which puts the plane's left edge
at x = 0.5 and its right edge at 641.5 in the bitmap. Clipped guides extend half a pixel past the
image; the left column is half-covered. Only odd widths or heights.

---

## 11. First render with zero bounds never centres the camera

**Where:** `src/Viewport/ViewportControl.cs:52-62, 78-93`

`Render` sets `_centered = true` *before* calling `CenterCamera`, which returns early when
`Bounds` is empty. If the first render happens before layout gives the control a size, the camera
stays at (0, 0) with the plane's centre at the viewport's top-left corner, and `OnSizeChanged` only
keeps the centre from then on. Unlikely under Avalonia's normal layout order; cheap to make robust.

---

## 12. A point exactly on the plane edge loses its corner guides

**Where:** `src/Viewport/Prim.cs:339-385`

The outside test uses `<`/`>` and the nine region branches use strict inequalities on both axes, so
a point with `X == -halfWidth` (or any edge coordinate) and the other coordinate outside the plane
matches the outer `if` but none of the branches. No lines to the plane corners are drawn. Typed
coordinates in the points panel make this easy to hit (type 0 for X).

---

## 13. Undo coalescing merges unrelated flips

**Where:** `src/Viewport/ViewportControl.cs:471-474, 997-1014`

`ChangeReference` skips `PushUndo` when the top undo entry has the same name. Both flip commands are
named "Flip reference", so flipping horizontally and then vertically is one undo step that reverts
both. The same rule also coalesces two opacity runs separated by any number of view-only actions
(zoom, pan, palette), because those never push.

---

## 14. Skipping invalid boxes on load shifts every shape's box index

**Where:** `src/Model/World.cs:192-211, 225, 240`

`Restore` drops a box with non-finite corners but keeps the shapes that referenced later boxes by
index, so a subdivision on box 2 now follows box 1. Corrupt input only, but the loader's own
validation causes the mismatch.

---

## 15. Version check only works for integer-looking versions (*verified*)

**Where:** `src/IO/ProjectFile.cs:66-72`

`TryGetValue<int>` fails for `"version": 1.0` (checked), so a future `2.0` file is read as version 1,
its unknown fields dropped, and a later Save overwrites it silently.

---

## 16. Far trace-line crossings are reported as "do not cross"

**Where:** `src/Geometry/Geom.cs:36-67`, `src/Model/World.cs:666`

`GetLineIntersection` extends each segment by `CrazyFar` (32 × the longer document side) and
accepts the crossing only inside that range. Two nearly parallel trace lines aimed at a distant
vanishing point do cross, but beyond ~20 000 units on a 640 px document, and Ctrl+Shift+N says
"Trace lines do not cross". Distant vanishing points are the normal case for shallow perspective.

---

## Minor

- Alt+wheel scales the reference about the plane centre, not the pointer or the image centre, so
  an offset image drifts while scaling (`Renderer.cs:117-125`, `ViewportControl.cs:265-271`).
- `Restore` runs `ResetDocument`, which fires `Changed` while the world is half-restored (old points,
  new size). Listeners only schedule a chrome refresh today, so it is harmless until one reads state
  synchronously (`World.cs:278`).
- `ApplyCoords` compares the typed text to the formatted integer, so typing `100.4` into a field that
  shows `100` moves the point by 0.4 and the field snaps back to `100` (`MainWindow.Sheet.cs:370-385`).

## What was checked and how

- Scratch .NET 8 console program (not kept in the repo): `1e999` → `∞` through `JsonSerializer`;
  `"version": 1.0` fails `TryGetValue<int>`; `Math.Clamp(NaN, …)` returns NaN; the `Sheet.Ticks`
  arithmetic reproduced the table in finding 5.
- Self-test coverage was read for findings 3, 5 and 2: none of the three cases is asserted.
- Not exercised: the window by hand, clipboard, file pickers, macOS/Linux.
