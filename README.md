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

Press `F1` in the app for the full in-window list, or `Ctrl+F` to find any command by name. The
essentials:

- `Ctrl+K` switches the window between the compact sheet (rulers, tool tray, notes, title block)
  and the detailed panels (inspector, tool options, editable zoom and density). Every launch
  starts compact.
- `Ctrl+F` focuses Find in the menu bar: type words of a command name, `Up`/`Down`, `Enter` runs,
  `Escape` closes. Click an index tab on a ruler to select that vanishing point.

- `1`–`9` drop a vanishing point at the cursor; `Ctrl+1`–`9` select one; `Alt+1`–`9` delete one.
- `Ctrl+Shift+1`–`9` drop a point where the two trace lines cross.
- Left-drag a point to move it, middle-drag to pan, wheel to zoom, right-drag to pull a trace line
  (`Ctrl` snaps 5°, `Shift` snaps 15°).
- `G` grid, `Ctrl+G` grid style, `+`/`-` grid size, `H` level points 1 and 2.
- `S` rotate the palette assignment, `Ctrl+Shift+P` cycle palettes (ink / atelier / charcoal); new
  documents start on ink, and the whole window takes the palette's colours.
- `V` cone of vision and station point; Camera menu has 1-/2-/3-point presets and saved rigs.
- `B` box tool: three drags set width, depth and height (`1`/`2` force an axis, `Backspace` deletes
  the last box, `Escape` leaves the tool).
- `D` subdivision tool: click a box face or four corners, `2`–`9` set cells per axis, `Enter`
  commits; `Shift` at the first click subdivides a receding edge instead.
- `E` ellipse tool: click a box face or four corners, then drag from the centre to set the radius.
- `Ctrl+C` copy the plane (`Ctrl+Shift+C` grayscale), `Ctrl+V` paste an image, `Ctrl+I` load one,
  `I` toggle it, `Alt+I` remove it.
- `Ctrl+N` new document, `Ctrl+R` clear it, `Ctrl+Alt+C` resize it, `Home` recentre, `Escape`
  deselect. Every menu item shows its shortcut.

## Verified

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `--selftest`: renders the 1400x900 viewport, the 640x480 export and the construction-shapes
  scene, and prints every `CHECK ... PASS` before `SELFTEST_OK`.
- Interactive smoke test on X11 (before the polish stage): window, Help > About, Ctrl+N resize
  dialog, Escape to cancel, dropping points 1/3/7, help overlay.
- Polish stage: the self-test also renders the real window headless into `.verify/screens/`
  (3 palettes × compact and detailed, Find open); copies are in `docs/polish stage/screens/`.
- Clipboard, file picker and macOS/Windows runtime behaviour were not exercised on this host (no
  display server, Linux only); they compile against the documented Avalonia APIs.

## Known limitations

- Boxes, subdivisions and ellipses store screen-space points; moving a vanishing point afterwards
  does not re-project them.
- The ellipse is exact for square faces and for the projected-square quad the box tool builds; a
  general quadrilateral maps a parameter circle, which is the right shape only when the face is
  square in 3D.

