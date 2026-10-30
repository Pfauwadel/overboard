using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    // Selection, and the selection gesture in progress: a rubber band or a move. Board coordinates.
    private readonly HashSet<InkElement> _selection = [];
    private Point _selectFrom;
    private Rect? _band;
    private HashSet<InkElement> _bandKeeps = [];
    private bool _moving;
    private Vector _moveOffset;
    // Resizing or rotating the selection: the handle held, the frame when it started, and the elements as they would become.
    private Handle _handle;
    private SelectionFrame _handleFrame;
    private Matrix _handleTransform = Matrix.Identity;
    private Dictionary<InkElement, InkElement>? _transformed;
    // Cropping a picture: the picture, the part of it kept (in its own coordinates), and the gesture in progress.
    private ImageElement? _cropping;
    private Rect _cropFrame;
    private Rect _cropFrom;
    private Handle _cropHandle;
    private bool _cropDragging;
    // Double-clicked with the selection tool: cropped (pictures) or edited (text) on release, unless dragged.
    private InkElement? _doubleClicked;
    // What the pointer would grab with the selection tool: an element, a handle; null for nothing.
    private Cursor? _hoverCursor;
    private static readonly Dictionary<StandardCursorType, Cursor> HandleCursors = [];
    // Shared by every canvas: copy on the screen, paste on the board.
    private static IReadOnlyList<InkElement> _clipboard = [];
    // What Copy last put on the system clipboard: while it is still there, pasting uses _clipboard.
    private static IAsyncDataTransfer? _systemClipboard;

    private TextBox? _editor;
    // Text being changed in the editor: hidden meanwhile.
    private TextElement? _editing;
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
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>The background of pictures made from the canvas (export, copy); transparent if null.</summary>
    public IBrush? Paper { get; set; }

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
        EndCrop(keep: false);
        if (!_undo.TryPop(out var previous)) return;
        _redo.Push(_elements);
        _elements = previous;
        _selection.Clear();
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Redo()
    {
        CommitText();
        EndCrop(keep: false);
        if (!_redo.TryPop(out var next)) return;
        _undo.Push(_elements);
        _elements = next;
        _selection.Clear();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Erases everything, as one undoable step.</summary>
    public void Clear()
    {
        CommitText();
        EndCrop(keep: false);
        if (_elements.Count == 0) return;
        PushUndo();
        _elements.Clear();
        _selection.Clear();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Erases everything, history included.</summary>
    public void Reset()
    {
        CancelText();
        _elements = [];
        _selection.Clear();
        _band = null;
        _moving = false;
        _moveOffset = default;
        _handle = Handle.None;
        _handleTransform = Matrix.Identity;
        _transformed = null;
        _cropping = null;
        _cropDragging = false;
        _doubleClicked = null;
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
                // On existing text: edits it.
                StartText(point.Position, ElementAt(position) as TextElement);
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
            case Tool.Select:
                StartSelect(position, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.ClickCount);
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

    public bool HasSelection => _selection.Count > 0;

    /// <summary>The topmost element under <paramref name="point"/>, with a few pixels of tolerance.</summary>
    private InkElement? ElementAt(Point point)
    {
        var radius = 6 / _zoom;
        for (var i = _elements.Count - 1; i >= 0; i--)
        {
            var element = _elements[i];
            if (element.Bounds.Inflate(radius).Contains(point) && element.HitTest(point, radius)) return element;
        }
        return null;
    }

    /// <summary>
    /// A handle resizes or rotates the selection, or moves an end of a line. Clicking an element selects it
    /// (Shift: adds or removes it) and starts moving the selection; double-clicking a picture crops it,
    /// double-clicking text edits it. Clicking elsewhere starts a rubber band, which selects the elements it fully encloses.
    /// </summary>
    private void StartSelect(Point position, bool adding, int clicks)
    {
        _selectFrom = position;
        _moveOffset = default;
        if (_cropping != null && StartCropGesture(position, clicks)) return;
        // Handles first, even with Shift held: it keeps proportions or snaps the angle.
        if (HandleAt(position) is var handle and not Handle.None)
        {
            _handle = handle;
            _handleFrame = SelectionFrame.Of(Selected()) ?? default;
            return;
        }
        var hit = ElementAt(position);
        if (hit is null)
        {
            _bandKeeps = adding ? [.. _selection] : [];
            if (!adding) _selection.Clear();
            _band = new Rect(position, position);
            return;
        }
        if (adding && _selection.Remove(hit)) return;
        if (!adding && !_selection.Contains(hit)) _selection.Clear();
        _selection.Add(hit);
        _moving = true;
        // Unless the second click drags it: a click then a quick move still moves.
        _doubleClicked = clicks == 2 && !adding && hit is ImageElement or TextElement ? hit : null;
    }

    /// <summary>The handle of the selection under <paramref name="position"/>: an end of a single line or arrow, else the frame's.</summary>
    private Handle HandleAt(Point position)
    {
        var selected = Selected();
        if (selected is [ShapeElement { IsLine: true } line])
            return SelectionFrame.Grabs(line.End, position, _zoom) ? Handle.End
                : SelectionFrame.Grabs(line.Start, position, _zoom) ? Handle.Start
                : Handle.None;
        return SelectionFrame.Of(selected) is { } frame ? frame.HandleAt(position, _zoom, rotate: true) : Handle.None;
    }

    /// <summary>Whether resizing by a corner keeps the proportions, before Shift: for pictures, text and several elements.</summary>
    private bool KeepsProportions => _selection.Count > 1 || _selection.Any(e => e.KeepsProportions);

    private void DragSelect(Point position, KeyModifiers modifiers)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (_cropDragging)
        {
            DragCrop(position);
        }
        else if (_handle is Handle.Start or Handle.End && Selected() is [ShapeElement line])
        {
            // Shift: 45° angles from the other end.
            var other = _handle == Handle.Start ? line.End : line.Start;
            var end = shift ? ShapeElement.Constrain(line.Kind, other, position) : position;
            _transformed = new() { [line] = _handle == Handle.Start ? line.WithEnds(end, line.End) : line.WithEnds(line.Start, end) };
        }
        else if (_handle != Handle.None)
        {
            var transform = _handle == Handle.Rotate
                ? _handleFrame.Rotate(_selectFrom, position, snap: shift)
                : _handleFrame.Resize(_handle, position, SelectionFrame.IsCorner(_handle) && KeepsProportions != shift, 8 / _zoom);
            _handleTransform = transform;
            _transformed = Selected().ToDictionary(e => e, e => e.Transform(transform));
        }
        else if (_band != null)
        {
            var band = new Rect(_selectFrom, position);
            _band = band;
            _selection.Clear();
            _selection.UnionWith(_bandKeeps);
            _selection.UnionWith(_elements.Where(e => band.Contains(e.Bounds)));
        }
        else if (_moving)
        {
            _moveOffset = position - _selectFrom;
        }
    }

    private void EndSelect()
    {
        var offset = _moveOffset;
        var replaced = _handle != Handle.None ? _transformed : null;
        var doubleClicked = _doubleClicked;
        _doubleClicked = null;
        _band = null;
        _moving = false;
        _moveOffset = default;
        _handle = Handle.None;
        _handleTransform = Matrix.Identity;
        _transformed = null;
        _cropDragging = false;
        if (offset != default) MoveSelection(offset);
        else if (replaced != null) ReplaceSelection(replaced);
        else if (doubleClicked is ImageElement image) StartCrop(image);
        else if (doubleClicked is TextElement text) StartText(default, text);
    }

    /// <summary>Shows what the selection tool would grab under <paramref name="position"/>: a handle, an element, the part of a picture kept.</summary>
    private void UpdateHover(Point position)
    {
        Cursor? cursor;
        if (_cropping is { } image)
        {
            var frame = new SelectionFrame(_cropFrame, image.Placement, Sides: true);
            var handle = frame.HandleAt(position, _zoom, rotate: false);
            cursor = handle != Handle.None ? HandleCursor(handle) : frame.Contains(position) ? _panCursor : null;
        }
        else
        {
            var handle = HandleAt(position);
            cursor = handle != Handle.None ? HandleCursor(handle) : ElementAt(position) != null ? _panCursor : null;
        }
        if (cursor == _hoverCursor) return;
        _hoverCursor = cursor;
        ApplyCursor();
    }

    private static Cursor HandleCursor(Handle handle)
    {
        var type = handle switch
        {
            Handle.TopLeft => StandardCursorType.TopLeftCorner,
            Handle.TopRight => StandardCursorType.TopRightCorner,
            Handle.BottomRight => StandardCursorType.BottomRightCorner,
            Handle.BottomLeft => StandardCursorType.BottomLeftCorner,
            Handle.Top => StandardCursorType.TopSide,
            Handle.Bottom => StandardCursorType.BottomSide,
            Handle.Left => StandardCursorType.LeftSide,
            Handle.Right => StandardCursorType.RightSide,
            Handle.Start or Handle.End => StandardCursorType.Cross,
            _ => StandardCursorType.Hand,
        };
        if (!HandleCursors.TryGetValue(type, out var cursor)) HandleCursors[type] = cursor = new Cursor(type);
        return cursor;
    }

    private void MoveSelection(Vector offset) => TransformSelection(Matrix.CreateTranslation(offset));

    private void TransformSelection(Matrix transform)
    {
        EndCrop(keep: true);
        ReplaceSelection(Selected().ToDictionary(e => e, e => e.Transform(transform)));
    }

    /// <summary>Replaces selected elements, in place, as one undoable step; the replacements are selected.</summary>
    private void ReplaceSelection(Dictionary<InkElement, InkElement> replacements)
    {
        PushUndo();
        for (var i = 0; i < _elements.Count; i++)
        {
            if (replacements.TryGetValue(_elements[i], out var replacement)) _elements[i] = replacement;
        }
        _selection.ExceptWith(replacements.Keys);
        _selection.UnionWith(replacements.Values);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Mirrors the selection across its middle: left and right, or top and bottom. Text stays readable.</summary>
    public void Flip(bool horizontal)
    {
        EndCrop(keep: true);
        if (SelectionFrame.Of(Selected()) is { } frame) TransformSelection(frame.Flip(horizontal));
    }

    /// <summary>Moves the selection by an on-screen distance (arrow keys).</summary>
    public void Nudge(Vector screenOffset)
    {
        if (HasSelection) MoveSelection(screenOffset / _zoom);
    }

    public bool IsCropping => _cropping != null;

    /// <summary>Whether the selection is a single picture, which can be cropped.</summary>
    public bool CanCrop => _cropping is null && _selection.Count == 1 && _selection.Single() is ImageElement;

    /// <summary>Crops the selected picture: its edges get handles, and what is cut off shows faded.</summary>
    public void StartCrop()
    {
        if (CanCrop) StartCrop((ImageElement)_selection.Single());
    }

    private void StartCrop(ImageElement image)
    {
        CommitText();
        _cropping = image;
        _cropFrame = image.Frame;
        _hoverCursor = null;
        ApplyCursor();
        InvalidateVisual();
    }

    /// <summary>
    /// While cropping: a handle moves the edges, inside moves the part kept. A double-click, or a click
    /// elsewhere, ends cropping. Returns whether the click was for cropping.
    /// </summary>
    private bool StartCropGesture(Point position, int clicks)
    {
        var frame = new SelectionFrame(_cropFrame, _cropping!.Placement, Sides: true);
        var handle = frame.HandleAt(position, _zoom, rotate: false);
        var inside = handle != Handle.None || frame.Contains(position);
        if (inside && clicks < 2)
        {
            _cropHandle = handle;
            _cropFrom = _cropFrame;
            _cropDragging = true;
            return true;
        }
        EndCrop(keep: true);
        return inside;
    }

    private void DragCrop(Point position)
    {
        var image = _cropping!;
        if (!image.Placement.TryInvert(out var toImage)) return;
        var full = image.FullFrame;
        var point = position.Transform(toImage);
        if (_cropHandle == Handle.None)
        {
            // The part kept slides over the picture, without leaving it.
            var moved = _cropFrom.Translate(point - _selectFrom.Transform(toImage));
            _cropFrame = moved.Translate(new Vector(
                Math.Clamp(moved.X, full.Left, full.Right - moved.Width) - moved.X,
                Math.Clamp(moved.Y, full.Top, full.Bottom - moved.Height) - moved.Y));
        }
        else
        {
            var min = 8 / _zoom / Math.Max(Math.Sqrt(Math.Abs(image.Placement.GetDeterminant())), 1e-12);
            _cropFrame = SelectionFrame.MoveSides(_cropFrom, _cropHandle, point, full, min);
        }
    }

    /// <summary>Ends cropping, keeping the new edges as one undoable step, or not.</summary>
    public void EndCrop(bool keep)
    {
        if (_cropping is not { } image) return;
        _cropping = null;
        _cropDragging = false;
        _hoverCursor = null;
        ApplyCursor();
        InvalidateVisual();
        if (keep && _cropFrame != image.Frame) Replace(image, image.WithFrame(_cropFrame));
    }

    public void SelectAll()
    {
        CommitText();
        EndCrop(keep: true);
        _settings.Tool = Tool.Select;
        _selection.UnionWith(_elements);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        EndCrop(keep: true);
        _selection.Clear();
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        EndCrop(keep: false);
        if (!HasSelection) return;
        PushUndo();
        _elements.RemoveAll(_selection.Contains);
        _selection.Clear();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>The selected elements, in drawing order.</summary>
    private List<InkElement> Selected() => [.. _elements.Where(_selection.Contains)];

    /// <summary>The selection if there is one, otherwise every element.</summary>
    public IReadOnlyList<InkElement> SelectedOrAll() => HasSelection ? Selected() : _elements;

    /// <summary>The selection (or everything) as a picture, as it looks on this canvas.</summary>
    public RenderTargetBitmap? RenderPicture()
    {
        var elements = SelectedOrAll();
        return elements.Count == 0 ? null : InkPicture.Render(elements, Paper, _darkBackground);
    }

    /// <summary>
    /// Copies the selection for pasting in Overboard, and as a picture on the system clipboard
    /// for pasting in other applications.
    /// </summary>
    public void Copy()
    {
        if (!HasSelection) return;
        _clipboard = Selected();
        _ = PutOnSystemClipboardAsync(InkPicture.Render(_clipboard, Paper, _darkBackground));
    }

    private async Task PutOnSystemClipboardAsync(Bitmap picture)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var item = new DataTransferItem();
        item.SetBitmap(picture);
        var data = new DataTransfer();
        data.Add(item);
        _systemClipboard = data;
        await clipboard.SetDataAsync(data);
    }

    public void Cut()
    {
        Copy();
        DeleteSelection();
    }

    /// <summary>
    /// Pastes, centered on the pointer (or the view), and selects what was pasted: the elements copied
    /// in Overboard if they are still what the system clipboard holds, otherwise a picture or picture files.
    /// </summary>
    public async void Paste()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var ownCopy = _systemClipboard != null && clipboard != null && await clipboard.TryGetInProcessDataAsync() == _systemClipboard;
        if (clipboard != null && !ownCopy)
        {
            if (await clipboard.TryGetBitmapAsync() is { } picture)
            {
                AddPictures([picture], PasteTarget());
                return;
            }
            if (await clipboard.TryGetFilesAsync() is { } files && InkPicture.Load(files) is { Count: > 0 } pictures)
            {
                AddPictures(pictures, PasteTarget());
                return;
            }
        }
        PasteElements();
    }

    /// <summary>Where pasted things go, in control coordinates: the pointer if it is over the canvas, else the middle.</summary>
    private Point PasteTarget()
    {
        var view = new Rect(Bounds.Size);
        return _pointer is { } p && view.Contains(p) ? p : view.Center;
    }

    private void PasteElements()
    {
        if (_clipboard.Count == 0) return;
        var copied = _clipboard.Select(e => e.Bounds).Aggregate((a, b) => a.Union(b));
        Vector offset = ToBoard(PasteTarget()) - copied.Center;
        // Pasting in place would hide the copy behind the original.
        if (Math.Abs(offset.X) * _zoom < 4 && Math.Abs(offset.Y) * _zoom < 4) offset = new Vector(24, 24) / _zoom;
        AddSelected([.. _clipboard.Select(e => e.Translate(offset))]);
    }

    /// <summary>
    /// Adds pictures side by side, centered on <paramref name="target"/> (control coordinates), at their
    /// on-screen pixel size, shrunk if needed to fit most of the view.
    /// </summary>
    private void AddPictures(IReadOnlyList<Bitmap> pictures, Point target)
    {
        const double gap = 16;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var sizes = pictures.Select(p => p.PixelSize.ToSize(scaling)).ToList();
        var width = sizes.Sum(s => s.Width) + gap * (sizes.Count - 1);
        var height = sizes.Max(s => s.Height);
        var fit = Math.Min(1, Math.Min(Bounds.Width * 0.8 / width, Bounds.Height * 0.8 / height));
        // Sizes on screen, then in board units.
        var x = target.X - width * fit / 2;
        var added = new List<InkElement>();
        for (var i = 0; i < pictures.Count; i++)
        {
            var size = sizes[i] * fit;
            var topLeft = ToBoard(new Point(x, target.Y - size.Height / 2));
            added.Add(new ImageElement(pictures[i], new Rect(topLeft, size / _zoom)));
            x += size.Width + gap * fit;
        }
        AddSelected(added);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles()?.Any(InkPicture.IsPicture) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files || InkPicture.Load(files) is not { Count: > 0 } pictures) return;
        AddPictures(pictures, e.GetPosition(this));
        e.Handled = true;
    }

    public void Duplicate()
    {
        if (HasSelection) AddSelected([.. Selected().Select(e => e.Translate(new Vector(24, 24) / _zoom))]);
    }

    private void AddSelected(List<InkElement> elements)
    {
        CommitText();
        EndCrop(keep: true);
        _settings.Tool = Tool.Select;
        Add(elements);
        _selection.Clear();
        _selection.UnionWith(elements);
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
                if (_settings.Tool == Tool.Select) UpdateHover(ToBoard(_pointer.Value));
                return;
            case Tool.Select:
                DragSelect(ToBoard(e.GetPosition(this)), e.KeyModifiers);
                break;
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
        else if (_gesture is Tool.Line or Tool.Arrow or Tool.Rectangle or Tool.Ellipse) _preview = ShapeTo(position, e.KeyModifiers);
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
            case Tool.Select:
                EndSelect();
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
            _elements.RemoveAll(element =>
                element.IsErasable && element.Bounds.Inflate(radius).Contains(point) && element.HitTest(point, radius));
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// Opens a text box at <paramref name="position"/>, in control coordinates, or over <paramref name="existing"/>
    /// text to change it (shown unturned while typing, at its size on screen).
    /// </summary>
    private void StartText(Point position, TextElement? existing = null)
    {
        CommitText();
        _editing = existing;
        _editorColor = existing?.Color ?? _settings.Color;
        _editorFontSize = existing is null ? _settings.FontSize : existing.FontSize * existing.Scale * _zoom;
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

        if (existing is null)
        {
            // Center the first line on the click.
            _editorPosition = new Point(position.X - 5, position.Y - _editorFontSize * 0.7 - 3);
        }
        else
        {
            // The text where it was drawn, past the box's border and padding.
            var origin = existing.Origin.Transform(existing.Placement);
            _editorPosition = new Point(origin.X * _zoom + _offset.X - 5, origin.Y * _zoom + _offset.Y - 3);
            editor.Text = existing.Text;
            editor.CaretIndex = existing.Text.Length;
            InvalidateVisual();
        }
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

    /// <summary>Turns the text being typed into a text element, or changes the text being edited (removed if emptied).</summary>
    public void CommitText()
    {
        if (_editor is not { } editor) return;
        _editor = null;
        var text = editor.Text?.TrimEnd() ?? "";
        if (_editing is { } existing)
        {
            _editing = null;
            if (text != existing.Text) Replace(existing, text.Length > 0 ? existing.WithText(text) : null);
            InvalidateVisual();
        }
        else if (text.Length > 0)
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
        _editing = null;
        RemoveEditor(editor);
    }

    /// <summary>Replaces an element, or removes it if <paramref name="replacement"/> is null, as one undoable step.</summary>
    private void Replace(InkElement element, InkElement? replacement)
    {
        var index = _elements.IndexOf(element);
        if (index < 0) return;
        PushUndo();
        if (replacement is null) _elements.RemoveAt(index);
        else _elements[index] = replacement;
        if (_selection.Remove(element) && replacement != null) _selection.Add(replacement);
        InvalidateVisual();
        Changed?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        // A transparent fill makes the whole surface hit-testable.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var visible = new Rect(ToBoard(default), ToBoard(new Point(Bounds.Width, Bounds.Height)));
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset)))
        {
            foreach (var element in _elements)
            {
                if (element == _cropping)
                {
                    RenderCropping(context);
                    continue;
                }
                if (element == _editing) continue;
                // The selection being moved, resized or rotated is drawn as it will be.
                var shown = _transformed?.GetValueOrDefault(element) ?? element;
                var offset = _moveOffset != default && _selection.Contains(element) ? _moveOffset : default;
                if (!shown.Bounds.Translate(offset).Intersects(visible)) continue;
                using (context.PushTransform(Matrix.CreateTranslation(offset)))
                    shown.Render(context, _darkBackground);
            }
            _preview?.Render(context, _darkBackground);
            RenderLaser(context);
            RenderSelection(context);
        }
        if (_settings.Tool == Tool.Spotlight) RenderSpotlight(context);
    }

    /// <summary>The picture being cropped: whole and faded, with the part kept on top.</summary>
    private void RenderCropping(DrawingContext context)
    {
        var image = _cropping!;
        using (context.PushOpacity(0.35))
            image.WithFrame(image.FullFrame).Render(context, _darkBackground);
        image.WithFrame(_cropFrame).Render(context, _darkBackground);
    }

    /// <summary>
    /// The selection frame and its handles, a dashed frame around each element when several are selected,
    /// and the rubber band. While cropping, the picture's edges and the handles of the part kept.
    /// </summary>
    private void RenderSelection(DrawingContext context)
    {
        var accent = Color.Parse(_darkBackground ? "#60a5fa" : "#2563eb");
        var dashed = new ImmutablePen(new ImmutableSolidColorBrush(accent), 1.25 / _zoom, new ImmutableDashStyle([4, 3], 0));
        if (_cropping is { } image)
        {
            context.DrawGeometry(null, dashed, new SelectionFrame(image.FullFrame, image.Placement, false).Outline());
            new SelectionFrame(_cropFrame, image.Placement, Sides: true).Render(context, _zoom, accent, rotate: false);
            return;
        }
        if ((_selection.Count == 0 && _band is null) || _editing != null) return;
        var gesture = _moveOffset != default ? Matrix.CreateTranslation(_moveOffset) : _handleTransform;
        var selected = Selected();
        if (_band is null && selected is [ShapeElement { IsLine: true } line])
        {
            // A line or arrow: a handle at each end.
            var shown = (ShapeElement)(_transformed?.GetValueOrDefault(line) ?? line);
            SelectionFrame.RenderHandle(context, shown.Start + _moveOffset, _zoom, accent, round: true);
            SelectionFrame.RenderHandle(context, shown.End + _moveOffset, _zoom, accent, round: true);
            return;
        }
        if (selected.Count > 1)
        {
            foreach (var element in selected)
                context.DrawGeometry(null, dashed, new SelectionFrame(element.Frame, element.Placement, false).Transform(gesture).Outline());
        }
        if (_band is { } band)
            context.DrawRectangle(new ImmutableSolidColorBrush(accent, 0.08), new ImmutablePen(new ImmutableSolidColorBrush(accent, 0.7), 1 / _zoom), band);
        else if (SelectionFrame.Of(selected) is { } frame)
            frame.Transform(gesture).Render(context, _zoom, accent, rotate: true);
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
            Tool.Select => new Cursor(StandardCursorType.Arrow),
            // The bright circle is the cursor.
            Tool.Spotlight => new Cursor(StandardCursorType.None),
            _ => new Cursor(StandardCursorType.Cross),
        };
        // The selection only shows, and only applies, with the selection tool.
        if (_settings.Tool != Tool.Select)
        {
            EndCrop(keep: true);
            _selection.Clear();
            _hoverCursor = null;
        }
        ApplyCursor();
        previous?.Dispose();
        // The spotlight appears, disappears or resizes with the tool settings.
        InvalidateVisual();
    }

    private void ApplyCursor() =>
        Cursor = _panFrom != null || _panKeyHeld ? _panCursor : _settings.Tool == Tool.Select ? _hoverCursor ?? _cursor : _cursor;

    private readonly record struct LaserPoint(Point Position, long Time);
}
