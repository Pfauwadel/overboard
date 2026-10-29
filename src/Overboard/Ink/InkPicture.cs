using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Overboard.Ink;

/// <summary>Elements to pictures (export, system clipboard) and pictures to load (paste, drop).</summary>
public static class InkPicture
{
    /// <summary>Exports at twice the on-screen size, for sharp text and strokes.</summary>
    private const double Scale = 2;
    /// <summary>Largest side of an exported picture, in pixels.</summary>
    private const int MaxSide = 8192;
    private const double Margin = 24;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>Renders the elements around their bounds, on <paramref name="background"/> (transparent if null).</summary>
    public static RenderTargetBitmap Render(IReadOnlyList<InkElement> elements, IBrush? background, bool darkBackground)
    {
        var area = elements.Select(e => e.Bounds).Aggregate((a, b) => a.Union(b)).Inflate(Margin);
        var scale = Math.Min(Scale, MaxSide / Math.Max(area.Width, area.Height));
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(area.Width * scale)), Math.Max(1, (int)Math.Ceiling(area.Height * scale)));
        var bitmap = new RenderTargetBitmap(size);
        using var context = bitmap.CreateDrawingContext();
        if (background != null) context.FillRectangle(background, new Rect(size.ToSize(1)));
        using (context.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y) * Matrix.CreateScale(scale, scale)))
        {
            foreach (var element in elements)
                element.Render(context, darkBackground);
        }
        return bitmap;
    }

    public static bool IsPicture(IStorageItem item) =>
        item is IStorageFile && Extensions.Contains(Path.GetExtension(item.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>The pictures among <paramref name="items"/>, skipping files that cannot be read.</summary>
    public static List<Bitmap> Load(IEnumerable<IStorageItem> items)
    {
        var pictures = new List<Bitmap>();
        foreach (var item in items.Where(IsPicture))
        {
            if (item.TryGetLocalPath() is not { } path) continue;
            try
            {
                pictures.Add(new Bitmap(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                // Not a picture after all, or unreadable: leave it out.
            }
        }
        return pictures;
    }
}
