using System.Globalization;
using System.Numerics;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;
using RegionInfo = Wyszyciuszek.Core.Vector.RegionInfo;

namespace Wyszyciuszek.Core.Output;

/// <summary>Jak narysować sam wzór.</summary>
public sealed record DesignStyle
{
    /// <summary>Pełne kolory nici (podgląd) zamiast wzoru do odrysowania.</summary>
    public bool FullColor { get; init; }
    /// <summary>Wypełnienie kolorem we wzorze (0 = tło w kolorze papieru).</summary>
    public double Tint { get; init; }
    public bool Numbers { get; init; } = true;
    public bool Outline { get; init; } = true;
    public bool Frame { get; init; }
    /// <summary>Grubość linii w jednostkach wzoru (pikselach obrazu roboczego).</summary>
    public float LineWidth { get; init; } = 1;
    /// <summary>Wielkość numerów (w jednostkach wzoru): najmniejsza czytelna i największa.</summary>
    public float MinFont { get; init; } = 6;
    public float MaxFont { get; init; } = 14;
    /// <summary>Tło wzoru do odrysowania (biały papier; w projektorze czarne — wtedy świecą tylko linie).</summary>
    public Rgb Paper { get; init; } = Rgb.White;
    /// <summary>Kolor konturów i numerów.</summary>
    public Rgb Ink { get; init; } = new(26, 26, 26);

    /// <summary>Linie detali.</summary>
    public bool Lines { get; init; } = true;
    /// <summary>Grubość linii detali rysowanych kolorem nici (podgląd w kolorach).</summary>
    public float DetailWidth { get; init; } = 2;
    /// <summary>Kreska linii detali na wzorze do odrysowania (null — linia ciągła).</summary>
    public float[]? DetailDash { get; init; }

    /// <summary>Strzałki kierunku ściegu.</summary>
    public bool Arrows { get; init; }
    public Rgb ArrowColor { get; init; } = new(196, 60, 64);
    public float ArrowWidth { get; init; } = 1;
    /// <summary>Najkrótsza i najdłuższa strzałka (jednostki wzoru) — w mniejszych obszarach strzałki nie ma.</summary>
    public float MinArrow { get; init; } = 8;
    public float MaxArrow { get; init; } = 40;
    /// <summary>Kierunki ustawione ręcznie grubszą kreską (podczas ustawiania).</summary>
    public bool EmphasizeManual { get; init; }

    /// <summary>Obszary tej nici wyraźnie zamalowane (projektor: „teraz szyję tę nić”); −1 = żadna.</summary>
    public int Highlight { get; init; } = -1;
}

/// <summary>
/// Składa wzór, kartki do druku i legendę z poleceń <see cref="IDrawSurface"/>. Ten sam kod rysuje podgląd, PDF, PNG,
/// wydruk i obraz projektora.
/// </summary>
public sealed class PatternComposer
{
    public static readonly Rgb Ink = new(26, 26, 26);
    private static readonly Rgb Gray = new(120, 120, 120);
    private static readonly Rgb LightGray = new(185, 185, 185);
    public static readonly Rgb Accent = new(196, 60, 64);

    private readonly PatternDocument _doc;
    private readonly PatternAnalysis _an;
    private StitchDirection[]? _directions;
    private PathData[]? _linePaths;

    public PatternComposer(PatternDocument document, PatternAnalysis analysis)
    {
        _doc = document;
        _an = analysis;
        Layout = PageLayout.Compute(DesignWidthMm, DesignHeightMm, Papers.Get(document.Print.Paper), document.Print.Legend);
    }

    public PageLayout Layout { get; }
    public int PageCount => Layout.PageCount;
    public double DesignWidthMm => _doc.Print.WidthMm;
    public double DesignHeightMm => _doc.Print.HeightMm(_an.Width, _an.Height);
    private float MmPerPx => (float)(DesignWidthMm / _an.Width);
    public int DesignWidthPx => _an.Width;
    public int DesignHeightPx => _an.Height;

    /// <summary>Kierunek ściegu każdego obszaru (automatyczny albo ustawiony ręcznie).</summary>
    public StitchDirection[] Directions => _directions ??= StitchPlan.Directions(_an, _doc.StitchHints);

