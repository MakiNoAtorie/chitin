using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Chitin;

public enum ToolKind
{
    Trace,
    Box,
    Subdivision,
    Ellipse
}

internal abstract class ConstructionMode : MouseMode
{
    protected readonly Action PushUndo;
    protected Vec2 CurrentWorld;
    protected bool Dragging;
    protected readonly List<Vec2> Corners = new();

    protected ConstructionMode(World world, Action pushUndo) : base(world)
    {
        PushUndo = pushUndo;
    }

    public abstract ToolKind Kind { get; }

    // The current step for the foot, or null for the tool's general hint.
    public virtual string? Hint => Corners.Count > 0 ? $"Corner {Corners.Count + 1} of 4 · Backspace restarts" : null;

    public virtual void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Init(screen, world);
        CurrentWorld = world;
        Dragging = true;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        CurrentWorld = world;
    }

    public override void MouseUp()
    {
        Dragging = false;
    }

    public virtual bool HandleKey(Key key, KeyModifiers modifiers)
    {
        return false;
    }

    protected void Draw(List<Prim> prims, DrawingContext context)
    {
        Renderer.DrawPrims(context, prims, RenderMode.Interactive);
    }

    // The box and face of the last pick, or -1 for four clicked corners.
    protected int PickedBox = -1;
    protected int PickedFace;

    // The first click on a box face picks that face; otherwise four clicks give the corners.
    protected Vec2[]? PickQuad(Vec2 world)
    {
        PickedBox = -1;
        if (Corners.Count == 0 && BoxGeometry.HitFace(World.Boxes, world, World.VanishingPoints) is { } hit)
        {
            PickedBox = hit.Box;
            PickedFace = hit.Face;
            return hit.Quad;
        }

        Corners.Add(world);
        if (Corners.Count < 4)
        {
            return null;
        }

        Vec2[] quad = Corners.ToArray();
        Corners.Clear();
        if (!BoxGeometry.IsConvex(quad))
        {
            World.SetStatus("Corners refused: click the four corners in order around the shape.");
            return null;
        }

        return quad;
    }

    protected void DrawCorners(DrawingContext context)
    {
        for (int i = 0; i < Corners.Count; i++)
        {
            context.DrawEllipse(World.Styles.VPFill[0], World.Styles.BoxEdgePen, ToPoint(Corners[i]),
                3.0 / World.Zoom, 3.0 / World.Zoom);
            if (i > 0)
            {
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(Corners[i - 1]),
                    ToPoint(Corners[i]));
            }
        }

        if (Corners.Count > 0)
        {
            context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(Corners[^1]), ToPoint(CurrentWorld));
        }
    }

    protected static Point ToPoint(Vec2 point)
    {
        return new Point(point.X, point.Y);
    }
}

// Four steps: first corner, width, depth, height. Each click sets the current step from the
// pointer, and between clicks the preview follows it, so clicks and drags both work: a drag from
// the first corner sets the width where it ends. Width runs along the ray from the first corner to
// its vanishing point; depth puts the far corner under the pointer; height follows the pointer's
// move up or down from the depth click, toward point 3 when it exists. The box keeps its links to
// the points, so it follows them when they move.
internal sealed class BoxTool : ConstructionMode
{
    // A shorter move is a click, and a shorter edge is no edge, in screen pixels.
    private const double MinPixels = 4;

    private int _stage;
    private bool _starting;
    private Point _press;
    private Vec2 _p0;
    private Vec2 _p1;
    private Vec2 _p2;
    private Vec2 _heightFrom;
    private int _axis;
    private int _forced = -1;

    public BoxTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Box;

