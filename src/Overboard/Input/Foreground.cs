using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Overboard.Input;

/// <summary>
/// Brings a window in front of the active application when asked from a global mouse button.
/// Windows only lets the process that received the last input take the foreground, and the
/// hook does not count: an empty input event makes Overboard that process (as PowerToys does).
/// </summary>
public static class Foreground
{
    /// <summary>Whether the window is the one the user works in, not only the active one of Overboard.</summary>
    public static bool Contains(Window window) =>
        OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle
            ? GetForegroundWindow() == handle.Handle
            : window.IsActive;

    public static void Bring(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle)
        {
            var input = new Input { Type = 0 };
            SendInput(1, [input], Marshal.SizeOf<Input>());
            SetForegroundWindow(handle.Handle);
        }
        window.Activate();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>INPUT with its mouse member, the largest of the union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
