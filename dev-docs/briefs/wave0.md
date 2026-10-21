# Wave 0 — implementation brief

Work in this repository only (`~/AI-Projects/chitin-avalonia`). `dev-docs/PLAN.md` **Wave 0** is the
spec: fourteen items, W0-1 … W0-14. Read the whole Wave 0 section before writing code, then implement
every item, in the order the plan gives at the end of that section:

1. W0-9  `Vec2` → readonly struct, pure `Normalized()`, delete `GetSize()`, one constructor
2. W0-10 primitive-list cache keyed by `(World.Revision, Zoom)`
3. W0-13 ponytail refactors: merge `VPVoidLines` into `VPOutline`, merge the Up/Down density cases,
   drop the direct `Avalonia` PackageReference, delete `ViewportControl._mouseMode`
4. W0-1  `World.StatusMessage` + try/catch in every `async void` + global dispatcher handler
5. W0-2  `SetReferenceImage` with disposal, load-error messages, 64 MB / 40 MP guards
6. W0-3  clamps: document `[1, 8192]`, grid size `[2, 4096]`, resize dialog validation
7. W0-5  collinear → false, degenerate horizon skip, HUD null guard
8. W0-6  `RenderPlane()` with `finally`, detached bitmap for the clipboard
9. W0-4  CLI: usage on stderr + exit 2, `--help`, return lifetime exit code, selftest file assertions
10. W0-7 refocus viewport when a menu closes; window-level accelerators
11. W0-8 dialog validation feedback, `IsCancel` on About, `HyperlinkButton NavigateUri`, automation names
12. W0-12 automatic fan density cap 96 + off-plane cull
13. W0-11 `FormattedText` cache (bounded, cleared on zoom/palette change)
14. W0-14 SPEC corrections + Deviations section + NOTICE.md + README `DOTNET_ROOT` note

## Rules

- Ponytail is active: no new dependencies, abstractions or files beyond what an item names.
- This project has a local semantic index: query `~/AI-Projects/semantic-search/bin/semsearch search "<query>"`
  before grepping. If it says `no index covers …`, fall back to `rg` and do not rebuild the index.
- Touch nothing outside this repo. Never edit `reference/original-decompiled/`.
- Every item in `--selftest` must print `CHECK <name> PASS` (or `FAIL`) for the assertions the plan
  lists for it, and a single failing check must make the process exit non-zero. Do not weaken an
  assertion to make it pass; if an expected value in the plan is arithmetically wrong, say so in the
  report and fix the plan line in the same commit.
- Keep the existing self-test scene and both PNG outputs working.
- One commit per item (or per tightly related pair), message `W0-n: <what> — <what was verified>`.
  Never commit a tree that does not build.
- Do not invent behaviour the plan does not ask for. If an item is impossible as written, stop that
  item, leave the code building, and report exactly why.

## Before you finish

Run `dev-docs/verify.sh` and paste its real output into your final report. Then report: the commit
hashes, the build tail, every `CHECK` line, the CLI exit codes, and `git diff --stat` for the wave.
