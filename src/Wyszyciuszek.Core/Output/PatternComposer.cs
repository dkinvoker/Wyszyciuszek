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
    /// <summary>Wypełnienie kolorem we wzorze (0 = białe tło).</summary>
    public double Tint { get; init; }
    public bool Numbers { get; init; } = true;
    public bool Outline { get; init; } = true;
    public bool Frame { get; init; }
    /// <summary>Grubość linii w jednostkach wzoru (pikselach obrazu roboczego).</summary>
    public float LineWidth { get; init; } = 1;
    /// <summary>Wielkość numerów (w jednostkach wzoru): najmniejsza czytelna i największa.</summary>
    public float MinFont { get; init; } = 6;
    public float MaxFont { get; init; } = 14;
}

/// <summary>
/// Składa wzór, kartki do druku i legendę z poleceń <see cref="IDrawSurface"/>. Ten sam kod rysuje podgląd, PDF, PNG i wydruk.
/// </summary>
public sealed class PatternComposer
{
    public static readonly Rgb Ink = new(26, 26, 26);
    private static readonly Rgb Gray = new(120, 120, 120);
    private static readonly Rgb LightGray = new(185, 185, 185);
    private static readonly Rgb Accent = new(196, 60, 64);

    private readonly PatternDocument _doc;
    private readonly PatternAnalysis _an;

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

    /// <summary>Styl wzoru do druku w skali 1:1 (grubości i czcionki z ustawień w mm).</summary>
    public DesignStyle PrintStyle(bool fullColor = false)
    {
        var p = _doc.Print;
        float k = 1 / MmPerPx;
        return new DesignStyle
        {
            FullColor = fullColor, Tint = p.Tint, Numbers = p.Numbers && !fullColor, Outline = !fullColor, Frame = p.Frame,
            LineWidth = (float)p.LineWidthMm * k, MinFont = 1.7f * k, MaxFont = 4.2f * k,
        };
    }

    // ================================================================== sam wzór (jednostki: piksele obrazu roboczego)

    public void DrawDesign(IDrawSurface s, DesignStyle style)
    {
        var palette = _doc.Palette;
        var bounds = new RectF(0, 0, _an.Width, _an.Height);
        if (!style.FullColor) s.Fill(Shapes.Rect(0, 0, _an.Width, _an.Height), Rgb.White);
        for (int c = 0; c < _an.ColorPaths.Length && c < palette.Count; c++)
        {
            var path = _an.ColorPaths[c];
            if (path.IsEmpty) continue;
            var entry = palette[c];
            if (style.FullColor) s.Fill(path, entry.Color);
            else if (style.Tint > 0.001 && !entry.IsFabric) s.Fill(path, Rgb.White.Lerp(entry.Color, style.Tint));
        }
        if (style.Outline && !_an.Outline.IsEmpty) s.Stroke(_an.Outline, Ink, style.LineWidth);
        if (style.Frame && !_an.Frame.IsEmpty) s.Stroke(_an.Frame, Ink, style.LineWidth);
        if (style.Numbers) DrawNumbers(s, style, bounds);
    }

    /// <summary>Numery nici w obszarach (w miejscu najdalszym od krawędzi; za małe obszary — bez numeru).</summary>
    public void DrawNumbers(IDrawSurface s, DesignStyle style, RectF visible)
    {
        foreach (var r in _an.Regions)
        {
            if (!NumberFor(r, style, out var text, out var size)) continue;
            if (!visible.Inflate(size).Contains(r.LabelPoint)) continue;
            var color = style.FullColor && _doc.Palette[r.Color].Color.PrefersLightText ? Rgb.White : Ink;
            s.Text(text, r.LabelPoint + new Vector2(0, size * 0.36f), size, color, TextAnchor.Middle);
        }
    }

    private bool NumberFor(RegionInfo r, DesignStyle style, out string text, out float size)
    {
        text = "";
        size = 0;
        if (r.Color >= _doc.Palette.Count || _doc.Palette[r.Color].IsFabric) return false;
        text = (r.Color + 1).ToString(CultureInfo.InvariantCulture);
        float fit = r.LabelRadius * 2 / (0.6f * text.Length + 0.25f);
        size = Math.Min(style.MaxFont, Math.Min(fit, r.LabelRadius * 1.6f));
        return size >= style.MinFont;
    }

    /// <summary>Ile obszarów (do szycia) nie zmieści numeru przy danym stylu.</summary>
    public int CountUnnumbered(DesignStyle style) =>
        _an.Regions.Count(r => r.Color < _doc.Palette.Count && !_doc.Palette[r.Color].IsFabric && !NumberFor(r, style, out _, out _));

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
        var title = string.IsNullOrWhiteSpace(_doc.Title) ? "Wzór" : _doc.Title;
        s.Text(title, new Vector2(m, y), 7f, Ink, bold: true);
        y += 7;
        var stitched = Enumerable.Range(0, _doc.Palette.Count).Where(i => !_doc.Palette[i].IsFabric && i < _an.ColorArea.Length && _an.ColorArea[i] > 0).ToList();
        var hoop = Hoops.Get(_doc.Print.Hoop);
        s.Text($"Rozmiar {Cm(L.DesignWidth)} × {Cm(L.DesignHeight)} cm · nici: {stitched.Count} · obszary: {_an.RegionCount}"
               + (hoop is null ? "" : $" · tamborek: {hoop.Name}"), new Vector2(m, y), 3.4f, Gray);
        y += 5;
        s.Text(L.Tiles.Count > 1
            ? $"Wzór na {L.Tiles.Count} kartkach ({L.Columns}×{L.Rows}): nałóż je tak, by krzyżyki się pokryły, i sklej."
            : "Wzór mieści się na jednej kartce.", new Vector2(m, y), 3.4f, Gray);

