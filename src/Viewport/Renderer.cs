using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Chitin;

public enum RenderMode
{
    Interactive,
    OpaqueExport,
    TransparentExport
}

public static class Renderer
{
    private const double HelpTextSize = 11;
    private const double HelpTextSmallSize = 9;

    private static readonly Typeface Sans = new(FontFamily.Default);
    private static readonly Typeface Serif = new("serif");
    private static readonly Dictionary<(string Text, double Size, IBrush Brush, Typeface Face), FormattedText>
        TextCache = new();

    internal static void ClearTextCache()
    {
        TextCache.Clear();
    }

    private enum HAlign
    {
        Near,
        Center,
        Far
    }

    private enum VAlign
    {
        Near,
        Center
    }

    public static void Draw(DrawingContext context, World world, Size viewport, RenderMode mode)
    {
        Draw(context, world, viewport, mode, world.Styles, world.Camera);
    }

    // Styles carry the zoom. With styles other than the world's own (an export), the guides are
    // built for this call and the cached ones are left alone.
    public static void Draw(DrawingContext context, World world, Size viewport, RenderMode mode,
        Styles styles, Vec2 camera)
    {
        bool interactive = mode == RenderMode.Interactive;
        if (interactive)
        {
            context.FillRectangle(styles.ViewportClear, new Rect(0, 0, viewport.Width, viewport.Height));
        }

        Prim[] prims = ReferenceEquals(styles, world.Styles) ? world.Prims : GuideBuilder.Build(world, styles);
        using (context.PushTransform(Matrix.CreateTranslation(camera.X, camera.Y) * Matrix.CreateScale(styles.Zoom, styles.Zoom)))
        {
            DrawUnder(context, world, styles, mode);
            DrawPrims(context, prims, mode);

            if (interactive)
            {
                world.Overlay?.DrawOver(context);
            }
        }

        if (interactive)
        {
            DrawCentringMarks(context, styles, viewport);

            if (world.ShowHelp)
            {
                DrawHelp(context, world, styles, viewport);
            }
        }
    }