    public override string? Hint => _stage switch
    {
        1 => "Box 2 of 4: move along a guide, click to set the width · 1 / 2 pick point 1 or 2 · Backspace restarts",
        2 => "Box 3 of 4: move out from the edge, click to set the depth · Backspace restarts",
        3 => "Box 4 of 4: move up or down, click to set the height · Backspace restarts",
        _ => "Box 1 of 4: click the first corner, or drag the first edge · Esc leaves"
    };

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        _press = screen;
        if (_stage == 0)
        {
            _p0 = world;
            _forced = -1;
            _starting = true;
            _stage = 1;
        }
    }

    public override void MouseUp()
    {
        base.MouseUp();
        bool click = new Vec2(_press.X, _press.Y).DistFrom(LastScreen) < MinPixels;
        if (_stage == 1 && _starting)
        {
            // The press that placed the first corner: a click waits for the width click.
            _starting = false;
            if (click)
            {
                return;
            }
        }

        switch (_stage)
        {
            case 1:
            {
                int axis = FirstAxis();
                Vec2 p1 = OnAxis(axis, CurrentWorld);
                if (Pixels(p1, _p0) >= MinPixels)
                {
                    _axis = axis;
                    _p1 = p1;
                    _stage = 2;
                }

                break;
            }

            case 2:
            {
                Vec2 p2 = DepthCorner(CurrentWorld);
                if (Pixels(p2, _p0) >= MinPixels)
                {
                    _p2 = p2;
                    _heightFrom = CurrentWorld;
                    _stage = 3;
                }

                break;
            }

            case 3:
            {
                Vec2 h = Height();
                if (h.Length() * World.Zoom >= MinPixels)
                {
                    PushUndo();
                    World.AddBox(Pending(_p2, h));
                    _stage = 0;
                }

                break;
            }
        }
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None)
        {
            return false;
        }

        if (key is Key.D1 or Key.D2 && _stage == 1)
        {
            _forced = key == Key.D1 ? 0 : 1;
            return true;
        }

        if (key == Key.Back)
        {
            if (_stage > 0)
            {
                _stage = 0;
            }
            else if (World.Boxes.Count > 0)
            {
                PushUndo();
                World.RemoveLastBox();
            }

            return true;
        }

        return false;
    }

    public override void DrawOver(DrawingContext context)
    {
        Rect plane = GuideBuilder.PlaneRect(World);
        var prims = new List<Prim>();
        switch (_stage)
        {
            case 1:
                prims.Add(Prim.Line(World.Styles.ConstructionPreviewPen, _p0, OnAxis(FirstAxis(), CurrentWorld),
                    plane));
                break;

            case 2:
            {
                (Vec2[] bottom, _) = BoxGeometry.Corners(Pending(DepthCorner(CurrentWorld), new Vec2()),
                    World.VanishingPoints);
                prims.Add(Prim.Poly(World.Styles.ConstructionPreviewPen, null, bottom, true, plane));
                break;
            }

            case 3:
                GuideBuilder.AddBox(prims, World, World.Styles, Pending(_p2, Height()), plane);
                break;
        }

        Draw(prims, context);
        if (_stage > 0)
        {
            context.DrawEllipse(World.Styles.VPFill[0], World.Styles.BoxEdgePen, ToPoint(_p0),
                3.0 / World.Zoom, 3.0 / World.Zoom);
        }
    }

    // The box as it stands, with its links to the points that exist now.
    private BoxState Pending(Vec2 p2, Vec2 h)
    {
        int a = _axis;
        int b = 1 - _axis;
        Vec2 p3 = BoxGeometry.FourthCorner(_p0, _p1, p2, AxisDirection(b, _p1), AxisDirection(a, p2));
        Vec2? up = PointFor(2);
        return new BoxState
        {
            P0 = _p0,
            P1 = _p1,
            P2 = p2,
            P3 = p3,
            H = h,
            Follow = new[] { PointFor(a) != null ? a : -1, PointFor(b) != null ? b : -1, up != null ? 2 : -1 },
            Lengths = new[]
            {
                Vec2.Dot(_p1 - _p0, AxisDirection(a, _p0)),
                Vec2.Dot(p2 - _p0, AxisDirection(b, _p0)),
                up is { } top ? Vec2.Dot(h, (top - _p0).Normalized()) : 0
            }
        };
    }

    private int FirstAxis()
    {
        if (_forced >= 0)
        {
            return _forced;
        }

        Vec2 drag = (CurrentWorld - _p0).Normalized();
        double first = Math.Abs(Vec2.Dot(drag, AxisDirection(0, _p0)));
        double second = Math.Abs(Vec2.Dot(drag, AxisDirection(1, _p0)));
        return first >= second ? 0 : 1;
    }

    // The pointer projected onto the ray from p0 along the axis.
    private Vec2 OnAxis(int axis, Vec2 pointer)
    {
        Vec2 direction = AxisDirection(axis, _p0);
        return _p0 + direction * Vec2.Dot(pointer - _p0, direction);
    }

    // The depth corner p2 that puts the far corner p3 at the pointer: p3 slides along the line from
    // p1 toward the depth point, and p2 is where the line from p3 toward the width point meets the
    // depth ray from p0.
    private Vec2 DepthCorner(Vec2 pointer)
    {
        int a = _axis;
        int b = 1 - _axis;
        Vec2 along = AxisDirection(b, _p1);
        Vec2 p3 = _p1 + along * Vec2.Dot(pointer - _p1, along);
        return Geom.IntersectLines(_p0, AxisDirection(b, _p0), p3, AxisDirection(a, p3), out Vec2 p2)
            ? p2
            : _p0 + (p3 - _p1);
    }

    // The pointer's move from the depth click, along the line toward point 3, or straight up or down.
    private Vec2 Height()
    {
        Vec2 move = CurrentWorld - _heightFrom;
        if (PointFor(2) is { } point)
        {
            Vec2 direction = (point - _p0).Normalized();
            return direction * Vec2.Dot(move, direction);
        }

        return new Vec2(0, move.Y);
    }

    private Vec2? PointFor(int index)
    {
        return World.VanishingPoints[index] is { } point && point.Pos.DistFrom(_p0) > 1e-9 ? point.Pos : null;
    }

    // Toward the axis point. Without it: horizontal, which with one point is 1-point perspective;
    // with neither point the depth runs up and to the right, an oblique view.
    private Vec2 AxisDirection(int index, Vec2 origin)
    {
        if (World.VanishingPoints[index] is { } point && point.Pos.DistFrom(origin) > 1e-9)
        {
            return (point.Pos - origin).Normalized();
        }

        return index == 0 || World.VanishingPoints[0] != null
            ? new Vec2(1, 0)
            : new Vec2(Math.Sqrt(0.5), -Math.Sqrt(0.5));
    }

    private double Pixels(Vec2 a, Vec2 b)
    {
        return a.DistFrom(b) * World.Zoom;
    }
}

