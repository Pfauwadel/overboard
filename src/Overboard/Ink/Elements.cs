using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace Overboard.Ink;

/// <summary>
/// Something drawn on a canvas. Elements are immutable once created. Each has its own coordinates,
/// placed on the board by <see cref="Placement"/>: the rotation and scale it was given, if any.
/// </summary>
public abstract class InkElement(Matrix placement)
{
    /// <summary>Area covered on the board, axis-aligned.</summary>
    public abstract Rect Bounds { get; }

    /// <summary>The element's box in its own coordinates: what the selection frame and its handles follow.</summary>
    public abstract Rect Frame { get; }

    /// <summary>Maps the element's own coordinates to the board.</summary>
    public Matrix Placement { get; } = placement;

    public abstract void Render(DrawingContext context, bool darkBackground);

    /// <summary>Whether a circle of the given radius around <paramref name="point"/> touches the element.</summary>
    public abstract bool HitTest(Point point, double radius);

    /// <summary>The same element, with <paramref name="transform"/> (board coordinates) applied after its placement.</summary>
    public abstract InkElement Transform(Matrix transform);

    /// <summary>The same element, moved by <paramref name="offset"/>.</summary>
    public InkElement Translate(Vector offset) => Transform(Matrix.CreateTranslation(offset));

    /// <summary>Whether the eraser removes the element.</summary>
    public virtual bool IsErasable => true;

    /// <summary>Whether resizing by a corner keeps the proportions (unless Shift is held).</summary>
    public virtual bool KeepsProportions => false;

    /// <summary>Whether the sides of the selection frame stretch it in one direction.</summary>
    public virtual bool CanStretch => true;

    /// <summary>How much the placement enlarges lengths, on average: line widths follow it.</summary>
    public double Scale => Math.Sqrt(Math.Abs(Placement.GetDeterminant()));

    /// <summary>Maps a board point to the element's own coordinates.</summary>
    protected Point ToLocal(Point point) => Placement.TryInvert(out var inverse) ? point.Transform(inverse) : point;

    /// <summary>A move only: applied to the content itself, files stay readable by earlier versions.</summary>
    protected static bool IsTranslation(Matrix m) => m.M11 == 1 && m.M12 == 0 && m.M21 == 0 && m.M22 == 1;

    protected static Vector Offset(Matrix m) => new(m.M31, m.M32);
}

/// <summary>A freehand stroke, filled as an outline. Highlighter strokes are translucent.</summary>
public sealed class StrokeElement : InkElement
{
    private readonly Geometry _geometry;
    private readonly IBrush _brush;
    private readonly IBrush _darkBrush;

    public StrokeElement(Stroke stroke, double opacity = 1) : this(stroke, opacity, Matrix.Identity)
    {
    }

    public StrokeElement(Stroke stroke, double opacity, Matrix placement) : base(placement)
    {
        Stroke = stroke;
        Opacity = opacity;
        // Placed points, so the outline keeps an even width whatever the stretching.
        var placed = placement.IsIdentity ? stroke : Stroke.FromPoints(stroke.Color, stroke.Size * Scale, stroke.UsesPressure,
            stroke.Points.Select(p => Place(p, placement)));
        _geometry = StrokeGeometry.Build(placed);
        _brush = new ImmutableSolidColorBrush(stroke.Color, opacity);
        _darkBrush = new ImmutableSolidColorBrush(ToolSettings.Display(stroke.Color, true), opacity);
        Bounds = _geometry.Bounds;
        Frame = stroke.Points.Count == 0
            ? default
            : new Rect(new Point(stroke.Points.Min(p => p.X), stroke.Points.Min(p => p.Y)),
                new Point(stroke.Points.Max(p => p.X), stroke.Points.Max(p => p.Y))).Inflate(stroke.Size / 2);
    }

    private static InkPoint Place(InkPoint point, Matrix placement)
    {
        var placed = new Point(point.X, point.Y).Transform(placement);
        return point with { X = placed.X, Y = placed.Y };
    }

    public Stroke Stroke { get; }
    public double Opacity { get; }
    public override Rect Bounds { get; }
    public override Rect Frame { get; }

    public override void Render(DrawingContext context, bool darkBackground) =>
        context.DrawGeometry(darkBackground ? _darkBrush : _brush, null, _geometry);

    public override bool HitTest(Point point, double radius) =>
        _geometry.FillContains(point) || _geometry.StrokeContains(new Pen(Brushes.Black, radius * 2), point);

    public override InkElement Transform(Matrix transform)
    {
        var placement = Placement * transform;
        if (!IsTranslation(placement)) return new StrokeElement(Stroke, Opacity, placement);
        var offset = Offset(placement);
        return new StrokeElement(Stroke.FromPoints(Stroke.Color, Stroke.Size, Stroke.UsesPressure,
            Stroke.Points.Select(p => p with { X = p.X + offset.X, Y = p.Y + offset.Y })), Opacity);
    }
}

