using Overboard.Controls;
using Overboard.Ink;
using Overboard.Overlay;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Overboard.Board;

/// <summary>
/// An infinite whiteboard in a regular window. The board is saved continuously to
/// an autosave file, so closing never loses anything, and to a .overboard file on demand.
/// </summary>
public sealed class BoardWindow : Window
{
    public static readonly string DefaultAutosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Overboard", "tableau" + BoardFile.Extension);

    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1);
    private static readonly FilePickerFileType FileType = new("Tableau Overboard") { Patterns = ["*" + BoardFile.Extension] };

    private readonly ToolSettings _settings;
    private readonly string _autosavePath;
    private readonly InkCanvas _canvas;
    private readonly BoardBackground _background;
    private readonly Toolbar _toolbar;
    private readonly ToolbarButton _gridButton;
    private readonly Hud _hud = new();
    // The .overboard file the board was opened from or saved to, and whether it changed since.
    private string? _filePath;
    private bool _modified;
    private double _shownZoom = 1;
    private IDisposable? _pendingAutosave;

    public BoardWindow(ToolSettings settings, string autosavePath)
    {
        _settings = settings;
        _autosavePath = autosavePath;
        Width = 1280;
        Height = 800;
        MinWidth = 480;
        MinHeight = 360;
        WindowState = WindowState.Maximized;
        Icon = TrayIconFactory.Create(active: false);

        _canvas = new InkCanvas(settings) { CanNavigate = true };
        _canvas.Changed += MarkModified;
        _canvas.ViewChanged += OnViewChanged;
        _background = new BoardBackground(_canvas);

        _toolbar = new Toolbar(settings);
        _toolbar.UndoRequested += _canvas.Undo;
        _toolbar.RedoRequested += _canvas.Redo;
        _toolbar.ClearRequested += _canvas.Clear;
        _gridButton = Toolbar.Button("M4 4 H20 V20 H4 Z M9.3 4 V20 M14.7 4 V20 M4 9.3 H20 M4 14.7 H20", "Grille (Ctrl+G)", ToggleGrid);
        _toolbar.AddSection(
            Toolbar.Button("M6 3 H14 L19 8 V21 H6 Z M14 3 V8 H19", "Nouveau tableau (Ctrl+N)", () => _ = NewAsync()),
            Toolbar.Button("M3 6 H9 L11 8 H21 V19 H3 Z", "Ouvrir (Ctrl+O)", () => _ = OpenAsync()),
            Toolbar.Button("M5 4 H16 L20 8 V20 H5 Z M8 4 V9 H15 V4 M8 20 V14 H16 V20", "Enregistrer (Ctrl+S)", () => _ = SaveAsync(saveAs: false)),
            Toolbar.Button("M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15", "Tout voir (Ctrl+0)", _canvas.FitContent),
            Toolbar.Button("M20 14 A8 8 0 1 1 10 4 A6 6 0 0 0 20 14 Z", "Fond clair ou sombre (B)", ToggleDark),
            _gridButton);

        Content = new Panel { Children = { _background, _canvas, _toolbar, _hud } };

        Opened += (_, _) =>
        {
            _canvas.Focus();
            _hud.Show(TimeSpan.FromSeconds(4),
                Hud.Key("Espace + glisser"), Hud.Text("déplacer"), Hud.Key("Ctrl + molette"), Hud.Text("zoom"),
                Hud.Key("Ctrl+0"), Hud.Text("tout voir"));
        };
        Deactivated += (_, _) => _canvas.PanKeyHeld = false;
        Closing += (_, _) => SaveSession();
        Restore();
    }

    private bool Dark => _canvas.DarkBackground;

    private string BoardName => _filePath is null ? "Tableau" : Path.GetFileNameWithoutExtension(_filePath);

    /// <summary>Whether replacing the board would lose work that only lives in the autosave.</summary>
    private bool HasUnsavedWork => _filePath is null ? _canvas.Elements.Count > 0 : _modified;

    /// <summary>Reopens the board as it was left.</summary>
    private void Restore()
    {
        Apply(new BoardContent([], Application.Current?.ActualThemeVariant == ThemeVariant.Dark, Grid: true, default, 1));
        if (File.Exists(_autosavePath))
        {
            try
            {
                var (content, session) = BoardFile.Read(_autosavePath);
                Apply(content);
                _filePath = session?.FilePath;
                _modified = session?.Modified ?? false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Keep the unreadable file aside rather than overwriting it.
                try { File.Move(_autosavePath, _autosavePath + ".bak", overwrite: true); }
                catch (IOException) { }
            }
        }
        UpdateTitle();
    }

    private void Apply(BoardContent content)
    {
        _canvas.Load(content.Elements);
        _shownZoom = Math.Clamp(content.Zoom, InkCanvas.MinZoom, InkCanvas.MaxZoom);
        _canvas.SetView(content.Offset, content.Zoom);
        SetDark(content.Dark);
        SetGrid(content.Grid);
    }

    private BoardContent Snapshot() => new([.. _canvas.Elements], Dark, _background.ShowGrid, _canvas.Offset, _canvas.Zoom);

    private void SetDark(bool dark)
    {
        _canvas.DarkBackground = dark;
        _background.Dark = dark;
        _toolbar.DarkBackground = dark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void SetGrid(bool grid)
    {
        _background.ShowGrid = grid;
        _gridButton.IsSelected = grid;
    }

    private void ToggleDark()
    {
        SetDark(!Dark);
        MarkModified();
    }

    private void ToggleGrid()
    {
        SetGrid(!_background.ShowGrid);
        MarkModified();
    }

    private void MarkModified()
    {
        _modified = true;
        UpdateTitle();
        ScheduleAutosave();
    }

    private void OnViewChanged()
    {
        if (_canvas.Zoom != _shownZoom)
        {
            _shownZoom = _canvas.Zoom;
            _hud.Show(TimeSpan.FromSeconds(1), Hud.Text(_canvas.Zoom.ToString("P0")));
        }
        ScheduleAutosave();
    }

    private void UpdateTitle() => Title = $"{BoardName}{(_filePath != null && _modified ? " •" : "")} — Overboard";

    private void ScheduleAutosave()
    {
        _pendingAutosave?.Dispose();
        _pendingAutosave = DispatcherTimer.RunOnce(SaveSession, AutosaveDelay);
    }

    /// <summary>Writes the autosave now.</summary>
    public void SaveSession()
    {
        _canvas.CommitText();
        _pendingAutosave?.Dispose();
        _pendingAutosave = null;
        try
        {
            BoardFile.Write(_autosavePath, Snapshot(), new BoardSession(_filePath, _modified));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _hud.ShowMessage("Sauvegarde automatique impossible");
        }
    }

    /// <summary>Saves to the board's file, asking for one if it has none (or <paramref name="saveAs"/>). Returns whether it was saved.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        _canvas.CommitText();
        var path = saveAs ? null : _filePath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le tableau",
                SuggestedFileName = BoardName,
                DefaultExtension = BoardFile.Extension,
                FileTypeChoices = [FileType],
                ShowOverwritePrompt = true,
            });
            if (file is null) return false;
            path = file.TryGetLocalPath();
            if (path is null)
            {
                _hud.ShowMessage("Cet emplacement n'est pas pris en charge");
                return false;
            }
        }
        try
        {
            BoardFile.Write(path, Snapshot());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _hud.ShowMessage($"Impossible d'enregistrer : {e.Message}");
            return false;
        }
        _filePath = path;
        _modified = false;
        UpdateTitle();
        SaveSession();
        _hud.ShowMessage($"Enregistré : {Path.GetFileName(path)}");
        return true;
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmReplaceAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir un tableau",
            FileTypeFilter = [FileType],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        Open(path);
    }

    public void Open(string path)
    {
        try
        {
            Apply(BoardFile.Read(path).Content);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _hud.ShowMessage($"Impossible d'ouvrir {Path.GetFileName(path)} : {e.Message}");
            return;
        }
        _filePath = path;
        _modified = false;
        UpdateTitle();
        SaveSession();
    }

    /// <summary>Places screens sent from drawing mode to the right of the content, as one undoable step, and shows them.</summary>
    public void AddCaptures(IReadOnlyList<ScreenCapture> captures)
    {
        const double gap = 80;
        var content = _canvas.ContentBounds;
        var x = content is { } bounds ? bounds.Right + gap : 0;
        var y = content?.Top ?? 0;
        var added = new List<InkElement>();
        Rect? area = null;
        foreach (var capture in captures)
        {
            var frame = new Rect(new Point(x, y), capture.Size);
            if (capture.Screenshot is { } picture) added.Add(new ImageElement(picture, frame));
            added.AddRange(capture.Elements.Select(e => e.Translate(new Vector(x, y))));
            area = area?.Union(frame) ?? frame;
            x = frame.Right + gap;
        }
        _canvas.Add(added);
        if (area is { } shown) _canvas.ShowArea(shown);
        if (captures.Any(c => c.Screenshot is null))
            _hud.ShowMessage("Capture d'écran indisponible : seules les annotations ont été envoyées");
    }

    private async Task NewAsync()
    {
        if (!await ConfirmReplaceAsync()) return;
        Apply(new BoardContent([], Dark, _background.ShowGrid, default, 1));
        _filePath = null;
        _modified = false;
        UpdateTitle();
        SaveSession();
    }

    /// <summary>Offers to save the board before it is replaced. Returns false to cancel.</summary>
    private async Task<bool> ConfirmReplaceAsync()
    {
        _canvas.CommitText();
        if (!HasUnsavedWork) return true;
        var dialog = new SaveChangesDialog(BoardName) { RequestedThemeVariant = RequestedThemeVariant };
        return await dialog.ShowDialog<SaveChoice>(this) switch
        {
            SaveChoice.Save => await SaveAsync(saveAs: false),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keys typed in a text box belong to the text.
        if (_canvas.IsEditingText)
        {
            base.OnKeyDown(e);
            return;
        }
        var command = InkShortcuts.IsCommand(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Space) _canvas.PanKeyHeld = true;
        else if (command && e.Key == Key.S) _ = SaveAsync(saveAs: shift);
        else if (command && e.Key == Key.O) _ = OpenAsync();
        else if (command && e.Key == Key.N) _ = NewAsync();
        else if (command && e.Key == Key.G) ToggleGrid();
        else if (command && e.PhysicalKey is PhysicalKey.Digit0 or PhysicalKey.NumPad0) _canvas.FitContent();
        else if (command && e.Key is Key.Add or Key.OemPlus) _canvas.ZoomBy(1.25);
        else if (command && e.Key is Key.Subtract or Key.OemMinus) _canvas.ZoomBy(0.8);
        else if (!command && e.Key == Key.B) ToggleDark();
        else if (e.Key == Key.Escape) _canvas.ClearSelection();
        else if (!InkShortcuts.Handle(e, _settings, _canvas))
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) _canvas.PanKeyHeld = false;
        base.OnKeyUp(e);
    }
}
