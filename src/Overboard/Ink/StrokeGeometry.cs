using Avalonia;
using Avalonia.Media;

namespace Overboard.Ink;

/// <summary>
/// Turns a stroke into a filled outline: an offset curve on each side of the
/// points, joined by round caps. Filling an outline (rather than stroking a
/// line) is what allows the width to follow pen pressure.
/// </summary>
public static class StrokeGeometry
{
    public static Geometry Build(Stroke stroke)
    {
        var points = stroke.Points;
        var n = points.Count;
        if (n == 0) return new StreamGeometry();
        if (n == 1) return Dot(points[0], Radius(stroke, points[0].Pressure));

        var left = new Point[n];
        var right = new Point[n];
        double nx = 0, ny = 0;
        for (var i = 0; i < n; i++)
        {
            var prev = points[Math.Max(i - 1, 0)];
            var next = points[Math.Min(i + 1, n - 1)];
            var dx = next.X - prev.X;
            var dy = next.Y - prev.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            // Keep the previous normal when the direction is undefined.
            if (length > 1e-6)
            {
                nx = -dy / length;
                ny = dx / length;
            }
            var p = points[i];
            var r = Radius(stroke, p.Pressure);
            left[i] = new Point(p.X + nx * r, p.Y + ny * r);
            right[i] = new Point(p.X - nx * r, p.Y - ny * r);
        }

        var startRadius = Radius(stroke, points[0].Pressure);
        var endRadius = Radius(stroke, points[^1].Pressure);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            ctx.BeginFigure(left[0], true);
            CurveThrough(ctx, left);
            ctx.ArcTo(right[^1], new Size(endRadius, endRadius), 0, false, SweepDirection.CounterClockwise);
            Array.Reverse(right);
            CurveThrough(ctx, right);
            ctx.ArcTo(left[0], new Size(startRadius, startRadius), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return geometry;
    }

    private static double Radius(Stroke stroke, double pressure)
    {
        var factor = stroke.UsesPressure ? Math.Clamp(0.25 + 1.5 * pressure, 0.25, 1.75) : 1;
        return Math.Max(stroke.Size * factor / 2, 0.5);
    }

    private static Geometry Dot(InkPoint p, double r) => new EllipseGeometry(new Rect(p.X - r, p.Y - r, r * 2, r * 2));

    /// <summary>Quadratic curves through the midpoints: smooth, and passes near every point.</summary>
    private static void CurveThrough(StreamGeometryContext ctx, Point[] points)
    {
        for (var i = 1; i < points.Length - 1; i++)
        {
            var mid = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
            ctx.QuadraticBezierTo(points[i], mid);
        }
        ctx.LineTo(points[^1]);
    }
}
