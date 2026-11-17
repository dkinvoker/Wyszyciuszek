namespace Wyszyciuszek.Core.Imaging;

/// <summary>Dobór ograniczonej palety kolorów do obrazu (k-średnich w L*a*b*, start k-means++).</summary>
public static class Quantizer
{
    /// <summary>
    /// Wyznacza do <paramref name="k"/> kolorów reprezentujących obraz. Wynik jest powtarzalny (stałe ziarno),
    /// posortowany od koloru zajmującego największą powierzchnię. Gdy obraz ma mniej różnych kolorów — zwraca mniej.
    /// </summary>
    public static Lab[] KMeans(LabImage img, int k, int seed = 1, int maxIterations = 24, int maxSamples = 60000, CancellationToken ct = default)
    {
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        int n = img.Width * img.Height;

        // próbka pikseli (równomierna siatka z lekkim losowym przesunięciem) — szybciej, a wynik praktycznie ten sam
        var rng = new Random(seed);
        int step = Math.Max(1, n / maxSamples);
        var samples = new List<Lab>(n / step + 1);
        for (int i = rng.Next(step); i < n; i += step) samples.Add(img[i]);
        int m = samples.Count;

        // k-means++: kolejne środki losowane z prawdopodobieństwem ∝ odległości² od najbliższego już wybranego
        var centers = new List<Lab> { samples[rng.Next(m)] };
        var d2 = new float[m];
        for (int i = 0; i < m; i++) d2[i] = samples[i].DistanceSquared(centers[0]);
        while (centers.Count < k)
        {
            double total = 0;
            for (int i = 0; i < m; i++) total += d2[i];
            if (total < 1e-6) break; // obraz ma mniej różnych kolorów niż k
            double r = rng.NextDouble() * total;
            int pick = m - 1;
            for (int i = 0; i < m; i++)
            {
                r -= d2[i];
                if (r <= 0) { pick = i; break; }
            }
            var c = samples[pick];
            centers.Add(c);
            for (int i = 0; i < m; i++) d2[i] = Math.Min(d2[i], samples[i].DistanceSquared(c));
        }

        int kk = centers.Count;
        var assign = new int[m];
        var sum = new double[kk * 3];
        var count = new int[kk];
        for (int iter = 0; iter < maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            var cs = centers.ToArray();
            Parallel.For(0, m, i => assign[i] = Nearest(cs, samples[i]));

            Array.Clear(sum);
            Array.Clear(count);
            for (int i = 0; i < m; i++)
            {
                int a = assign[i];
                sum[a * 3] += samples[i].L;
                sum[a * 3 + 1] += samples[i].A;
                sum[a * 3 + 2] += samples[i].B;
                count[a]++;
            }

            double moved = 0;
            for (int c = 0; c < kk; c++)
            {
                if (count[c] == 0) continue; // pusty klaster — zostaje gdzie był (zostanie usunięty na końcu)
                var nc = new Lab((float)(sum[c * 3] / count[c]), (float)(sum[c * 3 + 1] / count[c]), (float)(sum[c * 3 + 2] / count[c]));
                moved = Math.Max(moved, nc.DistanceSquared(centers[c]));
                centers[c] = nc;
            }
            if (moved < 0.01) break;
        }

        // ostateczne liczności (na próbce) — puste klastry wypadają, reszta od największego
        Array.Clear(count);
        var final = centers.ToArray();
        for (int i = 0; i < m; i++) count[Nearest(final, samples[i])]++;
        return Enumerable.Range(0, kk).Where(c => count[c] > 0).OrderByDescending(c => count[c]).Select(c => final[c]).ToArray();
    }

    /// <summary>Indeks najbliższego koloru z palety.</summary>
    public static int Nearest(Lab[] palette, Lab c)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            float d = palette[i].DistanceSquared(c);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Przypisuje każdy piksel do najbliższego koloru palety (indeks w bajcie — paleta do 255 kolorów).</summary>
    public static byte[] Assign(LabImage img, Lab[] palette, CancellationToken ct = default)
    {
        if (palette.Length is 0 or > 255) throw new ArgumentOutOfRangeException(nameof(palette), "Paleta musi mieć od 1 do 255 kolorów.");
        var labels = new byte[img.Width * img.Height];
        Parallel.For(0, img.Height, new ParallelOptions { CancellationToken = ct }, y =>
        {
            int row = y * img.Width;
            for (int x = 0; x < img.Width; x++) labels[row + x] = (byte)Nearest(palette, img[row + x]);
        });
        return labels;
    }

    /// <summary>
    /// Dopasowuje kolory do dostępnych nici: każdy kolor dostaje najbliższą nić (CIEDE2000). Jeśli najbliższa nić jest już
    /// zajęta, a inna wolna jest niewiele gorsza — bierze wolną (dwa różne odcienie zieleni → dwie różne zielone nici);
    /// w przeciwnym razie oba kolory łączą się w jedną nić. Zwraca indeksy wybranych nici (bez powtórzeń, w kolejności kolorów).
    /// </summary>
    public static int[] MatchToThreads(IReadOnlyList<Lab> colors, IReadOnlyList<Lab> threads, double tolerance = 8)
    {
        if (threads.Count == 0) throw new ArgumentException("Brak nici do wyboru.", nameof(threads));
        var chosen = new List<int>();
        foreach (var c in colors)
        {
            int best = -1, bestFree = -1;
            double bestD = double.MaxValue, bestFreeD = double.MaxValue;
            for (int t = 0; t < threads.Count; t++)
            {
                double d = ColorMath.DeltaE2000(c, threads[t]);
                if (d < bestD) { bestD = d; best = t; }
                if (!chosen.Contains(t) && d < bestFreeD) { bestFreeD = d; bestFree = t; }
            }
            if (!chosen.Contains(best)) chosen.Add(best);
            else if (bestFree >= 0 && bestFreeD <= bestD + tolerance) chosen.Add(bestFree);
        }
        return [.. chosen];
    }
}
