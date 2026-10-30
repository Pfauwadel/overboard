using Avalonia;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;

namespace Overboard.Input;

/// <summary>
/// System-wide keyboard and mouse hook: the toggle shortcut works whichever
/// application has focus. Handlers run on the hook thread and must stay short,
/// since they delay every input event of the system.
/// </summary>
public sealed class GlobalInput : IDisposable
{
    public const string ToggleLabel = "Ctrl+Shift+D";

    private readonly SimpleGlobalHook _hook = new(null);
    private volatile bool _toggleHeld;
    private volatile int _toggleButton;
    private volatile int _boardButton;
    private int _x;
    private int _y;

    /// <summary>Set by the UI: while true, Escape is captured to leave drawing mode.</summary>
    public volatile bool OverlayActive;

    public event Action? TogglePressed;
    public event Action? EscapePressed;
    public event Action? BoardPressed;
    public event Action? Failed;

    public PixelPoint CursorPosition => new(Volatile.Read(ref _x), Volatile.Read(ref _y));

    /// <summary>Mouse button that toggles drawing mode like the shortcut, NoButton for none. It never reaches applications.</summary>
    public MouseButton ToggleButton
    {
        get => (MouseButton)_toggleButton;
        set => _toggleButton = (int)value;
    }

    /// <summary>Mouse button for the board, NoButton for none; the toggle button wins if both are the same. It never reaches applications.</summary>
    public MouseButton BoardButton
    {
        get => (MouseButton)_boardButton;
        set => _boardButton = (int)value;
    }

    public void Start()
    {
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.MouseMoved += OnMouseMoved;
        _hook.MouseDragged += OnMouseMoved;
        _hook.MousePressed += OnMousePressed;
        _hook.MouseReleased += OnMouseReleased;
        _hook.RunAsync().ContinueWith(task =>
        {
            if (task.IsFaulted) Dispatcher.UIThread.Post(() => Failed?.Invoke());
        }, TaskScheduler.Default);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var mask = e.RawEvent.Mask;
        if (e.Data.KeyCode == KeyCode.VcD && (mask & EventMask.Ctrl) != 0 && (mask & EventMask.Shift) != 0)
        {
            // Keep the shortcut from reaching the focused application.
            e.SuppressEvent = true;
            // Ignore auto-repeat while the key is held down.
            if (_toggleHeld) return;
            _toggleHeld = true;
            Dispatcher.UIThread.Post(() => TogglePressed?.Invoke());
        }
        else if (e.Data.KeyCode == KeyCode.VcEscape && OverlayActive)
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(() => EscapePressed?.Invoke());
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcD) _toggleHeld = false;
    }

    private bool IsOwnButton(MouseHookEventArgs e) =>
        e.Data.Button != MouseButton.NoButton && ((int)e.Data.Button == _toggleButton || (int)e.Data.Button == _boardButton);

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (!IsOwnButton(e)) return;
        // Keep the button from reaching the application under the cursor (a browser would go forward).
        e.SuppressEvent = true;
        if ((int)e.Data.Button == _toggleButton) Dispatcher.UIThread.Post(() => TogglePressed?.Invoke());
        else Dispatcher.UIThread.Post(() => BoardPressed?.Invoke());
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (IsOwnButton(e)) e.SuppressEvent = true;
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        Volatile.Write(ref _x, e.Data.X);
        Volatile.Write(ref _y, e.Data.Y);
    }

    public void Dispose() => _hook.Dispose();
}