    /// <summary>Krzywe linii detali (liczone raz na kompozytor).</summary>
    public PathData[] LinePaths => _linePaths ??= _doc.Lines.Select(l => l.ToPath()).ToArray();

    /// <summary>Styl wzoru do druku w skali 1:1 (grubości i czcionki z ustawień w mm).</summary>
    public DesignStyle PrintStyle(bool fullColor = false)
    {
        var p = _doc.Print;
        float k = 1 / MmPerPx;
        return new DesignStyle
        {
            FullColor = fullColor, Tint = p.Tint, Numbers = p.Numbers && !fullColor, Outline = !fullColor, Frame = p.Frame,
            LineWidth = (float)p.LineWidthMm * k, MinFont = 1.7f * k, MaxFont = 4.2f * k,
            DetailWidth = (float)Math.Max(0.8, p.LineWidthMm * 2) * k,
            DetailDash = [2.4f * k, 0.9f * k, 0.35f * k, 0.9f * k],
            Arrows = p.StitchArrows && !fullColor, ArrowWidth = 0.22f * k, MinArrow = 4.5f * k, MaxArrow = 14f * k,
        };
    }

    // ================================================================== sam wzór (jednostki: piksele obrazu roboczego)

    public void DrawDesign(IDrawSurface s, DesignStyle style)
    {
        var palette = _doc.Palette;
        var bounds = new RectF(0, 0, _an.Width, _an.Height);
        if (!style.FullColor) s.Fill(Shapes.Rect(0, 0, _an.Width, _an.Height), style.Paper);
        for (int c = 0; c < _an.ColorPaths.Length && c < palette.Count; c++)
        {
            var path = _an.ColorPaths[c];
            if (path.IsEmpty) continue;
            var entry = palette[c];
            if (style.FullColor) s.Fill(path, entry.Color);
            else if (c == style.Highlight) s.Fill(path, style.Paper.Lerp(entry.Color, 0.75));
            else if (style.Tint > 0.001 && !entry.IsFabric) s.Fill(path, style.Paper.Lerp(entry.Color, style.Tint));
        }
        if (style.Outline && !_an.Outline.IsEmpty) s.Stroke(_an.Outline, style.Ink, style.LineWidth);
        if (style.Frame && !_an.Frame.IsEmpty) s.Stroke(_an.Frame, style.Ink, style.LineWidth);
        if (style.Lines) DrawLines(s, style);
        if (style.Arrows) DrawArrows(s, style, bounds);
        if (style.Numbers) DrawNumbers(s, style, bounds);
    }

    /// <summary>Linie detali: w kolorze nici (podgląd) albo kreską-kropką (wzór do odrysowania).</summary>
    public void DrawLines(IDrawSurface s, DesignStyle style)
    {
        var paths = LinePaths;
        for (int i = 0; i < paths.Length && i < _doc.Lines.Count; i++)
        {
            if (paths[i].IsEmpty) continue;
            if (style.FullColor)
            {
                int c = _doc.Lines[i].Color;
                s.Stroke(paths[i], c >= 0 && c < _doc.Palette.Count ? _doc.Palette[c].Color : style.Ink, style.DetailWidth);
            }
            else
            {
                s.Stroke(paths[i], style.Ink, style.LineWidth * 1.15f, style.DetailDash);
            }
        }
    }

    /// <summary>Numery nici w obszarach (w miejscu najdalszym od krawędzi; za małe obszary — bez numeru) i przy liniach detali.</summary>
    public void DrawNumbers(IDrawSurface s, DesignStyle style, RectF visible)
    {
        var numbers = _doc.ThreadNumbers();
        foreach (var r in _an.Regions)
        {
            if (!NumberFor(r, style, numbers, out var text, out var size)) continue;
            if (!visible.Inflate(size).Contains(r.LabelPoint)) continue;
            var color = style.FullColor && _doc.Palette[r.Color].Color.PrefersLightText ? Rgb.White : style.Ink;
            s.Text(text, r.LabelPoint + new Vector2(0, size * 0.36f), size, color, TextAnchor.Middle);
        }
        if (style.Lines && !style.FullColor) DrawLineNumbers(s, style, numbers, visible);
    }

