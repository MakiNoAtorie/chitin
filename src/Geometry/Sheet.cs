namespace Chitin;

// Level 0 is a minor tick, 1 a half-major tick, 2 a major tick with a label.
public readonly record struct RulerTick(double Pos, int Level, int Value);

// Where a vanishing point's index tab sits on one ruler. Pinned is -1 or 1 when the point is past
// that end of the viewport; Past is then its distance beyond the edge in document units.
public readonly record struct TabPlace(double At, int Pinned, double Past);

// Pure geometry of the sheet chrome: rulers, index tabs, the camera anchor on resize.
public static class Sheet
{
    public const double RulerSize = 20;
    public const double TabWidth = 20;
    private static readonly int[] Steps = { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000 };

    // Screen position of document unit u on an axis: (u + offset) * zoom, where
    // offset = camera - half the plane, because world 0 is the plane centre.
    public static List<RulerTick> Ticks(double zoom, double offset, double length)
    {
        int minor = Steps.FirstOrDefault(step => step * zoom >= 7, Steps[^1]);
        int major = minor * (minor * 5 * zoom >= 48 ? 5 : 10);
        var ticks = new List<RulerTick>();
        long first = (long)Math.Floor(-offset / minor) * minor;
        double last = length / zoom - offset;
        for (long u = first; u <= last; u += minor)
        {
            int level = u % major == 0 ? 2 : u % (major / 2) == 0 ? 1 : 0;
            ticks.Add(new RulerTick((u + offset) * zoom, level, (int)u));
        }

        return ticks;
    }

    public static TabPlace PlaceTab(double screen, double length, double zoom)
    {
        double half = TabWidth / 2;
        if (screen < 0)
        {
            return new TabPlace(half + 1, -1, -screen / zoom);
        }

        if (screen > length)
        {
            return new TabPlace(length - half - 1, 1, (screen - length) / zoom);
        }

        return new TabPlace(Math.Clamp(screen, half, Math.Max(half, length - half)), 0, 0);
    }

    // A point's tabs on the top ruler (X) and the left ruler (Y), from its viewport position.
    public static (TabPlace X, TabPlace Y) PlaceTabs(Vec2 screen, double width, double height, double zoom)
    {
        return (PlaceTab(screen.X, width, zoom), PlaceTab(screen.Y, height, zoom));
    }

    // Tabs on one ruler in screen order. Each tab takes `before` and `after` its centre (a pinned
    // tab adds room for its arrow and distance on the viewport side). A tab that would overlap the
    // previous one steps along; then tabs step back so the last one stays inside the ruler.
    public static double[] Spread(double[] at, double[] before, double[] after, double length)
    {
        int[] order = Enumerable.Range(0, at.Length).OrderBy(i => at[i]).ToArray();
        var result = new double[at.Length];
        double end = double.NegativeInfinity;
        foreach (int i in order)
        {
            result[i] = Math.Max(at[i], end + 1 + before[i]);
            end = result[i] + after[i];
        }

        double start = double.PositiveInfinity;
        foreach (int i in order.Reverse())
        {
            result[i] = Math.Min(result[i], Math.Min(start - 1 - after[i], length - after[i]));
            start = result[i] - before[i];
        }

        return result;
    }

    // On resize, shift the camera by half the change so the world point at the centre stays put.
    public static Vec2 KeepCentre(Vec2 camera, double zoom, double oldWidth, double oldHeight,
        double newWidth, double newHeight)
    {
        return new Vec2(camera.X + (newWidth - oldWidth) / 2 / zoom,
            camera.Y + (newHeight - oldHeight) / 2 / zoom);
    }
}
