namespace Chitin;

public readonly record struct ConeOfVision(
    Vec2 Principal,
    Vec2 Station,
    Vec2 V0,
    Vec2 V1,
    double Distance,
    double FovDegrees,
    double Radius)
{
    public const double HalfAngleDegrees = 30;

    public static bool TryBuild(Vec2? v0, Vec2? v1, double pictureWidth, out ConeOfVision cone)
    {
        cone = default;
        if (v0 is not { } a || v1 is not { } b || !double.IsFinite(pictureWidth) || pictureWidth <= 0)
        {
            return false;
        }

        var delta = b - a;
        double length = delta.Length();
        if (!double.IsFinite(length) || length < 1e-9)
        {
            return false;
        }

        var direction = new Vec2(delta.X / length, delta.Y / length);
        double x0 = Vec2.Dot(a, direction);
        double x1 = Vec2.Dot(b, direction);
        if (x0 * x1 >= 0)
        {
            return false;
        }

        double distance = Math.Sqrt(Math.Abs(x0) * Math.Abs(x1));
        if (!double.IsFinite(distance) || distance <= 0)
        {
            return false;
        }

        double fov = 2 * Math.Atan((pictureWidth / 2) / distance) * 180 / Math.PI;
        double radius = distance * Math.Tan(HalfAngleDegrees * Math.PI / 180);
        if (!double.IsFinite(fov) || !double.IsFinite(radius))
        {
            return false;
        }

        cone = new ConeOfVision(new Vec2(), new Vec2(0, distance), a, b, distance, fov, radius);
        return true;
    }
}
