using Avalonia.Media;

namespace Overboard.Ink;

public enum Tool
{
    Pen,
    Highlighter,
    Eraser,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
}

/// <summary>Current tool, color and size, shared by every overlay.</summary>
public sealed class ToolSettings
{
    public static readonly Color[] Palette =
    [
        Color.Parse("#e03131"),
        Color.Parse("#1971c2"),
        Color.Parse("#2f9e44"),
        Color.Parse("#f5b700"),
        Color.Parse("#1e1e1e"),
        Color.Parse("#ffffff"),
    ];

    public const double MinSize = 1;
    public const double MaxSize = 80;

    private Tool _tool = Tool.Pen;
    private Color _color = Palette[0];
    private double _size = 4;

    public event Action? Changed;

    public Tool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            Changed?.Invoke();
        }
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            Changed?.Invoke();
        }
    }

    public double Size
    {
        get => _size;
        set
        {
            var size = Math.Clamp(value, MinSize, MaxSize);
            if (size == _size) return;
            _size = size;
            Changed?.Invoke();
        }
    }

    public void Grow(int direction)
    {
        var step = Size < 10 ? 1 : 2;
        Size += direction * step;
    }

    public double HighlighterSize => Math.Clamp(Size * 4, 12, 160);
    public double EraserSize => Math.Clamp(Size * 3, 12, 160);
    public double FontSize => 12 + Size * 2.5;
}
