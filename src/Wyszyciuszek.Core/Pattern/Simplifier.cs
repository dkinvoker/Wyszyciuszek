using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Wynik upraszczania: mapa kolorów (indeks palety dla każdego piksela) i paleta.</summary>
public sealed record SimplifyResult(byte[] Labels, int Width, int Height, List<PaletteEntry> Palette);

/// <summary>
/// Upraszczanie obrazu do wzoru: wygładzenie z zachowaniem krawędzi → dobór kilku kolorów → przypisanie pikseli →
/// wyrównanie krawędzi → wtopienie drobnych plam w sąsiadów → paleta posortowana od największej powierzchni.
/// </summary>
public static class Simplifier
{
    public const int MaxColors = 24;

    /// <summary>Obraz roboczy: zmniejszony oryginał (dłuższy bok = <see cref="SimplifyOptions.WorkingSize"/>).</summary>
    public static ImageRgb PrepareWorkingImage(ImageRgb source, SimplifyOptions options) => source.Downscale(options.WorkingSize);

    public static SimplifyResult Run(ImageRgb working, SimplifyOptions options, IReadOnlyList<ThreadColor>? myThreads = null, CancellationToken ct = default)
    {
        int w = working.Width, h = working.Height;
        var lab = LabImage.From(working);
        var (radius, sigma, passes, modePasses) = options.SmoothingParameters();
        for (int p = 0; p < passes; p++) lab = Filters.Bilateral(lab, radius, sigma, ct);
        ct.ThrowIfCancellationRequested();

        int k = Math.Clamp(options.Colors, 2, MaxColors);
        var centers = Quantizer.KMeans(lab, k, options.Seed, ct: ct);
        List<PaletteEntry> palette;
        if (options.Source == PaletteSource.MyThreads && myThreads is { Count: > 0 })
        {
            var threadLab = myThreads.Select(t => t.Color.ToLab()).ToList();
            var chosen = Quantizer.MatchToThreads(centers, threadLab);
            centers = chosen.Select(i => threadLab[i]).ToArray();
            palette = chosen.Select(i => new PaletteEntry { Color = myThreads[i].Color, Name = myThreads[i].Name }).ToList();
        }
        else
        {
            palette = centers.Select(c =>
            {
                var rgb = c.ToRgb();
                return new PaletteEntry { Color = rgb, Name = ThreadCatalog.NameFor(rgb) };
            }).ToList();
        }
        ct.ThrowIfCancellationRequested();

        var labels = Quantizer.Assign(lab, centers, ct);
        labels = LabelOps.ModeFilter(labels, w, h, modePasses);
        labels = LabelOps.PluralityFilter(labels, w, h, 2, options.PluralityPasses);
        ct.ThrowIfCancellationRequested();
        LabelOps.RemoveThinRegions(labels, w, h, options.MinRegionRadius, centers, ct: ct);
        LabelOps.RemoveSmallRegions(labels, w, h, options.MinRegionArea(w, h), centers, ct: ct);

        Normalize(labels, palette);
        MakeNamesUnique(palette);
        if (options.BackgroundAsFabric && LabelOps.DominantBorderLabel(labels, w, h) is var bg and >= 0 && bg < palette.Count)
            palette[bg].IsFabric = true;
        // kolejność szycia: od spodu do wierzchu (tkanina pierwsza)
        ct.ThrowIfCancellationRequested();
        StitchOrder.Apply(labels, palette, StitchOrder.Layered(labels, w, h, palette));
        return new SimplifyResult(labels, w, h, palette);
    }

    /// <summary>Usuwa nieużywane kolory i ustawia paletę od największej powierzchni (kolejność szycia: najpierw duże plamy).</summary>
    public static void Normalize(byte[] labels, List<PaletteEntry> palette)
    {
        var hist = LabelOps.Histogram(labels, palette.Count);
        var order = Enumerable.Range(0, palette.Count).Where(i => hist[i] > 0).OrderByDescending(i => hist[i]).ToList();
        var map = new byte[256];
        for (int i = 0; i < order.Count; i++) map[order[i]] = (byte)i;
        LabelOps.Remap(labels, map);
        var reordered = order.Select(i => palette[i]).ToList();
        palette.Clear();
        palette.AddRange(reordered);
    }

    /// <summary>„Zielony”, „Zielony” → „Zielony”, „Zielony 2” — żeby w legendzie nie było dwóch takich samych nazw.</summary>
    public static void MakeNamesUnique(List<PaletteEntry> palette)
    {
        var seen = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var p in palette)
        {
            var name = string.IsNullOrWhiteSpace(p.Name) ? ThreadCatalog.NameFor(p.Color) : p.Name.Trim();
            if (seen.TryGetValue(name, out var n))
            {
                seen[name] = n + 1;
                p.Name = $"{name} {n + 1}";
            }
            else
            {
                seen[name] = 1;
                p.Name = name;
            }
        }
    }
}
