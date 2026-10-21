# Wave 0 — pre-close code review and fix brief

You are reviewing work you did not write. The repository is `~/AI-Projects/chitin-avalonia`. Wave 0 is
committed as `d4d64f2..HEAD` (fourteen commits, `W0-1` … `W0-14`). `dev-docs/PLAN.md` Wave 0 and
`dev-docs/briefs/wave0.md` state what was asked for.

## Phase 1 — review (read only, no edits yet)

Read the wave's diff in full (`git diff d4d64f2..HEAD`) and the files it touches. Hunt for **defects and
gaps**, not style:

1. **Logic**: wrong order of operations, off-by-one, clamping that can still be bypassed, guards that
   cannot trigger, assertions that pass no matter what the code does.
2. **Cache correctness** — this is the highest-risk part of the wave. `Prim[]` is cached by
   `(World.Revision, Zoom)`. Walk every mutation of document state (vanishing points, trace lines,
   grid, camera, zoom, document size, reference settings, palette, selection affecting appearance) and
   prove each one bumps `Revision` or is otherwise invisible to the cache. A missing bump means a stale
   render. Same question for the `FormattedText` cache: can it return text with the wrong colour after
   a palette change, or grow without bound?
3. **Resource lifetime**: every `Bitmap` path (load, paste, remove, reset, replace, close) — is the old
   one disposed exactly once, and is a disposed bitmap ever drawn or handed to the clipboard? Any
   `Stream` not disposed? Any `RenderTargetBitmap` used after its `using` block?
4. **Error paths**: do the new `try/catch` blocks actually surface something, or do they swallow?
   Does `StatusMessage` ever get cleared, and is it drawn where the user can see it? Does an exception
   inside a `finally`-protected block still restore zoom/camera?
5. **Async/threading**: `Dispatcher.UIThread` handler installed on the right thread, no `await` on the
   UI thread touching a disposed control, no fire-and-forget task whose exception vanishes.
6. **Behaviour regressions vs the original**: compare against `reference/original-decompiled/` and the
   Deviations section of `SPEC.md`. Anything changed that is not listed as a deviation is a bug.
7. **Plan compliance**: every W0 item present and actually done (not just claimed). Cross-check the
   `CHECK` names in `--selftest` against the plan's list. Flag any item that was silently skipped.
8. **Dead code / duplication** introduced by this wave only (the over-engineering pass already ran).

Known defect to fix regardless of what else you find: `--help` currently exits 2. Standard convention
is help on stdout with exit 0; 2 is for usage errors. Fix the code and the plan line that says
otherwise.

Write the findings as a numbered list, each with `file:line`, severity (`bug` / `gap` / `risk`), what is
wrong, and the minimal fix. Then, in the same run:

## Phase 2 — fix

- Apply every finding you listed. If a finding turns out to be wrong when you open the code, say so
  and leave that code alone — a refusal with a reason is a correct outcome.
- Do not restructure beyond the fix. Ponytail stays active: no new dependencies, files or abstractions.
- If a fix changes rendered output, say which PNG hash moved and why it is correct; otherwise the
  self-test PNGs must stay byte-identical.
- Rebuild, run `dev-docs/verify.sh`, and make every `CHECK` pass. Extend the self-test with a check for
  any bug you fixed where a check is possible (e.g. a revision-bump check that fails if a mutation is
  not covered).
- Commit the fixes as one commit: `W0 review: <n> findings fixed — <what was verified>`.

## Report

The numbered findings you raised, which you fixed, which you refused and why, the final gate output,
and the commit hash.
