using Overboard.Controls;
using Overboard.Ink;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Overboard.Overlay;

/// <summary>A transparent, borderless, always-on-top window covering one screen.</summary>
public sealed class OverlayWindow : Window
{
    private readonly ToolSettings _settings;
    private readonly Action _requestExit;
    private readonly Hud _hud = new();

    public OverlayWindow(PixelRect screenBounds, double screenScaling, ToolSettings settings, Action requestExit)
    {
        ScreenBounds = screenBounds;
        _settings = settings;
        _requestExit = requestExit;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        // Nearly invisible, but guarantees the window receives clicks everywhere.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        // Subpixel text leaves colored fringes on a transparent surface.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = screenBounds.Position;
        Width = screenBounds.Width / screenScaling;
        Height = screenBounds.Height / screenScaling;

        Canvas = new InkCanvas(settings);
        Toolbar = new Toolbar(settings, closeTip: "Quitter le dessin (Échap)") { IsVisible = false };
        Toolbar.UndoRequested += Canvas.Undo;
        Toolbar.RedoRequested += Canvas.Redo;
        Toolbar.ClearRequested += Canvas.Clear;
        Toolbar.CloseRequested += requestExit;
        Content = new Panel { Children = { Canvas, Toolbar, _hud } };

        // Windows can resize a window placed on a screen with a different scale factor.
        Opened += (_, _) =>
        {
            Position = screenBounds.Position;
            Width = screenBounds.Width / screenScaling;
            Height = screenBounds.Height / screenScaling;
        };
    }

    public PixelRect ScreenBounds { get; }
    public InkCanvas Canvas { get; }
    public Toolbar Toolbar { get; }

    public void FocusForInput()
    {
        Activate();
        Canvas.Focus();
    }

    public void ShowHelp() =>
        _hud.Show(TimeSpan.FromSeconds(3.5),
            Hud.Key("Échap"), Hud.Text("quitter"), Hud.Key("P S G T L F R E"), Hud.Text("outils"),
            Hud.Key("1–6"), Hud.Text("couleurs"), Hud.Key("Molette"), Hud.Text("taille"));

    public void HideHud() => _hud.Hide();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keys typed in a text box belong to the text.
        if (Canvas.IsEditingText)
        {
            base.OnKeyDown(e);
            return;
        }
        if (InkShortcuts.Handle(e, _settings, Canvas)) e.Handled = true;
        else if (e.Key == Key.Escape)
        {
            _requestExit();
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }
}
