using Overboard.Ink;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Overboard.Board;

/// <summary>The board's paper: a light or dark background, and a grid that follows the view.</summary>
public sealed class BoardBackground : Control
{
    /// <summary>Grid spacing in board units; halved or doubled to stay between the on-screen limits.</summary>
    private const double GridStep = 40;
    private const double MinScreenStep = 24;

    private static readonly IBrush LightPaper = new ImmutableSolidColorBrush(Color.Parse("#fafafa"));
    private static readonly IBrush DarkPaper = new ImmutableSolidColorBrush(Color.Parse("#1c1c1f"));
    private static readonly IPen LightLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(20, 0, 0, 0)), 1);
    private static readonly IPen DarkLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1);

    private readonly InkCanvas _canvas;
    private bool _dark;
    private bool _showGrid = true;

    public BoardBackground(InkCanvas canvas)
    {
        _canvas = canvas;
        canvas.ViewChanged += InvalidateVisual;
    }

    public static IBrush Paper(bool dark) => dark ? DarkPaper : LightPaper;

    public bool Dark
    {
        get => _dark;
        set
        {
            _dark = value;
            InvalidateVisual();
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(_dark ? DarkPaper : LightPaper, new Rect(size));
        if (!_showGrid) return;

        var step = GridStep * _canvas.Zoom;
        while (step < MinScreenStep) step *= 2;
        while (step >= MinScreenStep * 2) step /= 2;
        var pen = _dark ? DarkLine : LightLine;
        for (var x = Modulo(_canvas.Offset.X, step); x < size.Width; x += step)
            context.DrawLine(pen, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, size.Height));
        for (var y = Modulo(_canvas.Offset.Y, step); y < size.Height; y += step)
            context.DrawLine(pen, new Point(0, Math.Round(y) + 0.5), new Point(size.Width, Math.Round(y) + 0.5));
    }

    private static double Modulo(double value, double step) => (value % step + step) % step;
}
