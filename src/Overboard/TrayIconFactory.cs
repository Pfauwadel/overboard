using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Overboard;

/// <summary>Draws the tray icon: a dark rounded square crossed by a pen stroke, red while drawing.</summary>
public static class TrayIconFactory
{
    public static WindowIcon Create(bool active)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#1e1e1e")), null, new Rect(1, 1, 30, 30), 7, 7);
            var stroke = new SolidColorBrush(active ? Color.Parse("#ff4d4d") : Colors.White);
            ctx.DrawLine(new Pen(stroke, 5, lineCap: PenLineCap.Round), new Point(9, 23), new Point(23, 9));
        }
        return new WindowIcon(bitmap);
    }
}
