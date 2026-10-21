# Wave 1 — implementation brief (foundation)

One session, per `dev-docs/PLAN.md` Working rule 7: implement items 1, 2 and 3, then review your own
diff, fix what the review finds, re-run the gate, and only then report the wave closed.

`dev-docs/PLAN.md` **Wave 1** is the spec. `SPEC.md` describes the behaviour to preserve. The code shape
after Wave 0:

- `Vec2` is a `readonly struct` with `Normalized()`.
- `GuideBuilder.Build(World) -> Prim[]` caches by `(World.Revision, Zoom)`; `World.BumpRevision()` must be
  called by **every** mutation of document state, or the render goes stale. `Prim` carries `Kind`
  (Line/Circle/Text), `Pen`, `Brush`, `A`, `B`, `Radius`, `Text`, `Size`, `Serif`, `ScreenOnly`, `Clip`.
- `World` already owns: document/grid clamps, `SetGridSize`, `SetReferenceImage(Bitmap?)` with disposal
  and the 64 MB / 40 MP guards, `StatusMessage` + `StatusChanged`, `Revision`.
- `ViewportControl` has guarded async handlers, `RenderPlane()`, and window-level accelerator routing
  (`Ctrl+S/O/E/Z` are routed but have no commands yet — this wave gives them commands).

## Item 1 — undo/redo + project save/load

- Introduce `WorldState` covering **the full document as it will exist after Wave 3**: document size,
  zoom, camera, nine vanishing points, trace lines, grid, reference settings (define the record now even
  though item 3 fills in the behaviour), palette name (default `"atelier"`), and empty lists for boxes,
  subdivisions and ellipses so later waves extend the schema rather than rewrite it.
- `World.Capture()`, `World.Restore(state)` (restores pens, bumps revision), `UndoStack` depth 50,
  redo cleared on push. Push **before** a mutation, once per action: one push per pointer drag, one per
  key command, one per dialog OK, one per menu command. `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`.
- Project file `.chitin.json` per the schema in the plan, `System.Text.Json`, `Ctrl+S` /
  `Ctrl+Shift+S` / `Ctrl+O`, File menu entries. Background image embedded as base64 PNG; refuse > 64 MB
  encoded with a status message. Malformed JSON must never crash: status message, document untouched.
- Dirty flag: window title gains ` *` while unsaved; closing with unsaved changes asks before discarding.

## Item 2 — SVG and transparent-PNG export

- `SvgWriter.Write(World, width, height) -> string`, consuming `GuideBuilder` primitives (build them at
  zoom 1 with a centred camera so the viewBox is the document rect). Skip `ScreenOnly` primitives (HUD),
  honour `Prim.Clip`, emit `stroke-opacity` / `fill-opacity` from the ARGB alpha, dash arrays from the
  pen's dash style, and `<text>` with the same sizes (serif/sans as `font-family`).
- PNG: `RenderTargetBitmap` at the requested size with `Zoom = width / documentWidth`, centred camera,
  guides only. Transparent mode skips the plane fill and frame — extend the renderer's boolean
  `screenshot` into an explicit mode (`Interactive` / `OpaqueExport` / `TransparentExport`) rather than
  adding a second boolean.
- `ExportDialog`: format (SVG/PNG), width, height, transparent checkbox; defaults to the document size,
  PNG, opaque. `Ctrl+E`, File menu entry.

## Item 3 — reference-image controls

- `ReferenceSettings { double Opacity = 1; bool FlipH, FlipV; double Scale = 1; Vec2 Offset; bool Hidden }`
  on `World`; `SetReferenceSettings` bumps revision and fires the status message when a limit is hit.
- Draw the image clipped to the plane, `PushOpacity`, transform = translate(offset) × scale × flip about
  the plane centre. `I` toggles `Hidden`, `[` / `]` change opacity by 10 %, `Ctrl+Alt+I` opens
  `ReferenceDialog` (opacity, scale, flip, offset X/Y, Fit, Reset) with live preview.

## Checks the self-test must gain (all must PASS, and each must be able to fail)

`CHECK undo_restores`, `CHECK project_roundtrip`, `CHECK svg_well_formed` (parse with `XDocument.Parse`,
≥ 100 `<line>`, every stroke a valid hex), `CHECK png_transparent` (corner alpha 0, ≥ 1 % opaque),
`CHECK reference_transform` (2×2 test bitmap: flipH swaps the red/green sample points; opacity 0.5 over
white reads ≈ (255,128,128) ±2). Every Wave 0 check must still pass; the interactive/export PNG hashes
must stay unchanged unless a deliberate behaviour change moves them (say which and why).

## Rules

- Ponytail active: no new dependencies, no MVVM, no service layer, no files beyond those the items name.
- Query the semantic index first: `~/AI-Projects/semantic-search/bin/semsearch search "<query>"`; if it
  says `no index covers …`, use `rg`. Never rebuild the index.
- Repo only; never edit `reference/`. One commit per item, message `Item n: <what> — <what was verified>`.
  Never commit a tree that does not build.
- If a plan line is wrong (arithmetic, API that does not exist), say so and fix the plan line in the same
  commit rather than bending the code.

## Closing phase (same session, after the three items are committed)

1. Review your own diff (`git diff <wave base>..HEAD`) for defects and gaps: undo coverage (does every
   mutating path push exactly once?), serialisation completeness (every field saved and restored?),
   SVG fidelity vs the screen, disposal of bitmaps on load/save/close, error paths, `Revision` bumps for
   every new mutation, and plan compliance.
2. Fix what the review finds, or refuse with a reason.
3. Run `dev-docs/verify.sh` until it prints `GATE_OK`, then commit `Wave 1 review: <n> findings fixed`.
4. Report: item commits, review findings (fixed/refused), the gate output, and what is left for Wave 2.
