using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Overboard.Overlay;

/// <summary>Takes a picture of part of the screen: GDI on Windows, the system's screenshot tool elsewhere.</summary>
public static class ScreenGrabber
{
    /// <param name="area">Physical pixels, in virtual screen coordinates.</param>
    /// <param name="scaling">The screen's scale factor, for tools that work in logical units.</param>
    /// <returns>The picture, or null when the platform does not allow it.</returns>
    public static Bitmap? Grab(PixelRect area, double scaling)
    {
        if (OperatingSystem.IsWindows()) return GrabWindows(area);
        var file = Path.Combine(Path.GetTempPath(), $"overboard-{Guid.NewGuid():N}.png");
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var points = area.ToRect(scaling);
                Run("screencapture", "-x", $"-R{points.X},{points.Y},{points.Width},{points.Height}", file);
            }
            else if (!Run("grim", "-g", $"{area.X},{area.Y} {area.Width}x{area.Height}", file))
            {
                Run("import", "-window", "root", "-crop", $"{area.Width}x{area.Height}+{area.X}+{area.Y}", file);
            }
            return File.Exists(file) ? new Bitmap(file) : null;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Runs a command line tool. Returns false if it is missing or fails.</summary>
    private static bool Run(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(TimeSpan.FromSeconds(5))) return false;
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static Bitmap? GrabWindows(PixelRect area)
    {
        const uint SrcCopy = 0x00CC0020;
        // Includes layered (translucent) windows.
        const uint CaptureBlt = 0x40000000;

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = area.Width,
            Height = -area.Height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var dib = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero) return null;
            var previous = SelectObject(memory, dib);
            var copied = BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy | CaptureBlt);
            SelectObject(memory, previous);
            if (!copied) return null;

            var stride = area.Width * 4;
            var pixels = new byte[stride * area.Height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves the alpha channel at zero.
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var bitmap = new WriteableBitmap(area.Size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                for (var row = 0; row < area.Height; row++)
                    Marshal.Copy(pixels, row * stride, buffer.Address + row * buffer.RowBytes, stride);
            }
            return bitmap;
        }
        finally
        {
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits,
        IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX,
        int sourceY, uint operation);
}
