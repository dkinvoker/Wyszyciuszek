using System.IO.Compression;
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
    public const int FormatVersion = 1;

    public static void Save(PatternDocument doc, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var dto = new ProjectDto
        {
            Format = FormatVersion,
            Title = doc.Title,
            Width = doc.Width,
            Height = doc.Height,
            Palette = doc.Palette.Select(p => new PaletteDto { Color = p.Color.Hex, Name = p.Name, Fabric = p.IsFabric }).ToList(),
            Options = doc.Options,
            Print = doc.Print,
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
