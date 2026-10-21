Apply the Ponytail review findings below to this repo. They were produced by `/ponytail-review` over the
commit range `8b3f6ba..HEAD`. Change nothing outside this list, and do not touch `reference/` or `SPEC.md`.

Rules:
- Ponytail is active; no new dependencies, abstractions or files.
- Every removal must be re-verified: after the edits, `dotnet build -c Release` must be clean and
  `dotnet run -c Release --no-build -- --selftest .verify/selftest.png` must print
  `SELFTEST_OK …` and exit 0. The two PNGs must still render the same scene (the app must not lose
  a feature or throw). If a finding breaks the build or the render, revert just that finding and say
  why in your report — keeping the code is a correct outcome when the finding is wrong.
- Finding 2 is a behavioural one (the reference deliberately uses a 360-entry direction table and a
  dot-product scan, including its tie-breaking). If you apply it, the snap must still land on exact
  5°/15° multiples and the drag overlay must look identical; if you cannot prove that from the diff,
  refuse it and say so instead.
- One commit for the whole pass is fine. Message: what was cut, and what you verified.

Findings (verbatim):

1. `Vec2.cs:L63-76`: delete: `+`, `-`, `*` operators have no callers. Remove all three.
2. `MouseMode.cs:L112-135`, `World.cs:L61,L69-72`: stdlib: 360-entry `UnitVectors` table plus dot scan only finds the nearest 5°/15° direction. `Math.Atan2`/`Math.Round` gives the same snap; delete the table and now-unused `Vec2.Dot` at `Vec2.cs:L50-53`.
3. `ViewportControl.cs:L187-232`: shrink: four arrow-key blocks repeat mirror/density logic. Combine with `Key.Up or Key.Down when control` and `Key.Left or Key.Right when control`, inline the three-line bodies.
4. `ViewportControl.cs:L38-58`: shrink: hand-rolled fit branch. `Math.Min(1, (double)width/W, (double)height/H)`, which SPEC §5 explicitly allows.
5. `TraceLine.cs:L8-12`, `VanishingPoint.cs:L8-12`: delete: zero-init constructors duplicate field defaults. Remove both and pass `new Vec2()` at the three call sites.
6. `World.cs:L75,L129-132`: shrink: `ZoomChanged` is a one-line wrapper and the constructor rebuilds pens twice. Rename `RebuildZoomPens` to `ZoomChanged`, drop the extra call.
7. `ViewportControl.cs:L372-375`: stdlib: manual array clear. `Array.Clear(World.VanishingPoints)`.
8. `ViewportControl.cs:L517-519`: shrink: framebuffer is always `Bgra8888`, so the `bgra`/`red`/`blue` branch is dead. Use indices 2 and 0 directly.
9. `World.cs:L44`: delete: `SolidRed` pen is never used. Remove it.
10. `World.cs:L27`: delete: `PlaneBackgroundShot` duplicates `PlaneBackground` and is never reassigned. Use `PlaneBackground` in `Renderer.cs:L87`.
11. `World.cs:L9-10`: delete: `InfoLineH`/`StatusLineH` are unused; renderer hardcodes 36/32. Remove both.
12. `MouseMode.cs:L12,L24,L34`: delete: `LastWorld` is written but never read. Remove the field and both assignments.
13. `ResizeDialog.axaml.cs:L11-13`: delete: parameterless constructor is never called; the dialog is only created with dimensions.

Report: the commit hash, the build tail, the `SELFTEST_OK` line, which findings you applied, which you
refused and why, and the final `net:` line count delta (`git diff --stat`).
