using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;

namespace Wyszyciuszek.Core.Project;

/// <summary>
/// Plik projektu „.wyszycie” — zwykłe archiwum ZIP: project.json (paleta, ustawienia), labels.bin (mapa kolorów
/// po wszystkich poprawkach) i source.* (oryginalny obraz, żeby dało się uprościć od nowa).
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".wyszycie";
    /// <summary>
    /// Najnowszy format: 2 = linie detali i kierunki ściegu. Projekt bez nich zapisuje się jako 1, więc otworzy go też
    /// starsza wersja programu.
    /// </summary>
    public const int FormatVersion = 2;

    public static void Save(PatternDocument doc, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var dto = new ProjectDto
        {
            Format = doc.Lines.Count > 0 || doc.StitchHints.Count > 0 ? 2 : 1,
            Title = doc.Title,
            Width = doc.Width,
            Height = doc.Height,
            Palette = doc.Palette.Select(p => new PaletteDto { Color = p.Color.Hex, Name = p.Name, Fabric = p.IsFabric }).ToList(),
            Options = doc.Options,
            Print = doc.Print,
            Lines = doc.Lines.Count == 0 ? null : doc.Lines.Select(l => new LineDto
            {
                Color = l.Color,
                Points = l.Points.SelectMany(p => new[] { MathF.Round(p.X, 2), MathF.Round(p.Y, 2) }).ToArray(),
            }).ToList(),
            Fabric = doc.FabricColor?.Hex,
            Stitches = doc.StitchHints.Count == 0 ? null : doc.StitchHints.Select(h => new StitchDto
            {
                X = MathF.Round(h.Point.X, 2), Y = MathF.Round(h.Point.Y, 2), Angle = MathF.Round(h.Angle, 1),
            }).ToList(),
        };
        var entry = zip.CreateEntry("project.json", CompressionLevel.Optimal);
        using (var s = entry.Open()) JsonSerializer.Serialize(s, dto, ProjectJson.Default.ProjectDto);

        entry = zip.CreateEntry("labels.bin", CompressionLevel.Optimal);
        using (var s = entry.Open()) s.Write(doc.Labels);

        if (doc.SourceImage is { Length: > 0 } src)
        {
            // obraz (JPG/PNG) i tak jest skompresowany
            entry = zip.CreateEntry("source" + GuessExtension(src), CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(src);
        }
    }

    public static void Save(PatternDocument doc, string path)
    {
        // zapis do pliku tymczasowego i podmiana — przerwany zapis nie niszczy poprzedniej wersji
        var temp = path + ".tmp";
        using (var fs = File.Create(temp)) Save(doc, fs);
        File.Move(temp, path, overwrite: true);
    }

    public static PatternDocument Load(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var jsonEntry = zip.GetEntry("project.json") ?? throw new InvalidDataException("To nie jest plik projektu Wyszyciuszka (brak project.json).");
        ProjectDto dto;
        using (var s = jsonEntry.Open())
            dto = JsonSerializer.Deserialize(s, ProjectJson.Default.ProjectDto) ?? throw new InvalidDataException("Pusty opis projektu.");
        if (dto.Format > FormatVersion)
            throw new InvalidDataException("Ten projekt zapisała nowsza wersja Wyszyciuszka — zaktualizuj program.");

        var doc = new PatternDocument
        {
            Title = dto.Title ?? "Wzór",
            Width = dto.Width,
            Height = dto.Height,
            Palette = (dto.Palette ?? []).Select(p => new PaletteEntry
            {
                Color = Rgb.TryParse(p.Color, out var c) ? c : Rgb.Black,
                Name = p.Name ?? "",
                IsFabric = p.Fabric,
            }).ToList(),
            Options = dto.Options ?? new SimplifyOptions(),
            Print = dto.Print ?? new PrintSettings(),
            Lines = (dto.Lines ?? [])
                .Where(l => l.Points is { Length: >= 4 } && l.Color >= 0 && l.Color < (dto.Palette?.Count ?? 0))
                .Select(l => new DetailLine
                {
                    Color = l.Color,
                    Points = Enumerable.Range(0, l.Points!.Length / 2).Select(i => new Vector2(l.Points[2 * i], l.Points[2 * i + 1])).ToList(),
                }).ToList(),
            StitchHints = (dto.Stitches ?? []).Select(s => new StitchHint { Point = new Vector2(s.X, s.Y), Angle = s.Angle }).ToList(),
            FabricColor = Rgb.TryParse(dto.Fabric, out var fabric) ? fabric : null,
        };

        if (zip.GetEntry("labels.bin") is { } labels)
        {
            using var s = labels.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            doc.Labels = ms.ToArray();
        }
        if (doc.Labels.Length != doc.Width * doc.Height)
            throw new InvalidDataException("Uszkodzony projekt: mapa kolorów nie pasuje do rozmiaru wzoru.");
        if (doc.Palette.Count > 0 && doc.Labels.Any(l => l >= doc.Palette.Count))
            throw new InvalidDataException("Uszkodzony projekt: mapa kolorów odwołuje się do nieistniejącego koloru.");

        if (zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("source", StringComparison.OrdinalIgnoreCase)) is { } source)
        {
            using var s = source.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            doc.SourceImage = ms.ToArray();
        }
        return doc;
    }

    /// <summary>Sam tytuł projektu (bez wczytywania mapy kolorów i obrazka); null — to nie jest plik projektu.</summary>
    public static string? ReadTitle(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("project.json") is not { } entry) return null;
            using var s = entry.Open();
            var dto = JsonSerializer.Deserialize(s, ProjectJson.Default.ProjectDto);
            return dto is null ? null : string.IsNullOrWhiteSpace(dto.Title) ? "Wzór" : dto.Title;
        }
        catch
        {
            return null;
        }
    }

    public static PatternDocument Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Rozszerzenie po nagłówku pliku (tylko informacyjnie — w archiwum).</summary>
    public static string GuessExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G') return ".png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return ".bmp";
        if (data.Length >= 4 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F') return ".gif";
        if (data.Length >= 12 && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P') return ".webp";
        return ".img";
    }
}

internal sealed class ProjectDto
{
    public int Format { get; set; }
    public string? Title { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<PaletteDto>? Palette { get; set; }
    public SimplifyOptions? Options { get; set; }
    public PrintSettings? Print { get; set; }
    public List<LineDto>? Lines { get; set; }
    public List<StitchDto>? Stitches { get; set; }
    /// <summary>Kolor materiału (#RRGGBB); starsze wersje go pomijają — to tylko podgląd.</summary>
    public string? Fabric { get; set; }
}

internal sealed class LineDto
{
    public int Color { get; set; }
    /// <summary>x0, y0, x1, y1, … w pikselach obrazu roboczego.</summary>
    public float[]? Points { get; set; }
}

internal sealed class StitchDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Angle { get; set; }
}

internal sealed class PaletteDto
{
    public string? Color { get; set; }
    public string? Name { get; set; }
    public bool Fabric { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectDto))]
internal sealed partial class ProjectJson : JsonSerializerContext
{
}
