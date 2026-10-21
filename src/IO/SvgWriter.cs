using System.Globalization;
using System.Security;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace Chitin;

public static class SvgWriter
{
    public static string Write(World world, int width, int height)
    {
        Rect plane = GuideBuilder.PlaneRect(world);
        Prim[] prims = GuideBuilder.Build(world, world.StylesAt(1));

        var clips = new Dictionary<Rect, int>();
        foreach (Prim prim in prims)
        {
            if (prim.ScreenOnly || prim.Clip is not { } clip || clips.ContainsKey(clip))
            {
                continue;
            }

            clips[clip] = clips.Count;
        }

        var svg = new StringBuilder();
        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width)
            .Append("\" height=\"").Append(height)
            .Append("\" viewBox=\"").Append(N(plane.X)).Append(' ').Append(N(plane.Y))
            .Append(' ').Append(N(plane.Width)).Append(' ').Append(N(plane.Height))
            .Append("\">\n");

        if (clips.Count > 0)
        {
            svg.Append("  <defs>\n");
            foreach ((Rect clip, int id) in clips)
            {
                svg.Append("    <clipPath id=\"clip").Append(id).Append("\"><rect x=\"")
                    .Append(N(clip.X)).Append("\" y=\"").Append(N(clip.Y))
                    .Append("\" width=\"").Append(N(clip.Width)).Append("\" height=\"")
                    .Append(N(clip.Height)).Append("\" /></clipPath>\n");
            }

            svg.Append("  </defs>\n");
        }

        foreach (Prim prim in prims)
        {
            if (prim.ScreenOnly)
            {
                continue;
            }

            int? clipId = prim.Clip is { } clip ? clips[clip] : null;
            if (clipId is { } id)
            {
                svg.Append("  <g clip-path=\"url(#clip").Append(id).Append(")\">");
            }

            switch (prim.Kind)
            {
                case PrimKind.Line:
                    WriteLine(svg, prim);
                    break;

                case PrimKind.Circle:
                    WriteCircle(svg, prim);
                    break;

                case PrimKind.Text:
                    WriteText(svg, prim);
                    break;

                case PrimKind.Poly:
                    WritePoly(svg, prim);
                    break;
            }

            if (clipId != null)
            {
                svg.Append("</g>");
            }

            svg.Append('\n');
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteLine(StringBuilder svg, Prim prim)
    {
        if (prim.Pen == null)
        {
            return;
        }

        Color color = ColorOf(prim.Pen.Brush);
        svg.Append("<line x1=\"").Append(N(prim.A.X)).Append("\" y1=\"").Append(N(prim.A.Y))
            .Append("\" x2=\"").Append(N(prim.B.X)).Append("\" y2=\"").Append(N(prim.B.Y))
            .Append("\" stroke=\"").Append(Hex(color)).Append("\" stroke-opacity=\"")
            .Append(N(color.A / 255.0)).Append("\" stroke-width=\"")
            .Append(N(prim.Pen.Thickness)).Append('"');
        WriteDash(svg, prim.Pen);
        svg.Append(" />");
    }

    private static void WriteCircle(StringBuilder svg, Prim prim)
    {
        Color stroke = ColorOf(prim.Pen?.Brush);
        svg.Append("<circle cx=\"").Append(N(prim.A.X)).Append("\" cy=\"").Append(N(prim.A.Y))
            .Append("\" r=\"").Append(N(prim.Radius)).Append('"');
        if (prim.Brush is { } brush)
        {
            Color fill = ColorOf(brush);
            svg.Append(" fill=\"").Append(Hex(fill)).Append("\" fill-opacity=\"")
                .Append(N(fill.A / 255.0)).Append('"');
        }
        else
        {
            svg.Append(" fill=\"none\"");
        }

        svg.Append(" stroke=\"").Append(Hex(stroke)).Append("\" stroke-opacity=\"")
            .Append(N(stroke.A / 255.0)).Append("\" stroke-width=\"")
            .Append(N(prim.Pen?.Thickness ?? 0)).Append('"');
        if (prim.Pen != null)
        {
            WriteDash(svg, prim.Pen);
        }

        svg.Append(" />");
    }

    private static void WritePoly(StringBuilder svg, Prim prim)
    {
        if (prim.Points is not { Length: > 1 } points)
        {
            return;
        }

        string tag = prim.Closed ? "polygon" : "polyline";
        svg.Append('<').Append(tag).Append(" points=\"");
        for (int i = 0; i < points.Length; i++)
        {
            if (i > 0)
            {
                svg.Append(' ');
            }

            svg.Append(N(points[i].X)).Append(',').Append(N(points[i].Y));
        }

        svg.Append('"');
        if (prim.Brush is { } brush)
        {
            Color fill = ColorOf(brush);
            svg.Append(" fill=\"").Append(Hex(fill)).Append("\" fill-opacity=\"")
                .Append(N(fill.A / 255.0)).Append('"');
        }
        else
        {
            svg.Append(" fill=\"none\"");
        }

        if (prim.Pen is { } pen)
        {
            Color stroke = ColorOf(pen.Brush);
            svg.Append(" stroke=\"").Append(Hex(stroke)).Append("\" stroke-opacity=\"")
                .Append(N(stroke.A / 255.0)).Append("\" stroke-width=\"")
                .Append(N(pen.Thickness)).Append('"');
            WriteDash(svg, pen);
        }
        else
        {
            svg.Append(" stroke=\"none\"");
        }

        svg.Append(" />");
    }

    private static void WriteText(StringBuilder svg, Prim prim)
    {
        Color fill = ColorOf(prim.Brush);
        svg.Append("<text x=\"").Append(N(prim.A.X)).Append("\" y=\"").Append(N(prim.A.Y))
            .Append("\" font-family=\"").Append(prim.Serif ? "serif" : "sans-serif")
            .Append("\" font-size=\"").Append(N(prim.Size)).Append("\" fill=\"")
            .Append(Hex(fill)).Append("\" fill-opacity=\"").Append(N(fill.A / 255.0))
            .Append("\" text-anchor=\"middle\" dominant-baseline=\"central\">")
            .Append(SecurityElement.Escape(prim.Text ?? string.Empty)).Append("</text>");
    }

    private static void WriteDash(StringBuilder svg, IPen pen)
    {
        if (pen.DashStyle is not { } dash || dash.Dashes is not { Count: > 0 } dashes)
        {
            return;
        }

        svg.Append(" stroke-dasharray=\"");
        for (int i = 0; i < dashes.Count; i++)
        {
            if (i > 0)
            {
                svg.Append(',');
            }

            svg.Append(N(dashes[i] * pen.Thickness));
        }

        svg.Append('"');
        if (dash.Offset != 0)
        {
            svg.Append(" stroke-dashoffset=\"").Append(N(dash.Offset * pen.Thickness)).Append('"');
        }
    }

    private static Color ColorOf(IBrush? brush)
    {
        return brush is ISolidColorBrush solid ? solid.Color : default;
    }

    private static string Hex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string N(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
