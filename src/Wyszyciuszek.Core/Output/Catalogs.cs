namespace Wyszyciuszek.Core.Output;

/// <summary>Format papieru (mm, pionowo).</summary>
public sealed record PaperSize(string Id, string Name, double Width, double Height);

public static class Papers
{
    public static IReadOnlyList<PaperSize> All { get; } =
    [
        new("A4", "A4 (210 × 297 mm)", 210, 297),
        new("A3", "A3 (297 × 420 mm)", 297, 420),
        new("Letter", "Letter (216 × 279 mm)", 215.9, 279.4),
        new("A5", "A5 (148 × 210 mm)", 148, 210),
    ];

    public static PaperSize Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

/// <summary>Tamborek (wewnętrzne pole robocze w mm).</summary>
public sealed record Hoop(string Id, string Name, double Width, double Height, bool Round)
{
    /// <summary>Czy wzór o danym rozmiarze mieści się w tamborku (z marginesem; prostokątny wolno obrócić).</summary>
    public bool Fits(double w, double h, double margin = 5)
    {
        if (Round)
        {
            double d = Width - 2 * margin;
            return w * w + h * h <= d * d;
        }
        double iw = Width - 2 * margin, ih = Height - 2 * margin;
        return (w <= iw && h <= ih) || (w <= ih && h <= iw);
    }

    /// <summary>Największa szerokość wzoru o proporcjach w:h, który mieści się w tamborku.</summary>
    public double MaxWidthFor(double aspectHOverW, double margin = 10)
    {
        if (Round)
        {
            double d = Width - 2 * margin;
            return d / Math.Sqrt(1 + aspectHOverW * aspectHOverW);
        }
        double iw = Width - 2 * margin, ih = Height - 2 * margin;
        double a = Math.Min(iw, ih / aspectHOverW);
        double b = Math.Min(ih, iw / aspectHOverW); // obrócony tamborek
        return Math.Max(a, b);
    }
}

public static class Hoops
{
    public static IReadOnlyList<Hoop> All { get; } =
    [
        new("r10", "Okrągły Ø 10 cm", 100, 100, true),
        new("r13", "Okrągły Ø 13 cm", 130, 130, true),
        new("r15", "Okrągły Ø 15 cm", 150, 150, true),
        new("r18", "Okrągły Ø 18 cm", 180, 180, true),
        new("r20", "Okrągły Ø 20 cm", 200, 200, true),
        new("r25", "Okrągły Ø 25 cm", 250, 250, true),
        new("r30", "Okrągły Ø 30 cm", 300, 300, true),
        new("10x10", "Prostokątny 10 × 10 cm", 100, 100, false),
        new("13x18", "Prostokątny 13 × 18 cm", 130, 180, false),
        new("16x26", "Prostokątny 16 × 26 cm", 160, 260, false),
        new("20x30", "Prostokątny 20 × 30 cm", 200, 300, false),
    ];

    public static Hoop? Get(string? id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(h => h.Id == id);
}
