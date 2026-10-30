using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Overboard.Ink;

/// <summary>A handle of the selection frame, named after its place on the frame before any rotation.</summary>
public enum Handle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Rotate,
    /// <summary>The ends of a line or arrow.</summary>
    Start,
    End,
}

/// <summary>
/// The frame around the selection, with its handles: a single element's own box, turned and scaled
/// with it, or the box around several elements. Gives the transforms its handles make.
/// </summary>
/// <param name="Box">The frame in its own coordinates.</param>
/// <param name="ToBoard">Maps the frame's coordinates to the board.</param>
/// <param name="Sides">Whether the sides have handles, to stretch in one direction.</param>
public readonly record struct SelectionFrame(Rect Box, Matrix ToBoard, bool Sides)
{
    /// <summary>On-screen sizes.</summary>
    private const double HandleSize = 9;
    private const double RotateDistance = 26;
    private const double MinSideHandlesLength = 36;

    private static readonly Handle[] Corners = [Handle.TopLeft, Handle.TopRight, Handle.BottomRight, Handle.BottomLeft];
    private static readonly Handle[] Edges = [Handle.Top, Handle.Right, Handle.Bottom, Handle.Left];

    /// <summary>The frame of these elements: its own for a single one, else the box around them all.</summary>
    public static SelectionFrame? Of(IReadOnlyList<InkElement> elements) => elements switch
    {
        [] => null,
        [var single] => new SelectionFrame(single.Frame, single.Placement, single.CanStretch),
        _ => new SelectionFrame(elements.Select(e => e.Bounds).Aggregate((a, b) => a.Union(b)), Matrix.Identity, false),
    };

    /// <summary>The same frame, with <paramref name="transform"/> (board coordinates) applied.</summary>
    public SelectionFrame Transform(Matrix transform) => this with { ToBoard = ToBoard * transform };

    public Point Center => Box.Center.Transform(ToBoard);

    /// <summary>Direction of the frame's horizontal sides, in radians.</summary>
    public double Angle => Math.Atan2(ToBoard.M12, ToBoard.M11);

    public static bool IsCorner(Handle handle) => Corners.Contains(handle);

    /// <summary>Which side of the box a handle is on, horizontally and vertically: −1, 0 (middle) or 1.</summary>
    private static (int X, int Y) Side(Handle handle) => handle switch
    {
        Handle.TopLeft => (-1, -1),
        Handle.Top => (0, -1),
        Handle.TopRight => (1, -1),
        Handle.Right => (1, 0),
        Handle.BottomRight => (1, 1),
        Handle.Bottom => (0, 1),
        Handle.BottomLeft => (-1, 1),
        Handle.Left => (-1, 0),
        _ => (0, 0),
    };

    private static Point At(Rect box, int x, int y) =>
        new(x < 0 ? box.Left : x > 0 ? box.Right : box.Center.X, y < 0 ? box.Top : y > 0 ? box.Bottom : box.Center.Y);

    private Point OnBoard(int x, int y) => At(Box, x, y).Transform(ToBoard);

    /// <summary>The handles shown at this zoom, with their place on the board.</summary>
    public IEnumerable<(Handle Handle, Point Position)> Handles(double zoom, bool rotate)
    {
        if (rotate) yield return (Handle.Rotate, RotatePosition(zoom));
        foreach (var handle in Corners) yield return (handle, OnBoard(Side(handle).X, Side(handle).Y));
        // Side handles on a small frame would cover the corners.
        var width = Distance(OnBoard(-1, 0), OnBoard(1, 0)) * zoom;
        var height = Distance(OnBoard(0, -1), OnBoard(0, 1)) * zoom;
        if (!Sides) yield break;
        foreach (var handle in Edges)
        {
            var length = Side(handle).X == 0 ? width : height;
            if (length >= MinSideHandlesLength) yield return (handle, OnBoard(Side(handle).X, Side(handle).Y));
        }
    }

    /// <summary>The handle under <paramref name="point"/> (board coordinates), if any.</summary>
    public Handle HandleAt(Point point, double zoom, bool rotate)
    {
        foreach (var (handle, position) in Handles(zoom, rotate))
            if (Grabs(position, point, zoom)) return handle;
        return Handle.None;
    }

    /// <summary>Whether a handle drawn at <paramref name="handle"/> is under <paramref name="point"/> (board coordinates).</summary>
    public static bool Grabs(Point handle, Point point, double zoom)
    {
        var reach = (HandleSize / 2 + 3) / zoom;
        return Math.Abs(point.X - handle.X) <= reach && Math.Abs(point.Y - handle.Y) <= reach;
    }

    public bool Contains(Point point) => ToBoard.TryInvert(out var toBox) && Box.Contains(point.Transform(toBox));

    /// <summary>Above the top side, whichever way the frame is turned.</summary>
    private Point RotatePosition(double zoom)
    {
        Vector up = new Point(0, -1).Transform(ToBoard) - new Point(0, 0).Transform(ToBoard);
        var length = up.Length;
        up = length > 1e-12 ? up / length : new Vector(0, -1);
        return OnBoard(0, -1) + up * (RotateDistance / zoom);
    }

    /// <summary>
    /// The transform made by dragging <paramref name="handle"/> to <paramref name="pointer"/> (board coordinates):
    /// a scale along the frame's sides, keeping the opposite side or corner in place.
    /// </summary>
    /// <param name="minSize">Smallest width and height the frame can shrink to, in board units.</param>
    public Matrix Resize(Handle handle, Point pointer, bool proportional, double minSize)
    {
        if (!ToBoard.TryInvert(out var toBox)) return Matrix.Identity;
        var (hx, hy) = Side(handle);
        var anchor = At(Box, -hx, -hy);
        var p = pointer.Transform(toBox);
        var min = minSize / Math.Max(Math.Sqrt(Math.Abs(ToBoard.GetDeterminant())), 1e-12);
        double sx = 1, sy = 1;
        if (hx != 0 && Box.Width > 1e-9) sx = Math.Max((p.X - anchor.X) * hx, min) / Box.Width;
        if (hy != 0 && Box.Height > 1e-9) sy = Math.Max((p.Y - anchor.Y) * hy, min) / Box.Height;
        if (proportional && hx != 0 && hy != 0)
        {
            // The pointer projected on the diagonal.
            var diagonal = At(Box, hx, hy) - anchor;
            var length2 = diagonal.X * diagonal.X + diagonal.Y * diagonal.Y;
            if (length2 > 1e-18)
            {
                var s = ((p.X - anchor.X) * diagonal.X + (p.Y - anchor.Y) * diagonal.Y) / length2;
                sx = sy = Math.Max(s, min / Math.Max(Box.Width, Box.Height));
            }
        }
        var scale = Matrix.CreateTranslation(-anchor.X, -anchor.Y) * Matrix.CreateScale(sx, sy) *
                    Matrix.CreateTranslation(anchor.X, anchor.Y);
        return toBox * scale * ToBoard;
    }

    /// <summary>
    /// The rotation around the frame's center made by dragging the rotation handle from <paramref name="from"/>
    /// to <paramref name="to"/> (board coordinates); <paramref name="snap"/> stops the frame at every 15°.
    /// </summary>
    public Matrix Rotate(Point from, Point to, bool snap)
    {
        var c = Center;
        var delta = Math.Atan2(to.Y - c.Y, to.X - c.X) - Math.Atan2(from.Y - c.Y, from.X - c.X);
        if (snap)
        {
            const double step = Math.PI / 12;
            delta = Math.Round((Angle + delta) / step) * step - Angle;
        }
        return Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(delta) * Matrix.CreateTranslation(c.X, c.Y);
    }

    /// <summary>The mirror image across the frame's middle, along its own sides: left and right, or top and bottom.</summary>
    public Matrix Flip(bool horizontal)
    {
        if (!ToBoard.TryInvert(out var toBox)) return Matrix.Identity;
        var c = Box.Center;
        return toBox * Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(horizontal ? -1 : 1, horizontal ? 1 : -1) *
               Matrix.CreateTranslation(c.X, c.Y) * ToBoard;
    }

    /// <summary>
    /// <paramref name="box"/> with the sides of <paramref name="handle"/> moved to <paramref name="point"/>
    /// (same coordinates), staying within <paramref name="limits"/> and at least <paramref name="min"/> wide and high.
    /// </summary>
    public static Rect MoveSides(Rect box, Handle handle, Point point, Rect limits, double min)
    {
        var (hx, hy) = Side(handle);
        double left = box.Left, top = box.Top, right = box.Right, bottom = box.Bottom;
        if (hx < 0) left = Math.Max(limits.Left, Math.Min(point.X, right - min));
        if (hx > 0) right = Math.Min(limits.Right, Math.Max(point.X, left + min));
        if (hy < 0) top = Math.Max(limits.Top, Math.Min(point.Y, bottom - min));
        if (hy > 0) bottom = Math.Min(limits.Bottom, Math.Max(point.Y, top + min));
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>The outline of the frame, as a closed shape on the board.</summary>
    public Geometry Outline()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(OnBoard(-1, -1), false);
            context.LineTo(OnBoard(1, -1));
            context.LineTo(OnBoard(1, 1));
            context.LineTo(OnBoard(-1, 1));
            context.EndFigure(true);
        }
        return geometry;
    }

    /// <summary>The frame, its handles and the rotation handle, at a constant on-screen size.</summary>
    public void Render(DrawingContext context, double zoom, Color accent, bool rotate)
    {
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(accent), 1.25 / zoom);
        context.DrawGeometry(null, pen, Outline());
        foreach (var (handle, position) in Handles(zoom, rotate))
        {
            if (handle == Handle.Rotate) context.DrawLine(pen, OnBoard(0, -1), position);
            RenderHandle(context, position, zoom, accent, round: handle == Handle.Rotate);
        }
    }

    /// <summary>A handle at a constant on-screen size: a square, or a round one.</summary>
    public static void RenderHandle(DrawingContext context, Point position, double zoom, Color accent, bool round)
    {
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(accent), 1.25 / zoom);
        var half = HandleSize / 2 / zoom;
        if (round) context.DrawEllipse(Brushes.White, pen, position, half, half);
        else context.DrawRectangle(Brushes.White, pen, new Rect(position.X - half, position.Y - half, half * 2, half * 2), 1.5 / zoom, 1.5 / zoom);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
