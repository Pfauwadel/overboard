using Avalonia;
using Avalonia.Media;

namespace Overboard.Ink;

public readonly record struct InkPoint(double X, double Y, double Pressure);

/// <summary>A freehand stroke. Input points are smoothed as they are added.</summary>
/// <param name="precision">Minimum distance between two points, in the same units as the points.</param>
public sealed class Stroke(Color color, double size, bool usesPressure, double precision = 0.4)
{
    /// <summary>How far each new point lags behind the raw input (0 = none, 1 = never moves).</summary>
    private const double Streamline = 0.45;
    private const double PressureSmoothing = 0.6;

    private readonly List<InkPoint> _points = [];

    public Color Color { get; } = color;
    public double Size { get; } = size;
    /// <summary>Pen input varies the width with pressure; mouse strokes keep a constant width.</summary>
    public bool UsesPressure { get; } = usesPressure;
    public IReadOnlyList<InkPoint> Points => _points;

    /// <summary>A stroke whose points are already smoothed, as read from a file.</summary>
    public static Stroke FromPoints(Color color, double size, bool usesPressure, IEnumerable<InkPoint> points)
    {
        var stroke = new Stroke(color, size, usesPressure);
        stroke._points.AddRange(points);
        return stroke;
    }

    public void Add(Point position, double pressure)
    {
        if (!UsesPressure) pressure = 0.5;
        if (_points.Count == 0)
        {
            _points.Add(new InkPoint(position.X, position.Y, pressure));
            return;
        }
        var last = _points[^1];
        var x = last.X + (position.X - last.X) * (1 - Streamline);
        var y = last.Y + (position.Y - last.Y) * (1 - Streamline);
        var p = last.Pressure + (pressure - last.Pressure) * (1 - PressureSmoothing);
        if (Math.Abs(x - last.X) + Math.Abs(y - last.Y) < precision) return;
        _points.Add(new InkPoint(x, y, p));
    }

    /// <summary>Snaps the end of the stroke to where the pointer was released.</summary>
    public void Finish(Point position)
    {
        if (_points.Count == 0) return;
        var last = _points[^1];
        if (Math.Abs(position.X - last.X) + Math.Abs(position.Y - last.Y) < precision) return;
        _points.Add(last with { X = position.X, Y = position.Y });
    }
}
