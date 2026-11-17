using System.Numerics;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>
/// Linia detalu — oczy, wąsy, łodyżki, pajęczyna: przeszycie samą linią, bez osobnego obszaru koloru.
/// </summary>
public sealed class DetailLine
{
    /// <summary>Punkty w pikselach obrazu roboczego — gładka krzywa przechodzi przez każdy z nich.</summary>
    public List<Vector2> Points { get; set; } = [];

    /// <summary>Nić (indeks palety).</summary>
    public int Color { get; set; }

    public DetailLine Clone() => new() { Points = [.. Points], Color = Color };

    /// <summary>Długość łamanej w pikselach obrazu roboczego.</summary>
    public float Length
    {
        get
        {
            float l = 0;
            for (int i = 1; i < Points.Count; i++) l += Vector2.Distance(Points[i - 1], Points[i]);
            return l;
        }
    }

    /// <summary>Gładka krzywa (Catmull–Rom) przez punkty linii.</summary>
    public PathData ToPath()
    {
        var path = new PathData();
        if (Points.Count < 2) return path;
        var segs = Curves.CatmullRom([.. Points], closed: false);
        path.MoveTo(segs[0].Start);
        foreach (var s in segs) path.CubicTo(s.C1, s.C2, s.End);
        return path;
    }

    /// <summary>Odległość punktu od linii (piksele obrazu roboczego).</summary>
    public float DistanceTo(Vector2 q)
    {
        if (Points.Count == 0) return float.MaxValue;
        if (Points.Count == 1) return Vector2.Distance(q, Points[0]);
        float best = float.MaxValue;
        for (int i = 1; i < Points.Count; i++) best = Math.Min(best, Curves.SegmentDistanceSquared(q, Points[i - 1], Points[i]));
        return MathF.Sqrt(best);
    }

    /// <summary>
    /// Pociągnięcie myszą → punkty linii: bez drżenia ręki i zbędnych punktów. Końce zostają na miejscu.
    /// </summary>
    public static List<Vector2> Smooth(IReadOnlyList<Vector2> raw)
    {
        var pts = new List<Vector2>();
        foreach (var p in raw)
            if (pts.Count == 0 || Vector2.Distance(p, pts[^1]) >= 0.75f) pts.Add(p);
        // koniec linii tam, gdzie puszczono przycisk
        if (raw.Count > 1 && pts.Count > 1 && pts[^1] != raw[^1]) pts[^1] = raw[^1];
        if (pts.Count < 3) return pts;
        var smooth = Curves.Taubin([.. pts], 3, closed: false);
        return [.. Curves.Simplify(smooth, 0.4f, closed: false)];
    }
}

/// <summary>
/// Ręcznie ustawiony kierunek ściegu. Dotyczy obszaru, w którym leży punkt — dzięki temu przetrwa poprawki wzoru
/// (numery obszarów zmieniają się po każdej zmianie mapy kolorów, a miejsce na obrazku nie).
/// </summary>
public sealed class StitchHint
{
    public Vector2 Point { get; set; }

    /// <summary>Kierunek w stopniach: 0 = poziomo, 90 = pionowo (oś Y w dół). Kąt i kąt+180° to ten sam kierunek.</summary>
    public float Angle { get; set; }

    public StitchHint Clone() => new() { Point = Point, Angle = Angle };
}

/// <summary>Kierunek wypełnienia obszaru.</summary>
public readonly record struct StitchDirection(float Angle, bool Manual);

/// <summary>Kierunki ściegu dla wszystkich obszarów: automatyczne (wzdłuż dłuższej osi plamy) albo ustawione ręcznie.</summary>
public static class StitchPlan
{
    public static StitchDirection[] Directions(PatternAnalysis analysis, IReadOnlyList<StitchHint> hints)
    {
        var d = new StitchDirection[analysis.RegionCount];
        for (int r = 0; r < d.Length; r++) d[r] = new StitchDirection(analysis.Regions[r].Axis, false);
        foreach (var h in hints)
        {
            int r = analysis.HitTest(h.Point);
            if (r >= 0) d[r] = new StitchDirection(Normalize(h.Angle), true);
        }
        return d;
    }

    /// <summary>Kąt sprowadzony do [0°, 180°).</summary>
    public static float Normalize(float degrees)
    {
        degrees %= 180;
        if (degrees < 0) degrees += 180;
        return degrees >= 180 ? 0 : degrees;
    }

    /// <summary>Kąt odcinka od <paramref name="from"/> do <paramref name="to"/> (stopnie, [0°, 180°)).</summary>
    public static float AngleOf(Vector2 from, Vector2 to)
    {
        var d = to - from;
        return Normalize(MathF.Atan2(d.Y, d.X) * 180 / MathF.PI);
    }
}
