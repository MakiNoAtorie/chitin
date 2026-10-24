using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace Chitin;

public sealed class DocState
{
    [JsonPropertyName("w")]
    public int W { get; set; } = 640;

    [JsonPropertyName("h")]
    public int H { get; set; } = 480;
}

public sealed class GridState
{
    [JsonPropertyName("show")]
    public bool Show { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; } = 32;
}

public sealed class ReferenceSettings
{
    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("flipH")]
    public bool FlipH { get; set; }

    [JsonPropertyName("flipV")]
    public bool FlipV { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1;

    [JsonPropertyName("offset")]
    public Vec2 Offset { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    public ReferenceSettings Clone()
    {
        return new ReferenceSettings
        {
            Opacity = Opacity,
            FlipH = FlipH,
            FlipV = FlipV,
            Scale = Scale,
            Offset = Offset,
            Hidden = Hidden
        };
    }

}

public sealed class BoxState
{
    [JsonPropertyName("p0")]
    public Vec2 P0 { get; set; }

    [JsonPropertyName("p1")]
    public Vec2 P1 { get; set; }

    [JsonPropertyName("p2")]
    public Vec2 P2 { get; set; }

    [JsonPropertyName("p3")]
    public Vec2 P3 { get; set; }

    [JsonPropertyName("h")]
    public Vec2 H { get; set; }

    // The point each edge aims at (width, depth, height; -1 none) and the signed edge lengths from
    // p0 toward it. Null: an older box, drawn as stored.
    [JsonPropertyName("follow")]
    public int[]? Follow { get; set; }

    [JsonPropertyName("lengths")]
    public double[]? Lengths { get; set; }

    public BoxState Clone()
    {
        return new BoxState
        {
            P0 = P0, P1 = P1, P2 = P2, P3 = P3, H = H,
            Follow = (int[]?)Follow?.Clone(),
            Lengths = (double[]?)Lengths?.Clone()
        };
    }
}

public sealed class SubdivisionState
{
    [JsonPropertyName("quad")]
    public Vec2[] Quad { get; set; } = Array.Empty<Vec2>();

    [JsonPropertyName("n")]
    public int N { get; set; }

    [JsonPropertyName("m")]
    public int M { get; set; }

    [JsonPropertyName("edge")]
    public bool Edge { get; set; }

    // The box face it was picked from, so it follows the box; -1: the stored quad.
    [JsonPropertyName("box")]
    public int Box { get; set; } = -1;

    [JsonPropertyName("face")]
    public int Face { get; set; }

    public SubdivisionState Clone()
    {
        return new SubdivisionState
        {
            Quad = Quad == null ? Array.Empty<Vec2>() : Copy(Quad),
            N = N,
            M = M,
            Edge = Edge,
            Box = Box,
            Face = Face
        };
    }

    internal static Vec2[] Copy(Vec2[] points)
    {
        var copy = new Vec2[points.Length];
        Array.Copy(points, copy, points.Length);
        return copy;
    }
}

public sealed class EllipseState
{
    [JsonPropertyName("quad")]
    public Vec2[] Quad { get; set; } = Array.Empty<Vec2>();

    [JsonPropertyName("cx")]
    public double Cx { get; set; }

    [JsonPropertyName("cy")]
    public double Cy { get; set; }

    [JsonPropertyName("r")]
    public double R { get; set; }

    [JsonPropertyName("box")]
    public int Box { get; set; } = -1;

    [JsonPropertyName("face")]
    public int Face { get; set; }

    public EllipseState Clone()
    {
        return new EllipseState
        {
            Quad = Quad == null ? Array.Empty<Vec2>() : SubdivisionState.Copy(Quad),
            Cx = Cx,
            Cy = Cy,
            R = R,
            Box = Box,
            Face = Face
        };
    }
}

public sealed class WorldState
{
    [JsonPropertyName("doc")]
    public DocState Doc { get; set; } = new();

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1;

    [JsonPropertyName("camera")]
    public Vec2 Camera { get; set; }

    [JsonPropertyName("vps")]
    public VanishingPoint?[] VanishingPoints { get; set; } = new VanishingPoint?[9];

    [JsonPropertyName("traceLines")]
    public List<TraceLine> TraceLines { get; set; } = new();

    [JsonPropertyName("grid")]
    public GridState Grid { get; set; } = new();

    [JsonPropertyName("boxes")]
    public List<BoxState> Boxes { get; set; } = new();

    [JsonPropertyName("subdivisions")]
    public List<SubdivisionState> Subdivisions { get; set; } = new();

    [JsonPropertyName("ellipses")]
    public List<EllipseState> Ellipses { get; set; } = new();

    [JsonPropertyName("reference")]
    public ReferenceSettings Reference { get; set; } = new();

    [JsonPropertyName("palette")]
    public string Palette { get; set; } = Chitin.Palette.Default.Name;

    [JsonPropertyName("showCone")]
    public bool ShowCone { get; set; }

    // Not serialised: ProjectFile stores the image as backgroundPng.
    [JsonIgnore]
    public Bitmap? Background { get; set; }
}

public sealed class UndoStack
{
    public const int MaxDepth = 50;

    // Each entry is the state before a command and that command's name.
    private readonly List<(WorldState State, string Name)> _undo = new();
    private readonly List<(WorldState State, string Name)> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public Action<WorldState>? Dropped;

    // The command an undo or redo would revert, for the Edit menu.
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    public bool Holds(Bitmap image)
    {
        return _undo.Any(entry => ReferenceEquals(entry.State.Background, image)) ||
               _redo.Any(entry => ReferenceEquals(entry.State.Background, image));
    }

    public void Push(WorldState state, string name)
    {
        _undo.Add((state, name));
        TrimUndo();
        DropAll(_redo);
    }

    public void Clear()
    {
        DropAll(_undo);
        DropAll(_redo);
    }

    public WorldState? Undo(WorldState current)
    {
        return Move(_undo, _redo, current);
    }

    public WorldState? Redo(WorldState current)
    {
        WorldState? state = Move(_redo, _undo, current);
        TrimUndo();
        return state;
    }

    private static WorldState? Move(List<(WorldState State, string Name)> from,
        List<(WorldState State, string Name)> to, WorldState current)
    {
        if (from.Count == 0)
        {
            return null;
        }

        (WorldState state, string name) = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add((current, name));
        return state;
    }

    private void TrimUndo()
    {
        if (_undo.Count > MaxDepth)
        {
            WorldState oldest = _undo[0].State;
            _undo.RemoveAt(0);
            Dropped?.Invoke(oldest);
        }
    }

    // Remove first, then notify, so Holds no longer sees the dropped states.
    private void DropAll(List<(WorldState State, string Name)> states)
    {
        WorldState[] dropped = states.Select(entry => entry.State).ToArray();
        states.Clear();
        foreach (WorldState state in dropped)
        {
            Dropped?.Invoke(state);
        }
    }
}
