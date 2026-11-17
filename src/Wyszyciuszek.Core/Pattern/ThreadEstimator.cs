using System.Globalization;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Szacunek dla jednej nici: wypełnienie, linie detali, długość nici i czas szycia.</summary>
public readonly record struct ThreadUsage(double FillCm2, double LineMm, double Meters, double Minutes, int Regions, int Lines)
{
    public static ThreadUsage operator +(ThreadUsage a, ThreadUsage b) =>
        new(a.FillCm2 + b.FillCm2, a.LineMm + b.LineMm, a.Meters + b.Meters, a.Minutes + b.Minutes, a.Regions + b.Regions, a.Lines + b.Lines);
}

/// <summary>
/// Orientacyjne zużycie nici i czas szycia z wolnej ręki. Założenia: gęste wypełnienie (rzędy co 0,5 mm, ścieg ok. 2 mm),
/// linie detali przeszyte dwa razy, spokojne tempo z przesuwaniem tamborka i chwilą na każdy obszar i zmianę nici.
/// </summary>
public static class ThreadEstimator
{
    /// <summary>Odstęp rzędów wypełnienia (mm).</summary>
    public const double RowSpacingMm = 0.5;

    /// <summary>Odstęp rzędów dla wybranej gęstości wypełnienia (mm).</summary>
    public static double RowSpacing(FillDensity fill) => fill switch
    {
        FillDensity.Medium => 1.0,
        FillDensity.Sketch => 2.5,
        _ => RowSpacingMm,
    };

    /// <summary>Opis gęstości do legendy i panelu.</summary>
    public static string Describe(FillDensity fill) => fill switch
    {
        FillDensity.Medium => "średnie wypełnienie",
        FillDensity.Sketch => "szkicowe wypełnienie",
        _ => "gęste wypełnienie",
    };
    /// <summary>Nić „wchodzi” w tkaninę i zawraca — zużycie większe niż sama długość ściegu.</summary>
    public const double TakeUp = 1.3;
    /// <summary>Ile razy przeszywa się linię detalu.</summary>
    public const double LinePasses = 2;
    public const double StitchLengthMm = 2;
    /// <summary>Średnie tempo z przerwami na przesunięcie tamborka (szycie z wolnej ręki jest wolniejsze niż haft maszynowy).</summary>
    public const double StitchesPerMinute = 200;
    public const double ColorChangeMinutes = 3;
    public const double RegionMinutes = 0.5;

    /// <summary>Szacunek dla każdego koloru palety (tkanina — zero, chyba że ma linie detali).</summary>
    public static ThreadUsage[] PerColor(PatternDocument doc, PatternAnalysis analysis)
    {
        int n = doc.Palette.Count;
        var result = new ThreadUsage[n];
        double mmPerPx = doc.Print.WidthMm / Math.Max(1, analysis.Width);
        double mm2PerPx = mmPerPx * mmPerPx;
        var lineMm = new double[n];
        var lineCount = new int[n];
        foreach (var line in doc.Lines)
        {
            if (line.Color < 0 || line.Color >= n) continue;
            lineMm[line.Color] += line.Length * mmPerPx;
            lineCount[line.Color]++;
        }
        for (int c = 0; c < n; c++)
        {
            bool fabric = doc.Palette[c].IsFabric;
            double fillMm2 = fabric || c >= analysis.ColorArea.Length ? 0 : analysis.ColorArea[c] * mm2PerPx;
            int regions = fabric || c >= analysis.ColorRegionCount.Length ? 0 : analysis.ColorRegionCount[c];
            double sewnMm = fillMm2 / RowSpacing(doc.Print.Fill) + lineMm[c] * LinePasses;
            if (sewnMm <= 0)
            {
                result[c] = new ThreadUsage(0, 0, 0, 0, 0, 0);
                continue;
            }
            double minutes = sewnMm / StitchLengthMm / StitchesPerMinute + (regions + lineCount[c]) * RegionMinutes + ColorChangeMinutes;
            result[c] = new ThreadUsage(fillMm2 / 100, lineMm[c], sewnMm * TakeUp / 1000, minutes, regions, lineCount[c]);
        }
        return result;
    }

    public static ThreadUsage Total(IEnumerable<ThreadUsage> usage) => usage.Aggregate(default(ThreadUsage), (a, b) => a + b);

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>„0,4 m”, „12 m”.</summary>
    public static string Meters(double m) => m < 0.05 ? "<0,1 m" : (m < 10 ? m.ToString("0.0", Pl) : m.ToString("0", Pl)) + " m";

    /// <summary>„25 min”, „1 h 10 min”, „6 h”.</summary>
    public static string Duration(double minutes)
    {
        int total = (int)Math.Round(minutes / 5) * 5;
        if (minutes < 10) total = Math.Max(1, (int)Math.Round(minutes));
        if (total < 60) return $"{total} min";
        int h = total / 60, min = total % 60;
        return min == 0 ? $"{h} h" : $"{h} h {min} min";
    }
}
