namespace Chitin;

public sealed class BuiltBox
{
    public Vec2[] Vertices { get; }
    public Vec2[][] Faces { get; }
    public (Vec2 A, Vec2 B)[] VisibleEdges { get; }
    public (Vec2 A, Vec2 B)[] HiddenEdges { get; }
    public int[] VisibleFaces { get; }

    internal BuiltBox(Vec2[] vertices, Vec2[][] faces, int[] visibleFaces,
        (Vec2 A, Vec2 B)[] visibleEdges, (Vec2 A, Vec2 B)[] hiddenEdges)
    {
        Vertices = vertices;
        Faces = faces;
        VisibleFaces = visibleFaces;
        VisibleEdges = visibleEdges;
        HiddenEdges = hiddenEdges;
    }
}

public static class BoxGeometry
{
    public static Vec2 FourthCorner(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 axisDirection1, Vec2 axisDirection2)
    {
        if (Geom.IntersectLines(p1, axisDirection1, p2, axisDirection2, out Vec2 corner))
        {
            return corner;
        }

        return p1 + p2 - p0;
    }

    public static BuiltBox Build(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 h, Vec2 axisDirection1,
        Vec2 axisDirection2)
    {
        Vec2 p3 = FourthCorner(p0, p1, p2, axisDirection1, axisDirection2);
        return Build(new BoxState { P0 = p0, P1 = p1, P2 = p2, P3 = p3, H = h });
    }

    public static BuiltBox Build(BoxState box)
    {
        Vec2 p0 = box.P0;
        Vec2 p1 = box.P1;
        Vec2 p2 = box.P2;
        Vec2 p3 = box.P3;
        Vec2 h = box.H;
        Vec2 q0 = p0 + h;
        Vec2 q1 = p1 + h;
        Vec2 q2 = p2 + h;
        Vec2 q3 = p3 + h;

        Vec2[] vertices = { p0, p1, p3, p2, q0, q1, q3, q2 };
        Vec2[][] faces =
        {
            new[] { p0, p1, p3, p2 },
            new[] { q0, q1, q3, q2 },
            new[] { p0, p1, q1, q0 },
            new[] { p0, p2, q2, q0 },
            new[] { p1, p3, q3, q1 },
            new[] { p2, p3, q3, q2 }
        };

        // Faces wound counter-clockwise seen from outside (same index order as faces). A face is in
        // front when its projection keeps the winding of the two side faces at p0: the first click
        // is the near corner. So a box above the horizon shows its underside, not its top.
        Vec2[][] outward =
        {
            new[] { p0, p2, p3, p1 },
            new[] { q0, q1, q3, q2 },
            new[] { p0, p1, q1, q0 },
            new[] { p0, q0, q2, p2 },
            new[] { p1, p3, q3, q1 },
            new[] { p2, q2, q3, p3 }
        };
        double frontSign = Math.Sign(Area(outward[2]) + Area(outward[3]));
        bool[] front = outward.Select(face => Area(face) * frontSign > 0).ToArray();
        int[] visibleFaces = Enumerable.Range(0, 6).Where(index => front[index]).ToArray();

        // An edge is visible when either face it joins is in front.
        (Vec2 A, Vec2 B, int F, int G)[] edges =
        {
            (p0, p1, 0, 2), (p0, p2, 0, 3),
            (q0, q1, 1, 2), (q1, q3, 1, 4), (q3, q2, 1, 5), (q2, q0, 1, 3),
            (p0, q0, 2, 3), (p1, q1, 2, 4), (p2, q2, 3, 5),
            (p1, p3, 0, 4), (p2, p3, 0, 5), (p3, q3, 4, 5)
        };
        (Vec2 A, Vec2 B)[] visible = edges.Where(edge => front[edge.F] || front[edge.G])
            .Select(edge => (edge.A, edge.B)).ToArray();
        (Vec2 A, Vec2 B)[] hidden = edges.Where(edge => !front[edge.F] && !front[edge.G])
            .Select(edge => (edge.A, edge.B)).ToArray();

        return new BuiltBox(vertices, faces, visibleFaces, visible, hidden);
    }

    public static Vec2[]? HitFace(IReadOnlyList<BoxState> boxes, Vec2 point)
    {
        for (int boxIndex = boxes.Count - 1; boxIndex >= 0; boxIndex--)
        {
            Vec2[]? best = null;
            double bestArea = double.MaxValue;
            BuiltBox built = Build(boxes[boxIndex]);
            foreach (Vec2[] face in built.Faces)
            {
                if (!ContainsQuad(face, point))
                {
                    continue;
                }

                double area = Math.Abs(Area(face));
                if (area < bestArea)
                {
                    bestArea = area;
                    best = face;
                }
            }

            if (best != null)
            {
                return best;
            }
        }

        return null;
    }

    public static bool ContainsQuad(Vec2[] quad, Vec2 point)
    {
        int sign = 0;
        for (int i = 0; i < quad.Length; i++)
        {
            Vec2 a = quad[i];
            Vec2 b = quad[(i + 1) % quad.Length];
            double cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (Math.Abs(cross) < 1e-9)
            {
                continue;
            }

            int next = cross > 0 ? 1 : -1;
            if (sign == 0)
            {
                sign = next;
            }
            else if (next != sign)
            {
                return false;
            }
        }

        return true;
    }

    // Every corner turns the same way: rejects a bow-tie (corners clicked out of order) and a dent.
    public static bool IsConvex(Vec2[] polygon)
    {
        int sign = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            Vec2 c = polygon[(i + 2) % polygon.Length];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            int turn = Math.Sign(cross);
            if (turn == 0 || (sign != 0 && turn != sign))
            {
                return false;
            }

            sign = turn;
        }

        return true;
    }

    public static double Area(Vec2[] polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum * 0.5;
    }
}
