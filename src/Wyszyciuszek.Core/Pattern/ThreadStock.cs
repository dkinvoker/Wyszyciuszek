using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Nić wzoru porównana z zapasem na szpulce z „Moich nici”.</summary>
/// <param name="Missing">Ile metrów zabraknie (0 — wystarczy).</param>
public readonly record struct StockCheck(int Color, string Name, double NeededMeters, double OnSpool, double Missing)
{
    public bool Short => Missing > 0;
}

/// <summary>
/// Czy nici z pudełka wystarczy na wzór: nić wzoru dopasowana do „Moich nici” po nazwie (i kolorze), zapas na szpulce
/// porównany z szacunkiem zużycia.
/// </summary>
public static class ThreadStock
{
    /// <summary>Nić z pudełka dla koloru wzoru: ta sama nazwa i kolor, a jeśli nie ma — ta sama nazwa.</summary>
    public static SavedThread? Match(PaletteEntry entry, IReadOnlyList<SavedThread> stock)
    {
        SavedThread? byName = null;
        foreach (var t in stock)
        {
            if (!string.Equals(t.Name.Trim(), entry.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)) continue;
            if (Rgb.TryParse(t.Color, out var c) && c == entry.Color) return t;
            byName ??= t;
        }
        return byName;
    }

    /// <summary>Nici wzoru, dla których w pudełku znamy zapas na szpulce (tkanina pomijana).</summary>
    public static List<StockCheck> Check(PatternDocument doc, ThreadUsage[] usage, IReadOnlyList<SavedThread>? stock)
    {
        var result = new List<StockCheck>();
        if (stock is not { Count: > 0 }) return result;
        for (int i = 0; i < doc.Palette.Count && i < usage.Length; i++)
        {
            var e = doc.Palette[i];
            if (usage[i].Meters <= 0 || Match(e, stock) is not { SpoolMeters: { } onSpool }) continue;
            result.Add(new StockCheck(i, e.Name, usage[i].Meters, onSpool, Math.Max(0, usage[i].Meters - onSpool)));
        }
        return result;
    }
}
