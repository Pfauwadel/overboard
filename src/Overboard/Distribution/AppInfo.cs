using System.Diagnostics;
using System.Reflection;

namespace Overboard.Distribution;

public static class AppInfo
{
    public const string Repository = "Pfauwadel/overboard";
    public const string ReleasesUrl = $"https://github.com/{Repository}/releases";

    /// <summary>
    /// Where the board's autosave and the preferences live. The OVERBOARD_DATA environment variable
    /// points it elsewhere, for a separate, isolated Overboard (tests, portable use).
    /// </summary>
    public static readonly string DataDirectory =
        Environment.GetEnvironmentVariable("OVERBOARD_DATA") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Overboard");

    /// <summary>The version set at build time (from the release tag), without the commit hash.</summary>
    public static readonly string Version =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>Opens a web page in the default browser.</summary>
    public static void OpenUrl(string url)
    {
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [url]);
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No browser to open it with: nothing more to do.
        }
    }
}