        // miniatura w kolorach
        float thumbMax = 62;
        float k = Math.Min(thumbMax / _an.Width, thumbMax / _an.Height);
        float tw = _an.Width * k, th = _an.Height * k;
        float tx = (float)pw - m - tw, ty = m;
        s.PushClip(new RectF(tx, ty, tw, th));
        s.PushTransform(Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(tx, ty));
        DrawDesign(s, new DesignStyle { FullColor = true, Outline = false, Numbers = false });
        s.Pop();
        s.Pop();
        s.Stroke(Shapes.Rect(tx, ty, tw, th), LightGray, 0.25f);

        // tabela nici
        y = Math.Max(y + 12, ty + th + 10);
        s.Text("NICI — W KOLEJNOŚCI SZYCIA", new Vector2(m, y), 3.2f, Accent, bold: true);
        y += 4;
        s.Stroke(Shapes.Line(new Vector2(m, y), new Vector2((float)pw - m, y)), LightGray, 0.25f);
        y += 2;
        double total = Math.Max(1, _an.Width * (double)_an.Height);
        double areaCm2 = L.DesignWidth * L.DesignHeight / 100;
        int rows = _doc.Palette.Count;
        float rowH = Math.Clamp(((float)ph - y - 40) / Math.Max(1, rows), 6.5f, 11f);
        for (int i = 0; i < rows; i++)
        {
            var e = _doc.Palette[i];
            int area = i < _an.ColorArea.Length ? _an.ColorArea[i] : 0;
            float cy = y + rowH / 2;
            float r = Math.Min(3.4f, rowH * 0.36f);
            // numer w kółku
            s.Fill(Shapes.Circle(new Vector2(m + r, cy), r), e.IsFabric ? Rgb.White : Ink);
            if (e.IsFabric) s.Stroke(Shapes.Circle(new Vector2(m + r, cy), r), Gray, 0.25f);
            s.Text(e.IsFabric ? "–" : (i + 1).ToString(CultureInfo.InvariantCulture), new Vector2(m + r, cy + r * 0.42f), r * 1.15f, e.IsFabric ? Gray : Rgb.White, TextAnchor.Middle, bold: true);
            // próbka
            float sx = m + 2 * r + 4;
            s.Fill(Shapes.Rect(sx, cy - rowH * 0.32f, 16, rowH * 0.64f), e.Color);
            s.Stroke(Shapes.Rect(sx, cy - rowH * 0.32f, 16, rowH * 0.64f), Gray, 0.2f);
            // nazwa i opis
            float nx = sx + 20;
            s.Text(string.IsNullOrWhiteSpace(e.Name) ? ThreadCatalog.NameFor(e.Color) : e.Name, new Vector2(nx, cy + 1.3f), 3.6f, Ink, bold: true);
            var detail = e.IsFabric
                ? "tkanina — nie szyjemy"
                : $"{e.Color.Hex} · {Pct(area / total)} · ok. {Num(areaCm2 * area / total)} cm² · obszary: {(i < _an.ColorRegionCount.Length ? _an.ColorRegionCount[i] : 0)}";
            s.Text(detail, new Vector2((float)pw - m, cy + 1.3f), 3f, Gray, TextAnchor.End);
            y += rowH;
            s.Stroke(Shapes.Line(new Vector2(m, y), new Vector2((float)pw - m, y)), new Rgb(225, 225, 225), 0.2f);
        }

        // wskazówki
        y = Math.Max(y + 8, (float)ph - 34);
        s.Text("WSKAZÓWKI", new Vector2(m, y), 3.2f, Accent, bold: true);
        string[] tips =
        [
            "Przenieś wzór na tkaninę przez kalkę, na podświetlonej szybie albo pisakiem znikającym.",
            "Pisaki i ołówki termotransferowe: drukuj z lustrzanym odbiciem, a potem przeprasuj kartkę na tkaninę.",
            "Szyj od 1 w górę: najpierw duże plamy, na końcu drobne detale i kontury.",
            "Znaczniki na krawędziach wzoru wskazują środek — ułatwiają wyśrodkowanie w tamborku.",
        ];
        foreach (var t in tips)
        {
            y += 5;
            s.Text("• " + t, new Vector2(m, y), 3f, Gray);
        }
    }

    /// <summary>Skraca tekst z „…”, żeby zmieścił się w <paramref name="maxWidth"/> (mm).</summary>
    public static string Fit(string text, float size, bool bold, float maxWidth)
    {
        if (FontMetrics.Measure(text, size, bold) <= maxWidth) return text;
        while (text.Length > 1 && FontMetrics.Measure(text + "…", size, bold) > maxWidth) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    public static string Cm(double mm) => (mm / 10).ToString(mm < 1000 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL"));

    private static string Num(double v) => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL"));

    private static string Pct(double f) => (f * 100).ToString(f < 0.1 ? "0.0" : "0", CultureInfo.GetCultureInfo("pl-PL")) + "%";
}
