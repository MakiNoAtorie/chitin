namespace Chitin;

public static class SubdivisionGeometry
{
    public const int MaxCells = 64;

    public static (Vec2 A, Vec2 B)[] BuildLines(Vec2[] quad, int n, int m)
    {
        if (quad.Length != 4)
        {
            return Array.Empty<(Vec2 A, Vec2 B)>();
        }

        int columns = Math.Clamp(n, 1, MaxCells);
        int rows = Math.Clamp(m, 1, MaxCells);
        Homography homography = Homography.FromUnitSquare(quad[0], quad[1], quad[2], quad[3]);
        if (!homography.IsInvertible)
        {
            return Array.Empty<(Vec2 A, Vec2 B)>();
        }

        var lines = new List<(Vec2 A, Vec2 B)>(Math.Max(0, columns - 1) + Math.Max(0, rows - 1));
        for (int i = 1; i < columns; i++)
        {
            double u = (double)i / columns;
            AddLine(lines, homography.Map(u, 0), homography.Map(u, 1));
        }

        for (int j = 1; j < rows; j++)
        {
            double v = (double)j / rows;
            AddLine(lines, homography.Map(0, v), homography.Map(1, v));
        }

        return lines.ToArray();
    }

    private static void AddLine(List<(Vec2 A, Vec2 B)> lines, Vec2 a, Vec2 b)
    {
        if (double.IsFinite(a.X) && double.IsFinite(a.Y) &&
            double.IsFinite(b.X) && double.IsFinite(b.Y))
        {
            lines.Add((a, b));
        }
    }

    public static int CellCount(int n, int m)
    {
        return Math.Clamp(n, 1, MaxCells) * Math.Clamp(m, 1, MaxCells);
    }
}

public static class EllipseGeometry
{
    public const int SampleCount = 128;

    public static Vec2[] Sample(Vec2[] quad, double cx, double cy, double r)
    {
        if (quad.Length != 4 || !double.IsFinite(cx) || !double.IsFinite(cy) ||
            !double.IsFinite(r) || r <= 0)
        {
            return Array.Empty<Vec2>();
        }

        Homography homography = Homography.FromUnitSquare(quad[0], quad[1], quad[2], quad[3]);
        if (!homography.IsInvertible)
        {
            return Array.Empty<Vec2>();
        }

        var points = new Vec2[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            double angle = 2 * Math.PI * i / SampleCount;
            Vec2 point = homography.Map(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return Array.Empty<Vec2>();
            }

            points[i] = point;
        }

        return points;
    }
}
