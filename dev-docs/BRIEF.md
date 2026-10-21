# Brief — implement the Chitin cross-platform port

You are working in `~/AI-Projects/chitin-avalonia` (this is your workdir; touch nothing outside it —
never `~`, never `/tmp`, no installs, no `dotnet tool install`).

## What this is

`SPEC.md` (already committed) is the source of truth and the acceptance contract. `reference/original-decompiled/`
is the C# decompiled out of the original 81 KB WinForms binary `Carapace.exe` (Epic's "Carapace V1.0",
a perspective-drawing aid). Your job: port it to **.NET 8 + Avalonia 11.3.22** as a faithful,
cross-platform desktop app, exactly as `SPEC.md` §2–§11 describe.

Read `SPEC.md` completely before writing code, then read the reference files you are porting
(`WWorld.cs`, `WViewportPanel.cs`, `WVanishingPoint.cs`, `WTraceLine.cs`, `WVector.cs`, `WGeom.cs`,
`WMouseMode*.cs`, `MainForm.cs`, `DlgNewDoc.cs`, `AboutBox.cs`). Port the logic; do not redesign it.

## Rules

- Ponytail is active: no unneeded dependencies, abstractions, or files. Package list is fixed in
  `SPEC.md` §2 — the only exception is `Avalonia.Skia` if the headless render in §11 genuinely
  needs it, and you must say so in your report.
- `reference/` must be excluded from compilation (`<Compile Remove="reference/**" />`) — those files
  are .NET Framework source, reference only.
- This project has a local semantic index: query `~/AI-Projects/semantic-search/bin/semsearch search "<query>"`
  before grepping. If it prints `no index covers …`, fall back to `rg` — do not rebuild the index.
- Do not fabricate build or test output. Run the commands, paste what they actually printed.
- Do not add tests beyond the `--selftest` mode in `SPEC.md` §11; that is the acceptance gate.
- Commit your work in small commits (scaffold, world/render core, input, dialogs, selftest) with
  messages that say what was verified. Never commit a tree that does not build.

## Build and verify (all inside the repo)

```
dotnet build -c Release
dotnet run -c Release -- --selftest out/selftest.png
```

`dotnet build` must be clean (0 warnings, 0 errors). The self-test must print `SELFTEST_OK <path1> <path2>`,
exit 0, and leave two non-trivial PNGs (a 1400x900 viewport render with HUD + help, and a 640x480
export render). Check the files exist and are non-empty; open them if you can (e.g. `file`, byte size).

Avalonia 11.3.22 restores from nuget.org (network is available). Verified API signatures are in
`SPEC.md` §12 — use them, do not guess.

## When done

Report: the commit hashes, the exact `dotnet build` tail and `SELFTEST_OK` line you got, the PNG paths
and sizes, which parts of `SPEC.md` are implemented, and anything unresolved or deliberately
different from the reference. If the headless render cannot be made to work, say exactly what failed
(the exception) instead of stubbing it.
