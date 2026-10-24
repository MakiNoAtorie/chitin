using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Chitin;

public class World
{
    public const int PicturePlaneBorderSz = 8;
    public const int HelpColumnWidth = 350;
    public const int MinDocumentSize = 1;
    public const int MaxDocumentSize = 8192;
    public const int MinGridSize = 2;
    public const int MaxGridSize = 4096;
    public const double MinReferenceScale = 0.25;
    public const double MaxReferenceScale = 4;

    public int PicturePlaneWidth;
    public int PicturePlaneHeight;
    public int PicturePlaneWidthHalf;
    public int PicturePlaneHeightHalf;
    public int CrazyFar;

    public VanishingPoint?[] VanishingPoints = new VanishingPoint?[9];
    public List<TraceLine> TraceLines = new();
    public List<BoxState> Boxes = new();
    public List<SubdivisionState> Subdivisions = new();
    public List<EllipseState> Ellipses = new();
    public List<RigState> Rigs = new();
    public double Zoom = 1;
    public Vec2 Camera = new();
    public MouseMode? Overlay;

    public Styles Styles { get; private set; } = null!;

    public const long MaxEncodedImageBytes = 64L * 1024 * 1024;
    public const long MaxDecodedImagePixels = 40_000_000;

    public Bitmap? BackgroundImg { get; private set; }

    // Undo snapshots can hold an image that is no longer current; such an image must stay alive.
    public Func<Bitmap, bool>? ImageInUse;
    public ReferenceSettings Reference = new();

    public string PaletteName { get; private set; } = Palette.Default.Name;
    public bool ShowCone;
    public int DisposedImageCount { get; private set; }
    public bool ShowHelp;
    public List<HelpRow> Help = new();
    public bool ShowGrid;
    public int GridStyle;
    public int GridSz = 32;

    public bool IsDirty { get; private set; }
    public event Action? DirtyChanged;

    public Vec2[] UnitVectors = new Vec2[360];
    public int SelectionIdx = -1;

    // The point under the pointer (view state): drawn with a ring.
    public int HoverIdx = -1;

    // The nearest point within radius of a world position, or -1. Nearest, not first: overlapping
    // points can each be grabbed.
    public int PointAt(Vec2 position, double radius)
    {
        int best = -1;
        double bestDistance = radius;
        for (int i = 0; i < VanishingPoints.Length; i++)
        {
            if (VanishingPoints[i] is { } point && point.Pos.DistFrom(position) < bestDistance)
            {
                best = i;
                bestDistance = point.Pos.DistFrom(position);
            }
        }

        return best;
    }

    public int Revision { get; private set; }
    public string? StatusMessage { get; private set; }

    public event Action? StatusChanged;

    private int _statusVersion;

    private Prim[]? _prims;
    private int _primsRevision = -1;
    private Styles? _primsStyles;

    public Prim[] Prims
    {
        get
        {
            if (_prims == null || _primsRevision != Revision || !ReferenceEquals(_primsStyles, Styles))
            {
                _prims = GuideBuilder.Build(this, Styles);
                _primsRevision = Revision;
                _primsStyles = Styles;
            }

            return _prims;
        }
    }

    // Raised on every revision bump: the inspector reads the world through it.
    public event Action? Changed;

    public void BumpRevision(bool document = true)
    {
        Revision++;
        Changed?.Invoke();
        if (document && !IsDirty)
        {
            IsDirty = true;
            DirtyChanged?.Invoke();
        }
    }

    public void MarkSaved()
    {
        IsDirty = false;
        DirtyChanged?.Invoke();
    }

    public WorldState Capture()
    {
        var state = new WorldState
        {
            Doc = new DocState { W = PicturePlaneWidth, H = PicturePlaneHeight },
            Zoom = Zoom,
            Camera = Camera,
            Grid = new GridState { Show = ShowGrid, Style = GridStyle, Size = GridSz },
            Reference = Reference.Clone(),
            Palette = PaletteName,
            PaletteOffset = _paletteOffset,
            ShowCone = ShowCone,
            Background = BackgroundImg
        };

        for (int i = 0; i < VanishingPoints.Length; i++)
        {
            if (VanishingPoints[i] is { } point)
            {
                state.VanishingPoints[i] = new VanishingPoint(point.Pos, point.Density);
            }
        }

        foreach (TraceLine traceLine in TraceLines)
        {
            state.TraceLines.Add(new TraceLine(traceLine.Start, traceLine.End));
        }

        foreach (BoxState box in Boxes)
        {
            state.Boxes.Add(box.Clone());
        }

        foreach (SubdivisionState subdivision in Subdivisions)
        {
            state.Subdivisions.Add(subdivision.Clone());
        }

        foreach (EllipseState ellipse in Ellipses)
        {
            state.Ellipses.Add(ellipse.Clone());
        }

        foreach (RigState rig in Rigs)
        {
            state.Rigs.Add(rig.Clone());
        }

        return state;
    }

