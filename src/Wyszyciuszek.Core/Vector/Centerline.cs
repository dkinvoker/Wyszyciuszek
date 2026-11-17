using System.Numerics;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Vector;

/// <summary>
/// Środkowe linie obszaru: szkielet (pocienianie Zhanga–Suena), z niego najdłuższa ścieżka i dłuższe boczne gałęzie
/// (np. trzy wąsy zbiegające się w jednej plamie), wygładzone i przedłużone do końców plamy. Z wąskich pasków robi linie
/// detali, które łatwiej przeszyć niż wypełnić.
/// </summary>
public static class Centerline
{
    /// <summary>Główna środkowa linia obszaru (najdłuższa); pusta lista dla obszaru bez pikseli.</summary>
    public static List<Vector2> Of(Components comp, int region) => Lines(comp, region).FirstOrDefault() ?? [];

    /// <summary>
    /// Wszystkie linie obszaru: najpierw najdłuższa, potem gałęzie dłuższe niż ok. dwie szerokości paska — każda
    /// zaczyna się w miejscu rozgałęzienia, więc linie się stykają.
    /// </summary>
    public static List<List<Vector2>> Lines(Components comp, int region)
    {
        int w = comp.Width, h = comp.Height;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
        for (int p = 0; p < comp.Map.Length; p++)
        {
            if (comp.Map[p] != region) continue;
            int x = p % w, y = p / w;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            area++;
        }
        if (area == 0) return [];

        // maska z ramką 1 px (pocienianie nie musi sprawdzać brzegów)
        int bw = maxX - minX + 3, bh = maxY - minY + 3;
        var m = new byte[bw * bh];
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (comp.Map[y * w + x] == region) m[(y - minY + 1) * bw + x - minX + 1] = 1;
        Thin(m, bw, bh);
        int skeleton = 0;
        foreach (var v in m) skeleton += v;
        var offset = new Vector2(minX - 1 + 0.5f, minY - 1 + 0.5f);
        if (skeleton < 2) return [Smooth(AxisLine(comp, region, area))];

        // szkielet kończy się pół szerokości przed końcem plamy — wolne końce przedłużamy
        float half = Math.Max(0.5f, area / (float)skeleton / 2);
        int minBranch = Math.Max(3, (int)Math.Ceiling(half * 6)); // gałąź krótsza niż ~3 szerokości to tylko ząbek
        var used = new bool[m.Length];
        var lines = new List<List<Vector2>>();
        while (Array.IndexOf(m, (byte)1) is var start and >= 0)
        {
            var path = LongestPath(m, bw, bh, start);
            bool startAttached = Attach(path, atEnd: false, used, bw, bh);
            bool endAttached = Attach(path, atEnd: true, used, bw, bh);
            foreach (int i in path)
            {
                m[i] = 0;
                used[i] = true;
            }
            int own = path.Count - (startAttached ? 1 : 0) - (endAttached ? 1 : 0);
            if (lines.Count > 0 && own < minBranch) continue;
            var pts = path.Select(i => new Vector2(i % bw, i / bw) + offset).ToList();
            if (pts.Count < 2) continue;
            if (!startAttached) pts[0] = Extend(pts, 0, half);
            if (!endAttached) pts[^1] = Extend(pts, pts.Count - 1, half);
            // gałąź zaczyna się na wolnym końcu — tam stanie numer nici, a nie w tłoku przy rozwidleniu
            if (startAttached && !endAttached) pts.Reverse();
            lines.Add(Smooth(pts));
        }
        return lines;
    }

    private static List<Vector2> Smooth(List<Vector2> pts)
    {
        if (pts.Count < 3) return pts;
        var arr = Curves.Taubin([.. pts], 6, closed: false);
        return [.. Curves.Simplify(arr, 0.6f, closed: false)];
    }

