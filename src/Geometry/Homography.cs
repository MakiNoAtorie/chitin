namespace Chitin;

public readonly struct Homography
{
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m13;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _m23;
    private readonly double _m31;
    private readonly double _m32;
    private readonly double _m33;

    public bool IsInvertible { get; }

    private Homography(double m11, double m12, double m13, double m21, double m22, double m23,
        double m31, double m32, double m33, bool invertible)
    {
        _m11 = m11;
        _m12 = m12;
        _m13 = m13;
        _m21 = m21;
        _m22 = m22;
        _m23 = m23;
        _m31 = m31;
        _m32 = m32;
        _m33 = m33;
        IsInvertible = invertible;
    }

    public static Homography FromUnitSquare(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double dx1 = b.X - c.X;
        double dx2 = d.X - c.X;
        double dx3 = a.X - b.X + c.X - d.X;
        double dy1 = b.Y - c.Y;
        double dy2 = d.Y - c.Y;
        double dy3 = a.Y - b.Y + c.Y - d.Y;

        double m11;
        double m12;
        double m13;
        double m21;
        double m22;
        double m23;
        double m31;
        double m32;
        double m33;

        if (Math.Abs(dx3) < 1e-12 && Math.Abs(dy3) < 1e-12)
        {
            m11 = b.X - a.X;
            m12 = d.X - a.X;
            m13 = a.X;
            m21 = b.Y - a.Y;
            m22 = d.Y - a.Y;
            m23 = a.Y;
            m31 = 0;
            m32 = 0;
            m33 = 1;
        }
        else
        {
            double denominator = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(denominator) < 1e-12)
            {
                return Invalid(a);
            }

            double g = (dx3 * dy2 - dx2 * dy3) / denominator;
            double h = (dx1 * dy3 - dx3 * dy1) / denominator;
            m11 = b.X - a.X + g * b.X;
            m12 = d.X - a.X + h * d.X;
            m13 = a.X;
            m21 = b.Y - a.Y + g * b.Y;
            m22 = d.Y - a.Y + h * d.Y;
            m23 = a.Y;
            m31 = g;
            m32 = h;
            m33 = 1;
        }

        double determinant = Determinant(m11, m12, m13, m21, m22, m23, m31, m32, m33);
        bool invertible = double.IsFinite(determinant) && Math.Abs(determinant) > 1e-12;
        return new Homography(m11, m12, m13, m21, m22, m23, m31, m32, m33, invertible);
    }

    public Vec2 Map(double u, double v)
    {
        double denominator = _m31 * u + _m32 * v + _m33;
        if (Math.Abs(denominator) < 1e-12)
        {
            return new Vec2(double.NaN, double.NaN);
        }

        return new Vec2(
            (_m11 * u + _m12 * v + _m13) / denominator,
            (_m21 * u + _m22 * v + _m23) / denominator);
    }

    public Vec2 Map(Vec2 point)
    {
        return Map(point.X, point.Y);
    }

    public Vec2? MapInverse(Vec2 point)
    {
        if (!IsInvertible)
        {
            return null;
        }

        double determinant = Determinant(_m11, _m12, _m13, _m21, _m22, _m23, _m31, _m32, _m33);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
        {
            return null;
        }

        double i11 = (_m22 * _m33 - _m23 * _m32) / determinant;
        double i12 = (_m13 * _m32 - _m12 * _m33) / determinant;
        double i13 = (_m12 * _m23 - _m13 * _m22) / determinant;
        double i21 = (_m23 * _m31 - _m21 * _m33) / determinant;
        double i22 = (_m11 * _m33 - _m13 * _m31) / determinant;
        double i23 = (_m13 * _m21 - _m11 * _m23) / determinant;
        double i31 = (_m21 * _m32 - _m22 * _m31) / determinant;
        double i32 = (_m12 * _m31 - _m11 * _m32) / determinant;
        double i33 = (_m11 * _m22 - _m12 * _m21) / determinant;

        double denominator = i31 * point.X + i32 * point.Y + i33;
        if (Math.Abs(denominator) < 1e-12)
        {
            return null;
        }

        return new Vec2(
            (i11 * point.X + i12 * point.Y + i13) / denominator,
            (i21 * point.X + i22 * point.Y + i23) / denominator);
    }

    private static Homography Invalid(Vec2 a)
    {
        return new Homography(1, 0, a.X, 0, 1, a.Y, 0, 0, 1, false);
    }

    private static double Determinant(double m11, double m12, double m13, double m21, double m22,
        double m23, double m31, double m32, double m33)
    {
        return m11 * (m22 * m33 - m23 * m32) -
               m12 * (m21 * m33 - m23 * m31) +
               m13 * (m21 * m32 - m22 * m31);
    }
}

public static class PerspectiveEdge
{
    public static Vec2[] Divide(Vec2 near, Vec2 far, Vec2? vanishingPoint, int n)
    {
        n = Math.Max(1, n);
        if (vanishingPoint is not { } vp || near.DistFrom(far) < 1e-9)
        {
            return Uniform(near, far, n);
        }

        double distanceNear = vp.DistFrom(near);
        double distanceFar = vp.DistFrom(far);
        if (distanceNear < 1e-9 || !double.IsFinite(distanceNear) ||
            !double.IsFinite(distanceFar) ||
            Math.Abs(distanceFar / distanceNear - 1) < 1e-9 ||
            Math.Abs(distanceFar - distanceNear) < 1e-9)
        {
            return Uniform(near, far, n);
        }

        double ratio = distanceFar / distanceNear;
        var points = new Vec2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double distance = distanceNear * n * ratio /
                (n * ratio + i * (1 - ratio));
            double t = (distance - distanceNear) / (distanceFar - distanceNear);
            if (!double.IsFinite(t))
            {
                t = (double)i / n;
            }

            points[i] = near + (far - near) * t;
        }

        return points;
    }

    private static Vec2[] Uniform(Vec2 near, Vec2 far, int n)
    {
        var points = new Vec2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            points[i] = near + (far - near) * t;
        }

        return points;
    }
}
