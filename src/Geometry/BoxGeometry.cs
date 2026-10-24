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

    // A box that follows its vanishing points is rebuilt from them; any other box is drawn as stored.
    public static BuiltBox Build(BoxState box, VanishingPoint?[]? points = null)
    {
        (Vec2[] bottom, Vec2[] top) = Corners(box, points);
        return Build(bottom, top);
    }

    // Base p0 p1 p3 p2 and top q0 q1 q3 q2. A following box keeps p0 and each edge length; the
    // edges aim at the live points, and with a height point the verticals converge on it. An edge
    // whose point is gone keeps its stored direction.
    public static (Vec2[] Bottom, Vec2[] Top) Corners(BoxState box, VanishingPoint?[]? points)
    {
        Vec2 p0 = box.P0;
        if (box.Follow is not { Length: 3 } follow || box.Lengths is not { Length: 3 } lengths || points == null)
        {
            Vec2 h = box.H;
            return (new[] { p0, box.P1, box.P3, box.P2 },
                new[] { p0 + h, box.P1 + h, box.P3 + h, box.P2 + h });
        }

        Vec2? Point(int slot) => follow[slot] is >= 0 and < 9 && points[follow[slot]] is { } point &&
            point.Pos.DistFrom(p0) > 1e-9 ? point.Pos : null;
        static Vec2 Toward(Vec2? point, Vec2 from, Vec2 fallback) =>
            point is { } target && target.DistFrom(from) > 1e-9 ? (target - from).Normalized() : fallback;

        Vec2? a = Point(0);
        Vec2? b = Point(1);
        Vec2? c = Point(2);
        Vec2 alongA = (box.P1 - p0).Normalized();
        Vec2 alongB = (box.P2 - p0).Normalized();
        Vec2 p1 = a is { } av ? p0 + (av - p0).Normalized() * lengths[0] : box.P1;
        Vec2 p2 = b is { } bv ? p0 + (bv - p0).Normalized() * lengths[1] : box.P2;
        Vec2 p3 = FourthCorner(p0, p1, p2, Toward(b, p1, alongB), Toward(a, p2, alongA));
        if (c is not { } cv)
        {
            Vec2 h = box.H;
            return (new[] { p0, p1, p3, p2 }, new[] { p0 + h, p1 + h, p3 + h, p2 + h });
        }

        Vec2 q0 = p0 + (cv - p0).Normalized() * lengths[2];
        Vec2 lift = q0 - p0;
        Vec2 Cross(Vec2 from, Vec2 direction, Vec2 other, Vec2 otherDirection, Vec2 parallel) =>
            Geom.IntersectLines(from, direction, other, otherDirection, out Vec2 corner) ? corner : parallel;
        Vec2 q1 = Cross(p1, cv - p1, q0, Toward(a, q0, alongA), p1 + lift);
        Vec2 q2 = Cross(p2, cv - p2, q0, Toward(b, q0, alongB), p2 + lift);
        Vec2 q3 = Cross(q1, Toward(b, q1, alongB), q2, Toward(a, q2, alongA), p3 + lift);
        return (new[] { p0, p1, p3, p2 }, new[] { q0, q1, q3, q2 });
    }

    private static BuiltBox Build(Vec2[] bottom, Vec2[] top)
    {
        (Vec2 p0, Vec2 p1, Vec2 p3, Vec2 p2) = (bottom[0], bottom[1], bottom[2], bottom[3]);
        (Vec2 q0, Vec2 q1, Vec2 q3, Vec2 q2) = (top[0], top[1], top[2], top[3]);

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
    // The smallest front face under the point on the topmost box that has one: its corners, box and
    // face index. A back face projects inside the front ones, so it is never picked.
    public static (Vec2[] Quad, int Box, int Face)? HitFace(IReadOnlyList<BoxState> boxes, Vec2 point,
        VanishingPoint?[]? points = null)
    {
        for (int boxIndex = boxes.Count - 1; boxIndex >= 0; boxIndex--)
        {
            int best = -1;
            double bestArea = double.MaxValue;
            BuiltBox built = Build(boxes[boxIndex], points);
            foreach (int face in built.VisibleFaces)
            {
                if (!ContainsQuad(built.Faces[face], point))
                {
                    continue;
                }

                double area = Math.Abs(Area(built.Faces[face]));
                if (area < bestArea)
                {
                    bestArea = area;
                    best = face;
                }
            }

            if (best >= 0)
            {
                return (built.Faces[best], boxIndex, best);
            }
        }

        return null;
    }

    // The live corners of a face picked from a box, or null when the box is gone.
    public static Vec2[]? FaceQuad(World world, int box, int face)
    {
        return box >= 0 && box < world.Boxes.Count && face is >= 0 and < 6
            ? Build(world.Boxes[box], world.VanishingPoints).Faces[face]
            : null;
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
