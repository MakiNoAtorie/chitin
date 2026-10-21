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

    public static bool GetLineIntersection(Vec2 start1, Vec2 end1, Vec2 start2, Vec2 end2,
        double crazyFar, out Vec2 intersectionPoint)
    {
        intersectionPoint = new Vec2();

        Vec2 direction = (end1 - start1).Normalized();
        Vec2 s1 = start1 - direction * crazyFar;
        Vec2 e1 = end1 + direction * crazyFar;

        direction = (end2 - start2).Normalized();
        Vec2 s2 = start2 - direction * crazyFar;
        Vec2 e2 = end2 + direction * crazyFar;

        double num = (e2.X - s2.X) * (s1.Y - s2.Y) - (e2.Y - s2.Y) * (s1.X - s2.X);
        double num2 = (e1.X - s1.X) * (s1.Y - s2.Y) - (e1.Y - s1.Y) * (s1.X - s2.X);
        double num3 = (e2.Y - s2.Y) * (e1.X - s1.X) - (e2.X - s2.X) * (e1.Y - s1.Y);

        if (Math.Abs(num3) <= 1E-05)
        {
            // Deliberate deviation from the original: collinear lines do not intersect.
            return false;
        }

        num /= num3;
        num2 /= num3;
        if (num >= 0 && num <= 1 && num2 >= 0 && num2 <= 1)
        {
            intersectionPoint = s1 + num * (e1 - s1);
            return true;
        }

        return false;
    }
}
