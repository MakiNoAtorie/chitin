using Avalonia;
using Avalonia.Media;

namespace Chitin;

public enum PrimKind
{
    Line,
    Circle,
    Text,
    Poly
}

public readonly struct Prim
{
    public PrimKind Kind { get; }
    public IPen? Pen { get; }
    public IBrush? Brush { get; }
    public Point A { get; }
    public Point B { get; }
    public double Radius { get; }
    public string? Text { get; }
    public double Size { get; }
    public bool Serif { get; }
    public bool ScreenOnly { get; }
    public Rect? Clip { get; }
    public Vec2[]? Points { get; }
    public bool Closed { get; }

    private Prim(PrimKind kind, IPen? pen, IBrush? brush, Point a, Point b, double radius,
        string? text, double size, bool serif, bool screenOnly, Rect? clip,
        Vec2[]? points = null, bool closed = false)
    {
        Kind = kind;
        Pen = pen;
        Brush = brush;
        A = a;
        B = b;
        Radius = radius;
        Text = text;
        Size = size;
        Serif = serif;
        ScreenOnly = screenOnly;
        Clip = clip;
        Points = points;
        Closed = closed;
    }

    public static Prim Line(IPen pen, Vec2 a, Vec2 b, Rect? clip, bool screenOnly = false)
    {
        return new Prim(PrimKind.Line, pen, null, new Point(a.X, a.Y), new Point(b.X, b.Y),
            0, null, 0, false, screenOnly, clip);
    }

    public static Prim Circle(IBrush? fill, IPen outline, Vec2 center, double radius, bool screenOnly = false)
    {
        return new Prim(PrimKind.Circle, outline, fill, new Point(center.X, center.Y), default,
            radius, null, 0, false, screenOnly, null);
    }

    public static Prim Label(string text, double size, IBrush brush, Vec2 center, bool serif,
        bool screenOnly = false)
    {
        return new Prim(PrimKind.Text, null, brush, new Point(center.X, center.Y), default,
            0, text, size, serif, screenOnly, null);
    }

    public static Prim Poly(IPen? pen, IBrush? brush, Vec2[] points, bool closed,
        Rect? clip = null, bool screenOnly = false)
    {
        return new Prim(PrimKind.Poly, pen, brush, default, default, 0, null, 0, false,
            screenOnly, clip, points, closed);
    }
}

public static class GuideBuilder
{
    public const int MaxAutomaticDensity = 96;

    public static int FanDensity(VanishingPoint point)
    {
        if (point.Density != 0)
        {
            return VanishingPoint.ClampDensity(point.Density);
        }

        return Math.Min(Math.Max((int)(point.Pos.Length() / 32.0), 4), MaxAutomaticDensity);
    }

    public static Rect PlaneRect(World world)
    {
        return new Rect(-world.PicturePlaneWidthHalf, -world.PicturePlaneHeightHalf,
            world.PicturePlaneWidth, world.PicturePlaneHeight);
    }

    public static Prim[] Build(World world, Styles styles)
    {
        var prims = new List<Prim>();
        Rect plane = PlaneRect(world);

        if (world.ShowGrid)
        {
            AddGrid(prims, world, styles, plane);
        }

        AddHorizon(prims, world, styles, plane);
        if (world.ShowCone)
        {
            AddCone(prims, world, styles);
        }

        AddConstruction(prims, world, styles, plane);

        for (int i = 0; i < 9; i++)
        {
            if (world.VanishingPoints[i] is { } point)
            {
                AddVanishingPoint(prims, world, styles, i, point, plane);
            }
        }

        foreach (TraceLine traceLine in world.TraceLines)
        {
            AddTraceLine(prims, world, styles, traceLine);
        }

        return prims.ToArray();
    }

    private static void AddGrid(List<Prim> prims, World world, Styles styles, Rect plane)
    {
        if (world.GridSz <= 0)
        {
            return;
        }

        int limit = Math.Max(world.PicturePlaneWidth, world.PicturePlaneHeight);
        double x = -world.PicturePlaneWidthHalf + world.GridSz;
        double y = -world.PicturePlaneHeightHalf + world.GridSz;
        for (int i = 0; i < limit; i += world.GridSz)
        {
            prims.Add(Prim.Line(styles.GridPen,
                new Vec2(x, -world.PicturePlaneHeightHalf),
                new Vec2(x, world.PicturePlaneHeightHalf), plane));
            x += world.GridSz;
        }

        for (int i = 0; i < limit; i += world.GridSz)
        {
            prims.Add(Prim.Line(styles.GridPen,
                new Vec2(-world.PicturePlaneWidthHalf, y),
                new Vec2(world.PicturePlaneWidthHalf, y), plane));
            y += world.GridSz;
        }
    }

