using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

namespace Overboard.Ink;

/// <summary>
/// A drawing surface: pointer input for every tool, rendering, undo and redo.
/// Elements live in board coordinates; the view (offset and zoom) maps them to
/// the control, and can be moved and zoomed when <see cref="CanNavigate"/> is set.
/// Sizes chosen in the tool settings are on-screen sizes, whatever the zoom.
/// </summary>
public sealed class InkCanvas : Control
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 10;
    private const double HighlighterOpacity = 0.4;
    private const double MinShapeLength = 3;
    private const double StrokePrecision = 0.4;
    private const long LaserLifetime = 1000;

    private static readonly Color LaserColor = Color.Parse("#ff2d2d");
    private static readonly IBrush SpotlightVeil = new ImmutableSolidColorBrush(Color.FromArgb(170, 0, 0, 0));

    private readonly ToolSettings _settings;
    // Snapshots of the element list: elements are immutable, so copying references is enough.
    private readonly Stack<List<InkElement>> _undo = new();
    private readonly Stack<List<InkElement>> _redo = new();
    private List<InkElement> _elements = [];

    // Board point shown at the top-left corner, scaled: screen = board × zoom + offset.
    private Vector _offset;
    private double _zoom = 1;
    private Point? _panFrom;
    private bool _panKeyHeld;
    private bool _darkBackground;
    private Rect? _pendingArea;
    // Last pointer position in control coordinates, where the spotlight shines.
    private Point? _pointer;

    // Laser trails fade point by point: each point lives LaserLifetime ms. Board coordinates.
    private readonly List<List<LaserPoint>> _laserTrails = [];
    private bool _laserFrameRequested;

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
    private readonly Cursor _panCursor = new(StandardCursorType.SizeAll);

    public InkCanvas(ToolSettings settings)
    {
        _settings = settings;
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Whether the view can be moved (Space or middle/right button drag) and zoomed (Ctrl+wheel).</summary>
    public bool CanNavigate { get; init; }

    public bool IsEditingText => _editor != null;
    public IReadOnlyList<InkElement> Elements => _elements;
    public Vector Offset => _offset;
    public double Zoom => _zoom;

    /// <summary>Raised when the user changes the elements: drawing, erasing, undo, redo or clearing.</summary>
    public event Action? Changed;

    /// <summary>Raised when the view moves or zooms.</summary>
    public event Action? ViewChanged;

    public bool DarkBackground
    {
        get => _darkBackground;
        set
        {
            if (_darkBackground == value) return;
            _darkBackground = value;
            InvalidateVisual();
            UpdateCursor();
        }
    }

    /// <summary>While held, the left button moves the view instead of drawing.</summary>
    public bool PanKeyHeld
    {
        get => _panKeyHeld;
        set
        {
            if (!CanNavigate || _panKeyHeld == value) return;
            _panKeyHeld = value;
            ApplyCursor();
        }
    }

    public Point ToBoard(Point point) => new((point.X - _offset.X) / _zoom, (point.Y - _offset.Y) / _zoom);

    public void SetView(Vector offset, double zoom)
    {
        CommitText();
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (offset == _offset && zoom == _zoom) return;
        _offset = offset;
        _zoom = zoom;
        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Zooms by <paramref name="factor"/>, keeping the board point under <paramref name="anchor"/> in place.</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        var board = ToBoard(anchor);
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        SetView(new Vector(anchor.X - board.X * zoom, anchor.Y - board.Y * zoom), zoom);
    }

    public void ZoomBy(double factor) => ZoomAt(new Rect(Bounds.Size).Center, factor);

    /// <summary>The area covered by every element, if any.</summary>
    public Rect? ContentBounds => _elements.Count == 0 ? null : _elements.Select(e => e.Bounds).Aggregate((a, b) => a.Union(b));

    /// <summary>Shows every element, without enlarging beyond 100 %.</summary>
    public void FitContent()
    {
        if (ContentBounds is { } content) ShowArea(content);
        else SetView(default, 1);
    }

    /// <summary>Centers <paramref name="area"/> (board coordinates), zoomed out if needed to fit, never enlarged beyond 100 %.</summary>
    public void ShowArea(Rect area)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            // Not laid out yet: done on the first size change.
            _pendingArea = area;
            return;
        }
        const double margin = 48;
        var zoom = Math.Min(1, Math.Min(
            Math.Max(size.Width - 2 * margin, 1) / Math.Max(area.Width, 1),
            Math.Max(size.Height - 2 * margin, 1) / Math.Max(area.Height, 1)));
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        SetView(new Vector(size.Width / 2 - area.Center.X * zoom, size.Height / 2 - area.Center.Y * zoom), zoom);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_pendingArea is not { } area) return;
        _pendingArea = null;
        ShowArea(area);
    }

    /// <summary>Adds elements as one undoable step.</summary>
    public void Add(IReadOnlyCollection<InkElement> elements)
    {
        CommitText();
        if (elements.Count == 0) return;
        PushUndo();
        _elements.AddRange(elements);
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Undo()
    {
        CommitText();
        if (!_undo.TryPop(out var previous)) return;
        _redo.Push(_elements);
        _elements = previous;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Redo()
    {
        CommitText();
        if (!_redo.TryPop(out var next)) return;
        _undo.Push(_elements);
        _elements = next;
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Erases everything, as one undoable step.</summary>
    public void Clear()
    {
        CommitText();
        if (_elements.Count == 0) return;
        PushUndo();
        _elements.Clear();
        InvalidateVisual();
        Changed?.Invoke();
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
        _laserTrails.Clear();
        InvalidateVisual();
    }

    /// <summary>Replaces every element, history included.</summary>
    public void Load(IEnumerable<InkElement> elements)
    {
        Reset();
        _elements = [.. elements];
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
        Changed?.Invoke();
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
        if (e.Source != this || _gesture != null || _panFrom != null) return;
        var buttons = point.Properties;
        if (CanNavigate && (buttons.IsMiddleButtonPressed || buttons.IsRightButtonPressed || (_panKeyHeld && buttons.IsLeftButtonPressed)))
        {
            CommitText();
            _panFrom = point.Position;
            ApplyCursor();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!buttons.IsLeftButtonPressed) return;
        CommitText();
        e.Handled = true;

        var position = ToBoard(point.Position);
        var tool = _settings.Tool;
        switch (tool)
        {
            case Tool.Text:
                StartText(point.Position);
                return;
            case Tool.Pen:
                StartStroke(new Stroke(_settings.Color, _settings.Size / _zoom, e.Pointer.Type == PointerType.Pen, StrokePrecision / _zoom),
                    1, point);
                break;
            case Tool.Highlighter:
                StartStroke(new Stroke(_settings.Color, _settings.HighlighterSize / _zoom, usesPressure: false, StrokePrecision / _zoom),
                    HighlighterOpacity, point);
                break;
            case Tool.Eraser:
                _beforeErase = [.. _elements];
                _lastErasePoint = position;
                EraseAlong(position, position);
                break;
            case Tool.Laser:
                _laserTrails.Add([new LaserPoint(position, Environment.TickCount64)]);
                RequestLaserFrame();
                break;
            case Tool.Spotlight:
                return;
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
        stroke.Add(ToBoard(point.Position), point.Properties.Pressure);
        _preview = new StrokeElement(stroke, opacity);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _pointer = e.GetPosition(this);
        if (_settings.Tool == Tool.Spotlight) InvalidateVisual();
        if (_panFrom is { } from)
        {
            var position = e.GetPosition(this);
            _panFrom = position;
            SetView(new Vector(_offset.X + position.X - from.X, _offset.Y + position.Y - from.Y), _zoom);
            return;
        }
        switch (_gesture)
        {
            case null:
                return;
            case Tool.Pen or Tool.Highlighter:
                // Intermediate points: every sample since the last event, not one per frame.
                foreach (var point in e.GetIntermediatePoints(this))
                    _stroke!.Add(ToBoard(point.Position), point.Properties.Pressure);
                _preview = new StrokeElement(_stroke!, _strokeOpacity);
                break;
            case Tool.Eraser:
                foreach (var point in e.GetIntermediatePoints(this))
                {
                    var position = ToBoard(point.Position);
                    EraseAlong(_lastErasePoint, position);
                    _lastErasePoint = position;
                }
                break;
            case Tool.Laser:
                foreach (var point in e.GetIntermediatePoints(this))
                    _laserTrails[^1].Add(new LaserPoint(ToBoard(point.Position), Environment.TickCount64));
                RequestLaserFrame();
                break;
            default:
                _preview = ShapeTo(ToBoard(e.GetPosition(this)), e.KeyModifiers);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_panFrom != null)
        {
            EndPan();
            e.Pointer.Capture(null);
            return;
        }
        var position = ToBoard(e.GetPosition(this));
        if (_gesture is Tool.Pen or Tool.Highlighter) _stroke?.Finish(position);
        else if (_gesture is not (null or Tool.Eraser)) _preview = ShapeTo(position, e.KeyModifiers);
        EndGesture();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndPan();
        EndGesture();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        if (_settings.Tool == Tool.Spotlight) InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (CanNavigate && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            ZoomAt(e.GetPosition(this), Math.Pow(1.2, e.Delta.Y));
        else
            _settings.Grow(Math.Sign(e.Delta.Y));
        e.Handled = true;
    }

    private void EndPan()
    {
        if (_panFrom == null) return;
        _panFrom = null;
        ApplyCursor();
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
                    Changed?.Invoke();
                }
                break;
            case Tool.Laser:
                // The trail fades by itself.
                break;
            default:
                if (Distance(_start, _end) * _zoom >= MinShapeLength && _preview != null) Commit(_preview);
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
        return new ShapeElement(kind, _start, end, _settings.Color, _settings.Size / _zoom);
    }

    /// <summary>Removes every element touched by the eraser moving from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private void EraseAlong(Point from, Point to)
    {
        var radius = _settings.EraserSize / 2 / _zoom;
        var steps = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / (radius / 2)));
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var point = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            _elements.RemoveAll(element => element.Bounds.Inflate(radius).Contains(point) && element.HitTest(point, radius));
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Opens a text box at <paramref name="position"/>, in control coordinates.</summary>
    private void StartText(Point position)
    {
        _editorColor = _settings.Color;
        _editorFontSize = _settings.FontSize;
        var brush = new SolidColorBrush(ToolSettings.Display(_editorColor, _darkBackground));
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
        // Enter and Escape validate, Shift+Enter starts a new line.
        if (e.Key == Key.Escape || (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            e.Handled = true;
            CommitText();
        }
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
            Commit(new TextElement(text, ToBoard(origin), _editorColor, _editorFontSize / _zoom));
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
        var visible = new Rect(ToBoard(default), ToBoard(new Point(Bounds.Width, Bounds.Height)));
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset)))
        {
            foreach (var element in _elements)
                if (element.Bounds.Intersects(visible))
                    element.Render(context, _darkBackground);
            _preview?.Render(context, _darkBackground);
            RenderLaser(context);
        }
        if (_settings.Tool == Tool.Spotlight) RenderSpotlight(context);
    }

    /// <summary>A red glow with a bright core, thinning and fading along the trail.</summary>
    private void RenderLaser(DrawingContext context)
    {
        var now = Environment.TickCount64;
        foreach (var trail in _laserTrails)
        {
            for (var i = 1; i < trail.Count; i++)
            {
                var life = 1 - (double)(now - trail[i - 1].Time) / LaserLifetime;
                if (life <= 0) continue;
                var (from, to) = (trail[i - 1].Position, trail[i].Position);
                context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(LaserColor, 0.3 * life), (6 + 10 * life) / _zoom,
                    lineCap: PenLineCap.Round), from, to);
                context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(LaserColor, life), (2 + 3 * life) / _zoom,
                    lineCap: PenLineCap.Round), from, to);
            }
        }
    }

    /// <summary>Redraws until every laser trail has faded.</summary>
    private void RequestLaserFrame()
    {
        InvalidateVisual();
        if (_laserFrameRequested || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _laserFrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _laserFrameRequested = false;
            var oldest = Environment.TickCount64 - LaserLifetime;
            foreach (var trail in _laserTrails) trail.RemoveAll(p => p.Time < oldest);
            // The trail being drawn stays, even faded out: the next move continues it.
            var active = _gesture == Tool.Laser ? _laserTrails[^1] : null;
            _laserTrails.RemoveAll(trail => trail.Count == 0 && trail != active);
            if (_laserTrails.Any(trail => trail.Count > 0)) RequestLaserFrame();
            else InvalidateVisual();
        });
    }

    /// <summary>Darkens everything but a circle around the pointer.</summary>
    private void RenderSpotlight(DrawingContext context)
    {
        var all = new RectangleGeometry(new Rect(Bounds.Size));
        if (_pointer is not { } p)
        {
            context.DrawGeometry(SpotlightVeil, null, all);
            return;
        }
        var r = _settings.SpotlightRadius;
        var spot = new EllipseGeometry(new Rect(p.X - r, p.Y - r, r * 2, r * 2));
        context.DrawGeometry(SpotlightVeil, null, new CombinedGeometry(GeometryCombineMode.Exclude, all, spot));
    }

    private void UpdateCursor()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var color = ToolSettings.Display(_settings.Color, _darkBackground);
        var previous = _cursor;
        _cursor = _settings.Tool switch
        {
            Tool.Pen => BrushCursor.Create(color, _settings.Size, scaling),
            Tool.Highlighter => BrushCursor.Create(Color.FromArgb((byte)(255 * HighlighterOpacity), color.R, color.G, color.B),
                _settings.HighlighterSize, scaling),
            Tool.Eraser => BrushCursor.CreateEraser(_settings.EraserSize, scaling),
            Tool.Text => new Cursor(StandardCursorType.Ibeam),
            Tool.Laser => BrushCursor.Create(LaserColor, 12, scaling),
            // The bright circle is the cursor.
            Tool.Spotlight => new Cursor(StandardCursorType.None),
            _ => new Cursor(StandardCursorType.Cross),
        };
        ApplyCursor();
        previous?.Dispose();
        // The spotlight appears, disappears or resizes with the tool settings.
        InvalidateVisual();
    }

    private void ApplyCursor() => Cursor = _panFrom != null || _panKeyHeld ? _panCursor : _cursor;

    private readonly record struct LaserPoint(Point Position, long Time);
}
