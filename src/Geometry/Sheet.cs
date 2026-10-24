namespace Chitin;

// Level 0 is a minor tick, 1 a half-major tick, 2 a major tick with a label.
public readonly record struct RulerTick(double Pos, int Level, int Value);

// Pure geometry of the sheet chrome: ruler ticks, the camera anchor on resize.
public static class Sheet
{
    public const double RulerSize = 20;
    private static readonly int[] Steps = { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000 };

    // Screen position of document unit u on an axis: (u + offset) * zoom, where
    // offset = camera - half the plane, because world 0 is the plane centre.
    public static List<RulerTick> Ticks(double zoom, double offset, double length)
    {
        int minor = Steps.FirstOrDefault(step => step * zoom >= 7, Steps[^1]);
        int major = minor * (minor * 5 * zoom >= 48 ? 5 : 10);
        // An odd major (5, 25, 125) has no whole half: no half-major ticks.
        int half = major % 2 == 0 ? major / 2 : 0;
        var ticks = new List<RulerTick>();
        long first = (long)Math.Floor(-offset / minor) * minor;
        double last = length / zoom - offset;
        for (long u = first; u <= last; u += minor)
        {
            int level = u % major == 0 ? 2 : half > 0 && u % half == 0 ? 1 : 0;
            ticks.Add(new RulerTick((u + offset) * zoom, level, (int)u));
        }

        return ticks;
    }

    // On resize, shift the camera by half the change so the world point at the centre stays put.
    public static Vec2 KeepCentre(Vec2 camera, double zoom, double oldWidth, double oldHeight,
        double newWidth, double newHeight)
    {
        return new Vec2(camera.X + (newWidth - oldWidth) / 2 / zoom,
            camera.Y + (newHeight - oldHeight) / 2 / zoom);
    }
}
