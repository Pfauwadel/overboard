using Overboard.Ink;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Overboard.Overlay;

/// <summary>A screen as sent to the board: its picture, if the platform allows one, and the annotations drawn over it.</summary>
/// <param name="Size">The screen size in device-independent units, the unit of the annotations.</param>
public sealed record ScreenCapture(Bitmap? Screenshot, Size Size, IReadOnlyList<InkElement> Elements);

/// <summary>Owns one overlay window per screen and switches drawing mode on and off.</summary>
public sealed class OverlayManager(ToolSettings settings, Func<PixelPoint> cursorPosition)
{
    /// <summary>Leaves time to render the cleared canvas before hiding, so the next show does not flash old strokes.</summary>
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(60);
    /// <summary>Leaves time for the overlays to vanish from the screen before capturing it.</summary>
    private static readonly TimeSpan CaptureDelay = TimeSpan.FromMilliseconds(300);

    private readonly List<OverlayWindow> _windows = [];
    // Never shown: gives access to the screen list before any overlay exists.
    private Window? _probe;
    private IDisposable? _pendingHide;
    private IDisposable? _pendingRebuild;

    public bool IsActive { get; private set; }
    public event Action? ActiveChanged;

    /// <summary>Raised with the screens sent to the board.</summary>
    public event Action<IReadOnlyList<ScreenCapture>>? Captured;

    public void Initialize()
    {
        _probe = new Window();
        _probe.Screens.Changed += (_, _) =>
        {
            _pendingRebuild?.Dispose();
            _pendingRebuild = DispatcherTimer.RunOnce(Rebuild, TimeSpan.FromMilliseconds(500));
        };
        Rebuild();
    }

    public void Toggle()
    {
        if (IsActive) Deactivate();
        else Activate();
    }

    public void Activate()
    {
        if (IsActive || _windows.Count == 0) return;
        IsActive = true;
        _pendingHide?.Dispose();

        var cursor = cursorPosition();
        var target = _windows.FirstOrDefault(w => w.ScreenBounds.Contains(cursor)) ?? _windows[0];
        foreach (var window in _windows)
        {
            window.Canvas.Reset();
            window.Toolbar.IsVisible = window == target;
            window.Show();
        }
        target.FocusForInput();
        target.ShowHelp();
        ActiveChanged?.Invoke();
    }

    /// <summary>Escape validates the text being typed, otherwise leaves drawing mode.</summary>
    public void HandleEscape()
    {
        var editing = _windows.FirstOrDefault(w => w.Canvas.IsEditingText);
        if (editing != null) editing.Canvas.CommitText();
        else Deactivate();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        foreach (var window in _windows)
        {
            window.Canvas.Reset();
            window.HideHud();
        }
        _pendingHide = DispatcherTimer.RunOnce(() => _windows.ForEach(w => w.Hide()), HideDelay);
        ActiveChanged?.Invoke();
    }

    /// <summary>
    /// Leaves drawing mode and hands the board every annotated screen (or the one under
    /// the cursor), captured once the overlays are gone, with the annotations drawn over it.
    /// </summary>
    public async void SendToBoard()
    {
        if (!IsActive) return;
        foreach (var window in _windows) window.Canvas.CommitText();
        var cursor = cursorPosition();
        var sources = _windows.Where(w => w.Canvas.Elements.Count > 0).OrderBy(w => w.ScreenBounds.X).ToList();
        if (sources.Count == 0) sources = [_windows.FirstOrDefault(w => w.ScreenBounds.Contains(cursor)) ?? _windows[0]];
        var annotated = sources.Select(w => (w.ScreenBounds, w.ScreenScaling, w.Canvas.Bounds.Size, Elements: w.Canvas.Elements.ToList())).ToList();

        Deactivate();
        await Task.Delay(CaptureDelay);
        Captured?.Invoke([.. annotated.Select(a => new ScreenCapture(ScreenGrabber.Grab(a.ScreenBounds, a.ScreenScaling), a.Size, a.Elements))]);
    }

    public void Shutdown()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        _probe?.Close();
    }

    private void Rebuild()
    {
        Deactivate();
        _pendingHide?.Dispose();
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        foreach (var screen in _probe!.Screens.All)
            _windows.Add(new OverlayWindow(screen.Bounds, screen.Scaling, settings, Deactivate, SendToBoard));
    }
}