public enum ShapeKind
{
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

/// <summary>
/// A line, arrow, rectangle or ellipse. Lines and arrows are transformed through their ends; rectangles and
/// ellipses keep a placement, drawn with an even line width.
/// </summary>
public sealed class ShapeElement : InkElement
{
    private readonly Geometry _geometry;
    private readonly IPen _pen;
    private readonly IPen _darkPen;

    public ShapeElement(ShapeKind kind, Point start, Point end, Color color, double thickness)
        : this(kind, start, end, color, thickness, Matrix.Identity)
    {
    }

    public ShapeElement(ShapeKind kind, Point start, Point end, Color color, double thickness, Matrix placement) : base(placement)
    {
        Kind = kind;
        Start = start;
        End = end;
        Color = color;
        Thickness = thickness;
        _pen = CreatePen(color, thickness * Scale);
        _darkPen = CreatePen(ToolSettings.Display(color, true), thickness * Scale);
        _geometry = kind switch
        {
            ShapeKind.Line => new LineGeometry(start, end),
            ShapeKind.Arrow => Arrow(start, end, thickness),
            ShapeKind.Rectangle => new RectangleGeometry(new Rect(start, end)),
            _ => new EllipseGeometry(new Rect(start, end)),
        };
        if (!placement.IsIdentity) _geometry.Transform = new MatrixTransform(placement);
        Bounds = _geometry.GetRenderBounds(_pen);
        Frame = new Rect(start, end);
    }

    public ShapeKind Kind { get; }
    /// <summary>Lines and arrows are edited by their ends.</summary>
    public bool IsLine => Kind is ShapeKind.Line or ShapeKind.Arrow;
    public Point Start { get; }
    public Point End { get; }
    public Color Color { get; }
    public double Thickness { get; }
    public override Rect Bounds { get; }
    public override Rect Frame { get; }

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

    /// <summary>The same line or arrow, between other ends.</summary>
    public ShapeElement WithEnds(Point start, Point end) => new(Kind, start, end, Color, Thickness);

    public override void Render(DrawingContext context, bool darkBackground) =>
        context.DrawGeometry(null, darkBackground ? _darkPen : _pen, _geometry);

    public override bool HitTest(Point point, double radius) =>
        _geometry.StrokeContains(new Pen(Brushes.Black, Thickness * Scale + radius * 2), point);

    public override InkElement Transform(Matrix transform)
    {
        var placement = Placement * transform;
        if (Kind is ShapeKind.Line or ShapeKind.Arrow || IsTranslation(placement))
        {
            // Through the ends: a line stays a line, with its arrow head upright.
            var scale = Math.Sqrt(Math.Abs(placement.GetDeterminant()));
            return new ShapeElement(Kind, Start.Transform(placement), End.Transform(placement), Color, Thickness * scale);
        }
        return new ShapeElement(Kind, Start, End, Color, Thickness, placement);
    }
}

public sealed class TextElement : InkElement
{
    public static readonly Typeface Typeface = new(new FontFamily("Segoe UI, Helvetica Neue, Arial"), weight: FontWeight.SemiBold);

    private readonly FormattedText _text;
    private readonly FormattedText _darkText;

    public TextElement(string text, Point origin, Color color, double fontSize) : this(text, origin, color, fontSize, Matrix.Identity)
    {
    }

    public TextElement(string text, Point origin, Color color, double fontSize, Matrix placement) : base(placement)
    {
        Text = text;
        Origin = origin;
        Color = color;
        FontSize = fontSize;
        _text = Format(text, color, fontSize);
        var darkColor = ToolSettings.Display(color, true);
        _darkText = darkColor == color ? _text : Format(text, darkColor, fontSize);
        Frame = new Rect(origin, new Size(_text.WidthIncludingTrailingWhitespace, _text.Height));
        Bounds = Frame.TransformToAABB(placement);
    }

    public string Text { get; }
    public Point Origin { get; }
    public Color Color { get; }
    public double FontSize { get; }
    public override Rect Bounds { get; }
    public override Rect Frame { get; }
    public override bool KeepsProportions => true;
    // Stretched letters only look wrong.
    public override bool CanStretch => false;

    public static FormattedText Format(string text, Color color, double fontSize) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, fontSize, new ImmutableSolidColorBrush(color));

