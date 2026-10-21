namespace Chitin;

public readonly record struct Vec2(double X = 0, double Y = 0)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 v, double scale) => new(v.X * scale, v.Y * scale);

    public static Vec2 operator *(double scale, Vec2 v) => new(scale * v.X, scale * v.Y);

    public double Length()
    {
        return Math.Sqrt(X * X + Y * Y);
    }

    public Vec2 Normalized()
    {
        double size = Length();
        return size == 0 ? this : new Vec2(X / size, Y / size);
    }

    public double DistFrom(Vec2 v)
    {
        return (this - v).Length();
    }

    public static double Dot(Vec2 a, Vec2 b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    public static Vec2 RotateAroundOrigin(double angle, Vec2 vtx)
    {
        double radians = angle * Math.PI / 180.0;
        double sin = Math.Sin(radians);
        double cos = Math.Cos(radians);
        return new Vec2(vtx.X * cos - vtx.Y * sin, vtx.X * sin + vtx.Y * cos);
    }
}
