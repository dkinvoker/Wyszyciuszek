using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Obszar za wąski na wygodne wypełnienie ściegiem.</summary>
/// <param name="WidthMm">Szerokość w najszerszym miejscu na tkaninie.</param>
/// <param name="Stripe">Wydłużony (pasek, kreska) — dobry kandydat na linię detalu.</param>
public readonly record struct NarrowRegion(int Region, double WidthMm, bool Stripe);

/// <summary>
/// Wąskie obszary: w rzeczywistym rozmiarze węższe niż ok. 3 mm. Takie plamy trudno wypełnić ściegiem z wolnej ręki —
/// lepiej je przeszyć linią albo wtopić w sąsiadów.
/// </summary>
public static class NarrowRegions
{
    public const double DefaultMinWidthMm = 3;

    /// <summary>Szerokość obszaru (mm): średnica największego koła, które się w nim mieści.</summary>
    public static double WidthMm(RegionInfo r, double mmPerPixel) => Math.Max(1, 2 * r.LabelRadius - 0.5) * mmPerPixel;

    /// <summary>Wąskie obszary do szycia (tkanina pomijana).</summary>
    public static List<NarrowRegion> Find(PatternDocument doc, PatternAnalysis analysis, double minWidthMm = DefaultMinWidthMm)
    {
        var result = new List<NarrowRegion>();
        if (analysis.Width <= 0) return result;
        double mmPerPx = doc.Print.WidthMm / analysis.Width;
        foreach (var r in analysis.Regions)
        {
            if (r.Color >= doc.Palette.Count || doc.Palette[r.Color].IsFabric) continue;
            double width = WidthMm(r, mmPerPx);
            if (width >= minWidthMm) continue;
            double widthPx = Math.Max(1, 2 * r.LabelRadius - 0.5);
            bool stripe = r.Area / (widthPx * widthPx) >= 3; // długość ≈ pole / szerokość
            result.Add(new NarrowRegion(r.Id, width, stripe));
        }
        return result;
    }
}