// Three steps: a box face (or four corners), the centre, the radius. Each click sets the current
// step and the preview follows the pointer between clicks, so clicks and drags both work: a drag
// from the centre sets the radius where it ends. Centre and radius are kept in the face's
// unit-square coordinates, so the ellipse lies in the face's plane.
internal sealed class EllipseTool : ConstructionMode
{
    // A shorter move is a click, and a shorter radius is no radius, in screen pixels.
    private const double MinPixels = 4;

    private Vec2[]? _quad;
    private Homography _homography;
    private Vec2? _centre;
    private bool _placing;
    private Point _press;
    private int _box = -1;
    private int _face;

    public EllipseTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Ellipse;

    public override string? Hint => _quad == null
        ? Corners.Count > 0
            ? $"Ellipse 1 of 3: corner {Corners.Count + 1} of 4 · Backspace restarts"
            : "Ellipse 1 of 3: click a box face, or click four corners · Esc leaves"
        : _centre == null
            ? "Ellipse 2 of 3: click the centre, or drag out the radius · Backspace restarts"
            : "Ellipse 3 of 3: move out, click to set the radius · Backspace restarts";

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        _press = screen;
        if (_quad == null)
        {
            if (PickQuad(world) is { } quad)
            {
                SelectQuad(quad);
            }
        }
        else if (_centre == null && _homography.MapInverse(world) is { } centre)
        {
            _centre = centre;
            _placing = true;
        }
    }

    public override void MouseUp()
    {
        base.MouseUp();
        if (_quad is not { } quad || _centre is not { } centre)
        {
            return;
        }

        // The press that placed the centre: a click waits for the radius click.
        if (_placing)
        {
            _placing = false;
            if (new Vec2(_press.X, _press.Y).DistFrom(LastScreen) < MinPixels)
            {
                return;
            }
        }

        if (Radius() is not { } radius ||
            _homography.Map(centre.X, centre.Y).DistFrom(CurrentWorld) * World.Zoom < MinPixels)
        {
            return;
        }

        PushUndo();
        World.AddEllipse(new EllipseState
        {
            Quad = quad,
            Cx = centre.X,
            Cy = centre.Y,
            R = radius,
            Box = _box,
            Face = _face
        });
        Restart();
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None && key == Key.Back)
        {
            if (_quad != null || Corners.Count > 0)
            {
                Restart();
            }
            else if (World.Ellipses.Count > 0)
            {
                PushUndo();
                World.RemoveLastEllipse();
            }

            return true;
        }

        return false;
    }

    public override void DrawOver(DrawingContext context)
    {
        if (_quad is not { } quad)
        {
            DrawCorners(context);
            return;
        }

        Rect plane = GuideBuilder.PlaneRect(World);
        var prims = new List<Prim> { Prim.Poly(World.Styles.ConstructionPreviewPen, null, quad, true, plane) };
        if (_centre is { } centre && Radius() is { } radius && radius > 0)
        {
            GuideBuilder.AddEllipse(prims, World, World.Styles, new EllipseState
            {
                Quad = quad,
                Cx = centre.X,
                Cy = centre.Y,
                R = radius
            }, World.Styles.ConstructionPreviewPen, plane);
        }

        Draw(prims, context);
        if (_centre is { } dot)
        {
            context.DrawEllipse(World.Styles.VPFill[0], World.Styles.BoxEdgePen,
                ToPoint(_homography.Map(dot.X, dot.Y)), 3.0 / World.Zoom, 3.0 / World.Zoom);
        }
    }

    // The pointer's distance from the centre, in the face's unit square.
    private double? Radius()
    {
        return _centre is { } centre && _homography.MapInverse(CurrentWorld) is { } point
            ? centre.DistFrom(point)
            : null;
    }

    private void SelectQuad(Vec2[] quad)
    {
        Homography homography = Homography.FromUnitSquare(quad[0], quad[1], quad[2], quad[3]);
        if (!homography.IsInvertible)
        {
            World.SetStatus("Ellipse needs a non-degenerate quad.");
            return;
        }

        _quad = quad;
        _homography = homography;
        _box = PickedBox;
        _face = PickedFace;
    }

    private void Restart()
    {
        _quad = null;
        _centre = null;
        _placing = false;
        Corners.Clear();
    }
}