    public void Restore(WorldState state)
    {
        ResetDocument(state.Doc.W, state.Doc.H);
        Bitmap? previousImage = BackgroundImg;
        BackgroundImg = state.Background;
        ReleaseImage(previousImage);
        Zoom = Math.Clamp(state.Zoom, 0.05, 5);
        Camera = state.Camera;

        VanishingPoints = new VanishingPoint?[9];
        for (int i = 0; i < VanishingPoints.Length && i < state.VanishingPoints.Length; i++)
        {
            if (state.VanishingPoints[i] is { } point)
            {
                VanishingPoints[i] = new VanishingPoint(point.Pos, point.Density);
            }
        }

        TraceLines = new List<TraceLine>();
        foreach (TraceLine traceLine in state.TraceLines.Take(2))
        {
            TraceLines.Add(new TraceLine(traceLine.Start, traceLine.End));
        }

        Boxes = new List<BoxState>();
        foreach (BoxState box in state.Boxes ?? new List<BoxState>())
        {
            if (box == null || !IsFinite(box.P0) || !IsFinite(box.P1) || !IsFinite(box.P2) ||
                !IsFinite(box.P3) || !IsFinite(box.H))
            {
                continue;
            }

            Boxes.Add(box.Clone());
        }

        Subdivisions = new List<SubdivisionState>();
        foreach (SubdivisionState subdivision in state.Subdivisions ?? new List<SubdivisionState>())
        {
            if (subdivision?.Quad == null || subdivision.Quad.Length != 4 ||
                !subdivision.Quad.All(IsFinite))
            {
                continue;
            }

            SubdivisionState copy = subdivision.Clone();
            copy.N = Math.Clamp(copy.N, 1, 64);
            copy.M = Math.Clamp(copy.M, 1, 64);
            Subdivisions.Add(copy);
        }

        Ellipses = new List<EllipseState>();
        foreach (EllipseState ellipse in state.Ellipses ?? new List<EllipseState>())
        {
            if (ellipse?.Quad == null || ellipse.Quad.Length != 4 || !ellipse.Quad.All(IsFinite) ||
                !double.IsFinite(ellipse.Cx) || !double.IsFinite(ellipse.Cy) ||
                !double.IsFinite(ellipse.R) || ellipse.R <= 0)
            {
                continue;
            }

            Ellipses.Add(ellipse.Clone());
        }

        Rigs = new List<RigState>();
        foreach (RigState rig in state.Rigs ?? new List<RigState>())
        {
            if (rig?.Vps == null || string.IsNullOrWhiteSpace(rig.Name))
            {
                continue;
            }

            Rigs.Add(rig.Clone());
        }

        ShowGrid = state.Grid.Show;
        GridStyle = Math.Clamp(state.Grid.Style, 0, 2);
        GridSz = Math.Clamp(state.Grid.Size, MinGridSize, MaxGridSize);
        Reference = NormalizeReference(state.Reference, out _);
        ShowCone = state.ShowCone;
        ApplyPalette(state.Palette,
            state.PaletteOffset, bump: false);
        SelectionIdx = -1;
        BumpRevision();
    }

    // An error stays until the next command; a prompt clears itself after five seconds.
    public bool StatusIsError { get; private set; }

    public void SetError(string message)
    {
        ShowStatus(message, error: true);
    }

    public void ClearError()
    {
        if (StatusIsError)
        {
            StatusMessage = null;
            StatusIsError = false;
            _statusVersion++;
            StatusChanged?.Invoke();
        }
    }

    public void SetStatus(string message)
    {
        ShowStatus(message, error: false);
    }