    /// <summary>Numer nici tuż przed początkiem każdej linii detalu.</summary>
    private void DrawLineNumbers(IDrawSurface s, DesignStyle style, int[] numbers, RectF visible)
    {
        float size = Math.Min(style.MaxFont, style.MinFont * 1.35f);
        foreach (var line in _doc.Lines)
        {
            if (line.Points.Count < 2 || line.Color < 0 || line.Color >= numbers.Length || numbers[line.Color] == 0) continue;
            var p0 = line.Points[0];
            var next = line.Points.FirstOrDefault(p => Vector2.Distance(p, p0) > 0.5f, line.Points[^1]);
            var dir = next == p0 ? Vector2.UnitX : Vector2.Normalize(next - p0);
            var at = p0 - dir * size * 0.85f;
            if (!visible.Inflate(size).Contains(at)) continue;
            s.Text(numbers[line.Color].ToString(CultureInfo.InvariantCulture), at + new Vector2(0, size * 0.36f), size, style.Ink, TextAnchor.Middle, bold: true);
        }
    }

    private static bool NumberFor(RegionInfo r, DesignStyle style, int[] numbers, out string text, out float size)
    {
        text = "";
        size = 0;
        if (r.Color >= numbers.Length || numbers[r.Color] == 0) return false;
        text = numbers[r.Color].ToString(CultureInfo.InvariantCulture);
        float fit = r.LabelRadius * 2 / (0.6f * text.Length + 0.25f);
        size = Math.Min(style.MaxFont, Math.Min(fit, r.LabelRadius * 1.6f));
        return size >= style.MinFont;
    }

    /// <summary>Ile obszarów (do szycia) nie zmieści numeru przy danym stylu.</summary>
    public int CountUnnumbered(DesignStyle style)
    {
        var numbers = _doc.ThreadNumbers();
        return _an.Regions.Count(r => r.Color < numbers.Length && numbers[r.Color] != 0 && !NumberFor(r, style, numbers, out _, out _));
    }

    /// <summary>
    /// Podwójne strzałki kierunku wypełnienia (szyje się tam i z powrotem) przez środek obszaru — z przerwą na numer.
    /// </summary>
    public void DrawArrows(IDrawSurface s, DesignStyle style, RectF visible)
    {
        var dirs = Directions;
        var numbers = _doc.ThreadNumbers();
        var palette = _doc.Palette;
        for (int i = 0; i < _an.Regions.Length && i < dirs.Length; i++)
        {
            var r = _an.Regions[i];
            if (r.Color >= palette.Count || palette[r.Color].IsFabric) continue;
            float half = Math.Min(r.LabelRadius * 0.85f, style.MaxArrow / 2);
            bool manual = dirs[i].Manual;
            if (half * 2 < style.MinArrow && !manual) continue;
            if (!visible.Inflate(Math.Max(half, style.MinArrow)).Contains(r.LabelPoint)) continue;

            float rad = dirs[i].Angle * MathF.PI / 180;
            var d = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
            float gap = 0;
            if (style.Numbers && NumberFor(r, style, numbers, out var text, out var size))
            {
                // odległość od środka do brzegu numeru wzdłuż strzałki (+ mały odstęp)
                float tw = FontMetrics.Measure(text, size) / 2 + size * 0.3f, th = size * 0.36f + size * 0.3f;
                float ax = Math.Abs(d.X), ay = Math.Abs(d.Y);
                gap = Math.Min(ax > 1e-3f ? tw / ax : float.MaxValue, ay > 1e-3f ? th / ay : float.MaxValue);
            }
            float minShaft = Math.Max(style.MinArrow * 0.3f, style.ArrowWidth * 4);
            if (half - gap < minShaft)
            {
                if (!manual) continue;
                // kierunek ustawiony ręcznie zawsze widać — w wąskiej plamie strzałka może wyjść poza jej brzeg
                half = gap + Math.Max(style.MinArrow * 0.4f, style.ArrowWidth * 6);
            }
            float shaft = half - gap;
            float head = Math.Min(shaft * 0.55f, Math.Max(half * 0.28f, style.ArrowWidth * 3));

            var path = new PathData();
            var c = r.LabelPoint;
            for (int side = -1; side <= 1; side += 2)
            {
                var dir = d * side;
                var tip = c + dir * half;
                path.MoveTo(c + dir * gap);
                path.LineTo(tip);
                var back = -dir * head;
                path.MoveTo(tip + Rotate(back, 0.5f));
                path.LineTo(tip);
                path.LineTo(tip + Rotate(back, -0.5f));
            }
            var color = style.FullColor ? (palette[r.Color].Color.PrefersLightText ? Rgb.White : Ink) : style.ArrowColor;
            float width = style.EmphasizeManual && manual ? style.ArrowWidth * 1.8f : style.ArrowWidth;
            s.Stroke(path, color, width);
        }
    }

