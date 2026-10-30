using Overboard.Board;
using Avalonia.Platform.Storage;
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
    private readonly SingleInstance? _instance;

    /// <param name="instance">Receives later launches of Overboard (files to open), if given.</param>
    public App(SingleInstance? instance = null) => _instance = instance;

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
            _overlays.BoardRequested += ShowBoard;
            _input.TogglePressed += _overlays.Toggle;
            _input.BoardPressed += ToggleBoard;
            _input.EscapePressed += _overlays.HandleEscape;
            _input.Failed += () => _tray?.ToolTipText = $"Overboard : raccourci {GlobalInput.ToggleLabel} indisponible";

            CreateTray(desktop);
            _overlays.Initialize();
            _input.Start();
            ScheduleUpdateChecks();

            // Files to open: on the command line (Windows, Linux), later launches, and the Finder (macOS).
            _instance?.Listen(OpenLaunched);
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } path) OpenLaunched(path);
                };
            if (desktop.Args?.FirstOrDefault(File.Exists) is { } launchFile) OpenLaunched(launchFile);

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
        var updatesItem = new NativeMenuItem("Rechercher les mises à jour")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _preferences.CheckForUpdates,
        };
        updatesItem.Click += (_, _) =>
        {
            _preferences.CheckForUpdates = !_preferences.CheckForUpdates;
            _preferences.Save();
            updatesItem.IsChecked = _preferences.CheckForUpdates;
            if (_preferences.CheckForUpdates) CheckForUpdate();
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
                .. CreateMouseButtonMenus(), autostartItem, updatesItem, new NativeMenuItemSeparator(),
                versionItem, quitItem,
            ],
        };
        if (FileAssociation.IsConfigurable)
        {
            // Opt-in: changes which application opens .overboard files.
            var associationItem = new NativeMenuItem($"Ouvrir les fichiers {BoardFile.Extension} avec Overboard")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = FileAssociation.IsRegistered(),
            };
            associationItem.Click += (_, _) =>
            {
                FileAssociation.Set(!FileAssociation.IsRegistered());
                associationItem.IsChecked = FileAssociation.IsRegistered();
            };
            _tray.Menu.Items.Insert(_tray.Menu.Items.IndexOf(updatesItem) + 1, associationItem);
        }
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
        // Unchecking the menu item means no request at all, not just no notification.
        if (!_preferences.CheckForUpdates || _updateItem != null) return;
        if (await UpdateChecker.FindNewerReleaseAsync() is not { } release) return;
        _updateItem = new NativeMenuItem($"Nouvelle version {release.Version} : télécharger");
        _updateItem.Click += (_, _) => AppInfo.OpenUrl(release.Url);
        _tray!.Menu!.Items.Insert(0, _updateItem);
        _tray.Menu.Items.Insert(1, new NativeMenuItemSeparator());
        _tray.ToolTipText = $"Overboard : version {release.Version} disponible";
    }

    /// <summary>
    /// Choice of the mouse buttons that toggle drawing mode and the board, remembered between runs.
    /// A button does one thing: choosing it for one frees it from the other.
    /// </summary>
    private NativeMenuItem[] CreateMouseButtonMenus()
    {
        (MouseButton Button, string Label)[] choices =
        [
            (MouseButton.NoButton, "Aucun"),
            (MouseButton.Button4, "Bouton latéral arrière"),
            (MouseButton.Button5, "Bouton latéral avant"),
        ];
        var items = new List<(bool Board, MouseButton Button, NativeMenuItem Item)>();
        void Refresh()
        {
            _input.ToggleButton = _preferences.ToggleMouseButton;
            _input.BoardButton = _preferences.BoardMouseButton;
            foreach (var (board, button, item) in items)
                item.IsChecked = button == (board ? _preferences.BoardMouseButton : _preferences.ToggleMouseButton);
        }
        NativeMenuItem Menu(string title, bool board)
        {
            var menu = new NativeMenu();
            foreach (var (button, label) in choices)
            {
                var item = new NativeMenuItem(label) { ToggleType = MenuItemToggleType.Radio };
                item.Click += (_, _) =>
                {
                    if (board) _preferences.BoardMouseButton = button;
                    else _preferences.ToggleMouseButton = button;
                    if (button != MouseButton.NoButton && _preferences.BoardMouseButton == _preferences.ToggleMouseButton)
                    {
                        if (board) _preferences.ToggleMouseButton = MouseButton.NoButton;
                        else _preferences.BoardMouseButton = MouseButton.NoButton;
                    }
                    _preferences.Save();
                    Refresh();
                };
                items.Add((board, button, item));
                menu.Items.Add(item);
            }
            return new NativeMenuItem(title) { Menu = menu };
        }
        NativeMenuItem[] menus = [Menu("Bouton de souris pour dessiner", board: false), Menu("Bouton de souris pour le tableau", board: true)];
        Refresh();
        return menus;
    }

    /// <summary>
    /// The board's mouse button: in drawing mode, sends the annotated screen to the board;
    /// otherwise shows the board, or puts it away when it is already in front.
    /// </summary>
    private void ToggleBoard()
    {
        if (_overlays!.IsActive) _overlays.SendToBoard();
        else if (_board != null && _board.WindowState != WindowState.Minimized && Foreground.Contains(_board))
            _board.WindowState = WindowState.Minimized;
        else ShowBoard();
    }

    /// <summary>A board file opened from the system, or Overboard launched again (null): shows the board.</summary>
    private void OpenLaunched(string? path)
    {
        ShowBoard();
        if (path != null && Path.GetExtension(path).Equals(BoardFile.Extension, StringComparison.OrdinalIgnoreCase))
            _ = _board!.OpenFileAsync(path);
    }

    private void ShowBoard()
    {
        if (_board is null)
        {
            _board = new BoardWindow(_settings, BoardWindow.DefaultAutosavePath);
            _board.Closed += (_, _) => _board = null;
            _board.Show();
        }
        else if (_board.WindowState == WindowState.Minimized) _board.WindowState = WindowState.Maximized;
        Foreground.Bring(_board);
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