// A pick (box face or four corners) adds the subdivision at once; 2-9 and + / - then change the
// cells of the one just added, each change one undo step.
internal sealed class SubdivisionTool : ConstructionMode
{
    private SubdivisionState? _last;
    private int _n = 2;
    private int _m = 2;

    public SubdivisionTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Subdivision;

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        if (PickQuad(world) is not { } quad)
        {
            return;
        }

        if (!Homography.FromUnitSquare(quad[0], quad[1], quad[2], quad[3]).IsInvertible)
        {
            World.SetStatus("Subdivision needs a non-degenerate quad.");
            return;
        }

        PushUndo();
        _last = new SubdivisionState { Quad = quad, N = _n, M = _m, Edge = false, Box = PickedBox, Face = PickedFace };
        World.AddSubdivision(_last);
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        // Only plain keys and Shift (rows only) belong to the tool; the rest go to the viewport.
        bool shift = modifiers == KeyModifiers.Shift;
        if (modifiers != KeyModifiers.None && !shift)
        {
            return false;
        }

        int? cells = key is >= Key.D2 and <= Key.D9 ? key - Key.D0 : null;
        int step = key is Key.OemPlus or Key.Add ? 1 : key is Key.OemMinus or Key.Subtract ? -1 : 0;
        if (cells != null || step != 0)
        {
            int n = shift ? _n : Math.Clamp(cells ?? _n + step, 1, 9);
            int m = Math.Clamp(cells ?? _m + step, 1, 9);
            SetCells(n, m);
            return true;
        }

        if (key == Key.Back)
        {
            if (Corners.Count > 0)
            {
                Corners.Clear();
            }
            else if (World.Subdivisions.Count > 0)
            {
                PushUndo();
                World.RemoveLastSubdivision();
                _last = null;
            }

            return true;
        }

        return false;
    }

    // The counts are also the next pick's. After an undo the last subdivision is a copy, so only
    // the counts change.
    private void SetCells(int n, int m)
    {
        _n = n;
        _m = m;
        if (_last != null && World.Subdivisions.Contains(_last) && (_last.N != n || _last.M != m))
        {
            PushUndo();
            _last.N = n;
            _last.M = m;
            World.BumpRevision();
        }
    }

    public override void DrawOver(DrawingContext context)
    {
        DrawCorners(context);
    }
}
