using System.Numerics;

namespace Wyszyciuszek.Core.Imaging;

/// <summary>Spójne obszary jednego koloru (4-sąsiedztwo) w mapie kolorów.</summary>
public sealed class Components
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Numer obszaru dla każdego piksela.</summary>
    public required int[] Map { get; init; }
    /// <summary>Kolor (indeks palety) każdego obszaru.</summary>
    public required byte[] Label { get; init; }
    /// <summary>Powierzchnia każdego obszaru w pikselach.</summary>
    public required int[] Area { get; init; }

    public int Count => Label.Length;
}

/// <summary>Operacje na mapie kolorów (bajt = indeks palety dla każdego piksela).</summary>
public static class LabelOps
{
    /// <summary>Dzieli mapę na spójne obszary jednego koloru (sąsiedztwo przez krawędź, nie przez róg).</summary>
    public static Components FindComponents(byte[] labels, int w, int h)
    {
        var map = new int[w * h];
        Array.Fill(map, -1);
        var labelList = new List<byte>();
        var areaList = new List<int>();
        var stack = new int[w * h];
        for (int start = 0; start < w * h; start++)
        {
            if (map[start] >= 0) continue;
            int id = labelList.Count;
            byte lab = labels[start];
            int sp = 0, area = 0;
            stack[sp++] = start;
            map[start] = id;
            while (sp > 0)
            {
                int p = stack[--sp];
                area++;
                int x = p % w, y = p / w;
                if (x > 0 && map[p - 1] < 0 && labels[p - 1] == lab) { map[p - 1] = id; stack[sp++] = p - 1; }
                if (x < w - 1 && map[p + 1] < 0 && labels[p + 1] == lab) { map[p + 1] = id; stack[sp++] = p + 1; }
                if (y > 0 && map[p - w] < 0 && labels[p - w] == lab) { map[p - w] = id; stack[sp++] = p - w; }
                if (y < h - 1 && map[p + w] < 0 && labels[p + w] == lab) { map[p + w] = id; stack[sp++] = p + w; }
            }
            labelList.Add(lab);
            areaList.Add(area);
        }
        return new Components { Width = w, Height = h, Map = map, Label = [.. labelList], Area = [.. areaList] };
    }

