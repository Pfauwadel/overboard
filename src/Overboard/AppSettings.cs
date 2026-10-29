using System.Text.Json;
using Overboard.Distribution;
using SharpHook.Data;

namespace Overboard;

/// <summary>Preferences kept between runs, in settings.json next to the board's autosave.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppInfo.DataDirectory, "settings.json");

    /// <summary>Mouse button that switches drawing mode on and off, from anywhere. NoButton for none.</summary>
    public MouseButton ToggleMouseButton { get; set; } = MouseButton.Button5;

    /// <summary>Whether to ask GitHub daily for a newer release: Overboard's only network access.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience: keep running with the current ones.
        }
    }
}
