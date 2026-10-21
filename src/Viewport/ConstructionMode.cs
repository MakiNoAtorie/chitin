using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Chitin;

public enum ToolKind
{
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

    // The first click on a box face picks that face; otherwise four clicks give the corners.
    protected Vec2[]? PickQuad(Vec2 world)
    {
        if (Corners.Count == 0 && BoxGeometry.HitFace(World.Boxes, world) is { } face)
        {
            return face;
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
        if (_stage == 0)
        {
            _p0 = world;
            _stage = 1;
            _forced = -1;
        }
        else if (_stage == 1)
        {
            _p0 = world;
            _forced = -1;
        }
        else if (_stage == 2)
        {
            _forced = -1;
        }
    }

    public override void MouseUp()
    {
        base.MouseUp();
        if (_stage == 1)
        {
            if (!HasMoved)
            {
                _stage = 0;
                World.SetStatus("Box cancelled: drag to set the first axis.");
                return;
            }

            _p1 = CurrentWorld;
            _axis = _forced >= 0 ? _forced : ChooseAxis();
            _stage = 2;
        }
        else if (_stage == 2)
        {
            if (!HasMoved)
            {
                return;
            }

            _p2 = CurrentWorld;
            _stage = 3;
        }
        else if (_stage == 3)
        {
            Vec2 h = Height();
            if (h.Length() < 1e-6)
            {
                return;
            }

            BuiltBox built = BoxGeometry.Build(_p0, _p1, _p2, h,
                AxisDirection(1 - _axis, _p1), AxisDirection(_axis, _p2));
            PushUndo();
            World.AddBox(new BoxState
            {
                P0 = _p0,
                P1 = _p1,
                P2 = _p2,
                P3 = built.Vertices[2],
                H = h
            });
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
        switch (_stage)
        {
            case 1:
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(_p0), ToPoint(CurrentWorld));
                break;

            case 2:
            {
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(_p0), ToPoint(_p1));
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(_p0), ToPoint(CurrentWorld));
                Vec2 p3 = BoxGeometry.FourthCorner(_p0, _p1, CurrentWorld,
                    AxisDirection(1 - _axis, _p1), AxisDirection(_axis, CurrentWorld));
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(_p1), ToPoint(p3));
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(CurrentWorld), ToPoint(p3));
                break;
            }

            case 3:
            {
                if (!Dragging)
                {
                    Vec2 p3 = BoxGeometry.FourthCorner(_p0, _p1, _p2,
                        AxisDirection(1 - _axis, _p1), AxisDirection(_axis, _p2));
                    var baseEdges = new List<Prim>
                    {
                        Prim.Line(World.Styles.ConstructionPreviewPen, _p0, _p1, plane),
                        Prim.Line(World.Styles.ConstructionPreviewPen, _p1, p3, plane),
                        Prim.Line(World.Styles.ConstructionPreviewPen, p3, _p2, plane),
                        Prim.Line(World.Styles.ConstructionPreviewPen, _p2, _p0, plane)
                    };
                    Draw(baseEdges, context);
                    break;
                }

                Vec2 h = Height();
                BuiltBox built = BoxGeometry.Build(_p0, _p1, _p2, h,
                    AxisDirection(1 - _axis, _p1), AxisDirection(_axis, _p2));
                var prims = new List<Prim>();
                GuideBuilder.AddBox(prims, World, World.Styles, new BoxState
                {
                    P0 = _p0,
                    P1 = _p1,
                    P2 = _p2,
                    P3 = built.Vertices[2],
                    H = h
                }, plane);
                Draw(prims, context);
                break;
            }
        }
    }

    private int ChooseAxis()
    {
        Vec2 drag = (_p1 - _p0).Normalized();
        double first = Math.Abs(Vec2.Dot(drag, AxisDirection(0, _p0)));
        double second = Math.Abs(Vec2.Dot(drag, AxisDirection(1, _p0)));
        return first >= second ? 0 : 1;
    }

    private Vec2 Height()
    {
        Vec2 drag = CurrentWorld - _p0;
        if (World.VanishingPoints[2] is { } point && point.Pos.DistFrom(_p0) > 1e-9)
        {
            Vec2 direction = (point.Pos - _p0).Normalized();
            double length = Vec2.Dot(drag, direction);
            return direction * length;
        }

        return drag;
    }

    private Vec2 AxisDirection(int index, Vec2 origin)
    {
        if (World.VanishingPoints[index] is { } point && point.Pos.DistFrom(origin) > 1e-9)
        {
            return (point.Pos - origin).Normalized();
        }

        return index == 0 ? new Vec2(1, 0) : new Vec2(0, 1);
    }

}

internal sealed class EllipseTool : ConstructionMode
{
    private Vec2[]? _quad;
    private Homography _homography;
    private Vec2 _centre;
    private double _radius;
    private bool _radiusDrag;

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
                R = _radius
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
    }

}

internal sealed class SubdivisionTool : ConstructionMode
{
    private Vec2[]? _quad;
    private bool _edge;
    private Vec2? _edgeNear;
    private Vec2? _edgeFar;
    private int _forcedVp = -1;
    private int _n = 2;
    private int _m = 2;

    public SubdivisionTool(World world, Action pushUndo) : base(world, pushUndo)
    {
    }

    public override ToolKind Kind => ToolKind.Subdivision;

