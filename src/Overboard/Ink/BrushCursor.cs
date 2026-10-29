using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Overboard.Ink;

/// <summary>An OS cursor showing the brush size and color: drawn by the system, so it never lags.</summary>
public static class BrushCursor
{
    /// <summary>Larger cursor bitmaps are rejected by some systems.</summary>
    private const double MaxDiameter = 240;

    public static Cursor Create(Color color, double size, double scaling)
    {
        var outline = IsLight(color) ? Color.FromArgb(166, 0, 0, 0) : Color.FromArgb(217, 255, 255, 255);
        return Circle(color, outline, size, scaling);
    }

    public static Cursor CreateEraser(double size, double scaling) =>
        Circle(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(200, 30, 30, 30), size, scaling);

    private static Cursor Circle(Color fill, Color outline, double size, double scaling)
    {
        var diameter = Math.Min(Math.Max(size, 4) * scaling, MaxDiameter);
        var box = (int)Math.Ceiling(diameter + 4 * scaling);
        var center = box / 2.0;

        var bitmap = new RenderTargetBitmap(new PixelSize(box, box));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.DrawEllipse(new SolidColorBrush(fill), new Pen(new SolidColorBrush(outline), 1.25 * scaling),
                new Point(center, center), diameter / 2, diameter / 2);
        }
        return new Cursor(bitmap, new PixelPoint(box / 2, box / 2));
    }

    private static bool IsLight(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 160;
}
