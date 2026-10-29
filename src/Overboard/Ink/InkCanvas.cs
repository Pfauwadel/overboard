using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Overboard.Ink;

/// <summary>A drawing surface: pointer input for every tool, rendering, undo and redo.</summary>
public sealed class InkCanvas : Control
{
    private const double HighlighterOpacity = 0.4;
    private const double MinShapeLength = 3;

    private readonly ToolSettings _settings;
    // Snapshots of the element list: elements are immutable, so copying references is enough.
    private readonly Stack<List<InkElement>> _undo = new();
    private readonly Stack<List<InkElement>> _redo = new();
    private List<InkElement> _elements = [];

    // Gesture in progress: the tool it started with, even if the tool changes meanwhile.
    private Tool? _gesture;
    private Stroke? _stroke;
    private double _strokeOpacity;
    private Point _start;
    private Point _end;
    private Point _lastErasePoint;
    private List<InkElement>? _beforeErase;
    private InkElement? _preview;

    private TextBox? _editor;
    private Point _editorPosition;
    private Color _editorColor;
    private double _editorFontSize;
    private Cursor? _cursor;

    public InkCanvas(ToolSettings settings)
    {
        _settings = settings;
        Focusable = true;
        ClipToBounds = true;
    }

    public bool IsEditingText => _editor != null;

    public void Undo()
    {
        CommitText();
        if (!_undo.TryPop(out var previous)) return;
        _redo.Push(_elements);
        _elements = previous;
        InvalidateVisual();
    }

    public void Redo()
    {
        CommitText();
        if (!_redo.TryPop(out var next)) return;
        _undo.Push(_elements);
        _elements = next;
        InvalidateVisual();
    }

    /// <summary>Erases everything, as one undoable step.</summary>
    public void Clear()
    {
        CommitText();
        if (_elements.Count == 0) return;
        PushUndo();
        _elements.Clear();
        InvalidateVisual();
    }

    /// <summary>Erases everything, history included.</summary>
    public void Reset()
    {
        CancelText();
        _elements = [];
        _undo.Clear();
        _redo.Clear();
        _gesture = null;
        _stroke = null;
        _preview = null;
        _beforeErase = null;
        InvalidateVisual();
    }

    private void PushUndo()
    {
        _undo.Push([.. _elements]);
        _redo.Clear();
    }

    private void Commit(InkElement element)
    {
        PushUndo();
        _elements.Add(element);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += UpdateCursor;
        UpdateCursor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settings.Changed -= UpdateCursor;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (e.Source != this || !point.Properties.IsLeftButtonPressed) return;
        CommitText();
        e.Handled = true;

        var position = point.Position;
        var tool = _settings.Tool;
        switch (tool)
        {
            case Tool.Text:
                StartText(position);
                return;
            case Tool.Pen:
                StartStroke(new Stroke(_settings.Color, _settings.Size, e.Pointer.Type == PointerType.Pen), 1, point);
                break;
            case Tool.Highlighter:
                StartStroke(new Stroke(_settings.Color, _settings.HighlighterSize, usesPressure: false), HighlighterOpacity, point);
                break;
            case Tool.Eraser:
                _beforeErase = [.. _elements];
                _lastErasePoint = position;
                EraseAlong(position, position);
                break;
            default:
                _start = position;
                _gesture = tool;
                _preview = ShapeTo(position, e.KeyModifiers);
                break;
        }
        _gesture = tool;
        Focus();
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    private void StartStroke(Stroke stroke, double opacity, PointerPoint point)
    {
        _stroke = stroke;
        _strokeOpacity = opacity;
        stroke.Add(point.Position, point.Properties.Pressure);
        _preview = new StrokeElement(stroke, opacity);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        switch (_gesture)
        {
            case null:
                return;
            case Tool.Pen or Tool.Highlighter:
                // Intermediate points: every sample since the last event, not one per frame.
                foreach (var point in e.GetIntermediatePoints(this))
                    _stroke!.Add(point.Position, point.Properties.Pressure);
                _preview = new StrokeElement(_stroke!, _strokeOpacity);
                break;
            case Tool.Eraser:
                foreach (var point in e.GetIntermediatePoints(this))
                {
                    EraseAlong(_lastErasePoint, point.Position);
                    _lastErasePoint = point.Position;
                }
                break;
            default:
                _preview = ShapeTo(e.GetPosition(this), e.KeyModifiers);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_gesture is Tool.Pen or Tool.Highlighter) _stroke?.Finish(e.GetPosition(this));
        else if (_gesture is not (null or Tool.Eraser)) _preview = ShapeTo(e.GetPosition(this), e.KeyModifiers);
        EndGesture();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndGesture();

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _settings.Grow(Math.Sign(e.Delta.Y));
        e.Handled = true;
    }

    private void EndGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        switch (gesture)
        {
            case null:
                return;
            case Tool.Pen or Tool.Highlighter:
                Commit(new StrokeElement(_stroke!, _strokeOpacity));
                break;
            case Tool.Eraser:
                if (_beforeErase != null && _beforeErase.Count != _elements.Count)
                {
                    _undo.Push(_beforeErase);
                    _redo.Clear();
                }
                break;
            default:
                if (Distance(_start, _end) >= MinShapeLength && _preview != null) Commit(_preview);
                break;
        }
        _stroke = null;
        _preview = null;
        _beforeErase = null;
        InvalidateVisual();
    }

