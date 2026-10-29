using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Overboard.Distribution;

/// <summary>
/// Opens .overboard files with this executable on double-click, for the current user only (no
/// administrator rights): the user's registry classes on Windows, the user's MIME database on Linux.
/// On macOS, Overboard.app declares the file type itself (Info.plist).
/// </summary>
public static class FileAssociation
{
    private const string Extension = ".overboard";
    private const string ProgId = "Overboard.Board";
    private const string MimeType = "application/x-overboard";
    private const string Classes = @"Software\Classes";

    /// <summary>Whether this platform needs Overboard to register the file type.</summary>
    public static bool IsConfigurable => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable");

    private static string OpenCommand(string executable) => $"\"{executable}\" \"%1\"";

    private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
        ? data
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    private static string MimePackage => Path.Combine(DataHome, "mime", "packages", "overboard.xml");
    private static string DesktopEntry => Path.Combine(DataHome, "applications", "overboard.desktop");

    /// <summary>Whether .overboard files open with this executable (an association with an older copy does not count).</summary>
    public static bool IsRegistered(string? executable = null)
    {
        executable ??= Executable;
        try
        {
            if (OperatingSystem.IsWindows()) return ReadCommand() == OpenCommand(executable);
            if (OperatingSystem.IsLinux()) return File.Exists(MimePackage) && File.Exists(DesktopEntry) && File.ReadAllText(DesktopEntry).Contains(executable);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>Registers or removes the association. Returns whether the change was made.</summary>
    public static bool Set(bool registered, string? executable = null)
    {
        executable ??= Executable;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (registered) RegisterWindows(executable);
                else UnregisterWindows();
                // Lets Explorer pick up the new icon and command without a restart.
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            else if (OperatingSystem.IsLinux())
            {
                if (registered) RegisterLinux(executable);
                else UnregisterLinux();
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ProgId}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executable)
    {
        using (var extension = Registry.CurrentUser.CreateSubKey($@"{Classes}\{Extension}"))
        {
            extension.SetValue(null, ProgId);
            extension.SetValue("Content Type", "application/json");
        }
        using (var type = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgId}"))
            type.SetValue(null, "Tableau Overboard");
        using (var icon = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgId}\DefaultIcon"))
            icon.SetValue(null, $"\"{executable}\",0");
        using (var command = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgId}\shell\open\command"))
            command.SetValue(null, OpenCommand(executable));
    }

    [SupportedOSPlatform("windows")]
    private static void UnregisterWindows()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{ProgId}", throwOnMissingSubKey: false);
        // Only forget the extension if it still points to Overboard.
        using var extension = Registry.CurrentUser.OpenSubKey($@"{Classes}\{Extension}");
        if (extension?.GetValue(null) as string == ProgId)
            Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{Extension}", throwOnMissingSubKey: false);
    }

    private static void RegisterLinux(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MimePackage)!);
        File.WriteAllText(MimePackage, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
              <mime-type type="{MimeType}">
                <comment>Tableau Overboard</comment>
                <glob pattern="*{Extension}"/>
              </mime-type>
            </mime-info>
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopEntry)!);
        File.WriteAllText(DesktopEntry, $"""
            [Desktop Entry]
            Type=Application
            Name=Overboard
            Comment=Dessiner sur l'écran et tableau blanc
            Exec="{executable}" %f
            MimeType={MimeType};
            Terminal=false
            """);
        RefreshLinuxDatabases();
        RunQuietly("xdg-mime", "default", "overboard.desktop", MimeType);
    }

    private static void UnregisterLinux()
    {
        File.Delete(MimePackage);
        File.Delete(DesktopEntry);
        RefreshLinuxDatabases();
    }

    private static void RefreshLinuxDatabases()
    {
        RunQuietly("update-mime-database", Path.Combine(DataHome, "mime"));
        RunQuietly("update-desktop-database", Path.Combine(DataHome, "applications"));
    }

    /// <summary>Runs a desktop integration tool if it is installed; desktops without it simply skip the step.</summary>
    private static void RunQuietly(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Tool not installed.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
