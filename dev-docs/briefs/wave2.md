# Wave 2 — implementation brief (construction geometry)

One session, per `dev-docs/PLAN.md` Working rule 7: implement items 4, 5 and 7, then review your own
diff, fix what the review finds, re-run the gate, and only then report the wave closed.

`dev-docs/PLAN.md` **Wave 2** is the spec (box tool, subdivision, ellipse). Read it fully, plus SPEC's
Deviations section. The shape you inherit from Waves 0–1:

- `Vec2` is a `readonly struct`; `GuideBuilder.Build(World) -> Prim[]` is cached on
  `(World.Revision, Zoom)` and must be invalidated by a `BumpRevision()` on every mutation.
- `Prim` supports Line / Circle / Text with `Pen`, `Brush`, `A`, `B`, `Radius`, `Text`, `Size`, `Serif`,
  `ScreenOnly`, `Clip`. **Extend it with what this wave needs** (`Poly` and a filled polygon), keeping
  both consumers — `Renderer` and `SvgWriter` — in step. No third renderer.
- `WorldState` + `Capture`/`Restore`, `.chitin.json` with `boxes`/`subdivisions`/`ellipses` keys
  already reserved, `UndoStack`, `StatusMessage`, `RenderMode` (Interactive / OpaqueExport /
  TransparentExport), `ExportDialog`, `ReferenceSettings`.

## Shared math (write it once, pure, testable)

- `Homography.FromUnitSquare(Vec2 a, Vec2 b, Vec2 c, Vec2 d)` — 3×3 solved from four correspondences, with
  `Map(u, v)`. Invertible-case guard.
- `PerspectiveEdge.Divide(Vec2 near, Vec2 far, Vec2 vp, int n)` — distances from the VP follow
  `d_i = d_A · n·r / (n·r + i·(1−r))`, `r = d_B/d_A`. Shortcuts: `r == 1` (VP at infinity) → uniform.
- `BoxGeometry.Build(...)` — the fourth base corner is the intersection of the two receding lines, with
  the parallel fallback `p1 + (p2 − p0)`.

## Item 4 — box tool

Three-stage drag as the plan describes (`B` toggles, `1`/`2` force an axis, `Backspace` deletes the last
box, Esc exits). Store `p0..p3` + height vector. Derive all 12 edges and 6 faces at draw time; hidden
edges dashed at 40 % alpha; visible faces filled at 8 % ink. Boxes must render identically in the
viewport, the PNG export and the SVG export.

## Item 5 — subdivision

Face mode (click a face of an existing box, or four corners; digits `2`–`9` set the count per axis,
`+`/`-` adjust, Enter commits) through the homography; edge mode with `Shift` at the first click using
`PerspectiveEdge.Divide`. Store the quad + counts; recompute at draw time.

## Item 7 — ellipse in a plane

Same homography, 128 samples of the inscribed unit circle, closed poly. Face or four corners, then a
centre-and-radius drag. `E` toggles, `Backspace` deletes the last.

## State and schema

Wire `Boxes`, `Subdivisions`, `Ellipses` into `World`, `WorldState.Capture`/`Restore` (deep copies),
`WorldState.ContentEquals` (currently count-only — it must compare contents, or `undo_restores` and
`project_roundtrip` become vacuous), `.chitin.json` and the revision counter. Every mutation path
(create, delete, clear, restore, load) bumps.

## Self-test

New named checks, each able to fail:

- `CHECK homography_identity` — unit square onto itself returns the inputs.
- `CHECK subdivision_centre` — for a projected square (fixed VP rig), the (0.5, 0.5) image equals the
  quad's diagonal intersection within 1e-6.
- `CHECK subdivision_counts` — N=4, M=3 → 4+3 interior lines, 12 cells.
- `CHECK edge_division` — d_A=300, d_B=150, N=2 → midpoint at 200 from the VP; N=4 → 240, 200, 171.43.
- `CHECK box_geometry` — exact 8 vertices for fixed inputs; parallel-axis fallback path.
- `CHECK ellipse_is_circle` — screen-parallel square: all 128 samples within 0.5 px of R.
- `CHECK ellipse_perspective` — projected square: polyline inside the quad, touching the four edge
  midpoints within 1e-4.
- Extend `project_roundtrip` to contain one box, one subdivision and one ellipse and compare contents.

Render the new geometry into a **third** PNG (`<path>-shapes.png`) with a dedicated scene (stacked boxes,
a subdivided face, an ellipse), so the two existing baseline PNGs stay byte-identical and their hashes
remain the stability witness. Report the new hash of the shapes PNG, which becomes the Wave 3 baseline.

## Rules

- Ponytail active: no new dependencies, no files beyond `Homography`/`BoxGeometry`-style helpers the
  items need, no interfaces with one implementation.
- Query the semantic index first (`~/AI-Projects/semantic-search/bin/semsearch search "<q>"`), else `rg`;
  never rebuild it.
- Repo only; never edit `reference/`. One commit per item (`Item 4: …`, `Item 5: …`, `Item 7: …`), each
  with the checks it passes. Never commit a tree that does not build.

## Closing phase (same session)

1. Review your own diff for defects and gaps: geometry edge cases (degenerate quad, collinear points,
  VP at infinity, division by a zero determinant), undo/restore completeness for the new lists,
  revision coverage for every new mutation, SVG/PNG/viewport parity for the new prims, and plan
  compliance.
2. Fix what you find, or refuse with a reason.
3. Run `dev-docs/verify.sh` until `GATE_OK`; confirm the two baseline hashes unchanged and report the
   shapes hash.
4. Commit `Wave 2 review: <n> findings fixed`, then report: commits, findings fixed/refused, gate
   output, hashes, and what Wave 3 needs.