    private static void AddHorizon(List<Prim> prims, World world, Styles styles, Rect plane)
    {
        if (world.VanishingPoints[0] is not { } first || world.VanishingPoints[1] is not { } second ||
            first.Pos.DistFrom(second.Pos) == 0)
        {
            return;
        }

        Vec2 direction = (second.Pos - first.Pos).Normalized();
        Vec2 start = first.Pos - direction * world.CrazyFar;
        Vec2 end = first.Pos + direction * world.CrazyFar;

        prims.Add(Prim.Line(styles.HorizonPen, start, end, plane));

        double far = world.CrazyFar;
        Rect[] outside =
        {
            new(-far, -far, plane.X + far, far * 2),
            new(plane.Right, -far, far - plane.Right, far * 2),
            new(plane.X, -far, plane.Width, plane.Y + far),
            new(plane.X, plane.Bottom, plane.Width, far - plane.Bottom)
        };

        foreach (Rect clip in outside)
        {
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                continue;
            }

            prims.Add(Prim.Line(styles.HorizonVoidPen, start, end, clip));
        }
    }

    private static void AddCone(List<Prim> prims, World world, Styles styles)
    {
        if (!ConeOfVision.TryBuild(world.VanishingPoints[0]?.Pos, world.VanishingPoints[1]?.Pos,
                world.PicturePlaneWidth, out ConeOfVision cone))
        {
            return;
        }

        prims.Add(Prim.Circle(null, styles.ConePen, cone.Principal, cone.Radius));
        prims.Add(Prim.Line(styles.ConeLegPen, cone.Station, cone.V0, null));
        prims.Add(Prim.Line(styles.ConeLegPen, cone.Station, cone.V1, null));

        double marker = 5.0 / styles.Zoom;
        prims.Add(Prim.Circle(styles.StationFill, styles.StationPen, cone.Station, marker));
        prims.Add(Prim.Label("S", 12.0 / styles.Zoom, styles.HudText, cone.Station, serif: true));

        double offset = 22.0 / styles.Zoom;
        double tick = 6.0 / styles.Zoom;
        for (int i = 0; i < world.VanishingPoints.Length; i++)
        {
            if (world.VanishingPoints[i] is not { } point ||
                point.Pos.DistFrom(cone.Principal) <= cone.Radius + 1e-9)
            {
                continue;
            }

            var centre = new Vec2(point.Pos.X + offset, point.Pos.Y - offset);
            prims.Add(Prim.Line(styles.WarningPen,
                new Vec2(centre.X - tick, centre.Y - tick),
                new Vec2(centre.X + tick, centre.Y + tick), null, screenOnly: true));
            prims.Add(Prim.Line(styles.WarningPen,
                new Vec2(centre.X - tick, centre.Y + tick),
                new Vec2(centre.X + tick, centre.Y - tick), null, screenOnly: true));
        }
    }

    private static void AddConstruction(List<Prim> prims, World world, Styles styles, Rect plane)
    {
        foreach (BoxState box in world.Boxes)
        {
            AddBox(prims, world, styles, box, plane);
        }

        foreach (SubdivisionState subdivision in world.Subdivisions)
        {
            AddSubdivision(prims, world, styles, subdivision, styles.ConstructionPen, plane);
        }

        foreach (EllipseState ellipse in world.Ellipses)
        {
            AddEllipse(prims, world, styles, ellipse, styles.ConstructionPen, plane);
        }
    }

    internal static void AddBox(List<Prim> prims, World world, Styles styles, BoxState box, Rect plane)
    {
        BuiltBox built = BoxGeometry.Build(box);
        foreach (int index in built.VisibleFaces)
        {
            prims.Add(Prim.Poly(null, styles.BoxFaceBrush, built.Faces[index], true, plane));
        }

        foreach ((Vec2 a, Vec2 b) in built.VisibleEdges)
        {
            prims.Add(Prim.Line(styles.BoxEdgePen, a, b, plane));
        }

        foreach ((Vec2 a, Vec2 b) in built.HiddenEdges)
        {
            prims.Add(Prim.Line(styles.BoxHiddenPen, a, b, plane));
        }
    }

    internal static void AddSubdivision(List<Prim> prims, World world, Styles styles, SubdivisionState subdivision,
        IPen pen, Rect plane)
    {
        if (subdivision.Quad.Length != 4)
        {
            return;
        }

        if (!subdivision.Edge)
        {
            foreach ((Vec2 a, Vec2 b) in SubdivisionGeometry.BuildLines(subdivision.Quad,
                subdivision.N, subdivision.M))
            {
                prims.Add(Prim.Line(pen, a, b, plane));
            }

            return;
        }

        Vec2 near = subdivision.Quad[0];
        Vec2 far = subdivision.Quad[1];
        Vec2? vanishingPoint = subdivision.Quad[2].DistFrom(near) > 1e-9
            ? subdivision.Quad[2]
            : null;
        Vec2[] points = PerspectiveEdge.Divide(near, far, vanishingPoint, subdivision.N);
        Vec2 direction = (far - near).Normalized();
        Vec2 normal = new Vec2(-direction.Y, direction.X);
        double half = 8.0 / styles.Zoom;
        for (int i = 1; i < points.Length - 1; i++)
        {
            Vec2 point = points[i];
            prims.Add(Prim.Line(pen,
                point - normal * half,
                point + normal * half, plane));
        }
    }

    internal static void AddEllipse(List<Prim> prims, World world, Styles styles, EllipseState ellipse,
        IPen pen, Rect plane)
    {
        if (ellipse.Quad.Length != 4)
        {
            return;
        }

        Vec2[] points = EllipseGeometry.Sample(ellipse.Quad, ellipse.Cx, ellipse.Cy, ellipse.R);
        if (points.Length > 1)
        {
            prims.Add(Prim.Poly(pen, null, points, true, plane));
        }
    }

    private static void AddVanishingPoint(List<Prim> prims, World world, Styles styles, int index,
        VanishingPoint point, Rect plane)
    {
        double far = world.CrazyFar;
        var fanBounds = new Rect(point.Pos.X - far, point.Pos.Y - far, far * 2, far * 2);
        if (fanBounds.Intersects(plane))
        {
            int density = FanDensity(point);
            double step = 360.0 / (density * 4.0);
            double angle = 0;
            for (int i = 0; i < density * 2; i++)
            {
                Vec2 direction = Vec2.RotateAroundOrigin(angle, new Vec2(1, 0));
                Vec2 start = point.Pos - direction * far;
                Vec2 end = point.Pos + direction * far;
                prims.Add(Prim.Line(styles.VPLines[index], start, end, plane));
                angle += step;
            }
        }

        double halfWidth = world.PicturePlaneWidthHalf;
        double halfHeight = world.PicturePlaneHeightHalf;
        if (point.Pos.X < -halfWidth || point.Pos.X > halfWidth ||
            point.Pos.Y < -halfHeight || point.Pos.Y > halfHeight)
        {
            Vec2? cornerA = null;
            Vec2? cornerB = null;

            if (point.Pos.X < -halfWidth && point.Pos.Y < -halfHeight)
            {
                cornerA = new Vec2(-halfWidth, halfHeight);
                cornerB = new Vec2(halfWidth, -halfHeight);
            }
            else if (point.Pos.X > -halfWidth && point.Pos.X < halfWidth && point.Pos.Y < -halfHeight)
            {
                cornerA = new Vec2(-halfWidth, -halfHeight);
                cornerB = new Vec2(halfWidth, -halfHeight);
            }
            else if (point.Pos.X > halfWidth && point.Pos.Y < -halfHeight)
            {
                cornerA = new Vec2(-halfWidth, -halfHeight);
                cornerB = new Vec2(halfWidth, halfHeight);
            }
            else if (point.Pos.X < -halfWidth && point.Pos.Y > -halfHeight && point.Pos.Y < halfHeight)
            {
                cornerA = new Vec2(-halfWidth, -halfHeight);
                cornerB = new Vec2(-halfWidth, halfHeight);
            }
            else if (point.Pos.X > halfWidth && point.Pos.Y > -halfHeight && point.Pos.Y < halfHeight)
            {
                cornerA = new Vec2(halfWidth, -halfHeight);
                cornerB = new Vec2(halfWidth, halfHeight);
            }
            else if (point.Pos.X < -halfWidth && point.Pos.Y > halfHeight)
            {
                cornerA = new Vec2(-halfWidth, -halfHeight);
                cornerB = new Vec2(halfWidth, halfHeight);
            }
            else if (point.Pos.X > -halfWidth && point.Pos.X < halfWidth && point.Pos.Y > halfHeight)
            {
                cornerA = new Vec2(-halfWidth, halfHeight);
                cornerB = new Vec2(halfWidth, halfHeight);
            }
            else if (point.Pos.X > halfWidth && point.Pos.Y > halfHeight)
            {
                cornerA = new Vec2(halfWidth, -halfHeight);
                cornerB = new Vec2(-halfWidth, halfHeight);
            }

            if (cornerA is { } a && cornerB is { } b)
            {
                IPen pen = styles.VPOutline[world.SelectionIdx == index ? 1 : 0];
                prims.Add(Prim.Line(pen, point.Pos, a, null, screenOnly: true));
                prims.Add(Prim.Line(pen, point.Pos, b, null, screenOnly: true));
            }
        }

        int selected = world.SelectionIdx == index ? 1 : 0;
        double radius = 16.0 / styles.Zoom;
        prims.Add(Prim.Circle(styles.VPFill[selected], styles.VPOutline[selected],
            point.Pos, radius, screenOnly: true));

        prims.Add(Prim.Label($"{index + 1}", 16.0 / styles.Zoom, styles.HudText, point.Pos,
            serif: true, screenOnly: true));
    }

    private static void AddTraceLine(List<Prim> prims, World world, Styles styles, TraceLine traceLine)
    {
        Vec2 direction = (traceLine.End - traceLine.Start).Normalized();
        Vec2 start = traceLine.Start - direction * world.CrazyFar;
        Vec2 end = traceLine.End + direction * world.CrazyFar;
        prims.Add(Prim.Line(styles.TracePen, start, end, null));
    }
}
