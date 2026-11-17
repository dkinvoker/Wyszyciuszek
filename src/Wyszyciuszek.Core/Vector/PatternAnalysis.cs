using System.Numerics;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Vector;

/// <summary>Jeden obszar wzoru (spójna plama jednego koloru).</summary>
public sealed class RegionInfo
{
    public required int Id { get; init; }
    /// <summary>Indeks koloru w palecie.</summary>
    public required byte Color { get; init; }
    /// <summary>Powierzchnia w pikselach obrazu roboczego.</summary>
    public required int Area { get; init; }
    /// <summary>Miejsce na numer (najdalej od krawędzi) i odległość od krawędzi w pikselach.</summary>
    public required Vector2 LabelPoint { get; init; }
    public required float LabelRadius { get; init; }
    public required RectF Bounds { get; init; }
    /// <summary>Kierunek dłuższej osi plamy w stopniach [0°, 180°) — domyślny kierunek ściegu (45° dla plam okrągłych).</summary>
    public float Axis { get; init; } = 45;
}

/// <summary>
/// Wektorowa postać wzoru: gładkie kontury wspólne dla sąsiednich obszarów, wypełnienia per kolor i miejsca na numery.
/// Liczona od nowa po każdej zmianie mapy kolorów (w tle).
/// </summary>
public sealed class PatternAnalysis
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Components Components { get; init; }
    public required RegionInfo[] Regions { get; init; }
    /// <summary>Obwód (wypełnienie) każdego obszaru — zasada parzystości (dziury są osobnymi figurami).</summary>
    public required PathData[] RegionPaths { get; init; }
    /// <summary>Wszystkie obszary danego koloru razem (do wypełniania).</summary>
    public required PathData[] ColorPaths { get; init; }
    /// <summary>Linie między obszarami — każda raz (do wzoru do odrysowania).</summary>
    public required PathData Outline { get; init; }
    /// <summary>Krawędź obrazu.</summary>
    public required PathData Frame { get; init; }
    /// <summary>Powierzchnia (piksele) i liczba obszarów każdego koloru.</summary>
    public required int[] ColorArea { get; init; }
    public required int[] ColorRegionCount { get; init; }

    public int RegionCount => Regions.Length;

    /// <summary>Obszar pod punktem (współrzędne w pikselach obrazu roboczego) albo −1.</summary>
    public int HitTest(Vector2 p)
    {
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
        if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
        return Components.Map[y * Width + x];
    }

    /// <summary>Parametry wygładzenia linii dla suwaka „gładkość” 0–10.</summary>
    public static (int Iterations, float Tolerance) SmoothingFor(int smoothness)
    {
        smoothness = Math.Clamp(smoothness, 0, 10);
        return (1 + smoothness * 3, 0.25f + smoothness * 0.12f);
    }

    public static PatternAnalysis Compute(byte[] labels, int w, int h, int paletteCount, int smoothness, CancellationToken ct = default)
    {
        if (labels.Length != w * h) throw new ArgumentException("Mapa kolorów nie pasuje do rozmiaru.", nameof(labels));
        var comp = LabelOps.FindComponents(labels, w, h);
        ct.ThrowIfCancellationRequested();
        var traced = BoundaryTracer.Trace(comp.Map, w, h, comp.Count, ct);
        ct.ThrowIfCancellationRequested();

        var (iterations, tolerance) = SmoothingFor(smoothness);
        int w1 = w + 1;
        var curves = new CubicSeg[traced.Chains.Count][];
        Parallel.For(0, traced.Chains.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var chain = traced.Chains[i];
            var pts = Densify(chain.Vertices, w1, chain.Closed);
            // krótkie łamane wygładzamy słabiej, żeby małe wysepki nie znikały
            int it = Math.Min(iterations, Math.Max(1, pts.Length / 3));
            var smooth = Curves.Taubin(pts, it, chain.Closed);
            if (chain.Closed) smooth = PreserveArea(pts, smooth);
            var simple = Curves.Simplify(smooth, tolerance, chain.Closed);
            curves[i] = Curves.CatmullRom(simple, chain.Closed);
        });
        ct.ThrowIfCancellationRequested();

        var regionPaths = new PathData[comp.Count];
        for (int r = 0; r < comp.Count; r++)
        {
            var path = new PathData();
            foreach (var loop in traced.RegionLoops[r]) AppendLoop(path, loop, curves);
            regionPaths[r] = path;
        }

        var colorPaths = new PathData[paletteCount];
        for (int c = 0; c < paletteCount; c++) colorPaths[c] = new PathData();
        var colorArea = new int[paletteCount];
        var colorRegions = new int[paletteCount];
        var labelPoints = LabelOps.LabelPoints(comp);
        var axes = PrincipalAxes(comp, labelPoints);
        var regions = new RegionInfo[comp.Count];
        for (int r = 0; r < comp.Count; r++)
        {
            byte c = comp.Label[r];
            if (c < paletteCount)
            {
                colorPaths[c].Append(regionPaths[r]);
                colorArea[c] += comp.Area[r];
                colorRegions[c]++;
            }
            regions[r] = new RegionInfo
            {
                Id = r, Color = c, Area = comp.Area[r],
                LabelPoint = labelPoints[r].Point, LabelRadius = labelPoints[r].Radius,
                Bounds = regionPaths[r].Bounds, Axis = axes[r],
            };
        }

        var outline = new PathData();
        var frame = new PathData();
        for (int i = 0; i < traced.Chains.Count; i++)
        {
            var segs = curves[i];
            if (segs.Length == 0) continue;
            var target = traced.Chains[i].Left < 0 ? frame : outline;
            target.MoveTo(segs[0].Start);
            foreach (var s in segs) target.CubicTo(s.C1, s.C2, s.End);
            if (traced.Chains[i].Closed) target.Close();
        }

        return new PatternAnalysis
        {
            Width = w, Height = h, Components = comp, Regions = regions, RegionPaths = regionPaths,
            ColorPaths = colorPaths, Outline = outline, Frame = frame, ColorArea = colorArea, ColorRegionCount = colorRegions,
        };
    }

    private static void AppendLoop(PathData path, List<ChainRef> loop, CubicSeg[][] curves)
    {
        bool started = false;
        foreach (var cr in loop)
        {
            var segs = curves[cr.Chain];
            if (segs.Length == 0) continue;
            if (!started)
            {
                path.MoveTo(cr.Reversed ? segs[^1].End : segs[0].Start);
                started = true;
            }
            if (!cr.Reversed)
            {
                foreach (var s in segs) path.CubicTo(s.C1, s.C2, s.End);
            }
            else
            {
                for (int i = segs.Length - 1; i >= 0; i--) path.CubicTo(segs[i].C2, segs[i].C1, segs[i].Start);
            }
        }
        if (started) path.Close();
    }

    /// <summary>
    /// Z wierzchołków siatki (schodki) robi punkty w środkach krawędzi — znikają ząbki co piksel.
    /// Otwarta łamana zachowuje końce (węzły), żeby sąsiednie odcinki dalej się stykały.
    /// </summary>
    internal static Vector2[] Densify(int[] vertices, int w1, bool closed)
    {
        int n = vertices.Length;
        Vector2 V(int i) => new(vertices[i] % w1, vertices[i] / w1);
        if (closed)
        {
            var r = new Vector2[n];
            for (int i = 0; i < n; i++) r[i] = (V(i) + V((i + 1) % n)) * 0.5f;
            return r;
        }
        var o = new Vector2[n + 1];
        o[0] = V(0);
        for (int i = 0; i < n - 1; i++) o[i + 1] = (V(i) + V(i + 1)) * 0.5f;
        o[n] = V(n - 1);
        return o;
    }

    /// <summary>
    /// Domyślny kierunek ściegu każdego obszaru: dłuższa oś plamy w okolicy miejsca na numer (tam stoi strzałka),
    /// w stopniach [0°, 180°). Liczona lokalnie, bo np. łodyga z liściem albo wieniec płatków mają inny kształt
    /// w całości, a inny w miejscu, które się wypełnia. Plamy prawie okrągłe dostają 45° — ukośne wypełnienie
    /// nie układa się wzdłuż nitek tkaniny.
    /// </summary>
    internal static float[] PrincipalAxes(Components comp, (Vector2 Point, float Radius)[] labelPoints)
    {
        int n = comp.Count, w = comp.Width, h = comp.Height;
        const int S = 6; // n, Σx, Σy, Σx², Σy², Σxy
        var local = new double[n * S];
        var global = new double[n * S];
        var reach2 = new float[n];
        for (int r = 0; r < n; r++)
        {
            float reach = Math.Max(6, labelPoints[r].Radius * 2);
            reach2[r] = reach * reach;
        }
        for (int y = 0; y < h; y++)
        {
            float fy = y + 0.5f;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int r = comp.Map[row + x];
                float fx = x + 0.5f;
                Accumulate(global, r * S, fx, fy);
                var c = labelPoints[r].Point;
                float dx = fx - c.X, dy = fy - c.Y;
                if (dx * dx + dy * dy <= reach2[r]) Accumulate(local, r * S, fx, fy);
            }
        }
        var axes = new float[n];
        for (int r = 0; r < n; r++)
            axes[r] = AxisOf(local[r * S] >= 12 ? local : global, r * S);
        return axes;

        static void Accumulate(double[] m, int k, double x, double y)
        {
            m[k]++;
            m[k + 1] += x;
            m[k + 2] += y;
            m[k + 3] += x * x;
            m[k + 4] += y * y;
            m[k + 5] += x * y;
        }

        static float AxisOf(double[] m, int k)
        {
            double a = Math.Max(1, m[k]);
            double mx = m[k + 1] / a, my = m[k + 2] / a;
            double cxx = m[k + 3] / a - mx * mx, cyy = m[k + 4] / a - my * my, cxy = m[k + 5] / a - mx * my;
            double half = (cxx - cyy) / 2, root = Math.Sqrt(half * half + cxy * cxy);
            double major = (cxx + cyy) / 2 + root, minor = (cxx + cyy) / 2 - root;
            bool round = minor <= 1e-9 ? major <= 1e-9 : major / minor < 1.5; // stosunek osi < ~1,22
            return round ? 45 : StitchAngle(0.5 * Math.Atan2(2 * cxy, cxx - cyy));
        }
    }

    private static float StitchAngle(double radians)
    {
        double deg = radians * 180 / Math.PI % 180;
        if (deg < 0) deg += 180;
        return (float)(deg >= 180 ? 0 : deg);
    }

    /// <summary>Po wygładzeniu zamknięta figura jest nieco mniejsza — skalujemy ją z powrotem do pierwotnego pola.</summary>
    private static Vector2[] PreserveArea(Vector2[] original, Vector2[] smooth)
    {
        float a0 = Math.Abs(Curves.SignedArea(original)), a1 = Math.Abs(Curves.SignedArea(smooth));
        if (a1 < 1e-4f || a0 < 1e-4f) return smooth;
        float s = MathF.Sqrt(a0 / a1);
        if (s > 1.02f) // poprawiamy tylko zauważalne skurczenie (i najwyżej trzykrotnie)
        {
            var c = Vector2.Zero;
            foreach (var p in smooth) c += p;
            c /= smooth.Length;
            var r = new Vector2[smooth.Length];
            for (int i = 0; i < r.Length; i++) r[i] = c + (smooth[i] - c) * Math.Min(s, 3f);
            return r;
        }
        return smooth;
    }
}