    public override void Render(DrawingContext context, bool darkBackground)
    {
        using (context.PushTransform(Placement))
        // Subpixel text leaves dark fringes on a transparent surface.
        using (context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
            context.DrawText(darkBackground ? _darkText : _text, Origin);
    }

    public override bool HitTest(Point point, double radius) => Frame.Inflate(radius / Scale).Contains(ToLocal(point));

    /// <summary>The same text, saying something else.</summary>
    public TextElement WithText(string text) => new(text, Origin, Color, FontSize, Placement);

    public override InkElement Transform(Matrix transform)
    {
        var placement = Placement * transform;
        if (placement.GetDeterminant() < 0) placement = Readable(placement);
        return IsTranslation(placement)
            ? new TextElement(Text, Origin + Offset(placement), Color, FontSize)
            : new TextElement(Text, Origin, Color, FontSize, placement);
    }

    /// <summary>
    /// Text is never shown mirrored: flipped, it goes where its mirror image would be, mirrored once more
    /// about its own center, along the axis that keeps it closest to its previous direction.
    /// </summary>
    private Matrix Readable(Matrix mirrored)
    {
        var center = Frame.Center;
        Matrix Mirror(double scaleX, double scaleY) => Matrix.CreateTranslation(-center.X, -center.Y) *
            Matrix.CreateScale(scaleX, scaleY) * Matrix.CreateTranslation(center.X, center.Y) * mirrored;
        static double Angle(Matrix m) => Math.Atan2(m.M12, m.M11);
        var previous = Angle(Placement);
        var (horizontal, vertical) = (Mirror(-1, 1), Mirror(1, -1));
        return Math.Abs(Math.IEEERemainder(Angle(horizontal) - previous, 2 * Math.PI)) <=
               Math.Abs(Math.IEEERemainder(Angle(vertical) - previous, 2 * Math.PI))
            ? horizontal
            : vertical;
    }
}

/// <summary>
/// A picture, such as a screen capture. Always shown as is, whatever the background.
/// Cropping only hides the edges: the whole picture is kept, and can be uncropped.
/// </summary>
public sealed class ImageElement : InkElement
{
    /// <summary>The whole picture, as a fraction of its size.</summary>
    public static readonly Rect Uncropped = new(0, 0, 1, 1);

    private byte[]? _png;

    /// <param name="png">The bitmap already encoded as PNG, if known.</param>
    public ImageElement(Bitmap bitmap, Rect frame, byte[]? png = null) : this(bitmap, frame, Uncropped, Matrix.Identity, png)
    {
    }

    /// <param name="frame">Where the part shown is drawn, in the element's own coordinates.</param>
    /// <param name="crop">The part shown, as a fraction of the picture's size.</param>
    public ImageElement(Bitmap bitmap, Rect frame, Rect crop, Matrix placement, byte[]? png = null) : base(placement)
    {
        Bitmap = bitmap;
        Frame = frame;
        Crop = crop;
        Bounds = frame.TransformToAABB(placement);
        _png = png;
    }

    public Bitmap Bitmap { get; }
    public Rect Crop { get; }
    public override Rect Frame { get; }
    public override Rect Bounds { get; }
    public override bool KeepsProportions => true;

    /// <summary>Where the whole picture lies, in the element's own coordinates.</summary>
    public Rect FullFrame
    {
        get
        {
            var scaleX = Frame.Width / Crop.Width;
            var scaleY = Frame.Height / Crop.Height;
            return new Rect(Frame.X - Crop.X * scaleX, Frame.Y - Crop.Y * scaleY, scaleX, scaleY);
        }
    }

    /// <summary>The same picture showing only what lies within <paramref name="frame"/> (own coordinates, inside <see cref="FullFrame"/>).</summary>
    public ImageElement WithFrame(Rect frame)
    {
        var full = FullFrame;
        var crop = new Rect((frame.X - full.X) / full.Width, (frame.Y - full.Y) / full.Height,
            frame.Width / full.Width, frame.Height / full.Height);
        return new ImageElement(Bitmap, frame, crop, Placement, _png);
    }

    /// <summary>The bitmap as PNG, encoded once.</summary>
    public byte[] Png
    {
        get
        {
            if (_png is null)
            {
                using var stream = new MemoryStream();
                Bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                _png = stream.ToArray();
            }
            return _png;
        }
    }

    public override void Render(DrawingContext context, bool darkBackground)
    {
        var size = Bitmap.Size;
        var source = new Rect(Crop.X * size.Width, Crop.Y * size.Height, Crop.Width * size.Width, Crop.Height * size.Height);
        using (context.PushTransform(Placement))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(Bitmap, source, Frame);
    }

    public override bool HitTest(Point point, double radius) => Frame.Inflate(radius / Scale).Contains(ToLocal(point));

    /// <summary>The eraser removes what is drawn over a picture, never the picture itself.</summary>
    public override bool IsErasable => false;

    public override InkElement Transform(Matrix transform)
    {
        var placement = Placement * transform;
        return IsTranslation(placement)
            ? new ImageElement(Bitmap, Frame.Translate(Offset(placement)), Crop, Matrix.Identity, _png)
            : new ImageElement(Bitmap, Frame, Crop, placement, _png);
    }
}