    private static Vector2 Rotate(Vector2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    // ================================================================== strony

    public (double Width, double Height) PageSize(int index) => Layout.PageSize(index);

    public void DrawPage(IDrawSurface s, int index)
    {
        if (Layout.IsLegendPage(index)) DrawLegend(s);
        else DrawTile(s, Layout.Tiles[index]);
    }

    /// <summary>Przekształcenie: piksele wzoru → mm na wydruku (z lustrzanym odbiciem, jeśli włączone).</summary>
    public Matrix3x2 DesignToOutputMm()
    {
        var m = Matrix3x2.CreateScale(MmPerPx);
        if (_doc.Print.Mirror) m *= Matrix3x2.CreateScale(-1, 1) * Matrix3x2.CreateTranslation((float)DesignWidthMm, 0);
        return m;
    }

    private void DrawTile(IDrawSurface s, PageTile tile)
    {
        var L = Layout;
        var a = tile.Area;
        float ox = (float)(tile.PageX - a.X), oy = (float)(tile.PageY - a.Y);
        var page = new RectD(tile.PageX, tile.PageY, a.W, a.H);

        DrawHeader(s, tile);

        // fragment wzoru
        s.PushClip(new RectF((float)page.X, (float)page.Y, (float)page.W, (float)page.H));
        s.PushTransform(DesignToOutputMm() * Matrix3x2.CreateTranslation(ox, oy));
        DrawDesign(s, PrintStyle());
        s.Pop();
        s.Pop();

        // zakładki i znaczniki do sklejania
        var dash = new[] { 1.5f, 1.5f };
        foreach (var (dc, dr) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var n = L.TileAt(tile.Column + dc, tile.Row + dr);
            if (n is null) continue;
            if (dc != 0)
            {
                // granica fragmentu sąsiada na tej kartce
                double x = dc < 0 ? n.Area.Right : n.Area.X;
                float px = (float)(x + ox);
                s.Stroke(Shapes.Line(new Vector2(px, (float)page.Y - 3), new Vector2(px, (float)page.Bottom + 3)), Accent, 0.25f, dash);
                var label = dc < 0 ? $"< {n.Label}" : $"{n.Label} >";
                s.Text(label, new Vector2(px + (dc < 0 ? -1.2f : 1.2f), (float)page.Bottom + 5.5f), 3f, Accent, dc < 0 ? TextAnchor.End : TextAnchor.Start, bold: true);
            }
            else
            {
                double y = dr < 0 ? n.Area.Bottom : n.Area.Y;
                float py = (float)(y + oy);
                s.Stroke(Shapes.Line(new Vector2((float)page.X - 3, py), new Vector2((float)page.Right + 3, py)), Accent, 0.25f, dash);
                s.Text(dr < 0 ? $"^ {n.Label}" : $"v {n.Label}", new Vector2((float)page.Right + 1, py + (dr < 0 ? -1.2f : 3.6f)), 3f, Accent, TextAnchor.Start, bold: true);
            }
        }
        foreach (var c in Crosshairs())
        {
            if (!a.Contains(c.X, c.Y)) continue;
            s.Stroke(Shapes.Crosshair(new Vector2((float)c.X + ox, (float)c.Y + oy), 3.2f), Accent, 0.3f);
        }

        // znaczniki środka wzoru (do wyśrodkowania w tamborku) — na zewnątrz krawędzi
        if (_doc.Print.CenterMarks)
        {
            double cx = L.DesignWidth / 2, cy = L.DesignHeight / 2;
            const float len = 4f, gap = 1f;
            void Mark(double x, double y, Vector2 dir)
            {
                if (!a.Contains(x, y)) return;
                var p = new Vector2((float)x + ox, (float)y + oy) - dir * gap;
                s.Stroke(Shapes.Line(p, p - dir * len), Ink, 0.35f);
            }
            Mark(cx, 0, new Vector2(0, 1));
            Mark(cx, L.DesignHeight, new Vector2(0, -1));
            Mark(0, cy, new Vector2(1, 0));
            Mark(L.DesignWidth, cy, new Vector2(-1, 0));
        }
    }

    /// <summary>Krzyżyki w pasach zakładek (każdy na obu sąsiednich kartkach w tym samym miejscu wzoru).</summary>
    public IEnumerable<(double X, double Y)> Crosshairs()
    {
        var L = Layout;
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var t in L.Tiles)
        {
            if (t.Row == 0 && L.TileAt(t.Column + 1, 0) is { } right) xs.Add((t.Area.Right + right.Area.X) / 2);
            if (t.Column == 0 && L.TileAt(0, t.Row + 1) is { } below) ys.Add((t.Area.Bottom + below.Area.Y) / 2);
        }
        if (xs.Count == 0 && ys.Count == 0) yield break;
        // wzdłuż pasów pionowych — w kilku miejscach na wysokości, i odwrotnie
        foreach (var x in xs)
            foreach (var y in Spread(L.DesignHeight)) yield return (x, y);
        foreach (var y in ys)
            foreach (var x in Spread(L.DesignWidth)) yield return (x, y);
        foreach (var x in xs)
            foreach (var y in ys) yield return (x, y);
    }

