using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Materiał (kolor tkaniny) z listy gotowych.</summary>
public sealed record FabricPreset(string Name, Rgb Color);

/// <summary>Typowe kolory materiałów do haftu i sprawdzanie, czy nici będą na nich widoczne.</summary>
public static class Fabrics
{
    public static IReadOnlyList<FabricPreset> All { get; } =
    [
        new("Biel", Rgb.Parse("#FBFBF8")),
        new("Ecru", Rgb.Parse("#EFE6D2")),
        new("Len naturalny", Rgb.Parse("#C9B697")),
        new("Szary melanż", Rgb.Parse("#A9A9A6")),
        new("Grafit", Rgb.Parse("#47484B")),
        new("Czerń", Rgb.Parse("#161616")),
        new("Jeans", Rgb.Parse("#4B6384")),
        new("Granat", Rgb.Parse("#1F2A44")),
        new("Butelkowa zieleń", Rgb.Parse("#1E4D3A")),
        new("Oliwka", Rgb.Parse("#6B6B3A")),
        new("Czerwień", Rgb.Parse("#A3202B")),
        new("Pudrowy róż", Rgb.Parse("#E7C3C3")),
        new("Musztarda", Rgb.Parse("#C99A2E")),
        new("Błękit", Rgb.Parse("#A9C8E2")),
    ];

    /// <summary>Nazwa materiału dla koloru z listy, a dla innego — zapis szesnastkowy.</summary>
    public static string NameFor(Rgb color) => All.FirstOrDefault(f => f.Color == color)?.Name ?? color.Hex;

    /// <summary>Poniżej tej różnicy (CIEDE2000) nić zlewa się z tkaniną.</summary>
    public const double WeakContrast = 12;

    /// <summary>Kolor, na którym będzie się szyć: ustalony materiał albo kolor oznaczony jako tkanina.</summary>
    public static Rgb? Effective(PatternDocument doc) =>
        doc.FabricColor ?? doc.Palette.FirstOrDefault(p => p.IsFabric)?.Color;

    /// <summary>Nici (indeksy palety), które słabo odcinają się od tkaniny.</summary>
    public static List<int> LowContrast(PatternDocument doc, double threshold = WeakContrast)
    {
        var result = new List<int>();
        if (Effective(doc) is not { } fabric) return result;
        var lab = fabric.ToLab();
        for (int i = 0; i < doc.Palette.Count; i++)
        {
            var e = doc.Palette[i];
            if (!e.IsFabric && ColorMath.DeltaE2000(e.Color.ToLab(), lab) < threshold) result.Add(i);
        }
        return result;
    }

    /// <summary>Kolor wzoru najbardziej podobny do materiału, jeśli jest naprawdę blisko (do oznaczenia jako tkanina).</summary>
    public static int Closest(PatternDocument doc, Rgb fabric, double maxDistance = 10)
    {
        var lab = fabric.ToLab();
        int best = -1;
        double bestD = maxDistance;
        for (int i = 0; i < doc.Palette.Count; i++)
        {
            double d = ColorMath.DeltaE2000(doc.Palette[i].Color.ToLab(), lab);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }
}