    private static void DrawUnder(DrawingContext context, World world, Styles styles, RenderMode mode)
    {
        Rect plane = GuideBuilder.PlaneRect(world);
        if (mode == RenderMode.TransparentExport)
        {
            return;
        }

        if (mode == RenderMode.OpaqueExport)
        {
            context.FillRectangle(styles.PlaneBackground, plane);
            return;
        }

        context.FillRectangle(styles.PlaneBorder, new Rect(
            -world.PicturePlaneWidthHalf - World.PicturePlaneBorderSz,
            -world.PicturePlaneHeightHalf - World.PicturePlaneBorderSz,
            world.PicturePlaneWidth + World.PicturePlaneBorderSz * 2,
            world.PicturePlaneHeight + World.PicturePlaneBorderSz * 2));
        context.FillRectangle(styles.PlaneBackground, plane);

        if (world.BackgroundImg is { } image && !world.Reference.Hidden)
        {
            ReferenceSettings reference = world.Reference;
            double scaleX = reference.Scale * (reference.FlipH ? -1 : 1);
            double scaleY = reference.Scale * (reference.FlipV ? -1 : 1);
            using (context.PushClip(plane))
            using (context.PushOpacity(reference.Opacity))
            using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY) *
                Matrix.CreateTranslation(reference.Offset.X, reference.Offset.Y)))
            {
                context.DrawImage(image, plane);
            }
        }
    }

    internal static void DrawPrims(DrawingContext context, IReadOnlyList<Prim> prims, RenderMode mode)
    {
        Rect? clip = null;
        DrawingContext.PushedState? clipState = null;

        foreach (Prim prim in prims)
        {
            if (mode != RenderMode.Interactive && prim.ScreenOnly)
            {
                continue;
            }

            if (prim.Clip != clip)
            {
                clipState?.Dispose();
                clip = prim.Clip;
                clipState = clip is { } rect ? context.PushClip(rect) : null;
            }

            switch (prim.Kind)
            {
                case PrimKind.Line:
                    context.DrawLine(prim.Pen!, prim.A, prim.B);
                    break;

                case PrimKind.Circle:
                    context.DrawEllipse(prim.Brush, prim.Pen, prim.A, prim.Radius, prim.Radius);
                    break;

                case PrimKind.Text:
                    FormattedText formatted = Format(prim.Text!, prim.Size, prim.Brush!, prim.Serif ? Serif : Sans);
                    context.DrawText(formatted, new Point(
                        prim.A.X - formatted.Width / 2,
                        prim.A.Y - formatted.Height / 2));
                    break;

                case PrimKind.Poly:
                    context.DrawGeometry(prim.Brush, prim.Pen,
                        BuildPoly(prim.Points!, prim.Closed, prim.Brush != null));
                    break;
            }
        }

        clipState?.Dispose();
    }

    private static Geometry BuildPoly(Vec2[] points, bool closed, bool filled)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y), filled);
            for (int i = 1; i < points.Length; i++)
            {
                context.LineTo(new Point(points[i].X, points[i].Y));
            }

            context.EndFigure(closed);
        }

        return geometry;
    }

    // Centring marks: a short heavy tick at the middle of each viewport edge, at the point the
    // camera keeps when the panels change width.
    private static void DrawCentringMarks(DrawingContext context, Styles styles, Size viewport)
    {
        double cx = Math.Round(viewport.Width / 2) - 1.5;
        double cy = Math.Round(viewport.Height / 2) - 1.5;
        context.FillRectangle(styles.PromptText, new Rect(cx, 0, 3, 12));
        context.FillRectangle(styles.PromptText, new Rect(cx, viewport.Height - 12, 3, 12));
        context.FillRectangle(styles.PromptText, new Rect(0, cy, 12, 3));
        context.FillRectangle(styles.PromptText, new Rect(viewport.Width - 12, cy, 12, 3));
    }

    private static void DrawHelp(DrawingContext context, World world, Styles styles, Size viewport)
    {
        double top = 0;
        double bottom = viewport.Height;
        double y = top;
        double left = 0;
        bool altLine = false;
        context.FillRectangle(styles.HelpBackground, new Rect(left, top, World.HelpColumnWidth, bottom - top));

        void Row(string first, string second, double indent = 0)
        {
            double height = Format(first, HelpTextSize, styles.HelpTextA, Chrome.Mono).Height;
            // A short window gets a second column instead of rows hidden under the bottom bar.
            if (y + height > bottom && y > top)
            {
                left += World.HelpColumnWidth;
                y = top;
                context.FillRectangle(styles.HelpBackground,
                    new Rect(left, top, World.HelpColumnWidth, bottom - top));
            }

            altLine = !altLine;
            if (altLine)
            {
                context.FillRectangle(styles.HelpBackgroundAlt,
                    new Rect(left, (int)y, World.HelpColumnWidth, (int)height));
            }

            // A row with no text is a gap; a row with no second part is a heading.
            if (first != " ")
            {
                double x = left + 8 + indent;
                x += DrawText(context, first, HelpTextSize, styles.HelpTextA, x, y);
                if (second != " ")
                {
                    x += DrawText(context, ":", HelpTextSmallSize, styles.HelpTextC, x, y);
                    DrawText(context, second, HelpTextSmallSize, styles.HelpTextB, x, y);
                }
            }

            y += height;
        }

        foreach (HelpRow row in world.Help)
        {
            Row(row.Keys, row.Text, row.Indent);
        }
    }

    private static FormattedText Format(string text, double size, IBrush brush, Typeface face)
    {
        var key = (text, size, brush, face);
        if (TextCache.TryGetValue(key, out FormattedText? formatted))
        {
            return formatted;
        }

        // The help overlay alone needs about 120 strings; a smaller cache is rebuilt every frame.
        if (TextCache.Count >= 256)
        {
            TextCache.Clear();
        }

        formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face, size, brush);
        TextCache[key] = formatted;
        return formatted;
    }

    // Chrome text (the help overlay) is set in the bundled mono face.
    private static double DrawText(DrawingContext context, string text, double size, IBrush brush,
        double x, double y, HAlign horizontal = HAlign.Near, VAlign vertical = VAlign.Near)
    {
        FormattedText formatted = Format(text, size, brush, Chrome.Mono);
        double drawX = horizontal switch
        {
            HAlign.Center => x - formatted.Width / 2,
            HAlign.Far => x - formatted.Width,
            _ => x
        };
        double drawY = vertical == VAlign.Center ? y - formatted.Height / 2 : y;
        context.DrawText(formatted, new Point(drawX, drawY));
        return formatted.Width;
    }
}
