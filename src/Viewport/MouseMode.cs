using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Chitin;

public class MouseMode
{
    protected readonly World World;

    // A press that moves less than this, in screen pixels, is a click: a stylus, touch or fine mouse
    // click moves a pixel or two between press and release.
    public const double ClickSlop = 4;

    public Vec2 PressScreen = new();
    public Vec2 LastScreen = new();
    public Vec2 MoveDelta = new();
    public bool HasMoved;

    // Moved beyond the click slop since the press.
    public bool Dragged;

    protected MouseMode(World world)
    {
        World = world;
    }

    public virtual void Init(Point screen, Vec2 world)
    {
        LastScreen = new Vec2(screen.X, screen.Y);
        PressScreen = LastScreen;
        MoveDelta = new Vec2();
        HasMoved = false;
        Dragged = false;
    }

    public virtual void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        var position = new Vec2(screen.X, screen.Y);
        MoveDelta = position - LastScreen;
        LastScreen = position;
        HasMoved = true;
        Dragged |= position.DistFrom(PressScreen) >= ClickSlop;
    }

    public virtual void MouseUp()
    {
    }

    public virtual void DrawOver(DrawingContext context)
    {
    }
}

internal sealed class CameraMode : MouseMode
{
    public CameraMode(World world) : base(world)
    {
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        World.Camera = new Vec2(
            World.Camera.X + MoveDelta.X / World.Zoom,
            World.Camera.Y + MoveDelta.Y / World.Zoom);
        MoveDelta = new Vec2();
    }
}

internal sealed class VanishingPointMode : MouseMode
{
    private readonly Action _pushUndo;
    private bool _pushed;
    private bool _following;

    public VanishingPointMode(World world, Action pushUndo) : base(world)
    {
        _pushUndo = pushUndo;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        // Within the click slop the point stays; past it, the point catches up with the pointer.
        if (!Dragged)
        {
            MoveDelta = new Vec2();
            return;
        }

        if (!_following)
        {
            MoveDelta = LastScreen - PressScreen;
            _following = true;
        }

        int index = World.SelectionIdx;
        if (index > -1 && World.VanishingPoints[index] is { } point &&
            MoveDelta != default)
        {
            // Push on the first real move: a click that only selects adds no undo step.
            if (!_pushed)
            {
                _pushUndo();
                _pushed = true;
            }

            point.Pos = new Vec2(
                point.Pos.X + MoveDelta.X / World.Zoom,
                point.Pos.Y + MoveDelta.Y / World.Zoom);
            World.BumpRevision();
        }

        MoveDelta = new Vec2();
    }
}

internal sealed class TraceLineMode : MouseMode
{
    private readonly TraceLine _traceLine = new(new Vec2(), new Vec2());
    private readonly Action _pushUndo;

    public TraceLineMode(World world, Action pushUndo) : base(world)
    {
        _pushUndo = pushUndo;
    }

    public override void Init(Point screen, Vec2 world)
    {
        base.Init(screen, world);
        _traceLine.Start = world;
        _traceLine.End = world;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        _traceLine.End = world;
        if (modifiers == KeyModifiers.Control)
        {
            SnapEndPointToAngle(5);
        }

        if (modifiers == KeyModifiers.Shift)
        {
            SnapEndPointToAngle(15);
        }
    }

    private void SnapEndPointToAngle(int angle)
    {
        Vec2 direction = _traceLine.End - _traceLine.Start;
        double size = direction.Length();
        direction = direction.Normalized();

        int best = -1;
        double bestDot = -999;
        for (int i = 0; i < 360; i += angle)
        {
            double dot = Vec2.Dot(direction, World.UnitVectors[i]);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        if (best > -1)
        {
            _traceLine.End = _traceLine.Start + World.UnitVectors[best] * size;
        }
    }

    public override void MouseUp()
    {
        // A click adds no line: a line of a pixel or two has no direction worth a trace slot.
        if (!Dragged)
        {
            return;
        }

        _pushUndo();
        if (World.TraceLines.Count > 1)
        {
            World.TraceLines.RemoveAt(1);
        }

        World.TraceLines.Add(_traceLine);
        World.BumpRevision();
    }

    public override void DrawOver(DrawingContext context)
    {
        context.DrawLine(World.Styles.TraceNewPen,
            new Point(_traceLine.Start.X, _traceLine.Start.Y),
            new Point(_traceLine.End.X, _traceLine.End.Y));
    }
}

// Alt + left drag: move the reference image. One undo step on the first real move.
internal sealed class ReferenceMode : MouseMode
{
    private readonly Action _pushUndo;
    private bool _pushed;

    public ReferenceMode(World world, Action pushUndo) : base(world)
    {
        _pushUndo = pushUndo;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        if (MoveDelta == default)
        {
            return;
        }

        if (!_pushed)
        {
            _pushUndo();
            _pushed = true;
        }

        ReferenceSettings next = World.Reference.Clone();
        next.Offset = new Vec2(next.Offset.X + MoveDelta.X / World.Zoom, next.Offset.Y + MoveDelta.Y / World.Zoom);
        World.SetReferenceSettings(next);
        MoveDelta = new Vec2();
    }
}

// Ctrl + Space + drag: zoom about the press point. Right or up zooms in, left or down out; 100 px
// is about 2.7 times. View state: no undo.
internal sealed class ZoomDragMode : MouseMode
{
    private readonly Point _anchor;
    private readonly double _startZoom;
    private readonly Action<Point, double> _zoomAt;

    public ZoomDragMode(World world, Point anchor, Action<Point, double> zoomAt) : base(world)
    {
        _anchor = anchor;
        _startZoom = world.Zoom;
        _zoomAt = zoomAt;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
        _zoomAt(_anchor, _startZoom * Math.Exp((screen.X - _anchor.X - (screen.Y - _anchor.Y)) / 100));
    }
}
