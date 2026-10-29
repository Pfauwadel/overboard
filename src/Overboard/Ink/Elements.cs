using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Overboard.Ink;

/// <summary>Something drawn on a canvas, in board coordinates. Elements are immutable once created.</summary>
public abstract class InkElement
{
    public abstract Rect Bounds { get; }

    public abstract void Render(DrawingContext context, bool darkBackground);

    /// <summary>Whether a circle of the given radius around <paramref name="point"/> touches the element.</summary>
    public abstract bool HitTest(Point point, double radius);
}

/// <summary>A freehand stroke, filled as an outline. Highlighter strokes are translucent.</summary>
public sealed class StrokeElement : InkElement
{
    private readonly Geometry _geometry;
    private readonly IBrush _brush;
    private readonly IBrush _darkBrush;

    public StrokeElement(Stroke stroke, double opacity = 1)
    {
        Stroke = stroke;
        Opacity = opacity;
        _geometry = StrokeGeometry.Build(stroke);
        _brush = new ImmutableSolidColorBrush(stroke.Color, opacity);
        _darkBrush = new ImmutableSolidColorBrush(ToolSettings.Display(stroke.Color, true), opacity);
        Bounds = _geometry.Bounds;
    }

    public Stroke Stroke { get; }
    public double Opacity { get; }
    public override Rect Bounds { get; }

    public override void Render(DrawingContext context, bool darkBackground) =>
        context.DrawGeometry(darkBackground ? _darkBrush : _brush, null, _geometry);

    public override bool HitTest(Point point, double radius) =>
        _geometry.FillContains(point) || _geometry.StrokeContains(new Pen(Brushes.Black, radius * 2), point);
}

public enum ShapeKind
{
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

public sealed class ShapeElement : InkElement
{
    private readonly Geometry _geometry;
    private readonly IPen _pen;
    private readonly IPen _darkPen;

    public ShapeElement(ShapeKind kind, Point start, Point end, Color color, double thickness)
    {
        Kind = kind;
        Start = start;
        End = end;
        Color = color;
        Thickness = thickness;
        _pen = CreatePen(color, thickness);
        _darkPen = CreatePen(ToolSettings.Display(color, true), thickness);
        _geometry = kind switch
        {
            ShapeKind.Line => new LineGeometry(start, end),
            ShapeKind.Arrow => Arrow(start, end, thickness),
            ShapeKind.Rectangle => new RectangleGeometry(new Rect(start, end)),
            _ => new EllipseGeometry(new Rect(start, end)),
        };
        Bounds = _geometry.GetRenderBounds(_pen);
    }

    public ShapeKind Kind { get; }
    public Point Start { get; }
    public Point End { get; }
    public Color Color { get; }
    public double Thickness { get; }
    public override Rect Bounds { get; }

    private static ImmutablePen CreatePen(Color color, double thickness) =>
        new(new ImmutableSolidColorBrush(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>Applies the Shift constraint: 45° angles for lines, squares and circles for the others.</summary>
    public static Point Constrain(ShapeKind kind, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (kind is ShapeKind.Line or ShapeKind.Arrow)
        {
            var step = Math.PI / 4;
            var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
            var length = Math.Sqrt(dx * dx + dy * dy);
            return new Point(start.X + Math.Cos(angle) * length, start.Y + Math.Sin(angle) * length);
        }
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(start.X + Math.CopySign(side, dx), start.Y + Math.CopySign(side, dy));
    }

    private static Geometry Arrow(Point start, Point end, double thickness)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
        var head = Math.Min(Math.Max(thickness * 3.5, 12), length * 0.6);
        var angle = Math.Atan2(dy, dx);
        const double spread = Math.PI / 7;
        var wing1 = new Point(end.X - head * Math.Cos(angle - spread), end.Y - head * Math.Sin(angle - spread));
        var wing2 = new Point(end.X - head * Math.Cos(angle + spread), end.Y - head * Math.Sin(angle + spread));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.LineTo(end);
            ctx.EndFigure(false);
            ctx.BeginFigure(wing1, false);
            ctx.LineTo(end);
            ctx.LineTo(wing2);
            ctx.EndFigure(false);
        }
        return geometry;
    }

    public override void Render(DrawingContext context, bool darkBackground) =>
        context.DrawGeometry(null, darkBackground ? _darkPen : _pen, _geometry);

    public override bool HitTest(Point point, double radius) =>
        _geometry.StrokeContains(new Pen(Brushes.Black, Thickness + radius * 2), point);
}

public sealed class TextElement : InkElement
{
    public static readonly Typeface Typeface = new(new FontFamily("Segoe UI, Helvetica Neue, Arial"), weight: FontWeight.SemiBold);

    private readonly FormattedText _text;
    private readonly FormattedText _darkText;

    public TextElement(string text, Point origin, Color color, double fontSize)
    {
        Text = text;
        Origin = origin;
        Color = color;
        FontSize = fontSize;
        _text = Format(text, color, fontSize);
        var darkColor = ToolSettings.Display(color, true);
        _darkText = darkColor == color ? _text : Format(text, darkColor, fontSize);
    }

    public string Text { get; }
    public Point Origin { get; }
    public Color Color { get; }
    public double FontSize { get; }
    public override Rect Bounds => new(Origin, new Size(_text.WidthIncludingTrailingWhitespace, _text.Height));

    public static FormattedText Format(string text, Color color, double fontSize) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, fontSize, new ImmutableSolidColorBrush(color));

    public override void Render(DrawingContext context, bool darkBackground)
    {
        // Subpixel text leaves dark fringes on a transparent surface.
        using (context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
            context.DrawText(darkBackground ? _darkText : _text, Origin);
    }

    public override bool HitTest(Point point, double radius) => Bounds.Inflate(radius).Contains(point);
}
