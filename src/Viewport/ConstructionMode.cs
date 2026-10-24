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

// Three drags: width, depth, height. Width and depth lie on the rays from the first corner to
// their vanishing points; the height runs toward point 3, or straight up without it. The box
// keeps those links, so it follows the points when they move.
internal sealed class BoxTool : ConstructionMode
{
    private int _stage;
    private Vec2 _p0;
    private Vec2 _p1;
    private Vec2 _p2;
    private int _axis;
    private int _forced = -1;

    public BoxTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Box;

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        if (_stage <= 1)
        {
            _p0 = world;
            _stage = 1;
        }

        _forced = -1;
    }

    public override void MouseUp()
    {
        base.MouseUp();
        if (_stage == 1)
        {
            // A click without a drag sets nothing.
            if (!HasMoved)
            {
                _stage = 0;
                return;
            }

            _axis = FirstAxis();
            _p1 = OnAxis(_axis, CurrentWorld);
            _stage = 2;
        }
        else if (_stage == 2)
        {
            if (!HasMoved)
            {
                return;
            }

            _p2 = OnAxis(1 - _axis, CurrentWorld);
            _stage = 3;
        }
        else if (_stage == 3)
        {
            Vec2 h = Height();
            if (h.Length() < 1e-6)
            {
                return;
            }

            PushUndo();
            World.AddBox(Pending(_p2, h));
            _stage = 0;
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
            case 1 when Dragging:
                prims.Add(Prim.Line(World.Styles.ConstructionPreviewPen, _p0, OnAxis(FirstAxis(), CurrentWorld),
                    plane));
                break;

            case 2:
            case 3 when !Dragging:
            {
                Vec2 p2 = _stage == 2 ? OnAxis(1 - _axis, CurrentWorld) : _p2;
                (Vec2[] bottom, _) = BoxGeometry.Corners(Pending(p2, new Vec2()), World.VanishingPoints);
                prims.Add(Prim.Poly(World.Styles.ConstructionPreviewPen, null, bottom, true, plane));
                break;
            }

            case 3:
                GuideBuilder.AddBox(prims, World, World.Styles, Pending(_p2, Height()), plane);
                break;
        }

        Draw(prims, context);
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

    // The pointer projected onto the ray from p0 toward the axis point; free without that point.
    private Vec2 OnAxis(int axis, Vec2 pointer)
    {
        if (PointFor(axis) is null)
        {
            return pointer;
        }

        Vec2 direction = AxisDirection(axis, _p0);
        return _p0 + direction * Vec2.Dot(pointer - _p0, direction);
    }

    private Vec2 Height()
    {
        Vec2 drag = CurrentWorld - _p0;
        if (PointFor(2) is { } point)
        {
            Vec2 direction = (point - _p0).Normalized();
            return direction * Vec2.Dot(drag, direction);
        }

        return new Vec2(0, drag.Y);
    }

    private Vec2? PointFor(int index)
    {
        return World.VanishingPoints[index] is { } point && point.Pos.DistFrom(_p0) > 1e-9 ? point.Pos : null;
    }

    // Toward the axis point; without it, horizontal (with one point that is 1-point perspective).
    private Vec2 AxisDirection(int index, Vec2 origin)
    {
        if (World.VanishingPoints[index] is { } point && point.Pos.DistFrom(origin) > 1e-9)
        {
            return (point.Pos - origin).Normalized();
        }

        return index == 0 || World.VanishingPoints[0] != null ? new Vec2(1, 0) : new Vec2(0, 1);
    }
}

internal sealed class EllipseTool : ConstructionMode
{
    private Vec2[]? _quad;
    private Homography _homography;
    private Vec2 _centre;
    private double _radius;
    private bool _radiusDrag;
    private int _box = -1;
    private int _face;

    public EllipseTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Ellipse;

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        if (_quad == null)
        {
            if (PickQuad(world) is { } quad)
            {
                SelectQuad(quad);
            }

            return;
        }

        if (_radiusDrag || _homography.MapInverse(world) is not { } centre)
        {
            return;
        }

        _centre = centre;
        _radius = 0;
        _radiusDrag = true;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        if (_radiusDrag && _homography.MapInverse(world) is { } point)
        {
            _radius = _centre.DistFrom(point);
        }
    }

    public override void MouseUp()
    {
        base.MouseUp();
        if (!_radiusDrag || _quad == null)
        {
            return;
        }

        if (_radius > 1e-6)
        {
            PushUndo();
            World.AddEllipse(new EllipseState
            {
                Quad = _quad,
                Cx = _centre.X,
                Cy = _centre.Y,
                R = _radius,
                Box = _box,
                Face = _face
            });
            _quad = null;
            Corners.Clear();
        }

        _radius = 0;
        _radiusDrag = false;
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None && key == Key.Back)
        {
            if (_quad != null || Corners.Count > 0)
            {
                _quad = null;
                Corners.Clear();
                _radius = 0;
                _radiusDrag = false;
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
        Rect plane = GuideBuilder.PlaneRect(World);
        var prims = new List<Prim>();
        if (_quad is { } quad)
        {
            prims.Add(Prim.Poly(World.Styles.ConstructionPreviewPen, null, quad, true, plane));
            if (_radiusDrag && _radius > 0)
            {
                GuideBuilder.AddEllipse(prims, World, World.Styles, new EllipseState
                {
                    Quad = quad,
                    Cx = _centre.X,
                    Cy = _centre.Y,
                    R = _radius
                }, World.Styles.ConstructionPreviewPen, plane);
            }
        }
        else
        {
            DrawCorners(context);
        }

        Draw(prims, context);
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