    private void ShowStatus(string message, bool error)
    {
        StatusMessage = message;
        StatusIsError = error;
        int version = ++_statusVersion;
        StatusChanged?.Invoke();
        if (error)
        {
            return;
        }

        DispatcherTimer.RunOnce(() =>
        {
            if (version != _statusVersion)
            {
                return;
            }

            StatusMessage = null;
            StatusChanged?.Invoke();
        }, TimeSpan.FromSeconds(5), DispatcherPriority.Normal);
    }

    public void SetReferenceImage(Bitmap? image)
    {
        if (ReferenceEquals(BackgroundImg, image))
        {
            if (image == null)
            {
                Reference.Hidden = false;
                BumpRevision();
            }

            return;
        }

        if (image != null && (long)image.PixelSize.Width * image.PixelSize.Height > MaxDecodedImagePixels)
        {
            image.Dispose();
            SetError("Image refused: larger than 40 megapixels.");
            return;
        }

        Bitmap? previous = BackgroundImg;
        BackgroundImg = image;
        ReleaseImage(previous);
        Reference.Hidden = false;
        BumpRevision();
    }

    // Read the size from the header first: a small, compressible file can decode to gigabytes.
    public static Bitmap DecodeImage(byte[] bytes)
    {
        using (SKData data = SKData.CreateCopy(bytes))
        using (SKCodec? codec = SKCodec.Create(data))
        {
            if (codec == null)
            {
                throw new InvalidDataException("Image format not recognised.");
            }

            if ((long)codec.Info.Width * codec.Info.Height > MaxDecodedImagePixels)
            {
                throw new InvalidDataException("Image refused: larger than 40 megapixels.");
            }
        }

        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public void ReleaseImage(Bitmap? image)
    {
        if (image == null || ReferenceEquals(image, BackgroundImg) || ImageInUse?.Invoke(image) == true)
        {
            return;
        }

        image.Dispose();
        DisposedImageCount++;
    }

    private int _paletteOffset;

    public int PaletteOffset => _paletteOffset;

    public Palette ActivePalette => Palette.ByName(PaletteName);

    public World()
    {
        for (int i = 0; i < 360; i++)
        {
            UnitVectors[i] = Vec2.RotateAroundOrigin(i, new Vec2(0, 1));
        }

        ApplyPalette(Palette.Default.Name, 0, bump: false);
        ResetDocument(640, 480);
        MarkSaved();
    }

    private void ApplyPalette(string name, int offset, bool bump = true)
    {
        Palette palette = Palette.ByName(name);
        PaletteName = palette.Name;
        _paletteOffset = ((offset % 9) + 9) % 9;
        ZoomChanged();
        if (bump)
        {
            BumpRevision();
        }
    }

    public void SetPalette(string name)
    {
        ApplyPalette(name, 0);
    }

    public void CyclePalette()
    {
        int index = 0;
        for (int i = 0; i < Palette.BuiltIns.Count; i++)
        {
            if (string.Equals(Palette.BuiltIns[i].Name, PaletteName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        ApplyPalette(Palette.BuiltIns[(index + 1) % Palette.BuiltIns.Count].Name, 0);
    }

    public void RotatePalette()
    {
        ApplyPalette(PaletteName, _paletteOffset + 1);
    }

    public Styles StylesAt(double zoom)
    {
        return new Styles(ActivePalette, _paletteOffset, zoom);
    }

    public void ZoomChanged()
    {
        Styles = StylesAt(Zoom);
        Renderer.ClearTextCache();
    }

    public void SetReferenceSettings(ReferenceSettings settings)
    {
        Reference = NormalizeReference(settings, out bool limited);
        BumpRevision();
        if (limited)
        {
            SetStatus("Reference settings limited to 0-100% opacity and 25-400% scale.");
        }
    }

    private static ReferenceSettings NormalizeReference(ReferenceSettings settings, out bool limited)
    {
        ReferenceSettings next = settings.Clone();
        limited = false;
        if (next.Opacity < 0)
        {
            next.Opacity = 0;
            limited = true;
        }
        else if (next.Opacity > 1)
        {
            next.Opacity = 1;
            limited = true;
        }

        if (next.Scale < MinReferenceScale)
        {
            next.Scale = MinReferenceScale;
            limited = true;
        }
        else if (next.Scale > MaxReferenceScale)
        {
            next.Scale = MaxReferenceScale;
            limited = true;
        }

        return next;
    }

    public void SetGridSize(int size)
    {
        GridSz = Math.Clamp(size, MinGridSize, MaxGridSize);
        BumpRevision();
    }

    public void AddBox(BoxState box)
    {
        Boxes.Add(box);
        BumpRevision();
    }

    public bool RemoveLastBox()
    {
        if (Boxes.Count == 0)
        {
            return false;
        }

        Boxes.RemoveAt(Boxes.Count - 1);
        BumpRevision();
        return true;
    }

    public void AddSubdivision(SubdivisionState subdivision)
    {
        Subdivisions.Add(subdivision);
        BumpRevision();
    }

    public bool RemoveLastSubdivision()
    {
        if (Subdivisions.Count == 0)
        {
            return false;
        }

        Subdivisions.RemoveAt(Subdivisions.Count - 1);
        BumpRevision();
        return true;
    }

    public void AddEllipse(EllipseState ellipse)
    {
        Ellipses.Add(ellipse);
        BumpRevision();
    }

    public bool RemoveLastEllipse()
    {
        if (Ellipses.Count == 0)
        {
            return false;
        }

        Ellipses.RemoveAt(Ellipses.Count - 1);
        BumpRevision();
        return true;
    }

    public static VanishingPoint?[] BuildPreset(int points, int width, int height)
    {
        var result = new VanishingPoint?[9];
        double horizon = -height / 2.0 + height * 0.6;
        if (points == 1)
        {
            result[0] = new VanishingPoint(new Vec2(0, horizon), 0);
        }
        else if (points >= 2)
        {
            result[0] = new VanishingPoint(new Vec2(-width * 0.35, horizon), 0);
            result[1] = new VanishingPoint(new Vec2(width * 0.35, horizon), 0);
            if (points >= 3)
            {
                result[2] = new VanishingPoint(new Vec2(0, -height * 0.8), 0);
            }
        }

        return result;
    }

    public void ApplyPreset(int points)
    {
        VanishingPoints = BuildPreset(points, PicturePlaneWidth, PicturePlaneHeight);
        SelectionIdx = -1;
        BumpRevision();
    }

    public void SaveRig(string name)
    {
        name = name.Trim();
        var rig = new RigState { Name = name };
        for (int i = 0; i < VanishingPoints.Length; i++)
        {
            if (VanishingPoints[i] is { } point)
            {
                rig.Vps[i] = new VanishingPoint(point.Pos, point.Density);
            }
        }

        int existing = FindRig(name);
        if (existing >= 0)
        {
            Rigs[existing] = rig;
        }
        else
        {
            Rigs.Add(rig);
        }

        BumpRevision();
    }

    public bool ApplyRig(string name)
    {
        int index = FindRig(name);
        if (index < 0)
        {
            return false;
        }

        RigState rig = Rigs[index];
        VanishingPoints = new VanishingPoint?[9];
        VanishingPoint?[] slots = rig.Vps ?? Array.Empty<VanishingPoint?>();
        for (int i = 0; i < VanishingPoints.Length && i < slots.Length; i++)
        {
            if (slots[i] is { } point)
            {
                VanishingPoints[i] = new VanishingPoint(point.Pos, point.Density);
            }
        }

        SelectionIdx = -1;
        BumpRevision();
        return true;
    }

    // Names compare without case, as when a rig is saved over another.
    public int FindRig(string name)
    {
        return Rigs.FindIndex(rig => string.Equals(rig.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void DeleteRig(string name)
    {
        Rigs.RemoveAt(FindRig(name));
        BumpRevision();
    }

    public void RenameRig(string name, string newName)
    {
        Rigs[FindRig(name)].Name = newName.Trim();
        BumpRevision();
    }

    private static bool IsFinite(Vec2 point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    public void ResetDocument(int width, int height)
    {
        width = Math.Clamp(width, MinDocumentSize, MaxDocumentSize);
        height = Math.Clamp(height, MinDocumentSize, MaxDocumentSize);
        PicturePlaneWidth = width;
        PicturePlaneHeight = height;
        PicturePlaneWidthHalf = width / 2;
        PicturePlaneHeightHalf = height / 2;
        CrazyFar = (width > height ? width : height) * 32;
        Reference.Hidden = false;
        BumpRevision();
    }
}
