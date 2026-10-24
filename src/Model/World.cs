using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Chitin;

public class World
{
    public const int PicturePlaneBorderSz = 8;
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
    public bool ShowGrid;
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
            Grid = new GridState { Show = ShowGrid, Size = GridSz },
            Reference = Reference.Clone(),
            Palette = PaletteName,
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

        return state;
    }

    public void Restore(WorldState state)
    {
        SetDocumentSize(state.Doc.W, state.Doc.H);
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

        // A dropped box shifts the ones after it: shapes follow the new index, or their stored quad.
        Boxes = new List<BoxState>();
        var boxIndex = new List<int>();
        foreach (BoxState box in state.Boxes ?? new List<BoxState>())
        {
            if (box == null || !IsFinite(box.P0) || !IsFinite(box.P1) || !IsFinite(box.P2) ||
                !IsFinite(box.P3) || !IsFinite(box.H))
            {
                boxIndex.Add(-1);
                continue;
            }

            boxIndex.Add(Boxes.Count);

            // A bad link makes it an ordinary box, drawn as stored.
            BoxState copy = box.Clone();
            if (copy.Follow is { } follow && (follow.Length != 3 || follow.Any(index => index is < -1 or > 8) ||
                copy.Lengths is not { Length: 3 } lengths || !lengths.All(double.IsFinite)))
            {
                copy.Follow = null;
                copy.Lengths = null;
            }

            Boxes.Add(copy);
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
            copy.Box = RemapBox(boxIndex, copy.Box);
            copy.Box = ValidFace(copy.Box, copy.Face) && !copy.Edge ? copy.Box : -1;
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

            EllipseState ellipseCopy = ellipse.Clone();
            ellipseCopy.Box = RemapBox(boxIndex, ellipseCopy.Box);
            ellipseCopy.Box = ValidFace(ellipseCopy.Box, ellipseCopy.Face) ? ellipseCopy.Box : -1;
            Ellipses.Add(ellipseCopy);
        }

        RestoreView(state);
        Reference = NormalizeReference(state.Reference, out _);
        SelectionIdx = -1;
        BumpRevision();
    }

    private static int RemapBox(List<int> boxIndex, int box)
    {
        return box >= 0 && box < boxIndex.Count ? boxIndex[box] : -1;
    }

    // View state: saved in the file, but never in undo and never a reason to ask before closing.
    public void RestoreView(WorldState state)
    {
        ShowGrid = state.Grid.Show;
        GridSz = Math.Clamp(state.Grid.Size, MinGridSize, MaxGridSize);
        ShowCone = state.ShowCone;
        PaletteName = Palette.ByName(state.Palette).Name;
        ZoomChanged();
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
            // Nothing to remove: the document does not change, but a later image starts shown.
            if (image == null)
            {
                Reference.Hidden = false;
            }

            return;
        }

        if (image != null && ImageRefusal(image) is { } refusal)
        {
            image.Dispose();
            SetError(refusal);
            return;
        }

        Bitmap? previous = BackgroundImg;
        BackgroundImg = image;
        ReleaseImage(previous);
        Reference.Hidden = false;
        BumpRevision();
    }

    private Bitmap? _imageAccepted;

    // Why an image cannot be the reference, or null. Save embeds it as PNG, so an image that loads
    // but does not save is refused here. The encode is slow on a large image: the last image passed
    // is remembered, so a check before the undo step and the set after it encode once.
    public string? ImageRefusal(Bitmap image)
    {
        if (ReferenceEquals(image, _imageAccepted))
        {
            return null;
        }

        if ((long)image.PixelSize.Width * image.PixelSize.Height > MaxDecodedImagePixels)
        {
            return "Image refused: larger than 40 megapixels.";
        }

        if (!FitsInProject(EncodePng(image).LongLength))
        {
            return "Image refused: over 64 MB as PNG, too large to save in a project. Use a smaller image.";
        }

        _imageAccepted = image;
        return null;
    }

    public static byte[] EncodePng(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream);
        return stream.ToArray();
    }

    // The project stores the PNG as base64.
    public static bool FitsInProject(long pngBytes)
    {
        return (pngBytes + 2) / 3 * 4 <= MaxEncodedImageBytes;
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

    public Palette ActivePalette => Palette.ByName(PaletteName);

    public World()
    {
        for (int i = 0; i < 360; i++)
        {
            UnitVectors[i] = Vec2.RotateAroundOrigin(i, new Vec2(0, 1));
        }

        ZoomChanged();
        ResetDocument(640, 480);
        MarkSaved();
    }

    // View state: no undo, no unsaved-changes flag.
    public void SetPalette(string name)
    {
        PaletteName = Palette.ByName(name).Name;
        ZoomChanged();
        BumpRevision(false);
    }

    public Styles StylesAt(double zoom, bool export = false)
    {
        return new Styles(ActivePalette, zoom, export);
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
        BumpRevision(false);
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

        // Shapes on its faces keep where they are now and stop following.
        int last = Boxes.Count - 1;
        foreach (SubdivisionState subdivision in Subdivisions.Where(item => item.Box == last))
        {
            subdivision.Quad = BoxGeometry.FaceQuad(this, last, subdivision.Face)!;
            subdivision.Box = -1;
        }

        foreach (EllipseState ellipse in Ellipses.Where(item => item.Box == last))
        {
            ellipse.Quad = BoxGeometry.FaceQuad(this, last, ellipse.Face)!;
            ellipse.Box = -1;
        }

        Boxes.RemoveAt(last);
        BumpRevision();
        return true;
    }

    private bool ValidFace(int box, int face)
    {
        return box >= 0 && box < Boxes.Count && face is >= 0 and < 6;
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

    private static bool IsFinite(Vec2 point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    public void ResetDocument(int width, int height)
    {
        SetDocumentSize(width, height);
        Reference.Hidden = false;
        BumpRevision();
    }

    // No revision bump: Restore bumps once, when the whole state is in place.
    private void SetDocumentSize(int width, int height)
    {
        width = Math.Clamp(width, MinDocumentSize, MaxDocumentSize);
        height = Math.Clamp(height, MinDocumentSize, MaxDocumentSize);
        PicturePlaneWidth = width;
        PicturePlaneHeight = height;
        PicturePlaneWidthHalf = width / 2;
        PicturePlaneHeightHalf = height / 2;
        CrazyFar = (width > height ? width : height) * 32;
    }

    // Where the two trace lines cross, as drawn (lines without ends); null for fewer than two, or parallel.
    public Vec2? TraceCrossing()
    {
        if (TraceLines.Count < 2)
        {
            return null;
        }

        TraceLine first = TraceLines[0];
        TraceLine second = TraceLines[1];
        return Geom.IntersectLines(first.Start, (first.End - first.Start).Normalized(), second.Start,
            (second.End - second.Start).Normalized(), out Vec2 crossing) ? crossing : null;
    }
}
