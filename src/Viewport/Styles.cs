using Avalonia.Media;

namespace Chitin;

// Every pen and brush for one palette at one zoom. Pen widths are in world units, so they
// depend on the zoom; an export builds its own Styles instead of changing the live view.
public sealed class Styles
{
    public readonly double Zoom;

    public readonly IBrush ViewportClear;
    public readonly IBrush PlaneBackground;
    public readonly IBrush PlaneBorder;
    public readonly IBrush HudText;
    public readonly IBrush[] VPFill;

    public readonly IPen[] VPOutline;
    public readonly IPen[] VPLines;
    public readonly IPen HorizonPen;
    public readonly IPen HorizonVoidPen;
    public readonly IPen TracePen;
    public readonly IPen TraceNewPen;
    public readonly IPen GridPen;
    public readonly IBrush BoxFaceBrush;
    public readonly IPen BoxEdgePen;
    public readonly IPen BoxHiddenPen;
    public readonly IPen ConstructionPen;
    public readonly IPen ConstructionPreviewPen;
    public readonly IPen ConePen;
    public readonly IPen ConeLegPen;
    public readonly IPen StationPen;
    public readonly IBrush StationFill;
    public readonly IPen WarningPen;
    public readonly IPen HoverPen;

    public Styles(Palette palette, int paletteOffset, double zoom)
    {
        Zoom = zoom;
        VPLines = new IPen[9];
        for (int i = 0; i < 9; i++)
        {
            VPLines[i] = new Pen(Brush(palette.VpColors[(paletteOffset + i) % 9]), PenWidth(1));
        }

        GridPen = new Pen(Brush(Alpha(palette.Grid, 128)), PenWidth(1));

        VPOutline = new IPen[2];
        VPOutline[0] = new Pen(Brush(palette.Ink), PenWidth(2));
        VPOutline[1] = new Pen(Brush(palette.Paper), PenWidth(2));
        VPFill = new IBrush[2];
        VPFill[0] = Brush(Alpha(palette.Ink, 160));
        VPFill[1] = Brush(palette.Prompt);

        ViewportClear = Brush(palette.Void);
        PlaneBackground = Brush(palette.Paper);
        PlaneBorder = Brush(palette.Border);
        HudText = Brush(palette.Paper);

        HorizonPen = new Pen(Brush(palette.Horizon), PenWidth(1));
        HorizonVoidPen = new Pen(Brush(Alpha(palette.Horizon, 200)), PenWidth(1),
            new DashStyle(new double[] { 4, 3 }, 0));
        TracePen = new Pen(Brush(Alpha(palette.Ink, 160)), PenWidth(1));
        TraceNewPen = new Pen(Brush(Alpha(palette.Prompt, 192)), PenWidth(2),
            new DashStyle(new double[] { 4, 3, 1, 3, 1, 3 }, 0));
        BoxFaceBrush = Brush(Alpha(palette.Ink, 20));
        BoxEdgePen = new Pen(Brush(palette.Ink), PenWidth(1));
        BoxHiddenPen = new Pen(Brush(Alpha(palette.Ink, 102)), PenWidth(1),
            new DashStyle(new double[] { 4, 3 }, 0));
        ConstructionPen = new Pen(Brush(Alpha(palette.Ink, 160)), PenWidth(1));
        ConstructionPreviewPen = new Pen(Brush(Alpha(palette.Ink, 140)),
            PenWidth(1), new DashStyle(new double[] { 4, 3 }, 0));
        ConePen = new Pen(Brush(Alpha(palette.Prompt, 200)), PenWidth(1),
            new DashStyle(new double[] { 4, 3 }, 0));
        ConeLegPen = new Pen(Brush(Alpha(palette.Ink, 160)), PenWidth(1),
            new DashStyle(new double[] { 4, 3 }, 0));
        StationPen = new Pen(Brush(palette.Ink), PenWidth(2));
        StationFill = Brush(palette.Prompt);
        WarningPen = new Pen(Brush(palette.Prompt), PenWidth(2));
        HoverPen = new Pen(Brush(palette.Prompt), PenWidth(2));
    }

    private double PenWidth(double width)
    {
        return Math.Max(1, width / Zoom);
    }

    private static IBrush Brush(Color color)
    {
        return new SolidColorBrush(color);
    }

    private static Color Alpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
