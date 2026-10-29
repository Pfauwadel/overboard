using Overboard.Board;
using Overboard.Distribution;
using Overboard.Ink;
using Overboard.Input;
using Overboard.Overlay;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SharpHook.Data;

namespace Overboard;

/// <summary>Lives in the tray: overlays and the board window are shown on demand.</summary>
public sealed class App : Application
{
    private readonly GlobalInput _input = new();
    private readonly ToolSettings _settings = new();
    private readonly AppSettings _preferences = AppSettings.Load();
    private OverlayManager? _overlays;
    private BoardWindow? _board;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private NativeMenuItem? _updateItem;
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
            _overlays.Captured += captures =>
            {
                ShowBoard();
                _board!.AddCaptures(captures);
            };
            _input.ToggleButton = _preferences.ToggleMouseButton;
            _input.TogglePressed += _overlays.Toggle;
            _input.EscapePressed += _overlays.HandleEscape;
            _input.Failed += () => _tray?.ToolTipText = $"Overboard : raccourci {GlobalInput.ToggleLabel} indisponible";

            CreateTray(desktop);
            _overlays.Initialize();
            _input.Start();
            ScheduleUpdateChecks();

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
        var autostartItem = new NativeMenuItem("Lancer au démarrage de la session")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled,
        };
        autostartItem.Click += (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled);
            autostartItem.IsChecked = Autostart.IsEnabled;
        };
        var versionItem = new NativeMenuItem($"Overboard {AppInfo.Version} : versions sur GitHub");
        versionItem.Click += (_, _) => AppInfo.OpenUrl(AppInfo.ReleasesUrl);
        var quitItem = new NativeMenuItem("Quitter Overboard");
        quitItem.Click += (_, _) => desktop.Shutdown();

        _tray = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = "Overboard",
            Menu =
            [
                _toggleItem, boardItem, new NativeMenuItemSeparator(),
                CreateMouseButtonMenu(), autostartItem, new NativeMenuItemSeparator(),
                versionItem, quitItem,
            ],
        };
        _tray.Clicked += (_, _) => _overlays!.Toggle();
        TrayIcon.SetIcons(this, [_tray]);
    }

    /// <summary>Looks for a newer release shortly after startup, then daily, and offers it at the top of the tray menu.</summary>
    private void ScheduleUpdateChecks()
    {
        DispatcherTimer.RunOnce(CheckForUpdate, TimeSpan.FromSeconds(10));
        var daily = new DispatcherTimer { Interval = TimeSpan.FromDays(1) };
        daily.Tick += (_, _) => CheckForUpdate();
        daily.Start();
    }

    private async void CheckForUpdate()
    {
        if (_updateItem != null || await UpdateChecker.FindNewerReleaseAsync() is not { } release) return;
        _updateItem = new NativeMenuItem($"Nouvelle version {release.Version} : télécharger");
        _updateItem.Click += (_, _) => AppInfo.OpenUrl(release.Url);
        _tray!.Menu!.Items.Insert(0, _updateItem);
        _tray.Menu.Items.Insert(1, new NativeMenuItemSeparator());
        _tray.ToolTipText = $"Overboard : version {release.Version} disponible";
    }

    /// <summary>Choice of the mouse button that toggles drawing mode, remembered between runs.</summary>
    private NativeMenuItem CreateMouseButtonMenu()
    {
        (MouseButton Button, string Label)[] choices =
        [
            (MouseButton.NoButton, "Aucun"),
            (MouseButton.Button4, "Bouton latéral arrière"),
            (MouseButton.Button5, "Bouton latéral avant"),
        ];
        var menu = new NativeMenu();
        var items = new List<(MouseButton Button, NativeMenuItem Item)>();
        foreach (var (button, label) in choices)
        {
            var item = new NativeMenuItem(label)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = button == _preferences.ToggleMouseButton,
            };
            item.Click += (_, _) =>
            {
                _preferences.ToggleMouseButton = button;
                _preferences.Save();
                _input.ToggleButton = button;
                foreach (var (other, otherItem) in items) otherItem.IsChecked = other == button;
            };
            items.Add((button, item));
            menu.Items.Add(item);
        }
        return new NativeMenuItem("Bouton de souris pour dessiner") { Menu = menu };
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
