using System.Text.Json;
using System.Text.Json.Serialization;
using Overboard.Ink;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Overboard.Board;

/// <summary>Everything a board file holds.</summary>
public sealed record BoardContent(IReadOnlyList<InkElement> Elements, bool Dark, bool Grid, Vector Offset, double Zoom);

/// <summary>Where the board being edited comes from: kept in the autosave only.</summary>
public sealed record BoardSession(string? FilePath, bool Modified);

/// <summary>
/// Reads and writes .overboard files: JSON, element coordinates in board units.
/// Stroke points are flat arrays: x, y, pressure triples for pen strokes, x, y pairs otherwise.
/// </summary>
public static class BoardFile
{
    public const string Extension = ".overboard";
    private const string Format = "overboard";
    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Writes to a temporary file first, so a failure never leaves a truncated board.</summary>
    public static void Write(string path, BoardContent content, BoardSession? session = null)
    {
        var file = new FileDto
        {
            Format = Format,
            Version = Version,
            Background = content.Dark ? "dark" : "light",
            Grid = content.Grid,
            View = new ViewDto { X = Round(content.Offset.X), Y = Round(content.Offset.Y), Zoom = Math.Round(content.Zoom, 4) },
            Elements = [.. content.Elements.Select(ToDto)],
            Session = session is null ? null : new SessionDto { Path = session.FilePath, Modified = session.Modified },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(file, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <exception cref="InvalidDataException">The file is not a board this version can read.</exception>
    public static (BoardContent Content, BoardSession? Session) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var file = JsonSerializer.Deserialize<FileDto>(bytes, Options);
            if (file?.Format != Format) throw new InvalidDataException("Ce fichier n'est pas un tableau Overboard.");
            if (file.Version > Version) throw new InvalidDataException("Ce tableau vient d'une version plus récente d'Overboard.");
            var view = file.View ?? new ViewDto();
            var content = new BoardContent([.. file.Elements.Select(FromDto)], file.Background == "dark", file.Grid,
                new Vector(view.X, view.Y), view.Zoom > 0 ? view.Zoom : 1);
            var session = file.Session is { } s ? new BoardSession(s.Path, s.Modified) : null;
            return (content, session);
        }
        catch (Exception e) when (e is JsonException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Ce fichier est endommagé ou n'est pas un tableau Overboard.", e);
        }
    }

    private static ElementDto ToDto(InkElement element) => element switch
    {
        StrokeElement s => new StrokeDto
        {
            Color = Hex(s.Stroke.Color),
            Size = Math.Round(s.Stroke.Size, 3),
            Pressure = s.Stroke.UsesPressure,
            Opacity = s.Opacity,
            Points = [.. s.Stroke.Points.SelectMany(p => s.Stroke.UsesPressure
                ? new[] { Round(p.X), Round(p.Y), Math.Round(p.Pressure, 3) }
                : [Round(p.X), Round(p.Y)])],
        },
        ShapeElement s => new ShapeDto
        {
            Color = Hex(s.Color),
            Kind = s.Kind,
            Thickness = Math.Round(s.Thickness, 3),
            Start = Pair(s.Start),
            End = Pair(s.End),
        },
        TextElement t => new TextDto
        {
            Color = Hex(t.Color),
            Text = t.Text,
            FontSize = Math.Round(t.FontSize, 3),
            Origin = Pair(t.Origin),
        },
        ImageElement i => new ImageDto
        {
            Origin = Pair(i.Bounds.Position),
            Size = [Round(i.Bounds.Width), Round(i.Bounds.Height)],
            Png = i.Png,
        },
        _ => throw new NotSupportedException(element.GetType().Name),
    };

    private static InkElement FromDto(ElementDto dto) => dto switch
    {
        StrokeDto s => new StrokeElement(
            Stroke.FromPoints(Color.Parse(s.Color!), s.Size, s.Pressure, StrokePoints(s.Points, s.Pressure)), s.Opacity),
        ShapeDto s => new ShapeElement(s.Kind, ToPoint(s.Start), ToPoint(s.End), Color.Parse(s.Color!), s.Thickness),
        TextDto t => new TextElement(t.Text, ToPoint(t.Origin), Color.Parse(t.Color!), t.FontSize),
        ImageDto i => new ImageElement(new Bitmap(new MemoryStream(i.Png)), new Rect(ToPoint(i.Origin), ToSize(i.Size)), i.Png),
        _ => throw new NotSupportedException(dto.GetType().Name),
    };

    private static IEnumerable<InkPoint> StrokePoints(double[] values, bool pressure)
    {
        var stride = pressure ? 3 : 2;
        for (var i = 0; i + stride <= values.Length; i += stride)
            yield return new InkPoint(values[i], values[i + 1], pressure ? values[i + 2] : 0.5);
    }

    private static double Round(double value) => Math.Round(value, 2);
    private static double[] Pair(Point point) => [Round(point.X), Round(point.Y)];

    private static Point ToPoint(double[] values) =>
        values is [var x, var y] ? new Point(x, y) : throw new FormatException("Point attendu : [x, y]");

    private static Size ToSize(double[] values) =>
        values is [var width, var height] ? new Size(width, height) : throw new FormatException("Taille attendue : [largeur, hauteur]");

    private static string Hex(Color c) =>
        c.A == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";

    private sealed class FileDto
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string Background { get; set; } = "light";
        public bool Grid { get; set; } = true;
        public ViewDto? View { get; set; }
        public List<ElementDto> Elements { get; set; } = [];
        public SessionDto? Session { get; set; }
    }

    private sealed class ViewDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Zoom { get; set; } = 1;
    }

    private sealed class SessionDto
    {
        public string? Path { get; set; }
        public bool Modified { get; set; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(StrokeDto), "stroke")]
    [JsonDerivedType(typeof(ShapeDto), "shape")]
    [JsonDerivedType(typeof(TextDto), "text")]
    [JsonDerivedType(typeof(ImageDto), "image")]
    private abstract class ElementDto
    {
        public string? Color { get; set; }
    }

    /// <summary>A picture: PNG bytes, written as base64.</summary>
    private sealed class ImageDto : ElementDto
    {
        public double[] Origin { get; set; } = [];
        public double[] Size { get; set; } = [];
        public byte[] Png { get; set; } = [];
    }

    private sealed class StrokeDto : ElementDto
    {
        public double Size { get; set; }
        public bool Pressure { get; set; }
        public double Opacity { get; set; } = 1;
        public double[] Points { get; set; } = [];
    }

    private sealed class ShapeDto : ElementDto
    {
        public ShapeKind Kind { get; set; }
        public double Thickness { get; set; }
        public double[] Start { get; set; } = [];
        public double[] End { get; set; } = [];
    }

    private sealed class TextDto : ElementDto
    {
        public string Text { get; set; } = "";
        public double FontSize { get; set; }
        public double[] Origin { get; set; } = [];
    }
}
