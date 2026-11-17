using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Jak ułożyć nici na liście (kolejność szycia = numery na wzorze).</summary>
public enum PaletteOrder
{
    /// <summary>Od spodu do wierzchu: najpierw tło i szerokie plamy, potem to, co na nich leży, na końcu cienkie detale.</summary>
    Layered,
    /// <summary>Od największej powierzchni.</summary>
    Area,
    /// <summary>Od najjaśniejszego koloru.</summary>
    Lightness,
}

/// <summary>
/// Kolejność szycia. Przy hafcie z wolnej ręki każdą nić szyje się raz (wszystkie jej obszary), więc chodzi o kolejność
/// kolorów: to, co „pod spodem”, powinno być wyszyte wcześniej niż to, co na nim leży — wtedy brzegi górnych plam
/// przykrywają brzegi dolnych, a nie odwrotnie. Tkanina zawsze pierwsza (nie szyjemy jej).
/// </summary>
public static class StitchOrder
{
    /// <summary>Nowa kolejność palety: order[nowy indeks] = stary indeks.</summary>
    public static List<int> Compute(byte[] labels, int w, int h, IReadOnlyList<PaletteEntry> palette, PaletteOrder mode)
    {
        if (mode == PaletteOrder.Layered) return Layered(labels, w, h, palette);
        var hist = LabelOps.Histogram(labels, palette.Count);
        return Enumerable.Range(0, palette.Count)
            .OrderBy(i => palette[i].IsFabric ? 0 : 1)
            .ThenByDescending(i => mode == PaletteOrder.Lightness ? palette[i].Color.Luminance : hist[i])
            .ToList();
    }

    /// <summary>
    /// Od spodu do wierzchu. Plama otoczona (w większości obwodu) przez podobnie grubą plamę innego koloru leży „na niej” —
    /// tamten kolor idzie wcześniej. Gdy nic nie leży na niczym, szersze plamy (tło, duże wypełnienia) idą przed
    /// cienkimi (kontury, paski, drobiazgi).
    /// </summary>
    public static List<int> Layered(byte[] labels, int w, int h, IReadOnlyList<PaletteEntry> palette)
    {
        int n = palette.Count;
        if (n == 0) return [];
        var comp = LabelOps.FindComponents(labels, w, h);
        int rc = comp.Count;
        // grubość plamy = średnia odległość jej pikseli od brzegu: pasek ma ok. ¼ szerokości, koło — ⅓ promienia.
        // Średnia (a nie największe koło) nie daje się zmylić zgrubieniu, np. w miejscu, gdzie zbiegają się wąsy.
        var dist = LabelOps.DistanceToBoundary(comp.Map, w, h, frameIsBoundary: false); // tło ciągnie się poza kadr
        var depth = new double[rc];
        for (int p = 0; p < dist.Length; p++) depth[comp.Map[p]] += dist[p];
        for (int r = 0; r < rc; r++) depth[r] /= Math.Max(1, comp.Area[r]);
        var perimeter = new int[rc];
        var touchesFrame = new bool[rc];
        var shared = new Dictionary<long, int>();

        void Edge(int a, int b)
        {
            perimeter[a]++;
            perimeter[b]++;
            long key = a < b ? (long)a * rc + b : (long)b * rc + a;
            shared[key] = shared.GetValueOrDefault(key) + 1;
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x, r = comp.Map[p];
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                {
                    touchesFrame[r] = true;
                    perimeter[r]++;
                }
                if (x + 1 < w && comp.Map[p + 1] != r) Edge(r, comp.Map[p + 1]);
                if (y + 1 < h && comp.Map[p + w] != r) Edge(r, comp.Map[p + w]);
            }

        // dla każdej plamy: sąsiad z najdłuższą wspólną granicą
        var bestNeighbor = new int[rc];
        var bestShared = new int[rc];
        Array.Fill(bestNeighbor, -1);
        foreach (var (key, count) in shared)
        {
            int a = (int)(key / rc), b = (int)(key % rc);
            if (count > bestShared[a]) { bestShared[a] = count; bestNeighbor[a] = b; }
            if (count > bestShared[b]) { bestShared[b] = count; bestNeighbor[b] = a; }
        }

        // under[a, b] — ile powierzchni koloru b leży na kolorze a (a trzeba wyszyć wcześniej)
        var under = new double[n, n];
        var radiusArea = new double[n];
        var area = new double[n];
        for (int r = 0; r < rc; r++)
        {
            int color = comp.Label[r];
            if (color >= n) continue;
            area[color] += comp.Area[r];
            radiusArea[color] += comp.Area[r] * depth[r];
            int c = bestNeighbor[r];
            if (touchesFrame[r] || c < 0 || bestShared[r] * 2 < perimeter[r]) continue;
            int outer = comp.Label[c];
            // cienki kontur (albo kreski) wokół plamy nie są „pod nią” — otaczająca plama musi być podobnie gruba
            if (outer >= n || outer == color || depth[c] < 0.8 * depth[r]) continue;
            under[outer, color] += comp.Area[r];
        }
        var thickness = new double[n];
        for (int c = 0; c < n; c++) thickness[c] = area[c] > 0 ? radiusArea[c] / area[c] : 0;

        var order = Enumerable.Range(0, n).Where(i => palette[i].IsFabric).ToList();
        var remaining = Enumerable.Range(0, n).Where(i => !palette[i].IsFabric).ToList();
        while (remaining.Count > 0)
        {
            // kolor, który najmniej leży na jeszcze niewyszytych; przy remisie — „najbardziej tłowy”: szeroki i duży
            // (grubość × √powierzchni — wielkie tło przed pasem trawy, kropka przed cienką kreską)
            int pick = remaining
                .OrderBy(a => remaining.Where(b => b != a).Sum(b => under[b, a]))
                .ThenByDescending(a => thickness[a] * Math.Sqrt(area[a]))
                .ThenBy(a => a)
                .First();
            order.Add(pick);
            remaining.Remove(pick);
        }
        return order;
    }

    /// <summary>Przestawia paletę i mapę kolorów według kolejności (order[nowy] = stary).</summary>
    public static void Apply(byte[] labels, List<PaletteEntry> palette, IReadOnlyList<int> order)
    {
        var map = new byte[256];
        for (int i = 0; i < 256; i++) map[i] = (byte)i;
        for (int i = 0; i < order.Count; i++) map[order[i]] = (byte)i;
        LabelOps.Remap(labels, map);
        var reordered = order.Select(i => palette[i]).ToList();
        palette.Clear();
        palette.AddRange(reordered);
    }
}
