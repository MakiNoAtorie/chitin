namespace Chitin;

public static class Geom
{
    public static bool IntersectLines(Vec2 point1, Vec2 direction1, Vec2 point2, Vec2 direction2,
        out Vec2 intersection)
    {
        intersection = new Vec2();
        double cross = direction1.X * direction2.Y - direction1.Y * direction2.X;
        if (Math.Abs(cross) < 1e-9)
        {
            return false;
        }

        double t = ((point2.X - point1.X) * direction2.Y -
            (point2.Y - point1.Y) * direction2.X) / cross;
        intersection = point1 + direction1 * t;
        return double.IsFinite(intersection.X) && double.IsFinite(intersection.Y);
    }

    public static double DistanceToSegment(Vec2 point, Vec2 start, Vec2 end)
    {
        Vec2 edge = end - start;
        double lengthSquared = Vec2.Dot(edge, edge);
        if (lengthSquared == 0)
        {
            return point.DistFrom(start);
        }

        double t = Math.Clamp(Vec2.Dot(point - start, edge) /
            lengthSquared, 0, 1);
        Vec2 closest = start + edge * t;
        return point.DistFrom(closest);
    }
}
