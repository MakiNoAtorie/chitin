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
    private static readonly Typeface Sans = new(FontFamily.Default);
    private static readonly Typeface Serif = new("serif");
    private static readonly Dictionary<(string Text, double Size, IBrush Brush, Typeface Face), FormattedText>
        TextCache = new();

    internal static void ClearTextCache()
    {
        TextCache.Clear();
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
                if (world.HoverIdx >= 0 && world.VanishingPoints[world.HoverIdx] is { } hovered)
                {
                    context.DrawEllipse(null, styles.HoverPen, new Point(hovered.Pos.X, hovered.Pos.Y),
                        20 / styles.Zoom, 20 / styles.Zoom);
                }

                world.Overlay?.DrawOver(context);
            }
        }

        if (interactive && world.Overlay == null && world.VanishingPoints.All(point => point == null))
        {
            DrawEmptyHint(context, world, styles, camera);
        }
    }

    // An empty plane says how to start. The text wraps to the plane and stays on it: its colour is
    // for the paper, so a plane too small to hold it shows none.
    private static void DrawEmptyHint(DrawingContext context, World world, Styles styles, Vec2 camera)
    {
        const double pad = 12;
        var plane = new Rect((camera.X - world.PicturePlaneWidthHalf) * styles.Zoom,
            (camera.Y - world.PicturePlaneHeightHalf) * styles.Zoom,
            world.PicturePlaneWidth * styles.Zoom, world.PicturePlaneHeight * styles.Zoom);
        if (plane.Width < 2 * pad + 96)
        {
            return;
        }

        FormattedText hint = Format(EmptyHint, 12, styles.MutedText, Chrome.Mono);
        hint.MaxTextWidth = plane.Width - 2 * pad;
        hint.TextAlignment = TextAlignment.Center;
        if (hint.Height > plane.Height - 2 * pad)
        {
            return;
        }

        context.DrawText(hint, new Point(plane.X + pad, plane.Center.Y - hint.Height / 2));
    }

    // No hyphen: a wrap on a small plane must not split "2-point".
    internal const string EmptyHint = "Click to place point 1, or pick a preset in the Camera menu";

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
                context.DrawImage(image, ReferenceRect(image.Size, plane));
            }
        }
    }

    // Scale 1 fits the whole image inside the plane, centred, keeping its aspect ratio.
    internal static Rect ReferenceRect(Size image, Rect plane)
    {
        double fit = Math.Min(plane.Width / image.Width, plane.Height / image.Height);
        double width = image.Width * fit;
        double height = image.Height * fit;
        return new Rect(plane.Center.X - width / 2, plane.Center.Y - height / 2, width, height);
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
}
