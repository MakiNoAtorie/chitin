# Chitin (cross-platform port)

A reimagination of **Carapace**, Epic Games' free perspective-grid tool for artists.

## Requirements

- .NET 8 SDK to build (`dotnet --version` → 8.x). Nothing else; packages restore from nuget.org.

## Build and run

```bash
dotnet build -c Release
dotnet run -c Release
```

Running `bin/Release/net8.0/Chitin` directly on this host needs
`DOTNET_ROOT=/home/makisatelier/.dotnet`; `dotnet run` sets it up for you.

Self-test (headless render, writes three PNGs and prints `SELFTEST_OK`):

```bash
dotnet run -c Release -- --selftest out/selftest.png
```

## Publish a standalone binary

Each command produces a single self-contained file with the runtime bundled; the target machine
needs nothing installed.

```bash
dotnet publish -c Release -r linux-x64   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/linux-x64
dotnet publish -c Release -r win-x64     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/win-x64
dotnet publish -c Release -r osx-arm64   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/osx-arm64
dotnet publish -c Release -r osx-x64     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/osx-x64
```

## Controls

Press `F1` in the app for every key and gesture, or `Ctrl+F` to find any command by name. Every
menu item shows its shortcut. The essentials:

- **Points** (`P`, the default tool): click the empty plane to place the next point, drag a point to
  move it. `1`-`9` drop a point at the cursor, `Ctrl+1`-`9` select, `Alt+1`-`9` or `Delete` delete,
  `Up`/`Down` change the selected point's density, `H` levels points 1 and 2. Camera menu: 1-, 2-
  and 3-point presets.
- **Points panel** (right; `Ctrl+K` hides it): type a point's X and Y, step its density, delete it.
- **Navigate**: wheel or two-finger scroll pans, `Ctrl`+wheel or pinch zooms, `Space`+drag or
  middle-drag pans, `Home` fits the plane.
- **Zoom with a stylus**: `Ctrl+=` / `Ctrl+-` zoom in and out, `Ctrl+0` shows 100 %,
  `Ctrl`+`Space`+drag zooms (right or up zooms in), and the `-` / `+` cells beside the zoom in the
  bottom-right corner can be tapped.
- **Trace lines**: `T` and drag, or right-drag in any tool (`Ctrl` snaps 5°, `Shift` 15°).
  `Ctrl+Shift+1`-`9` drops a point where the two lines cross. `Backspace` deletes the last line.
- **Box** (`B`): three drags set width, depth and height along the vanishing points (`1`/`2` force
  an axis). Boxes follow their points when you move them.
- **Subdivide** (`D`): click a box face or four corners; `2`-`9` set the cells of the one just made.
- **Ellipse** (`E`): click a box face or four corners, then drag from the centre.
- `Backspace` in a tool deletes its last shape; `Escape` leaves the tool.
- **Reference image**: `Ctrl+I` load, `Ctrl+V` paste (fitted to the plane), `Alt`+drag move,
  `Alt`+wheel scale, `[`/`]` opacity, `I` show or hide, `Alt+I` remove.
- **View**: `G` grid, `+`/`-` grid size, `V` cone of vision and station point, `Ctrl+Shift+P` dark
  palette. These are view settings: saved with the project, but not undone and never a reason to
  ask before closing.
- **File**: `Ctrl+N` new, `Ctrl+O` open, `Ctrl+S` save, `Ctrl+E` export SVG or PNG,
  `Ctrl+Shift+C` copy the plane as an image, `Ctrl+Shift+R` resize. Edit > Clear document asks
  first. Closing with unsaved changes offers Save, Don't save and Cancel.

## Verified

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dev-docs/verify.sh`: the build, the self-test (every `CHECK ... PASS`, then `SELFTEST_OK`) and
  the command-line contract.
- `--selftest` also renders the real window headless into `.verify/screens/` (each palette, Find
  open, a following 3-point box, and an empty document at fit and zoomed out).
- The window has not been driven by hand on a desktop since the UX pass (`docs/UX-PASS.md`); its
  behaviour is covered by the headless checks. Clipboard, file pickers and the macOS/Linux runtime
  were not exercised on this host.

## Known limitations

- Boxes saved before the UX pass, and shapes made from four clicked corners, keep their screen
  points: moving a vanishing point does not re-project them.
- The ellipse is exact for square faces and for the projected-square quad the box tool builds; a
  general quadrilateral maps a parameter circle, which is the right shape only when the face is
  square in 3D.
