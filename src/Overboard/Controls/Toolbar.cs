using Overboard.Ink;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Overboard.Controls;

/// <summary>Floating tool palette, dragged by its background.</summary>
public sealed class Toolbar : Border
{
    private static readonly (Tool Tool, string Tip, string Icon)[] Tools =
    [
        (Tool.Pen, "Stylo (P)", "M4 20 L5 15 L15 5 L19 9 L9 19 Z M13 7 L17 11"),
        (Tool.Highlighter, "Surligneur (S)", "M14 4 L20 10 L12 18 L6 12 Z M6 12 L4 18 L6 20 L12 18 M3 21 L5 19"),
        (Tool.Eraser, "Gomme (G)", "M8 20 L3 15 L13 5 L20 12 L12 20 Z M12 20 H21 M8 10 L15 17"),
        (Tool.Text, "Texte (T)", "M5 7 V4 H19 V7 M12 4 V20 M9 20 H15"),
        (Tool.Line, "Ligne (L)", "M5 19 L19 5"),
        (Tool.Arrow, "Flèche (F)", "M5 19 L19 5 M10 5 H19 V14"),
        (Tool.Rectangle, "Rectangle (R)", "M4 6 H20 V18 H4 Z"),
        (Tool.Ellipse, "Ellipse (E)", "M3 12 A9 7 0 1 0 21 12 A9 7 0 1 0 3 12 Z"),
    ];

    private readonly ToolSettings _settings;
    private readonly Dictionary<Tool, ToolbarButton> _toolButtons = [];
    private readonly List<(Color Color, ToolbarButton Button, Ellipse Swatch)> _swatches = [];
    private readonly TextBlock _sizeLabel;
    private readonly StackPanel _sections;
    private readonly TranslateTransform _offset = new();
    private Point? _dragOrigin;

    /// <param name="closeTip">Tooltip of the close button, or null for no close button.</param>
    public Toolbar(ToolSettings settings, string? closeTip = null)
    {
        _settings = settings;

        var tools = new UniformGrid { Columns = 2 };
        foreach (var (tool, tip, icon) in Tools)
        {
            var button = new ToolbarButton(Icon(icon), tip);
            button.Click += () => _settings.Tool = tool;
            _toolButtons[tool] = button;
            tools.Children.Add(button);
        }

        var colors = new UniformGrid { Columns = 2 };
        for (var i = 0; i < ToolSettings.Palette.Length; i++)
        {
            var color = ToolSettings.Palette[i];
            var swatch = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                StrokeThickness = 1,
            };
            var button = new ToolbarButton(swatch, $"Couleur ({i + 1})");
            button.Click += () => _settings.Color = color;
            _swatches.Add((color, button, swatch));
            colors.Children.Add(button);
        }

