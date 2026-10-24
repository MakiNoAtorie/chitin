using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Chitin;

// Chrome colours and type. Every colour comes from the active palette.
public static class Chrome
{
    // B612 Mono, bundled with its OFL licence in assets/fonts.
    public static readonly FontFamily Family = new("avares://Chitin/assets/fonts#B612 Mono");
    public static readonly Typeface Mono = new(Family);
    public static readonly Typeface MonoBold = new(Family, FontStyle.Normal, FontWeight.Bold);

    // Hairlines: ink at 28 % over paper.
    public static Color Hair(Palette palette)
    {
        return Mix(palette.Ink, palette.Paper, 0.28);
    }

    // Muted text (the Find placeholder): ink at 70 % over paper.
    public static Color Muted(Palette palette)
    {
        return Mix(palette.Ink, palette.Paper, 0.7);
    }

    // Every text colour on every background the chrome uses; CHECK chrome_contrast holds each to 4.5:1.
    public static (Color Text, Color Background)[] TextPairs(Palette palette)
    {
        return new[]
        {
            (palette.Ink, palette.Paper),
            (palette.Paper, palette.Ink),
            (palette.Ink, Hair(palette)),
            (Muted(palette), palette.Paper)
        };
    }

    // Fluent's own brushes for the menus and text fields, taken from the palette.
    public static Dictionary<string, Color> FluentColours(Palette palette)
    {
        Color ink = palette.Ink;
        Color paper = palette.Paper;
        Color hair = Hair(palette);
        return new Dictionary<string, Color>
        {
            ["MenuFlyoutPresenterBackground"] = paper,
            ["MenuFlyoutPresenterBorderBrush"] = ink,
            ["MenuFlyoutItemBackground"] = paper,
            ["MenuFlyoutItemForeground"] = ink,
            ["MenuFlyoutItemBackgroundPointerOver"] = ink,
            ["MenuFlyoutItemForegroundPointerOver"] = paper,
            ["MenuFlyoutItemBackgroundPressed"] = ink,
            ["MenuFlyoutItemForegroundPressed"] = paper,
            ["MenuFlyoutItemForegroundDisabled"] = hair,
            ["MenuFlyoutItemKeyboardAcceleratorTextForeground"] = ink,
            ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver"] = paper,
            ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed"] = paper,
            ["MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled"] = hair,
            ["MenuFlyoutSubItemChevron"] = ink,
            ["MenuFlyoutSubItemChevronPointerOver"] = paper,
            ["MenuFlyoutSubItemChevronSubMenuOpened"] = paper,
            ["MenuFlyoutSeparatorBackground"] = hair,
            ["TextControlForeground"] = ink,
            ["TextControlForegroundPointerOver"] = ink,
            ["TextControlForegroundFocused"] = ink,
            ["TextControlBackground"] = paper,
            ["TextControlBackgroundPointerOver"] = paper,
            ["TextControlBackgroundFocused"] = paper,
            ["TextControlBorderBrush"] = ink,
            ["TextControlBorderBrushPointerOver"] = ink,
            ["TextControlBorderBrushFocused"] = palette.Prompt,
            ["TextControlPlaceholderForeground"] = Muted(palette),
            ["TextControlPlaceholderForegroundPointerOver"] = Muted(palette),
            ["TextControlPlaceholderForegroundFocused"] = Muted(palette),
            ["TextControlSelectionHighlightColor"] = ink,
            ["ScrollBarThumbFill"] = hair,
            ["ScrollBarThumbFillPointerOver"] = ink
        };
    }

    // WCAG 2 relative luminance and contrast ratio.
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double Contrast(Color left, Color right)
    {
        double a = Luminance(left);
        double b = Luminance(right);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static Color Mix(Color a, Color b, double amountOfA)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x * amountOfA + y * (1 - amountOfA));
        return Color.FromRgb(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }

    public static FormattedText Text(string text, double size, IBrush brush, bool bold = false)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bold ? MonoBold : Mono, size, brush);
    }
}

// A ruler in document units along one edge of the viewport, with the cursor hairline and the
// horizon mark.
public class Ruler : Control
{
    public ViewportControl? Viewport { get; set; }
    public bool Vertical { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Viewport is not { } viewport)
        {
            return;
        }

        World world = viewport.World;
        Palette palette = world.ActivePalette;
        IBrush ink = new SolidColorBrush(palette.Ink);
        IBrush paper = new SolidColorBrush(palette.Paper);
        var inkPen = new Pen(ink, 1);
        double length = Vertical ? Bounds.Height : Bounds.Width;
        const double size = Sheet.RulerSize;

        // Along the ruler, then across it: 0 is the outer edge, size the viewport edge.
        Point P(double along, double across) => Vertical ? new Point(across, along) : new Point(along, across);

        context.FillRectangle(paper, new Rect(Bounds.Size));
        double camera = Vertical ? world.Camera.Y : world.Camera.X;
        double half = Vertical ? world.PicturePlaneHeightHalf : world.PicturePlaneWidthHalf;
        foreach (RulerTick tick in Sheet.Ticks(world.Zoom, camera - half, length))
        {
            double at = Math.Round(tick.Pos) + 0.5;
            double height = tick.Level == 2 ? 9 : tick.Level == 1 ? 6 : 3;
            context.DrawLine(inkPen, P(at, size - height), P(at, size));
            if (tick.Level == 2)
            {
                FormattedText label = Chrome.Text(tick.Value.ToString(CultureInfo.InvariantCulture), 8.5, ink);
                if (Vertical)
                {
                    using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) *
                        Matrix.CreateTranslation(1, at - 3)))
                    {
                        context.DrawText(label, new Point(0, 0));
                    }
                }
                else
                {
                    context.DrawText(label, new Point(at + 3, 1));
                }
            }
        }

        VanishingPoint?[] points = world.VanishingPoints;
        if (Vertical && points[0] is { } first && points[1] is { } second && first.Pos.Y == second.Pos.Y)
        {
            double y = (first.Pos.Y + world.Camera.Y) * world.Zoom;
            DrawPoly(context, ink, null, P(y, size), P(y - 5, size - 8), P(y + 5, size - 8));
        }

        if (viewport.PointerPosition is { } pointer)
        {
            double at = Math.Round(Vertical ? pointer.Y : pointer.X) + 0.5;
            context.DrawLine(new Pen(new SolidColorBrush(palette.Prompt), 1.5), P(at, 0), P(at, size));
        }

        // The heavy rule along the viewport edge.
        context.DrawLine(new Pen(ink, 2), P(0, size - 1), P(length, size - 1));
    }

    private static void DrawPoly(DrawingContext context, IBrush? fill, IPen? pen, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(points[0], fill != null);
            foreach (Point point in points.Skip(1))
            {
                stream.LineTo(point);
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }
}
