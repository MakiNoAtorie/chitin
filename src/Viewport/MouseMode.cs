using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Chitin;

public class MouseMode
{
    protected readonly World World;

    public Vec2 LastScreen = new();
    public Vec2 MoveDelta = new();
    public bool HasMoved;

    protected MouseMode(World world)
    {
        World = world;
    }

    public virtual void Init(Point screen, Vec2 world)
    {
        LastScreen = new Vec2(screen.X, screen.Y);
        MoveDelta = new Vec2();
        HasMoved = false;
    }

    public virtual void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        var position = new Vec2(screen.X, screen.Y);
        MoveDelta = position - LastScreen;
        LastScreen = position;
        HasMoved = true;
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

    public VanishingPointMode(World world, Action pushUndo) : base(world)
    {
        _pushUndo = pushUndo;
    }

    public override void MouseMoved(Point screen, Vec2 world, KeyModifiers modifiers)
    {
        base.MouseMoved(screen, world, modifiers);
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
        if (!HasMoved)
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
