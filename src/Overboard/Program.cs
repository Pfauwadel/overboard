using Avalonia;

namespace Overboard;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = new Mutex(true, "overboard-single-instance", out var isFirstInstance);
        if (!isFirstInstance) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