    private static IEnumerable<double> Spread(double length)
    {
        int n = Math.Max(2, (int)Math.Round(length / 70));
        for (int i = 0; i < n; i++) yield return length * (i + 0.5) / n;
    }

    private void DrawHeader(IDrawSurface s, PageTile tile)
    {
        var L = Layout;
        float x = (float)PageLayout.Margin, y = (float)PageLayout.Margin + 3.6f;
        var size = $"{Cm(L.DesignWidth)} × {Cm(L.DesignHeight)} cm";
        var title = Fit(string.IsNullOrWhiteSpace(_doc.Title) ? "Wzór" : _doc.Title, 4f, true, (float)(L.PageWidth * 0.4));
        s.Text(title, new Vector2(x, y), 4f, Ink, bold: true);
        float tw = FontMetrics.Measure(title, 4f, true);
        var info = tile.Label.Length > 0
            ? $"{size} · kartka {tile.Label} ({tile.Index + 1}/{L.Tiles.Count}, {L.Columns}×{L.Rows})"
            : $"{size} · skala 1:1";
        if (_doc.Print.Mirror) info += " · LUSTRZANE ODBICIE";
        s.Text(info, new Vector2(x + tw + 3, y), 3f, Gray);

        // kwadrat 1 cm — sprawdzenie, czy drukarka nie przeskalowała
        float right = (float)(L.PageWidth - PageLayout.Margin);
        s.Stroke(Shapes.Rect(right - 10, (float)PageLayout.Margin - 1, 10, 10), Ink, 0.25f);
        s.Text("1 cm", new Vector2(right - 5, (float)PageLayout.Margin + 5.2f), 2.6f, Gray, TextAnchor.Middle);
        s.Text("drukuj w skali 100%", new Vector2(right - 12, (float)PageLayout.Margin + 5.2f), 2.6f, Gray, TextAnchor.End);
    }

    // ================================================================== legenda