        var smaller = new ToolbarButton(Icon("M7 12 H17"), "Plus fin (−)") { Width = 24 };
        smaller.Click += () => _settings.Grow(-1);
        var larger = new ToolbarButton(Icon("M7 12 H17 M12 7 V17"), "Plus épais (+)") { Width = 24 };
        larger.Click += () => _settings.Grow(1);
        _sizeLabel = new TextBlock
        {
            Foreground = ToolbarButton.Foreground,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var size = new DockPanel { Children = { smaller, larger, _sizeLabel } };
        DockPanel.SetDock(smaller, Dock.Left);
        DockPanel.SetDock(larger, Dock.Right);
        ToolTip.SetTip(size, "Taille (molette)");

        var undo = new ToolbarButton(Icon("M9 14 L4 9 L9 4 M4 9 H14 A5.5 5.5 0 0 1 14 20 H11"), "Annuler (Ctrl+Z)");
        undo.Click += () => UndoRequested?.Invoke();
        var redo = new ToolbarButton(Icon("M15 14 L20 9 L15 4 M20 9 H10 A5.5 5.5 0 0 0 10 20 H13"), "Rétablir (Ctrl+Y)");
        redo.Click += () => RedoRequested?.Invoke();
        var clear = new ToolbarButton(Icon("M4 7 H20 M10 11 V17 M14 11 V17 M6 7 L7 20 H17 L18 7 M9 7 V4 H15 V7"), "Tout effacer (Suppr)");
        clear.Click += () => ClearRequested?.Invoke();
        var actions = new UniformGrid { Columns = 2, Children = { undo, redo, clear } };
        if (closeTip != null) actions.Children.Add(Button("M6 6 L18 18 M18 6 L6 18", closeTip, () => CloseRequested?.Invoke()));

        _sections = new StackPanel { Children = { tools, Separator(), colors, Separator(), size, Separator(), actions } };
        Child = _sections;
        Background = new SolidColorBrush(Color.FromArgb(240, 24, 24, 27));
        BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(5);
        Margin = new Thickness(16);
        BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000");
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        RenderTransform = _offset;
        Cursor = new Cursor(StandardCursorType.Arrow);
        Update();
    }

    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? ClearRequested;
    public event Action? CloseRequested;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += Update;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settings.Changed -= Update;
    }

    private void Update()
    {
        foreach (var (tool, button) in _toolButtons) button.IsSelected = tool == _settings.Tool;
        foreach (var (color, button, _) in _swatches) button.IsSelected = color == _settings.Color;
        _sizeLabel.Text = _settings.Size.ToString("0");
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Parent is not Visual parent || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(parent);
        _dragOrigin = new Point(position.X - _offset.X, position.Y - _offset.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragOrigin is not { } origin || Parent is not Visual parent) return;
        var position = e.GetPosition(parent);
        _offset.X = position.X - origin.X;
        _offset.Y = position.Y - origin.Y;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => _dragOrigin = null;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _settings.Grow(Math.Sign(e.Delta.Y));
        e.Handled = true;
    }

    /// <summary>Shows the swatches in the colors painted on a dark or light board.</summary>
    public bool DarkBackground
    {
        set
        {
            foreach (var (color, _, swatch) in _swatches) swatch.Fill = new SolidColorBrush(ToolSettings.Display(color, value));
        }
    }

    /// <summary>Appends a group of buttons, two per row.</summary>
    public void AddSection(params ToolbarButton[] buttons)
    {
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.AddRange(buttons);
        _sections.Children.Add(Separator());
        _sections.Children.Add(grid);
    }

    /// <summary>A button with a line icon (path data on a 24×24 grid).</summary>
    public static ToolbarButton Button(string icon, string tip, Action click)
    {
        var button = new ToolbarButton(Icon(icon), tip);
        button.Click += click;
        return button;
    }

    /// <summary>A line icon drawn on a 24×24 grid.</summary>
    private static Avalonia.Controls.Shapes.Path Icon(string data) => new()
    {
        Data = Geometry.Parse(data),
        Width = 24,
        Height = 24,
        Stroke = ToolbarButton.Foreground,
        StrokeThickness = 1.75,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    };

    private static Border Separator() => new()
    {
        Height = 1,
        Margin = new Thickness(4, 5),
        Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
    };
}

/// <summary>A flat button that never takes keyboard focus, so shortcuts keep working.</summary>
public sealed class ToolbarButton : Border
{
    public static readonly IBrush Foreground = new SolidColorBrush(Color.Parse("#e4e4e7"));
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.Parse("#2563eb"));

    private bool _selected;
    private bool _hovered;
    private bool _pressed;

    public ToolbarButton(Control content, string tip)
    {
        Width = 38;
        Height = 38;
        Margin = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Background = Brushes.Transparent;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        content.IsHitTestVisible = false;
        Child = content;
        ToolTip.SetTip(this, tip);
        ToolTip.SetPlacement(this, PlacementMode.Right);
    }

    public event Action? Click;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateBackground();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        UpdateBackground();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        UpdateBackground();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        e.Handled = true;
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke();
    }

    private void UpdateBackground() =>
        Background = _selected ? SelectedBackground : _hovered ? HoverBackground : Brushes.Transparent;
}
