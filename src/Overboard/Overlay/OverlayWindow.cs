using Overboard.Ink;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Overboard.Overlay;

/// <summary>A transparent, borderless, always-on-top window covering one screen.</summary>
public sealed class OverlayWindow : Window
{
    private readonly ToolSettings _settings;
    private readonly Action _requestExit;
    private readonly Border _hud;
    private readonly StackPanel _hudContent;
    private IDisposable? _hudTimer;

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
        _hudContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _hud = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 24, 24, 27)),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(16, 10),
            Margin = new Thickness(0, 0, 0, 48),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Opacity = 0,
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }],
            Child = _hudContent,
        };
        Toolbar = new Toolbar(settings) { IsVisible = false };
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
        ShowHud(TimeSpan.FromSeconds(3.5),
            Key("Échap"), Text("quitter"), Key("P S G T L F R E"), Text("outils"),
            Key("1–6"), Text("couleurs"), Key("Molette"), Text("taille"));

    public void HideHud()
    {
        _hudTimer?.Dispose();
        _hud.Opacity = 0;
    }

    private void ShowHud(TimeSpan duration, params Control[] parts)
    {
        _hudContent.Children.Clear();
        _hudContent.Children.AddRange(parts);
        _hud.Opacity = 1;
        _hudTimer?.Dispose();
        _hudTimer = DispatcherTimer.RunOnce(() => _hud.Opacity = 0, duration);
    }

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#f4f4f5")),
        FontSize = 14,
        FontWeight = FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border Key(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keys typed in a text box belong to the text.
        if (Canvas.IsEditingText)
        {
            base.OnKeyDown(e);
            return;
        }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Physical keys for digits: they need Shift on AZERTY keyboards.
        var digit = e.PhysicalKey switch
        {
            PhysicalKey.Digit1 or PhysicalKey.NumPad1 => 1,
            PhysicalKey.Digit2 or PhysicalKey.NumPad2 => 2,
            PhysicalKey.Digit3 or PhysicalKey.NumPad3 => 3,
            PhysicalKey.Digit4 or PhysicalKey.NumPad4 => 4,
            PhysicalKey.Digit5 or PhysicalKey.NumPad5 => 5,
            PhysicalKey.Digit6 or PhysicalKey.NumPad6 => 6,
            _ => 0,
        };

        Tool? tool = ctrl ? null : e.Key switch
        {
            Avalonia.Input.Key.P => Tool.Pen,
            Avalonia.Input.Key.S => Tool.Highlighter,
            Avalonia.Input.Key.G => Tool.Eraser,
            Avalonia.Input.Key.T => Tool.Text,
            Avalonia.Input.Key.L => Tool.Line,
            Avalonia.Input.Key.F => Tool.Arrow,
            Avalonia.Input.Key.R => Tool.Rectangle,
            Avalonia.Input.Key.E => Tool.Ellipse,
            _ => null,
        };

        if (tool is { } selected) _settings.Tool = selected;
        else if (digit > 0 && !ctrl) _settings.Color = ToolSettings.Palette[digit - 1];
        else if (ctrl && e.Key == Avalonia.Input.Key.Z && !shift) Canvas.Undo();
        else if (ctrl && (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift))) Canvas.Redo();
        else if (e.Key is Avalonia.Input.Key.Delete or Avalonia.Input.Key.Back) Canvas.Clear();
        else if (e.Key is Avalonia.Input.Key.Add or Avalonia.Input.Key.OemPlus) _settings.Grow(1);
        else if (e.Key is Avalonia.Input.Key.Subtract or Avalonia.Input.Key.OemMinus) _settings.Grow(-1);
        else if (e.Key == Avalonia.Input.Key.Escape) _requestExit();
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
    }
}