    private void DrawLegend(IDrawSurface s)
    {
        var L = Layout;
        double pw = L.Paper.Width, ph = L.Paper.Height;
        float m = (float)PageLayout.Margin + 2;
        float y = m + 6;
        var numbers = _doc.ThreadNumbers();
        var usage = ThreadEstimator.PerColor(_doc, _an);
        var total = ThreadEstimator.Total(usage);

        // miniatura w kolorach (prawy górny róg)
        float thumbMax = 62;
        float k = Math.Min(thumbMax / _an.Width, thumbMax / _an.Height);
        float tw = _an.Width * k, th = _an.Height * k;
        float tx = (float)pw - m - tw, ty = m;
        s.PushClip(new RectF(tx, ty, tw, th));
        s.PushTransform(Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(tx, ty));
        DrawDesign(s, new DesignStyle { FullColor = true, Outline = false, Numbers = false, DetailWidth = Math.Max(1, 0.5f / k) });
        s.Pop();
        s.Pop();
        s.Stroke(Shapes.Rect(tx, ty, tw, th), LightGray, 0.25f);

        // nagłówek z opisem — zawijany, żeby nie wchodził na miniaturę
        float textWidth = tx - m - 5;
        var title = Fit(string.IsNullOrWhiteSpace(_doc.Title) ? "Wzór" : _doc.Title, 7f, true, textWidth);
        s.Text(title, new Vector2(m, y), 7f, Ink, bold: true);
        y += 2;
        int stitchedCount = Enumerable.Range(0, _doc.Palette.Count).Count(i => numbers[i] != 0 && usage[i].Meters > 0);
        var hoop = Hoops.Get(_doc.Print.Hoop);
        string[] info =
        [
            $"Rozmiar {Cm(L.DesignWidth)} × {Cm(L.DesignHeight)} cm · nici: {stitchedCount} · obszary: {_an.RegionCount}"
                + (_doc.Lines.Count > 0 ? $" · linie detali: {_doc.Lines.Count}" : "")
                + (hoop is null ? "" : $" · tamborek: {hoop.Name}"),
            L.Tiles.Count > 1
                ? $"Wzór na {L.Tiles.Count} kartkach ({L.Columns}×{L.Rows}): nałóż je tak, by krzyżyki się pokryły, i sklej."
                : "Wzór mieści się na jednej kartce.",
            $"Nici ok. {ThreadEstimator.Meters(total.Meters)} (górnej; dolnej podobnie) · szycie ok. {ThreadEstimator.Duration(total.Minutes)}"
                + " — orientacyjnie, przy gęstym wypełnieniu.",
        ];
        foreach (var paragraph in info)
            foreach (var line in Wrap(paragraph, 3.4f, textWidth))
            {
                y += 5;
                s.Text(line, new Vector2(m, y), 3.4f, Gray);
            }

        // wskazówki (u dołu strony) — liczone wcześniej, żeby tabela wiedziała, ile ma miejsca
        var tips = new List<string>
        {
            "Przenieś wzór na tkaninę przez kalkę, na podświetlonej szybie albo pisakiem znikającym.",
            "Pisaki i ołówki termotransferowe: drukuj z lustrzanym odbiciem, a potem przeprasuj kartkę na tkaninę.",
            "Szyj od 1 w górę: najpierw duże plamy, na końcu drobne detale i kontury.",
        };
        if (_doc.Print.StitchArrows)
            tips.Add("Czerwone strzałki pokazują kierunek wypełnienia — prowadź ścieg tam i z powrotem wzdłuż nich.");
        if (_doc.Lines.Count > 0)
            tips.Add("Linia kreska-kropka to detal do przeszycia samą linią (np. 2 razy) nicią o numerze przy jej początku.");
        tips.Add("Znaczniki na krawędziach wzoru wskazują środek — ułatwiają wyśrodkowanie w tamborku.");
        var tipLines = tips.SelectMany(t => Wrap("• " + t, 3f, (float)pw - 2 * m)).ToList();
        float tipsTop = (float)ph - 12 - 5 * tipLines.Count - 4;

        // tabela nici
        y = Math.Max(y + 10, ty + th + 10);
        s.Text("NICI — W KOLEJNOŚCI SZYCIA", new Vector2(m, y), 3.2f, Accent, bold: true);
        y += 4;
        s.Stroke(Shapes.Line(new Vector2(m, y), new Vector2((float)pw - m, y)), LightGray, 0.25f);
        y += 2;
        double pixels = Math.Max(1, _an.Width * (double)_an.Height);
        int rows = _doc.Palette.Count;
        float rowH = Math.Clamp((tipsTop - 6 - y) / Math.Max(1, rows), 6.5f, 11f);
        for (int i = 0; i < rows; i++)
        {
            var e = _doc.Palette[i];
            int area = i < _an.ColorArea.Length ? _an.ColorArea[i] : 0;
            float cy = y + rowH / 2;
            float r = Math.Min(3.4f, rowH * 0.36f);
            // numer w kółku
            s.Fill(Shapes.Circle(new Vector2(m + r, cy), r), e.IsFabric ? Rgb.White : Ink);
            if (e.IsFabric) s.Stroke(Shapes.Circle(new Vector2(m + r, cy), r), Gray, 0.25f);
            s.Text(e.IsFabric ? "–" : numbers[i].ToString(CultureInfo.InvariantCulture), new Vector2(m + r, cy + r * 0.42f), r * 1.15f, e.IsFabric ? Gray : Rgb.White, TextAnchor.Middle, bold: true);
            // próbka
            float sx = m + 2 * r + 4;
            s.Fill(Shapes.Rect(sx, cy - rowH * 0.32f, 16, rowH * 0.64f), e.Color);
            s.Stroke(Shapes.Rect(sx, cy - rowH * 0.32f, 16, rowH * 0.64f), Gray, 0.2f);
            // opis (do prawej) i nazwa (skrócona, jeśli by na niego weszła)
            var u = usage[i];
            int regionCount = i < _an.ColorRegionCount.Length ? _an.ColorRegionCount[i] : 0;
            var detail = e.IsFabric
                ? "tkanina — nie szyjemy" + (u.Lines > 0 ? $" · linie: {u.Lines} · ok. {ThreadEstimator.Meters(u.Meters)}" : "")
                : $"{e.Color.Hex} · {Pct(area / pixels)} · {Num(u.FillCm2)} cm² · obszary: {regionCount}"
                  + (u.Lines > 0 ? $" · linie: {u.Lines}" : "")
                  + $" · ok. {ThreadEstimator.Meters(u.Meters)}";
            s.Text(detail, new Vector2((float)pw - m, cy + 1.3f), 3f, Gray, TextAnchor.End);
            float nx = sx + 20;
            float nameMax = (float)pw - m - FontMetrics.Measure(detail, 3f) - 4 - nx;
            var name = string.IsNullOrWhiteSpace(e.Name) ? ThreadCatalog.NameFor(e.Color) : e.Name;
            s.Text(Fit(name, 3.6f, true, Math.Max(12, nameMax)), new Vector2(nx, cy + 1.3f), 3.6f, Ink, bold: true);
            y += rowH;
            s.Stroke(Shapes.Line(new Vector2(m, y), new Vector2((float)pw - m, y)), new Rgb(225, 225, 225), 0.2f);
        }

        // wskazówki
        y = Math.Max(y + 8, tipsTop);
        s.Text("WSKAZÓWKI", new Vector2(m, y), 3.2f, Accent, bold: true);
        foreach (var t in tipLines)
        {
            y += 5;
            s.Text(t, new Vector2(m, y), 3f, Gray);
        }
    }

    /// <summary>Skraca tekst z „…”, żeby zmieścił się w <paramref name="maxWidth"/> (mm).</summary>
    public static string Fit(string text, float size, bool bold, float maxWidth)
    {
        if (FontMetrics.Measure(text, size, bold) <= maxWidth) return text;
        while (text.Length > 1 && FontMetrics.Measure(text + "…", size, bold) > maxWidth) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>Dzieli tekst na wiersze nie szersze niż <paramref name="maxWidth"/> (mm), po spacjach; kolejne wiersze z wcięciem.</summary>
    public static List<string> Wrap(string text, float size, float maxWidth, bool bold = false)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Trim().Length > 0 && FontMetrics.Measure(candidate, size, bold) > maxWidth)
            {
                lines.Add(current);
                current = "   " + word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Trim().Length > 0) lines.Add(current);
        return lines;
    }

    public static string Cm(double mm) => (mm / 10).ToString(mm < 1000 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL"));

    private static string Num(double v) => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL"));

    private static string Pct(double f) => (f * 100).ToString(f < 0.1 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL")) + "%";
}
