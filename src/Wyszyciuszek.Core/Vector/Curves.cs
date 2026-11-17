using System.Numerics;

namespace Wyszyciuszek.Core.Vector;

/// <summary>Odcinek krzywej Béziera trzeciego stopnia.</summary>
public readonly record struct CubicSeg(Vector2 Start, Vector2 C1, Vector2 C2, Vector2 End)
{
    public CubicSeg Reversed => new(End, C2, C1, Start);

    public Vector2 At(float t)
    {
        float u = 1 - t;
        return u * u * u * Start + 3 * u * u * t * C1 + 3 * u * t * t * C2 + t * t * t * End;
    }
}

/// <summary>Wygładzanie i upraszczanie łamanych (z „schodków” pikseli robią się płynne linie).</summary>
public static class Curves
{
    /// <summary>
    /// Wygładzanie Taubina (λ/μ): jak uśrednianie z sąsiadami, ale bez kurczenia się kształtu.
    /// Dla łamanej otwartej końce zostają na miejscu (tam stykają się granice kilku obszarów).
    /// </summary>
    public static Vector2[] Taubin(Vector2[] points, int iterations, bool closed)
    {
        int n = points.Length;
        if (iterations <= 0 || n < 3) return points;
        var a = (Vector2[])points.Clone();
        var b = new Vector2[n];
        for (int it = 0; it < iterations; it++)
        {
            Step(a, b, 0.5f, closed);
            Step(b, a, -0.53f, closed);
        }
        return a;
    }

    private static void Step(Vector2[] src, Vector2[] dst, float factor, bool closed)
    {
        int n = src.Length;
        for (int i = 0; i < n; i++)
        {
            if (!closed && (i == 0 || i == n - 1))
            {
                dst[i] = src[i];
                continue;
            }
            var prev = src[(i - 1 + n) % n];
            var next = src[(i + 1) % n];
            dst[i] = src[i] + factor * ((prev + next) * 0.5f - src[i]);
        }
    }

    /// <summary>Upraszczanie Douglasa–Peuckera: usuwa punkty odstające od linii o mniej niż <paramref name="tolerance"/>.</summary>
    public static Vector2[] Simplify(Vector2[] points, float tolerance, bool closed)
    {
        int n = points.Length;
        if (n < 3 || tolerance <= 0) return points;
        if (!closed) return SimplifyOpen(points, tolerance);

        // zamknięta: dzielimy w punkcie najdalszym od pierwszego i upraszczamy obie połowy
        int far = 0;
        float farD = -1;
        for (int i = 1; i < n; i++)
        {
            float d = Vector2.DistanceSquared(points[0], points[i]);
            if (d > farD) { farD = d; far = i; }
        }
        var first = SimplifyOpen(points[..(far + 1)], tolerance);
        var second = SimplifyOpen([.. points[far..], points[0]], tolerance);
        var result = new List<Vector2>(first.Length + second.Length);
        result.AddRange(first);
        result.AddRange(second.AsSpan(1, second.Length - 2).ToArray()); // bez powtórzonego punktu podziału i startu
        if (result.Count < 3) return Decimate(points, 4);
        return [.. result];
    }

    private static Vector2[] SimplifyOpen(Vector2[] p, float tol)
    {
        int n = p.Length;
        if (n < 3) return p;
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, n - 1));
        float tol2 = tol * tol;
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            int idx = -1;
            float maxD = tol2;
            for (int i = a + 1; i < b; i++)
            {
                float d = SegmentDistanceSquared(p[i], p[a], p[b]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx < 0) continue;
            keep[idx] = true;
            stack.Push((a, idx));
            stack.Push((idx, b));
        }
        var result = new List<Vector2>();
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(p[i]);
        return [.. result];
    }

    /// <summary>Kwadrat odległości punktu od odcinka.</summary>
    public static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-12f) return Vector2.DistanceSquared(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0, 1);
        return Vector2.DistanceSquared(p, a + t * ab);
    }

    /// <summary>Co któryś punkt — awaryjnie, gdy upraszczanie zostawiłoby za mało punktów figury zamkniętej.</summary>
    private static Vector2[] Decimate(Vector2[] points, int count)
    {
        if (points.Length <= count) return points;
        var r = new Vector2[count];
        for (int i = 0; i < count; i++) r[i] = points[i * points.Length / count];
        return r;
    }

    /// <summary>
    /// Krzywa Catmulla–Roma przez punkty, zapisana jako odcinki Béziera. Przechodzi dokładnie przez każdy punkt,
    /// więc krawędzie sąsiednich obszarów (z tych samych punktów) idealnie do siebie pasują.
    /// </summary>
    public static CubicSeg[] CatmullRom(Vector2[] p, bool closed)
    {
        int n = p.Length;
        if (n < 2) return [];
        int segs = closed ? n : n - 1;
        var result = new CubicSeg[segs];
        for (int i = 0; i < segs; i++)
        {
            Vector2 p1 = p[i], p2 = p[(i + 1) % n];
            Vector2 p0 = closed ? p[(i - 1 + n) % n] : i > 0 ? p[i - 1] : p1;
            Vector2 p3 = closed ? p[(i + 2) % n] : i + 2 < n ? p[i + 2] : p2;
            result[i] = new CubicSeg(p1, p1 + (p2 - p0) / 6f, p2 - (p3 - p1) / 6f, p2);
        }
        return result;
    }

    /// <summary>Pole wielokąta ze znakiem (dodatnie = zgodnie z ruchem wskazówek zegara przy osi Y w dół).</summary>
    public static float SignedArea(IReadOnlyList<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i];
            var v = p[(i + 1) % p.Count];
            a += u.X * v.Y - v.X * u.Y;
        }
        return a / 2;
    }
}