    public override void Begin(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.Begin(screen, world, modifiers);
        if (_quad != null || _edgeFar != null)
        {
            return;
        }

        if (_edge)
        {
            _edgeFar = world;
            return;
        }

        if (Corners.Count == 0 && (modifiers & KeyModifiers.Shift) != 0)
        {
            _edge = true;
            _edgeNear = world;
            return;
        }

        _quad = PickQuad(world);
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        // Only plain keys and Shift (cells per row) belong to the tool; the rest go to the viewport.
        bool shift = modifiers == KeyModifiers.Shift;
        if (modifiers != KeyModifiers.None && !shift)
        {
            return false;
        }

        if (!shift && _edge && _edgeFar == null && key is Key.D1 or Key.D2)
        {
            _forcedVp = key == Key.D1 ? 0 : 1;
            return true;
        }

        if (key >= Key.D2 && key <= Key.D9)
        {
            int value = (int)key - (int)Key.D1 + 1;
            if (_edge)
            {
                _n = value;
            }
            else if (shift)
            {
                _m = value;
            }
            else
            {
                _n = value;
                _m = value;
            }

            return true;
        }

        if (key is Key.OemPlus or Key.Add)
        {
            if (_edge)
            {
                _n = Math.Min(9, _n + 1);
            }
            else if (shift)
            {
                _m = Math.Min(9, _m + 1);
            }
            else
            {
                _n = Math.Min(9, _n + 1);
                _m = Math.Min(9, _m + 1);
            }

            return true;
        }

        if (key is Key.OemMinus or Key.Subtract)
        {
            if (_edge)
            {
                _n = Math.Max(1, _n - 1);
            }
            else if (shift)
            {
                _m = Math.Max(1, _m - 1);
            }
            else
            {
                _n = Math.Max(1, _n - 1);
                _m = Math.Max(1, _m - 1);
            }

            return true;
        }

        if (key == Key.Enter)
        {
            if (_quad == null && _edgeFar == null)
            {
                return false;
            }

            Commit();
            return true;
        }

        if (key == Key.Back)
        {
            if (_quad != null || _edge)
            {
                ResetSelection();
            }
            else if (World.Subdivisions.Count > 0)
            {
                PushUndo();
                World.RemoveLastSubdivision();
            }

            return true;
        }

        return false;
    }

    public override void DrawOver(DrawingContext context)
    {
        Rect plane = GuideBuilder.PlaneRect(World);
        var prims = new List<Prim>();
        if (_edge)
        {
            if (_edgeNear is { } near)
            {
                context.DrawLine(World.Styles.ConstructionPreviewPen, ToPoint(near), ToPoint(CurrentWorld));
                if (_edgeFar is { } far)
                {
                    GuideBuilder.AddSubdivision(prims, World, World.Styles, new SubdivisionState
                    {
                        Quad = new[] { near, far, Marker(near, far), Marker(near, far) },
                        N = _n,
                        M = 1,
                        Edge = true
                    }, World.Styles.ConstructionPreviewPen, plane);
                }
            }
        }
        else if (_quad is { } quad)
        {
            prims.Add(Prim.Poly(World.Styles.ConstructionPreviewPen, null, quad, true, plane));
            GuideBuilder.AddSubdivision(prims, World, World.Styles, new SubdivisionState
            {
                Quad = quad,
                N = _n,
                M = _m,
                Edge = false
            }, World.Styles.ConstructionPreviewPen, plane);
        }
        else
        {
            DrawCorners(context);
        }

        Draw(prims, context);
    }

    private void Commit()
    {
        if (_edge && _edgeNear is { } near && _edgeFar is { } far)
        {
            if (near.DistFrom(far) < 1e-9)
            {
                World.SetStatus("Subdivision edge needs two distinct points.");
                return;
            }

            Vec2 marker = Marker(near, far);
            PushUndo();
            World.AddSubdivision(new SubdivisionState
            {
                Quad = new[] { near, far, marker, marker },
                N = _n,
                M = 1,
                Edge = true
            });
        }
        else if (_quad is { } quad)
        {
            if (!Homography.FromUnitSquare(quad[0], quad[1], quad[2], quad[3]).IsInvertible)
            {
                World.SetStatus("Subdivision needs a non-degenerate quad.");
                return;
            }

            PushUndo();
            World.AddSubdivision(new SubdivisionState
            {
                Quad = quad,
                N = _n,
                M = _m,
                Edge = false
            });
        }

        ResetSelection();
    }

    private void ResetSelection()
    {
        _quad = null;
        Corners.Clear();
        _edge = false;
        _edgeNear = null;
        _edgeFar = null;
        _forcedVp = -1;
    }

    private Vec2 Marker(Vec2 near, Vec2 far)
    {
        if (_forcedVp >= 0 && World.VanishingPoints[_forcedVp] is { } forced)
        {
            return forced.Pos;
        }

        int best = -1;
        double bestDot = -1;
        Vec2 edge = (far - near).Normalized();
        for (int i = 0; i < 2; i++)
        {
            if (World.VanishingPoints[i] is not { } point || point.Pos.DistFrom(near) < 1e-9)
            {
                continue;
            }

            Vec2 direction = (point.Pos - near).Normalized();
            double dot = Math.Abs(Vec2.Dot(direction, edge));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return best >= 0 ? World.VanishingPoints[best]!.Pos : near;
    }

}
