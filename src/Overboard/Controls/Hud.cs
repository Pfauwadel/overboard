using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Overboard.Controls;

/// <summary>A pill at the bottom of a window showing keys or a short message, then fading out.</summary>
public sealed class Hud : Border
{
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private IDisposable? _timer;

    public Hud()
    {
        Background = new SolidColorBrush(Color.FromArgb(225, 24, 24, 27));
        CornerRadius = new CornerRadius(999);
        Padding = new Thickness(16, 10);
        Margin = new Thickness(0, 0, 0, 48);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        IsHitTestVisible = false;
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
        Child = _content;
    }

    public void Show(TimeSpan duration, params Control[] parts)
    {
        _content.Children.Clear();
        _content.Children.AddRange(parts);
        Opacity = 1;
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(() => Opacity = 0, duration);
    }

    public void ShowMessage(string message) => Show(TimeSpan.FromSeconds(2.5), Text(message));

    public void Hide()
    {
        _timer?.Dispose();
        Opacity = 0;
    }

    public static TextBlock Text(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#f4f4f5")),
        FontSize = 14,
        FontWeight = FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Key(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text),
    };
}
