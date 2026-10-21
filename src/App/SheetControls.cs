using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

// Section hatching at 45 degrees: marks an empty or unavailable cell.
public class Hatch : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Hatch, IBrush?>(nameof(Stroke));

    static Hatch()
    {
        AffectsRender<Hatch>(StrokeProperty);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Stroke ?? Brushes.Gray, 1);
        double w = Bounds.Width;
        double h = Bounds.Height;
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            for (double x = -h; x < w; x += 6)
            {
                context.DrawLine(pen, new Point(x, h), new Point(x + h, 0));
            }
        }
    }
}

// A detail callout: a split circle with the section letter over the sheet number.
public class Bubble : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Bubble, IBrush?>(nameof(Stroke));

    static Bubble()
    {
        AffectsRender<Bubble>(StrokeProperty);
    }

    public Bubble()
    {
        Width = 26;
        Height = 26;
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public string Letter { get; set; } = "A";
    public string SheetNumber { get; set; } = "01";

    public override void Render(DrawingContext context)
    {
        IBrush brush = Stroke ?? Brushes.Black;
        context.DrawEllipse(null, new Pen(brush, 1.5), new Point(13, 13), 12, 12);
        context.DrawLine(new Pen(brush, 1), new Point(1, 13), new Point(25, 13));
        FormattedText letter = Chrome.Text(Letter, 9, brush, bold: true);
        FormattedText sheet = Chrome.Text(SheetNumber, 7, brush);
        context.DrawText(letter, new Point(13 - letter.Width / 2, 12 - letter.Height));
        context.DrawText(sheet, new Point(13 - sheet.Width / 2, 14));
    }
}

// A ruler in document units along one edge of the viewport, with the cursor hairline, the
// horizon mark and one index tab per vanishing point. Clicking a tab selects that point.
public class Ruler : Control
{
    public ViewportControl? Viewport { get; set; }
    public bool Vertical { get; set; }

    private readonly List<(double A, double B, int Index)> _tabs = new();

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

        DrawTabs(context, world, length, ink, paper, inkPen, P);

        // The heavy rule along the viewport edge.
        context.DrawLine(new Pen(ink, 2), P(0, size - 1), P(length, size - 1));
    }

    private void DrawTabs(DrawingContext context, World world, double length, IBrush ink, IBrush paper,
        IPen inkPen, Func<double, double, Point> P)
    {
        _tabs.Clear();
        var indices = new List<int>();
        var places = new List<TabPlace>();
        for (int i = 0; i < world.VanishingPoints.Length; i++)
        {
            if (world.VanishingPoints[i] is { } point)
            {
                Vec2 screen = (point.Pos + world.Camera) * world.Zoom;
                (TabPlace x, TabPlace y) = Sheet.PlaceTabs(screen, Bounds.Width, Bounds.Height, world.Zoom);
                indices.Add(i);
                places.Add(Vertical ? y : x);
            }
        }

        // Room on the viewport side of a pinned tab: arrow, and on the top ruler its distance.
        double room = Vertical ? 12 : 44;
        double half = Sheet.TabWidth / 2;
        double[] at = Sheet.Spread(places.Select(place => place.At).ToArray(),
            places.Select(place => place.Pinned > 0 ? half + room : half).ToArray(),
            places.Select(place => place.Pinned < 0 ? half + room : half).ToArray(),
            length);
        const double size = Sheet.RulerSize;
        for (int n = 0; n < indices.Count; n++)
        {
            int i = indices[n];
            bool selected = world.SelectionIdx == i;
            double a = at[n] - Sheet.TabWidth / 2;
            double b = at[n] + Sheet.TabWidth / 2;
            _tabs.Add((a, b, i));
            DrawPoly(context, selected ? ink : paper, inkPen,
                P(a, size), P(a, 5), P(a + 4, 1), P(b - 4, 1), P(b, 5), P(b, size));
            Point bandA = P(a, size - 4);
            Point bandB = P(b, size);
            context.FillRectangle(world.Styles.VPLines[i].Brush!, new Rect(bandA, bandB));
            FormattedText figure = Chrome.Text((i + 1).ToString(CultureInfo.InvariantCulture), 11,
                selected ? paper : ink, bold: true);
            Point centre = P(at[n], (size - 4) / 2 + 1);
            context.DrawText(figure, new Point(centre.X - figure.Width / 2, centre.Y - figure.Height / 2));

            TabPlace place = places[n];
            if (place.Pinned != 0)
            {
                double e = place.Pinned < 0 ? b + 3 : a - 3;
                double d = place.Pinned < 0 ? 6 : -6;
                DrawPoly(context, ink, null, P(e + d, 5), P(e, 10), P(e + d, 15));
                if (!Vertical)
                {
                    FormattedText past = Chrome.Text(Math.Round(place.Past).ToString(CultureInfo.InvariantCulture),
                        9, ink, bold: true);
                    double x = place.Pinned < 0 ? e + 9 : e - 9 - past.Width;
                    context.FillRectangle(paper, new Rect(x - 2, 1, past.Width + 4, size - 6));
                    context.DrawText(past, new Point(x, 4));
                }
            }
        }
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point position = e.GetPosition(this);
        double along = Vertical ? position.Y : position.X;
        foreach ((double a, double b, int index) in _tabs)
        {
            if (along >= a && along <= b && Viewport is { } viewport)
            {
                _ = viewport.RunCommandAsync(viewport.Commands.Single(command => command.Name == "Select vanishing point"),
                    index);
                e.Handled = true;
                return;
            }
        }
    }
}