    /// <summary>
    /// Filtr większościowy 3×3: piksel przyjmuje kolor, który ma większość (≥5 z 9) jego otoczenia.
    /// Wygładza poszarpane krawędzie i usuwa pojedyncze piksele, nie przesuwając granic dużych plam.
    /// </summary>
    public static byte[] ModeFilter(byte[] labels, int w, int h, int passes)
    {
        var src = labels;
        for (int pass = 0; pass < passes; pass++)
        {
            var dst = new byte[w * h];
            var s = src;
            Parallel.For(0, h, y =>
            {
                Span<byte> win = stackalloc byte[9];
                for (int x = 0; x < w; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = Math.Clamp(y + dy, 0, h - 1);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = Math.Clamp(x + dx, 0, w - 1);
                            win[n++] = s[yy * w + xx];
                        }
                    }
                    byte best = s[y * w + x];
                    for (int i = 0; i < 9; i++)
                    {
                        int c = 0;
                        for (int j = 0; j < 9; j++) if (win[j] == win[i]) c++;
                        if (c >= 5) { best = win[i]; break; }
                    }
                    dst[y * w + x] = best;
                }
            });
            src = dst;
        }
        return passes > 0 ? src : (byte[])labels.Clone();
    }

    /// <summary>
    /// Wtapia obszary mniejsze niż <paramref name="minArea"/> pikseli w sąsiada, z którym mają najdłuższą wspólną granicę
    /// (przy remisie — w sąsiada o najbliższym kolorze, jeśli podano paletę). Zmienia <paramref name="labels"/> w miejscu.
    /// Zwraca liczbę wtopionych obszarów.
    /// </summary>
    public static int RemoveSmallRegions(byte[] labels, int w, int h, int minArea, IReadOnlyList<Lab>? palette = null, int maxPasses = 8, CancellationToken ct = default)
    {
        if (minArea <= 1) return 0;
        return MergeRegions(labels, w, h, comp => Enumerable.Range(0, comp.Count).Where(r => comp.Area[r] < minArea).OrderBy(r => comp.Area[r]).ToList(),
            palette, maxPasses, ct);
    }

    /// <summary>
    /// Wtapia w sąsiadów obszary cieńsze niż ok. 2·<paramref name="minRadius"/> pikseli — paski z wygładzania krawędzi
    /// (antyaliasingu) i cienkie kreski, których i tak nie da się wyszyć wypełnieniem. Zwraca liczbę wtopionych obszarów.
    /// </summary>
    public static int RemoveThinRegions(byte[] labels, int w, int h, float minRadius, IReadOnlyList<Lab>? palette = null, int maxPasses = 4, CancellationToken ct = default)
    {
        if (minRadius <= 1) return 0;
        return MergeRegions(labels, w, h, comp =>
        {
            var points = LabelPoints(comp);
            return Enumerable.Range(0, comp.Count).Where(r => points[r].Radius < minRadius).OrderBy(r => comp.Area[r]).ToList();
        }, palette, maxPasses, ct);
    }

    /// <summary>
    /// Filtr „najczęstszy kolor w otoczeniu” o promieniu <paramref name="radius"/>: piksel zmienia kolor tylko wtedy, gdy inny
    /// kolor ma w oknie wyraźnie więcej pikseli niż jego własny. Prostuje drobne fale na granicach (np. w gradientach),
    /// lekko zaokrąglając ostre rogi.
    /// </summary>
    public static byte[] PluralityFilter(byte[] labels, int w, int h, int radius, int passes)
    {
        var src = labels;
        for (int pass = 0; pass < passes; pass++)
        {
            var dst = new byte[w * h];
            var s = src;
            Parallel.For(0, h, y =>
            {
                Span<int> count = stackalloc int[256];
                for (int x = 0; x < w; x++)
                {
                    int best = s[y * w + x], bestCount = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int row = Math.Clamp(y + dy, 0, h - 1) * w;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx * dx + dy * dy > radius * radius + radius) continue; // okno okrągłe
                            count[s[row + Math.Clamp(x + dx, 0, w - 1)]]++;
                        }
                    }
                    int own = s[y * w + x];
                    bestCount = count[own];
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int row = Math.Clamp(y + dy, 0, h - 1) * w;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx * dx + dy * dy > radius * radius + radius) continue;
                            int v = s[row + Math.Clamp(x + dx, 0, w - 1)];
                            if (count[v] > bestCount) { bestCount = count[v]; best = v; }
                            count[v] = 0; // zerowanie przy okazji
                        }
                    }
                    count[own] = 0;
                    dst[y * w + x] = (byte)best;
                }
            });
            src = dst;
        }
        return passes > 0 ? src : (byte[])labels.Clone();
    }

    /// <summary>
    /// Rozpuszcza obszar w sąsiadach warstwa po warstwie od brzegu — wąski pasek między dwoma kolorami dzieli się
    /// po środku (granica wypada tam, gdzie potem biegnie linia detalu). Zwraca liczbę zmienionych pikseli.
    /// </summary>
    public static int DissolveRegion(byte[] labels, Components comp, int region)
    {
        int w = comp.Width, h = comp.Height;
        if (labels.Length != w * h || region < 0 || region >= comp.Count) return 0;
        var pending = new List<int>();
        var isPending = new bool[w * h];
        for (int p = 0; p < comp.Map.Length; p++)
        {
            if (comp.Map[p] != region) continue;
            pending.Add(p);
            isPending[p] = true;
        }
        int total = pending.Count;
        var votes = new int[256];
        var assign = new List<(int P, byte C)>();
        while (pending.Count > 0)
        {
            assign.Clear();
            foreach (int p in pending)
            {
                Array.Clear(votes);
                int x = p % w, y = p / w, any = 0;
                if (x > 0 && !isPending[p - 1]) { votes[labels[p - 1]]++; any++; }
                if (x < w - 1 && !isPending[p + 1]) { votes[labels[p + 1]]++; any++; }
                if (y > 0 && !isPending[p - w]) { votes[labels[p - w]]++; any++; }
                if (y < h - 1 && !isPending[p + w]) { votes[labels[p + w]]++; any++; }
                if (any == 0) continue;
                int best = 0;
                for (int c = 1; c < 256; c++) if (votes[c] > votes[best]) best = c;
                assign.Add((p, (byte)best));
            }
            if (assign.Count == 0) break; // obszar bez sąsiadów (cały obraz)
            foreach (var (p, c) in assign)
            {
                labels[p] = c;
                isPending[p] = false;
            }
            pending.RemoveAll(p => !isPending[p]);
        }
        return total - pending.Count;
    }

    private static int MergeRegions(byte[] labels, int w, int h, Func<Components, List<int>> select, IReadOnlyList<Lab>? palette, int maxPasses, CancellationToken ct)
    {
        int merged = 0;
        Span<int> votes = stackalloc int[256];
        for (int pass = 0; pass < maxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var comp = FindComponents(labels, w, h);
            if (comp.Count <= 1) break;
            var small = select(comp);
            if (small.Count == 0) break;

            // piksele każdego obszaru (sortowanie przez zliczanie)
            var start = new int[comp.Count + 1];
            foreach (var r in comp.Map) start[r + 1]++;
            for (int i = 0; i < comp.Count; i++) start[i + 1] += start[i];
            var fill = (int[])start.Clone();
            var pixels = new int[w * h];
            for (int p = 0; p < w * h; p++) pixels[fill[comp.Map[p]]++] = p;

            int changed = 0;
            foreach (var r in small)
            {
                votes.Clear();
                byte own = comp.Label[r];
                // obecny kolor obszaru — mógł się zmienić wcześniej w tym przebiegu (wtedy pomijamy, poprawi się w następnym)
                if (labels[pixels[start[r]]] != own) continue;
                for (int k = start[r]; k < start[r + 1]; k++)
                {
                    int p = pixels[k];
                    int x = p % w, y = p / w;
                    if (x > 0 && labels[p - 1] != own) votes[labels[p - 1]]++;
                    if (x < w - 1 && labels[p + 1] != own) votes[labels[p + 1]]++;
                    if (y > 0 && labels[p - w] != own) votes[labels[p - w]]++;
                    if (y < h - 1 && labels[p + w] != own) votes[labels[p + w]]++;
                }
                int target = -1, bestVotes = 0;
                for (int c = 0; c < 256; c++)
                {
                    if (votes[c] == 0) continue;
                    bool better = votes[c] > bestVotes;
                    if (!better && votes[c] == bestVotes && palette is not null && target >= 0 && c < palette.Count && own < palette.Count)
                        better = palette[c].DistanceSquared(palette[own]) < palette[target].DistanceSquared(palette[own]);
                    if (better)
                    {
                        bestVotes = votes[c];
                        target = c;
                    }
                }
                if (target < 0) continue; // obszar bez sąsiadów (cały obraz)
                for (int k = start[r]; k < start[r + 1]; k++) labels[pixels[k]] = (byte)target;
                changed++;
            }
            merged += changed;
            if (changed == 0) break;
        }
        return merged;
    }

    /// <summary>
    /// Odległość każdego piksela od granicy jego obszaru (w pikselach, przybliżenie 1 / √2). Piksele przy granicy mają 1.
    /// </summary>
    /// <param name="frameIsBoundary">
    /// false — krawędź obrazka nie jest granicą (tło „ciągnie się” poza kadr); odległość tylko do innych obszarów.
    /// </param>
    public static float[] DistanceToBoundary(int[] regionMap, int w, int h, bool frameIsBoundary)
    {
        if (frameIsBoundary) return DistanceToBoundary(regionMap, w, h);
        var d = new float[w * h];
        const float Diag = 1.41421356f;
        float far = w + h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x, r = regionMap[p];
                bool edge = (x > 0 && regionMap[p - 1] != r) || (x < w - 1 && regionMap[p + 1] != r)
                            || (y > 0 && regionMap[p - w] != r) || (y < h - 1 && regionMap[p + w] != r);
                d[p] = edge ? 1 : far;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                float v = d[p];
                if (x > 0) v = Math.Min(v, d[p - 1] + 1);
                if (y > 0)
                {
                    v = Math.Min(v, d[p - w] + 1);
                    if (x > 0) v = Math.Min(v, d[p - w - 1] + Diag);
                    if (x < w - 1) v = Math.Min(v, d[p - w + 1] + Diag);
                }
                d[p] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int p = y * w + x;
                float v = d[p];
                if (x < w - 1) v = Math.Min(v, d[p + 1] + 1);
                if (y < h - 1)
                {
                    v = Math.Min(v, d[p + w] + 1);
                    if (x < w - 1) v = Math.Min(v, d[p + w + 1] + Diag);
                    if (x > 0) v = Math.Min(v, d[p + w - 1] + Diag);
                }
                d[p] = v;
            }
        return d;
    }

    public static float[] DistanceToBoundary(int[] regionMap, int w, int h)
    {
        var d = new float[w * h];
        const float Diag = 1.41421356f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x, r = regionMap[p];
                bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1
                            || regionMap[p - 1] != r || regionMap[p + 1] != r || regionMap[p - w] != r || regionMap[p + w] != r;
                d[p] = edge ? 1 : float.MaxValue;
            }
        for (int y = 1; y < h; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int p = y * w + x;
                float v = d[p];
                v = Math.Min(v, d[p - 1] + 1);
                v = Math.Min(v, d[p - w] + 1);
                v = Math.Min(v, d[p - w - 1] + Diag);
                v = Math.Min(v, d[p - w + 1] + Diag);
                d[p] = v;
            }
        for (int y = h - 2; y >= 0; y--)
            for (int x = w - 2; x >= 1; x--)
            {
                int p = y * w + x;
                float v = d[p];
                v = Math.Min(v, d[p + 1] + 1);
                v = Math.Min(v, d[p + w] + 1);
                v = Math.Min(v, d[p + w + 1] + Diag);
                v = Math.Min(v, d[p + w - 1] + Diag);
                d[p] = v;
            }
        return d;
    }

    /// <summary>
    /// Najlepsze miejsce na numer w każdym obszarze: punkt najdalej od granicy (środek piksela) i ta odległość.
    /// </summary>
    public static (Vector2 Point, float Radius)[] LabelPoints(Components comp)
    {
        var dist = DistanceToBoundary(comp.Map, comp.Width, comp.Height);
        var best = new (Vector2 Point, float Radius)[comp.Count];
        for (int p = 0; p < dist.Length; p++)
        {
            int r = comp.Map[p];
            if (dist[p] > best[r].Radius)
                best[r] = (new Vector2(p % comp.Width + 0.5f, p / comp.Width + 0.5f), dist[p]);
        }
        return best;
    }

    /// <summary>Maluje koło (środek i promień w pikselach). Zwraca liczbę zmienionych pikseli.</summary>
    public static int PaintCircle(byte[] labels, int w, int h, Vector2 center, float radius, byte value)
    {
        int changed = 0;
        float r2 = radius * radius;
        int y0 = Math.Max(0, (int)MathF.Floor(center.Y - radius)), y1 = Math.Min(h - 1, (int)MathF.Ceiling(center.Y + radius));
        int x0 = Math.Max(0, (int)MathF.Floor(center.X - radius)), x1 = Math.Min(w - 1, (int)MathF.Ceiling(center.X + radius));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - center.X, dy = y + 0.5f - center.Y;
                if (dx * dx + dy * dy > r2) continue;
                int p = y * w + x;
                if (labels[p] == value) continue;
                labels[p] = value;
                changed++;
            }
        // pędzel mniejszy niż piksel nadal coś maluje
        if (changed == 0 && radius < 1)
        {
            int px = (int)center.X, py = (int)center.Y;
            if (px >= 0 && py >= 0 && px < w && py < h && labels[py * w + px] != value)
            {
                labels[py * w + px] = value;
                changed = 1;
            }
        }
        return changed;
    }

    /// <summary>Maluje pociągnięcie pędzla — łamaną z okrągłą końcówką. Zwraca liczbę zmienionych pikseli.</summary>
    public static int PaintStroke(byte[] labels, int w, int h, IReadOnlyList<Vector2> points, float radius, byte value)
    {
        if (points.Count == 0) return 0;
        int changed = PaintCircle(labels, w, h, points[0], radius, value);
        float spacing = Math.Max(0.35f, radius * 0.35f);
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            float len = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(len / spacing));
            for (int s = 1; s <= steps; s++) changed += PaintCircle(labels, w, h, Vector2.Lerp(a, b, s / (float)steps), radius, value);
        }
        return changed;
    }

    /// <summary>Zmienia kolor wszystkich pikseli danego obszaru.</summary>
    public static int FillRegion(byte[] labels, Components comp, int region, byte value)
    {
        int changed = 0;
        for (int p = 0; p < labels.Length; p++)
        {
            if (comp.Map[p] != region || labels[p] == value) continue;
            labels[p] = value;
            changed++;
        }
        return changed;
    }

    /// <summary>Przepisuje kolory wg tablicy: nowy = map[stary].</summary>
    public static void Remap(byte[] labels, byte[] map)
    {
        for (int p = 0; p < labels.Length; p++) labels[p] = map[labels[p]];
    }

    /// <summary>Liczba pikseli każdego koloru.</summary>
    public static int[] Histogram(byte[] labels, int paletteCount)
    {
        var h = new int[Math.Max(256, paletteCount)];
        foreach (var l in labels) h[l]++;
        return h[..paletteCount];
    }

    /// <summary>
    /// Kolor tła: ten, który zajmuje co najmniej <paramref name="minShare"/> pikseli na brzegach obrazka.
    /// −1, gdy żaden nie dominuje (np. zdjęcie kadrowane „na styk” albo kilka kolorów tła).
    /// </summary>
    public static int DominantBorderLabel(byte[] labels, int w, int h, double minShare = 0.5)
    {
        if (w <= 0 || h <= 0 || labels.Length != w * h) return -1;
        var counts = new int[256];
        int total = 0;
        void Add(int x, int y)
        {
            counts[labels[y * w + x]]++;
            total++;
        }
        for (int x = 0; x < w; x++)
        {
            Add(x, 0);
            if (h > 1) Add(x, h - 1);
        }
        for (int y = 1; y < h - 1; y++)
        {
            Add(0, y);
            if (w > 1) Add(w - 1, y);
        }
        int best = 0;
        for (int i = 1; i < 256; i++) if (counts[i] > counts[best]) best = i;
        return counts[best] >= minShare * total ? best : -1;
    }
}