    private ShapeElement ShapeTo(Point end, KeyModifiers modifiers)
    {
        var kind = _gesture switch
        {
            Tool.Line => ShapeKind.Line,
            Tool.Arrow => ShapeKind.Arrow,
            Tool.Rectangle => ShapeKind.Rectangle,
            _ => ShapeKind.Ellipse,
        };
        if (modifiers.HasFlag(KeyModifiers.Shift)) end = ShapeElement.Constrain(kind, _start, end);
        _end = end;
        return new ShapeElement(kind, _start, end, _settings.Color, _settings.Size);
    }

    /// <summary>Removes every element touched by the eraser moving from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private void EraseAlong(Point from, Point to)
    {
        var radius = _settings.EraserSize / 2;
        var steps = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / (radius / 2)));
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var point = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            _elements.RemoveAll(element => element.HitTest(point, radius));
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void StartText(Point position)
    {
        _editorColor = _settings.Color;
        _editorFontSize = _settings.FontSize;
        var brush = new SolidColorBrush(_editorColor);
        var editor = new TextBox
        {
            AcceptsReturn = true,
            Foreground = brush,
            CaretBrush = brush,
            FontFamily = TextElement.Typeface.FontFamily,
            FontWeight = TextElement.Typeface.Weight,
            FontSize = _editorFontSize,
            Padding = new Thickness(4, 2),
            MinHeight = 0,
            MinWidth = 24,
            BorderThickness = new Thickness(1),
        };
        // The Fluent theme paints its own background and border: keep only a faint outline.
        var outline = new SolidColorBrush(Color.FromArgb(140, 128, 128, 128));
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            editor.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            editor.Resources[key] = outline;
        editor.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);

        // Center the first line on the click.
        _editorPosition = new Point(position.X - 5, position.Y - _editorFontSize * 0.7 - 3);
        editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        editor.LostFocus += (_, _) => CommitText();
        editor.Loaded += (_, _) => editor.Focus();
        _editor = editor;
        LogicalChildren.Add(editor);
        VisualChildren.Add(editor);
        InvalidateMeasure();
    }

    private void RemoveEditor(TextBox editor)
    {
        VisualChildren.Remove(editor);
        LogicalChildren.Remove(editor);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _editor?.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _editor?.Arrange(new Rect(_editorPosition, _editor.DesiredSize));
        return finalSize;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter validates, Shift+Enter starts a new line.
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        CommitText();
    }

    /// <summary>Turns the text being typed into a text element.</summary>
    public void CommitText()
    {
        if (_editor is not { } editor) return;
        _editor = null;
        var text = editor.Text?.TrimEnd() ?? "";
        if (text.Length > 0)
        {
            // Place the element exactly where the editor displayed the text.
            var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            var origin = presenter?.TranslatePoint(default, this) ?? _editorPosition;
            Commit(new TextElement(text, origin, _editorColor, _editorFontSize));
        }
        RemoveEditor(editor);
        Focus();
    }

    private void CancelText()
    {
        if (_editor is not { } editor) return;
        _editor = null;
        RemoveEditor(editor);
    }

    public override void Render(DrawingContext context)
    {
        // A transparent fill makes the whole surface hit-testable.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        foreach (var element in _elements)
            element.Render(context);
        _preview?.Render(context);
    }

    private void UpdateCursor()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var color = _settings.Color;
        var previous = _cursor;
        _cursor = _settings.Tool switch
        {
            Tool.Pen => BrushCursor.Create(color, _settings.Size, scaling),
            Tool.Highlighter => BrushCursor.Create(Color.FromArgb((byte)(255 * HighlighterOpacity), color.R, color.G, color.B),
                _settings.HighlighterSize, scaling),
            Tool.Eraser => BrushCursor.CreateEraser(_settings.EraserSize, scaling),
            Tool.Text => new Cursor(StandardCursorType.Ibeam),
            _ => new Cursor(StandardCursorType.Cross),
        };
        Cursor = _cursor;
        previous?.Dispose();
    }
}
