using Overboard.Board;
using Overboard.Ink;
using Overboard.Input;
using Overboard.Overlay;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Overboard;

/// <summary>Lives in the tray: overlays and the board window are shown on demand.</summary>
public sealed class App : Application
{
    private readonly GlobalInput _input = new();
    private readonly ToolSettings _settings = new();
    private OverlayManager? _overlays;
    private BoardWindow? _board;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private WindowIcon? _idleIcon;
    private WindowIcon? _activeIcon;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _overlays = new OverlayManager(_settings, () => _input.CursorPosition);
            _overlays.ActiveChanged += OnActiveChanged;
            _input.TogglePressed += _overlays.Toggle;
            _input.EscapePressed += _overlays.HandleEscape;
            _input.Failed += () => _tray?.ToolTipText = $"Overboard : raccourci {GlobalInput.ToggleLabel} indisponible";

            CreateTray(desktop);
            _overlays.Initialize();
            _input.Start();

            desktop.Exit += (_, _) =>
            {
                _board?.SaveSession();
                _input.Dispose();
                _overlays.Shutdown();
                _tray?.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _idleIcon = TrayIconFactory.Create(active: false);
        _activeIcon = TrayIconFactory.Create(active: true);

        _toggleItem = new NativeMenuItem($"Dessiner sur l'écran ({GlobalInput.ToggleLabel})")
        {
            ToggleType = MenuItemToggleType.CheckBox,
        };
        _toggleItem.Click += (_, _) => _overlays!.Toggle();
        var boardItem = new NativeMenuItem("Tableau blanc");
        boardItem.Click += (_, _) => ShowBoard();
        var quitItem = new NativeMenuItem("Quitter Overboard");
        quitItem.Click += (_, _) => desktop.Shutdown();

        _tray = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = "Overboard",
            Menu = [_toggleItem, boardItem, new NativeMenuItemSeparator(), quitItem],
        };
        _tray.Clicked += (_, _) => _overlays!.Toggle();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void ShowBoard()
    {
        if (_board is null)
        {
            _board = new BoardWindow(_settings, BoardWindow.DefaultAutosavePath);
            _board.Closed += (_, _) => _board = null;
            _board.Show();
            return;
        }
        if (_board.WindowState == WindowState.Minimized) _board.WindowState = WindowState.Maximized;
        _board.Activate();
    }

    private void OnActiveChanged()
    {
        var active = _overlays!.IsActive;
        _input.OverlayActive = active;
        _toggleItem!.IsChecked = active;
        _tray!.Icon = active ? _activeIcon : _idleIcon;
        _tray.ToolTipText = active ? "Overboard : dessin actif" : "Overboard";
    }
}
