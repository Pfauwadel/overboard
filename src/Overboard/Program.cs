using Avalonia;
using Overboard.Distribution;

namespace Overboard;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = new SingleInstance();
        if (!instance.IsFirst)
        {
            // Already running: hand it the file to open (or ask it to show the board).
            SingleInstance.Forward(args);
            return 0;
        }
        return AppBuilder.Configure(() => new App(instance)).UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
