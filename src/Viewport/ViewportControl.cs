using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Chitin;

public class ViewportControl : Control
{
    public World World { get; } = new();

    private bool _centered;
    private bool _spaceHeld;
    private Vec2 _mouseWorld = new();
    private Bitmap? _clipboardBitmap;
    private IStorageFile? _projectFile;

    // The file Save writes to; null for a document never saved. The window title shows its name.
    public IStorageFile? CurrentFile
    {
        get => _projectFile;
        private set
        {
            _projectFile = value;
            CurrentFileChanged?.Invoke();
        }
    }

    public event Action? CurrentFileChanged;

    public UndoStack History { get; } = new();

    private ConstructionMode? _construction;

    public ViewportControl()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        World.StatusChanged += InvalidateVisual;
        World.ImageInUse = History.Holds;
        History.Dropped = state => World.ReleaseImage(state.Background);
        BuildCommandTable();
    }

    public override void Render(DrawingContext context)
    {
        if (!_centered)
        {
            _centered = true;
            CenterCamera();
        }

        Renderer.Draw(context, World, Bounds.Size, RenderMode.Interactive);
        ViewChanged?.Invoke();
    }

    // Raised after each frame and on pointer moves, for the rulers, notes and title block.
    public event Action? ViewChanged;

    // The pointer in viewport pixels while it is over the viewport.
    public Point? PointerPosition { get; private set; }

    public ToolKind? Tool => _construction?.Kind;

    public void CenterCamera()
    {
        int width = (int)Bounds.Width;
        int height = (int)Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double zoom = Math.Clamp(Math.Min(1, Math.Min((double)width / World.PicturePlaneWidth,
            (double)height / World.PicturePlaneHeight)), 0.05, 5);

        World.Zoom = zoom;
        World.Camera = new Vec2((width / 2) / zoom, (height / 2) / zoom);
        World.ZoomChanged();
    }

    private Vec2 ToWorld(Point point)
    {
        return new Vec2(point.X / World.Zoom - World.Camera.X, point.Y / World.Zoom - World.Camera.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        World.ClearError();

        Point position = e.GetPosition(this);
        _mouseWorld = ToWorld(position);

        PointerUpdateKind kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        MouseMode? mode = null;

        // Space + left drag pans, as in most drawing apps, in every tool.
        if (kind == PointerUpdateKind.MiddleButtonPressed ||
            (kind == PointerUpdateKind.LeftButtonPressed && _spaceHeld))
        {
            mode = new CameraMode(World);
        }
        else if (kind == PointerUpdateKind.LeftButtonPressed && _construction != null)
        {
            _construction.Begin(position, _mouseWorld, e.KeyModifiers);
            World.Overlay = _construction;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        else if (kind == PointerUpdateKind.LeftButtonPressed)
        {
            World.SelectionIdx = World.PointAt(_mouseWorld, GrabRadius / World.Zoom);
            World.BumpRevision(false);
            mode = new VanishingPointMode(World, () => PushUndo("Move vanishing point"));
        }
        else if (kind == PointerUpdateKind.RightButtonPressed)
        {
            mode = new TraceLineMode(World, () => PushUndo("Add trace line"));
        }

        if (mode != null)
        {
            mode.Init(position, _mouseWorld);
            World.Overlay = mode;
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        _mouseWorld = ToWorld(position);
        PointerPosition = position;
        ViewChanged?.Invoke();
        // Only an overlay or a change of hovered point redraws on a pointer move.
        if (World.Overlay != null)
        {
            World.Overlay.MouseMoved(position, _mouseWorld, e.KeyModifiers);
            InvalidateVisual();
        }

        UpdateHover();
    }

    // Grab radius of a vanishing point, in screen pixels.
    private const double GrabRadius = 32;

    // The hovered point gets a ring and a hand cursor. During a point drag the ring stays on it.
    private void UpdateHover()
    {
        int hover = World.Overlay is VanishingPointMode ? World.SelectionIdx
            : World.Overlay != null || _spaceHeld ? -1
            : World.PointAt(_mouseWorld, GrabRadius / World.Zoom);
        if (hover != World.HoverIdx)
        {
            World.HoverIdx = hover;
            InvalidateVisual();
        }

        Cursor = new Cursor(_spaceHeld || World.Overlay is CameraMode ? StandardCursorType.SizeAll
            : hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Cross);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerPosition = null;
        if (World.HoverIdx >= 0)
        {
            World.HoverIdx = -1;
            InvalidateVisual();
        }

        ViewChanged?.Invoke();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // Passing over the plane must not take focus from a text field (Find, zoom).
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
        {
            Focus();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        World.Overlay?.MouseUp();
        World.Overlay = _construction;
        e.Pointer.Capture(null);
        UpdateHover();
        InvalidateVisual();
    }

    // Wheel and two-finger scroll pan; Ctrl + wheel (a trackpad pinch too) zooms about the cursor.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point position = e.GetPosition(this);
        Vector delta = e.Delta;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (delta.Y == 0)
            {
                return;
            }

            ZoomAt(position, World.Zoom * Math.Pow(1.1, delta.Y));
        }
        else
        {
            // A mouse with only a vertical wheel scrolls sideways with Shift.
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && delta.X == 0)
            {
                delta = new Vector(delta.Y, 0);
            }

            if (delta == default)
            {
                return;
            }

            World.Camera = new Vec2(World.Camera.X + delta.X * WheelStep / World.Zoom,
                World.Camera.Y + delta.Y * WheelStep / World.Zoom);
            InvalidateVisual();
        }

        _mouseWorld = ToWorld(position);
        UpdateHover();
        e.Handled = true;
    }

    // Screen pixels per wheel notch when the wheel pans.
    private const double WheelStep = 48;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            UpdateHover();
            e.Handled = true;
        }
    }

    // Zoom is view state: no undo. The world point under the anchor stays put.
    public void ZoomAt(Point anchor, double zoom)
    {
        Vec2 before = ToWorld(anchor);
        World.Zoom = Math.Clamp(zoom, 0.05, 5);
        Vec2 after = ToWorld(anchor);
        World.Camera = World.Camera + after - before;
        World.ZoomChanged();
        InvalidateVisual();
    }

    // Panels and window resizes keep the world point at the viewport centre.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_centered && e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
        {
            World.Camera = Sheet.KeepCentre(World.Camera, World.Zoom, e.PreviousSize.Width,
                e.PreviousSize.Height, e.NewSize.Width, e.NewSize.Height);
        }
    }

    // The command table and its key index. The active construction tool sees each key first.
    private readonly List<Command> _commands = new();
    private readonly Dictionary<(Key Key, KeyModifiers Modifiers), (Command Command, int Index)> _keys = new();

    public IReadOnlyList<Command> Commands => _commands;

    // Later commands come from the window (mode, Find, About); they join the same table.
    public void AddCommand(Command command)
    {
        _commands.Add(command);
        for (int i = 0; i < command.Keys.Length; i++)
        {
            _keys[(command.Keys[i].Key, command.Keys[i].KeyModifiers)] = (command, command.Indexed ? i : 0);
        }
    }

    private void BuildCommandTable()
    {
        const KeyModifiers none = KeyModifiers.None;
        const KeyModifiers ctrl = KeyModifiers.Control;
        const KeyModifiers shift = KeyModifiers.Shift;
        const KeyModifiers alt = KeyModifiers.Alt;
        string group = "";
        bool inMenu = false;
        bool separator = false;

        void Group(string name, bool menu)
        {
            group = name;
            inMenu = menu;
        }

        void Sep() => separator = true;
        static KeyGesture K(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);

        void Add(string name, KeyGesture[] keys, Func<int, Task> run, string? gesture = null, bool indexed = false)
        {
            AddCommand(new Command(name, group, gesture ?? (keys.Length > 0 ? Chitin.Commands.Label(keys[0]) : ""),
                keys, run, inMenu, separator, indexed));
            separator = false;
        }

        void On(string name, KeyGesture[] keys, Action action, string? gesture = null) => Add(name, keys, _ =>
        {
            action();
            return Task.CompletedTask;
        }, gesture);
        void OnAsync(string name, KeyGesture[] keys, Func<Task> command) => Add(name, keys, _ => command());
        // Undo snapshot before the change, dirty flag after it.
        void Edit(string name, KeyGesture[] keys, Action change, string? gesture = null) => On(name, keys, () =>
        {
            PushUndo(name);
            change();
            World.BumpRevision();
        }, gesture);
        void Digits(string name, KeyModifiers modifiers, Action<int> action, string gesture)
        {
            KeyGesture[] keys = Enumerable.Range(0, 9).Select(i => K(Key.D1 + i, modifiers)).ToArray();
            Add(name, keys, index =>
            {
                action(index);
                return Task.CompletedTask;
            }, gesture, indexed: true);
        }

        // Shift is allowed: on many layouts "+", "[" and "]" need it.
        KeyGesture[] WithShift(params Key[] keys) =>
            keys.SelectMany(key => new[] { K(key), K(key, shift) }).ToArray();

        Group("File", true);
        OnAsync("New document", new[] { K(Key.N, ctrl) }, NewDocumentAsync);
        OnAsync("Open project...", new[] { K(Key.O, ctrl) }, OpenProjectAsync);
        OnAsync("Save project", new[] { K(Key.S, ctrl) }, () => SaveProjectAsync(false));
        OnAsync("Save project as...", new[] { K(Key.S, ctrl | shift) }, () => SaveProjectAsync(true));
        Sep();
        OnAsync("Export SVG or PNG...", new[] { K(Key.E, ctrl) }, ExportAsync);

        Group("Edit", true);
        On("Undo", new[] { K(Key.Z, ctrl) }, Undo);
        On("Redo", new[] { K(Key.Y, ctrl), K(Key.Z, ctrl | shift) }, Redo);
        Sep();
        OnAsync("Copy plane", new[] { K(Key.C, ctrl) }, () => CopyPlaneAsync(false));
        OnAsync("Copy plane in grayscale", new[] { K(Key.C, ctrl | shift) }, () => CopyPlaneAsync(true));
        Sep();
        OnAsync("Resize document...", new[] { K(Key.C, ctrl | alt) }, ResizeDocumentAsync);
        On("Clear document", new[] { K(Key.R, ctrl) }, ClearDocument);

        Group("View", true);
        On("Toggle grid", new[] { K(Key.G) }, ToggleGrid);
        On("Cycle grid style", new[] { K(Key.G, ctrl) }, CycleGridStyle);
        Edit("Larger grid", WithShift(Key.OemPlus, Key.Add), () => World.SetGridSize(World.GridSz * 2));
        Edit("Smaller grid", WithShift(Key.OemMinus, Key.Subtract), () => World.SetGridSize(World.GridSz / 2));
        Sep();
        On("Cone of vision", new[] { K(Key.V) }, ToggleCone);
        On("Cycle palette", new[] { K(Key.P, ctrl | shift) }, CyclePalette);
        foreach (Palette palette in Palette.BuiltIns)
        {
            string paletteName = palette.Name;
            On($"Palette {paletteName}", Array.Empty<KeyGesture>(), () => UsePalette(paletteName));
        }

        Edit("Rotate palette colours", new[] { K(Key.S) }, World.RotatePalette);
        Sep();
        On("Centre view", new[] { K(Key.Home) }, CenterCamera);

        Group("Tools", true);
        On("Box tool", new[] { K(Key.B) }, () => ToggleTool(ToolKind.Box));
        On("Subdivision tool", new[] { K(Key.D) }, () => ToggleTool(ToolKind.Subdivision));
        On("Ellipse tool", new[] { K(Key.E) }, () => ToggleTool(ToolKind.Ellipse));
        On("Leave tool / unselect", new[] { K(Key.Escape) }, Escape);
        Sep();
        On("Level points 1 and 2", new[] { K(Key.H) }, LevelHorizon);
        On("Delete last trace line", new[] { K(Key.Back) }, () => RemoveTraceLines(all: false));
        On("Delete all trace lines", new[] { K(Key.Back, alt) }, () => RemoveTraceLines(all: true));

        Group("Points", false);
        Digits("Drop vanishing point at cursor", none, DropPoint, "1-9");
        Digits("Select vanishing point", ctrl, SelectPoint, "Ctrl+1-9");
        Digits("Delete vanishing point", alt, DeletePoint, "Alt+1-9");
        Digits("Drop point at trace crossing", ctrl | shift, DropPointAtCrossing, "Ctrl+Shift+1-9");
        On("Raise density", new[] { K(Key.Up) }, () => ChangeDensity(4), "Up");
        On("Lower density", new[] { K(Key.Down) }, () => ChangeDensity(-4), "Down");
        On("Flip point vertically", new[] { K(Key.Up, ctrl), K(Key.Down, ctrl) },
            () => EditSelected("Flip point vertically", point => point.Pos = new Vec2(point.Pos.X, -point.Pos.Y)),
            "Ctrl+Up / Down");
        On("Flip point horizontally", new[] { K(Key.Left, ctrl), K(Key.Right, ctrl) },
            () => EditSelected("Flip point horizontally", point => point.Pos = new Vec2(-point.Pos.X, point.Pos.Y)),
            "Ctrl+Left / Right");

        Group("Reference", true);
        OnAsync("Load reference image...", new[] { K(Key.I, ctrl) }, LoadReferenceAsync);
        OnAsync("Paste reference image", new[] { K(Key.V, ctrl) }, PasteBackgroundAsync);
        On("Show / hide reference", new[] { K(Key.I) }, ToggleReference);
        On("Remove reference", new[] { K(Key.I, alt) }, RemoveReference);
        OnAsync("Reference settings...", new[] { K(Key.I, ctrl | alt) }, ShowReferenceDialogAsync);
        Edit("Lower reference opacity", WithShift(Key.OemOpenBrackets), () => ChangeReferenceOpacity(-0.1));
        Edit("Raise reference opacity", WithShift(Key.OemCloseBrackets), () => ChangeReferenceOpacity(0.1));

        Group("Camera", true);
        for (int points = 1; points <= 3; points++)
        {
            int count = points;
            On($"{count}-point preset", Array.Empty<KeyGesture>(), () => ApplyCameraPreset(count));
        }

        Sep();
        OnAsync("Save rig...", Array.Empty<KeyGesture>(), SaveRigAsync);
    }

    internal bool HasKey(Key key, KeyModifiers modifiers)
    {
        return _keys.ContainsKey((key, modifiers));
    }

    // For menu items, Find and the inspector: the command without the construction tool's turn.
    // Index -1: no key. An indexed command then only says which keys run it.
    internal Task RunCommandAsync(Command command, int index = -1)
    {
        if (command.Indexed && index < 0)
        {
            World.SetStatus($"{command.Name}: press {command.Gesture} in the viewport.");
            InvalidateVisual();
            return Task.CompletedTask;
        }

        World.ClearError();
        Task task = command.Run(Math.Max(index, 0));
        World.BumpRevision(false);
        InvalidateVisual();
        return task;
    }

    internal Task RunCommandAsync(string name)
    {
        return RunCommandAsync(_commands.Single(command => command.Name == name));
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Space)
            {
                _spaceHeld = true;
                UpdateHover();
                e.Handled = true;
                return;
            }

            bool toolKey = _construction?.HandleKey(e.Key, e.KeyModifiers) == true;
            (Command Command, int Index) entry = default;
            if (!toolKey && !_keys.TryGetValue((e.Key, e.KeyModifiers), out entry))
            {
                base.OnKeyDown(e);
                return;
            }

            // Mark the key handled before the await, or it bubbles to MainWindow and runs again.
            e.Handled = true;
            if (!toolKey)
            {
                World.ClearError();
            }

            Task task = toolKey ? Task.CompletedTask : entry.Command!.Run(entry.Index);
            World.BumpRevision(false);
            InvalidateVisual();
            await task;
        }
        catch (Exception exception)
        {
            World.SetError($"Key handling failed: {exception.Message}");
            InvalidateVisual();
        }
    }

    private void Escape()
    {
        if (_construction != null)
        {
            ExitTool();
            return;
        }

        World.Overlay = null;
        World.SelectionIdx = -1;
    }

    // One undo step for a change to the selected point; with no selection, nothing happens.
    private void EditSelected(string name, Action<VanishingPoint> change)
    {
        if (World.SelectionIdx > -1 && World.VanishingPoints[World.SelectionIdx] is { } point)
        {
            PushUndo(name);
            change(point);
            World.BumpRevision();
        }
    }

    private void LevelHorizon()
    {
        if (World.VanishingPoints[0] == null && World.VanishingPoints[1] == null)
        {
            return;
        }

        PushUndo("Level points 1 and 2");
        for (int i = 0; i < 2; i++)
        {
            if (World.VanishingPoints[i] is { } point)
            {
                point.Pos = new Vec2(point.Pos.X, _mouseWorld.Y);
            }
        }

        World.BumpRevision();
    }

    private void RemoveTraceLines(bool all)
    {
        if (World.TraceLines.Count == 0)
        {
            return;
        }

        PushUndo(all ? "Delete all trace lines" : "Delete last trace line");
        if (all)
        {
            World.TraceLines.Clear();
        }
        else
        {
            World.TraceLines.RemoveAt(World.TraceLines.Count - 1);
        }

        World.BumpRevision();
    }

    private void DropPoint(int index)
    {
        PushUndo("Drop vanishing point at cursor");
        World.VanishingPoints[index] ??= new VanishingPoint(new Vec2(), 0);
        World.VanishingPoints[index]!.Pos = _mouseWorld;
        World.SelectionIdx = index;
        World.BumpRevision();
    }

    // The points panel: a typed position is one undo step, like a drag.
    public void MovePoint(int index, Vec2 position)
    {
        if (World.VanishingPoints[index] is not { } point || point.Pos == position)
        {
            return;
        }

        PushUndo("Move vanishing point");
        point.Pos = position;
        World.BumpRevision();
        InvalidateVisual();
    }

    private void SelectPoint(int index)
    {
        World.SelectionIdx = World.VanishingPoints[index] != null ? index : -1;
    }

    private void ChangeDensity(int delta)
    {
        EditSelected(delta > 0 ? "Raise density" : "Lower density",
            point => point.Density = VanishingPoint.ClampDensity(point.Density + delta));
    }

    private void DeletePoint(int index)
    {
        if (World.VanishingPoints[index] == null)
        {
            return;
        }

        PushUndo("Delete vanishing point");
        World.VanishingPoints[index] = null;
        World.SelectionIdx = -1;
        World.BumpRevision();
    }

    private void DropPointAtCrossing(int index)
    {
        if (World.TraceLines.Count < 2 ||
            !Geom.GetLineIntersection(World.TraceLines[0].Start, World.TraceLines[0].End,
                World.TraceLines[1].Start, World.TraceLines[1].End, World.CrazyFar, out Vec2 intersection))
        {
            World.SetStatus("Trace lines do not cross: draw two crossing trace lines first.");
            return;
        }

        PushUndo("Drop point at trace crossing");
        World.VanishingPoints[index] ??= new VanishingPoint(new Vec2(), 0);
        World.VanishingPoints[index]!.Pos = intersection;
        World.SelectionIdx = index;
        World.TraceLines.Clear();
        World.BumpRevision();
    }

    private void ToggleTool(ToolKind kind)
    {
        if (_construction?.Kind == kind)
        {
            ExitTool();
            return;
        }

        _construction = kind switch
        {
            ToolKind.Box => new BoxTool(World, () => PushUndo("Box tool")),
            ToolKind.Subdivision => new SubdivisionTool(World, () => PushUndo("Subdivision tool")),
            ToolKind.Ellipse => new EllipseTool(World, () => PushUndo("Ellipse tool")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        World.Overlay = _construction;
    }

    private void ExitTool()
    {
        _construction = null;
        World.Overlay = null;
    }

    // Clear keeps the size, palette, rigs, cone and reference settings; New resets everything.
    public void ClearDocument()
    {
        PushUndo("Clear document");
        ExitTool();
        Array.Clear(World.VanishingPoints);
        World.SelectionIdx = -1;
        World.TraceLines.Clear();
        World.Boxes.Clear();
        World.Subdivisions.Clear();
        World.Ellipses.Clear();
        World.ShowGrid = false;
        World.SetReferenceImage(null);
        InvalidateVisual();
    }

    public void NewDocument()
    {
        PushUndo("New document");
        ExitTool();
        World.Restore(new WorldState { Palette = Palette.Default.Name });
        CurrentFile = null;
        CenterCamera();
        InvalidateVisual();
    }

    public void Undo()
    {
        Step(History.Undo(World.Capture()));
    }

    public void Redo()
    {
        Step(History.Redo(World.Capture()));
    }

    // Zoom and pan are view state, not history: an undo step keeps the current view.
    private void Step(WorldState? state)
    {
        if (state == null)
        {
            return;
        }

        double zoom = World.Zoom;
        Vec2 camera = World.Camera;
        ExitTool();
        World.Restore(state);
        World.Zoom = zoom;
        World.Camera = camera;
        World.ZoomChanged();
        InvalidateVisual();
    }

    private void PushUndo(string name)
    {
        History.Push(World.Capture(), name);
    }

    public void ToggleGrid()
    {
        PushUndo("Toggle grid");
        World.ShowGrid = !World.ShowGrid;
        World.BumpRevision();
        InvalidateVisual();
    }

    public void CycleGridStyle()
    {
        PushUndo("Cycle grid style");
        World.GridStyle = (World.GridStyle + 1) % 3;
        World.BumpRevision();
        InvalidateVisual();
    }


    public void ToggleCone()
    {
        PushUndo("Cone of vision");
        World.ShowCone = !World.ShowCone;
        World.BumpRevision();
        InvalidateVisual();
    }

    public void UsePalette(string name)
    {
        if (string.Equals(World.PaletteName, name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PushUndo($"Palette {name}");
        World.SetPalette(name);
        InvalidateVisual();
    }

    public void CyclePalette()
    {
        PushUndo("Cycle palette");
        World.CyclePalette();
        InvalidateVisual();
    }

    public void ApplyCameraPreset(int points)
    {
        PushUndo($"{points}-point preset");
        ExitTool();
        World.ApplyPreset(points);
        InvalidateVisual();
    }

    public void RecallRig(string name)
    {
        if (World.FindRig(name) < 0)
        {
            World.SetError($"Rig not found: {name}");
        }
        else
        {
            PushUndo($"Recall rig {name}");
            ExitTool();
            World.ApplyRig(name);
        }

        InvalidateVisual();
    }

    public void DeleteRig(string name)
    {
        if (World.FindRig(name) >= 0)
        {
            PushUndo($"Delete rig {name}");
            World.DeleteRig(name);
            World.SetStatus($"Deleted rig {name}");
        }

        InvalidateVisual();
    }

    public async Task RenameRigAsync(string name)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || World.FindRig(name) < 0)
        {
            return;
        }

        string? newName = (await PromptForNameAsync(owner, "Rename Rig", name))?.Trim();
        if (newName == null)
        {
            return;
        }

        int other = World.FindRig(newName);
        if (newName.Length == 0)
        {
            World.SetStatus("Rig name cannot be empty.");
        }
        else if (other >= 0 && other != World.FindRig(name))
        {
            World.SetStatus($"A rig named {newName} already exists.");
        }
        else
        {
            PushUndo($"Rename rig {name}");
            World.RenameRig(name, newName);
            World.SetStatus($"Renamed rig {name} to {newName}");
        }

        InvalidateVisual();
    }

    public async Task SaveRigAsync()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            string? name = await PromptForNameAsync(owner, "Save Rig",
                $"Rig {World.Rigs.Count + 1}");
            if (name == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                World.SetStatus("Rig name cannot be empty.");
                return;
            }

            PushUndo("Save rig...");
            World.SaveRig(name);
            World.SetStatus($"Saved rig {name}");
        }
        catch (Exception exception)
        {
            World.SetError($"Save rig failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    private static async Task<string?> PromptForNameAsync(Window owner, string title, string initial)
    {
        var input = new TextBox { Text = initial };
        bool saved = await ChooseAsync(owner, title, 360, new[] { "Save", "Cancel" },
            new TextBlock { Text = "Rig name" }, input) == 0;
        return saved ? input.Text : null;
    }

    // A small modal dialog: the body, then one button per choice. The first choice is the default
    // (Enter), the last cancels (Escape, or closing the window). Returns the chosen index.
    internal static async Task<int> ChooseAsync(Window owner, string title, double width, string[] choices,
        params Control[] body)
    {
        int chosen = choices.Length - 1;
        var dialog = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = choices[i],
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = i == 0,
                IsCancel = i == choices.Length - 1
            };
            button.Click += (_, _) =>
            {
                chosen = index;
                dialog.Close();
            };
            buttons.Children.Add(button);
        }

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.AddRange(body);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return chosen;
    }

    public async Task SaveProjectAsync(bool saveAs)
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            IStorageFile? file = CurrentFile;
            if (saveAs || file == null)
            {
                file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Project",
                    SuggestedFileName = "project.chitin.json",
                    DefaultExtension = "json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Chitin Project")
                        {
                            Patterns = new[] { "*.chitin.json", "*.json" }
                        }
                    }
                });
                if (file == null)
                {
                    return;
                }
            }

            await WriteFileAsync(file, Encoding.UTF8.GetBytes(ProjectFile.Write(World)));

            CurrentFile = file;
            World.MarkSaved();
            World.SetStatus($"Saved {file.Name}");
        }
        catch (Exception exception)
        {
            World.SetError($"Save failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    private static async Task WriteFileAsync(IStorageFile file, byte[] bytes)
    {
        if (file.TryGetLocalPath() is { } path)
        {
            await WriteFileAtomicAsync(path, bytes);
            return;
        }

        await using Stream stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await stream.WriteAsync(bytes);
    }

    // Write beside the target, then rename over it, so a failed write leaves the old file intact.
    internal static async Task WriteFileAtomicAsync(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void ToggleReference()
    {
        PushUndo("Show / hide reference");
        ReferenceSettings next = World.Reference.Clone();
        next.Hidden = !next.Hidden;
        World.SetReferenceSettings(next);
        InvalidateVisual();
    }

    private void ChangeReferenceOpacity(double delta)
    {
        ReferenceSettings next = World.Reference.Clone();
        next.Opacity += delta;
        World.SetReferenceSettings(next);
        InvalidateVisual();
    }

    public async Task ShowReferenceDialogAsync()
    {
        ReferenceSettings? initial = null;
        bool wasDirty = World.IsDirty;
        // The dialog applies changes live; Cancel puts back the settings and the saved state.
        void Revert()
        {
            if (initial != null && !World.Reference.ContentEquals(initial))
            {
                World.SetReferenceSettings(initial);
            }

            if (!wasDirty)
            {
                World.MarkSaved();
            }
        }

        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            WorldState before = World.Capture();
            initial = World.Reference.Clone();
            var dialog = new ReferenceDialog(World, InvalidateVisual);
            if (await dialog.ShowDialog<bool>(owner))
            {
                History.Push(before, "Reference settings...");
            }
            else
            {
                Revert();
            }
        }
        catch (Exception exception)
        {
            Revert();
            World.SetError($"Reference settings failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    public void RemoveReference()
    {
        if (World.BackgroundImg != null)
        {
            PushUndo("Remove reference");
        }

        World.SetReferenceImage(null);
        InvalidateVisual();
    }

    public async Task ExportAsync()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var dialog = new ExportDialog(World.PicturePlaneWidth, World.PicturePlaneHeight);
            if (!await dialog.ShowDialog<bool>(owner))
            {
                return;
            }

            bool svg = dialog.IsSvg;
            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export",
                SuggestedFileName = svg ? "export.svg" : "export.png",
                DefaultExtension = svg ? "svg" : "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(svg ? "SVG" : "PNG")
                    {
                        Patterns = new[] { svg ? "*.svg" : "*.png" }
                    }
                }
            });
            if (file == null)
            {
                return;
            }

            using var buffer = new MemoryStream();
            if (svg)
            {
                buffer.Write(Encoding.UTF8.GetBytes(
                    SvgWriter.Write(World, dialog.OutputWidth, dialog.OutputHeight)));
            }
            else
            {
                using Bitmap bitmap = RenderPlane(dialog.OutputWidth, dialog.OutputHeight, dialog.Transparent);
                bitmap.Save(buffer);
            }

            await WriteFileAsync(file, buffer.ToArray());

            World.SetStatus($"Exported {file.Name}");
        }
        catch (Exception exception)
        {
            World.SetError($"Export failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    public async Task OpenProjectAsync()
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null || !await ConfirmDiscardAsync())
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open Project",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Chitin Project")
                        {
                            Patterns = new[] { "*.chitin.json", "*.json" }
                        },
                        FilePickerFileTypes.All
                    }
                });
            if (files.Count == 0)
            {
                return;
            }

            string json;
            await using (Stream stream = await files[0].OpenReadAsync())
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync();
            }

            LoadProject(ProjectFile.Read(json), files[0]);
            World.SetStatus($"Opened {files[0].Name}");
        }
        catch (Exception exception)
        {
            World.SetError($"Open failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    // Open clears the history: an undo past it would restore content that a later Save writes into this file.
    internal void LoadProject(WorldState state, IStorageFile? file)
    {
        History.Clear();
        ExitTool();
        World.Restore(state);
        CurrentFile = file;
        World.MarkSaved();
    }

    public async Task NewDocumentAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            NewDocument();
        }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!World.IsDirty || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return true;
        }

        // Save is the default button: Enter must not throw work away.
        int choice = await ChooseAsync(owner, "Unsaved changes", 420, new[] { "Save", "Don't save", "Cancel" },
            new TextBlock
            {
                Text = $"Save changes to {CurrentFile?.Name ?? "Untitled"}?",
                TextWrapping = TextWrapping.Wrap
            });
        if (choice == 0)
        {
            await SaveProjectAsync(false);
            return !World.IsDirty;
        }

        return choice == 1;
    }

    // For keys the window receives while focus is elsewhere (the menu), and for the self-test.
    internal void ProcessKey(KeyEventArgs e)
    {
        Focus();
        OnKeyDown(e);
    }

    private async Task ResizeDocumentAsync()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var dialog = new ResizeDialog(World.PicturePlaneWidth, World.PicturePlaneHeight);
            if (!await dialog.ShowDialog<bool>(owner))
            {
                return;
            }

            PushUndo("Resize document...");
            ExitTool();
            World.ResetDocument(dialog.NewWidth, dialog.NewHeight);
            World.SetReferenceImage(null);
            CenterCamera();
            InvalidateVisual();
        }
        catch (Exception exception)
        {
            World.SetError($"Resize failed: {exception.Message}");
            InvalidateVisual();
        }
    }

    public async Task LoadReferenceAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Load Background Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("All Images")
                    {
                        Patterns = new[]
                        {
                            "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp"
                        }
                    },
                    FilePickerFileTypes.All
                }
            });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > World.MaxEncodedImageBytes)
            {
                World.SetError("Image refused: file is larger than 64 MB.");
                InvalidateVisual();
                return;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Bitmap bitmap = World.DecodeImage(buffer.ToArray());

            PushUndo("Load reference image...");
            ExitTool();
            World.SetReferenceImage(bitmap);
            if (!ReferenceEquals(World.BackgroundImg, bitmap))
            {
                InvalidateVisual();
                return;
            }

            World.ResetDocument(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            CenterCamera();
        }
        catch (Exception exception)
        {
            World.SetError($"Could not load image: {exception.Message}");
        }

        InvalidateVisual();
    }

    private async Task PasteBackgroundAsync()
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        Bitmap? bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap == null)
        {
            World.SetStatus("Clipboard contains no image.");
            InvalidateVisual();
            return;
        }

        if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > World.MaxDecodedImagePixels)
        {
            bitmap.Dispose();
            World.SetError("Image refused: larger than 40 megapixels.");
            InvalidateVisual();
            return;
        }

        PushUndo("Paste reference image");
        ExitTool();
        World.SetReferenceImage(bitmap);
        if (ReferenceEquals(World.BackgroundImg, bitmap))
        {
            World.ResetDocument(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            CenterCamera();
        }

        InvalidateVisual();
    }

    private async Task CopyPlaneAsync(bool grayscale)
    {
        try
        {
            Bitmap bitmap = RenderPlane(grayscale);
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                bitmap.Dispose();
                return;
            }

            Bitmap? previous = _clipboardBitmap;
            _clipboardBitmap = bitmap;
            try
            {
                await clipboard.SetBitmapAsync(bitmap);
            }
            catch
            {
                _clipboardBitmap = previous;
                bitmap.Dispose();
                throw;
            }

            previous?.Dispose();
        }
        catch (Exception exception)
        {
            World.SetError($"Copy failed: {exception.Message}");
        }
        finally
        {
            InvalidateVisual();
        }
    }

    internal Bitmap RenderPlane(bool grayscale = false)
    {
        return RenderPlane(World.PicturePlaneWidth, World.PicturePlaneHeight, false, grayscale);
    }

    internal Bitmap RenderPlane(int width, int height, bool transparent, bool grayscale = false)
    {
        double zoom = (double)width / World.PicturePlaneWidth;
        var renderTarget = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (DrawingContext context = renderTarget.CreateDrawingContext())
        {
            Renderer.Draw(context, World, new Size(width, height),
                transparent ? RenderMode.TransparentExport : RenderMode.OpaqueExport,
                World.StylesAt(zoom), new Vec2(width / 2.0 / zoom, height / 2.0 / zoom));
        }

        if (!grayscale)
        {
            return renderTarget;
        }

        using (renderTarget)
        {
            return Grayscale(renderTarget);
        }
    }

    private static WriteableBitmap Grayscale(RenderTargetBitmap source)
    {
        var result = new WriteableBitmap(source.PixelSize, new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (ILockedFramebuffer framebuffer = result.Lock())
        {
            source.CopyPixels(framebuffer, AlphaFormat.Premul);

            int length = framebuffer.RowBytes * framebuffer.Size.Height;
            var pixels = new byte[length];
            Marshal.Copy(framebuffer.Address, pixels, 0, length);

            for (int i = 0; i + 3 < length; i += 4)
            {
                byte gray = (byte)(0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i]);
                pixels[i] = gray;
                pixels[i + 1] = gray;
                pixels[i + 2] = gray;
            }

            Marshal.Copy(pixels, 0, framebuffer.Address, length);
        }

        return result;
    }
}
