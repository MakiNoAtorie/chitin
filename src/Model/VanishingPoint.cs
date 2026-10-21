namespace Chitin;

public class VanishingPoint
{
    // 4 × the automatic cap: the HUD shows Density / 4, so a set value also tops out at 96.
    public const int MaxDensity = 4 * GuideBuilder.MaxAutomaticDensity;

    public Vec2 Pos;
    public int Density;

    public VanishingPoint(Vec2 pos, int density)
    {
        Pos = pos;
        Density = ClampDensity(density);
    }

    public static int ClampDensity(int density)
    {
        return Math.Clamp(density, 0, MaxDensity);
    }
}
