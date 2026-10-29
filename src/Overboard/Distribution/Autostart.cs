using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Overboard.Distribution;

/// <summary>
/// Starts Overboard with the user's session: the Run registry key on Windows,
/// a LaunchAgent on macOS, an XDG autostart entry on Linux. Points to the running executable.
/// </summary>
public static class Autostart
{
    private const string Name = "Overboard";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable");

    private static string MacAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "io.github.pfauwadel.overboard.plist");

    private static string LinuxEntryPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "overboard.desktop");

    /// <summary>Whether this executable is set to start with the session (an entry for an older copy does not count).</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows()) return ReadRunValue() == Quoted(Executable);
                var path = OperatingSystem.IsMacOS() ? MacAgentPath : LinuxEntryPath;
                return File.Exists(path) && File.ReadAllText(path).Contains(Executable);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>Returns whether the change was made.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WriteRunValue(enabled ? Quoted(Executable) : null);
            else if (OperatingSystem.IsMacOS()) WriteOrDelete(MacAgentPath, enabled ? MacAgent() : null);
            else WriteOrDelete(LinuxEntryPath, enabled ? LinuxEntry() : null);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static string Quoted(string path) => $"\"{path}\"";

    [SupportedOSPlatform("windows")]
    private static string? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteRunValue(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (command is null) key.DeleteValue(Name, throwOnMissingValue: false);
        else key.SetValue(Name, command);
    }

    private static void WriteOrDelete(string path, string? content)
    {
        if (content is null)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string MacAgent() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>io.github.pfauwadel.overboard</string>
          <key>ProgramArguments</key><array><string>{SecurityElement.Escape(Executable)}</string></array>
          <key>RunAtLoad</key><true/>
        </dict>
        </plist>
        """;

    private static string LinuxEntry() => $"""
        [Desktop Entry]
        Type=Application
        Name=Overboard
        Comment=Dessiner sur l'écran et tableau blanc
        Exec="{Executable}"
        X-GNOME-Autostart-enabled=true
        """;
}