    /// <summary>Jeśli koniec ścieżki sąsiaduje z pikselem wcześniejszej linii — dokleja go (gałąź dochodzi do rozwidlenia).</summary>
    private static bool Attach(List<int> path, bool atEnd, bool[] used, int w, int h)
    {
        int e = atEnd ? path[^1] : path[0];
        int x = e % w, y = e / w;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int n = ny * w + nx;
                if (!used[n]) continue;
                if (atEnd) path.Add(n);
                else path.Insert(0, n);
                return true;
            }
        return false;
    }

    /// <summary>Koniec ścieżki przesunięty o <paramref name="by"/> w kierunku, w którym ścieżka się kończy.</summary>
    private static Vector2 Extend(List<Vector2> pts, int end, float by)
    {
        int other = end == 0 ? Math.Min(pts.Count - 1, 5) : Math.Max(0, pts.Count - 6);
        var d = pts[end] - pts[other];
        return d.LengthSquared() < 1e-6f ? pts[end] : pts[end] + Vector2.Normalize(d) * by;
    }

    /// <summary>Dla plamek bez szkieletu: odcinek wzdłuż dłuższej osi przez środek.</summary>
    private static List<Vector2> AxisLine(Components comp, int region, int area)
    {
        int w = comp.Width;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        for (int p = 0; p < comp.Map.Length; p++)
        {
            if (comp.Map[p] != region) continue;
            double x = p % w + 0.5, y = p / w + 0.5;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
        }
        double mx = sx / area, my = sy / area;
        double cxx = sxx / area - mx * mx, cyy = syy / area - my * my, cxy = sxy / area - mx * my;
        double angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        float lo = 0, hi = 0;
        var c = new Vector2((float)mx, (float)my);
        for (int p = 0; p < comp.Map.Length; p++)
        {
            if (comp.Map[p] != region) continue;
            float t = Vector2.Dot(new Vector2(p % w + 0.5f, p / w + 0.5f) - c, dir);
            lo = Math.Min(lo, t);
            hi = Math.Max(hi, t);
        }
        return [c + dir * (lo - 0.5f), c + dir * (hi + 0.5f)];
    }

    /// <summary>Pocienianie Zhanga–Suena do szkieletu grubości 1 piksela.</summary>
    internal static void Thin(byte[] m, int w, int h)
    {
        var remove = new List<int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int step = 0; step < 2; step++)
            {
                remove.Clear();
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (m[i] == 0) continue;
                        int p2 = m[i - w], p3 = m[i - w + 1], p4 = m[i + 1], p5 = m[i + w + 1];
                        int p6 = m[i + w], p7 = m[i + w - 1], p8 = m[i - 1], p9 = m[i - w - 1];
                        int b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                        if (b < 2 || b > 6) continue;
                        int a = (p2 == 0 && p3 == 1 ? 1 : 0) + (p3 == 0 && p4 == 1 ? 1 : 0) + (p4 == 0 && p5 == 1 ? 1 : 0)
                              + (p5 == 0 && p6 == 1 ? 1 : 0) + (p6 == 0 && p7 == 1 ? 1 : 0) + (p7 == 0 && p8 == 1 ? 1 : 0)
                              + (p8 == 0 && p9 == 1 ? 1 : 0) + (p9 == 0 && p2 == 1 ? 1 : 0);
                        if (a != 1) continue;
                        bool ok = step == 0
                            ? p2 * p4 * p6 == 0 && p4 * p6 * p8 == 0
                            : p2 * p4 * p8 == 0 && p2 * p6 * p8 == 0;
                        if (ok) remove.Add(i);
                    }
                foreach (int i in remove) m[i] = 0;
                if (remove.Count > 0) changed = true;
            }
        }
    }

    /// <summary>Najdłuższa ścieżka w szkielecie (dwa przejścia BFS — średnica drzewa), jako indeksy pikseli.</summary>
    internal static List<int> LongestPath(byte[] m, int w, int h, int start)
    {
        if (start < 0 || m[start] == 0) return [];
        var (a, _) = Farthest(m, w, h, start);
        var (b, parent) = Farthest(m, w, h, a);
        var path = new List<int>();
        for (int i = b; i >= 0; i = parent[i]) path.Add(i);
        return path;
    }

    private static (int Farthest, int[] Parent) Farthest(byte[] m, int w, int h, int from)
    {
        var parent = new int[m.Length];
        Array.Fill(parent, -2);
        parent[from] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        int last = from;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            last = i;
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (m[n] == 0 || parent[n] != -2) continue;
                    parent[n] = i;
                    queue.Enqueue(n);
                }
        }
        return (last, parent);
    }
}
