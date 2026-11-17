using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Output;

/// <summary>Eksport wzoru do plików (bez interfejsu i Win2D).</summary>
public static class Exporters
{
    /// <summary>
    /// Wszystkie kartki (i legenda) jako wektorowy PDF w skali 1:1 — opcjonalnie z korektą skali z kalibracji drukarki
    /// (gdy PDF będzie drukowany na tej samej drukarce).
    /// </summary>
    public static void WritePdf(PatternDocument doc, PatternAnalysis analysis, Stream output, double scaleX = 1, double scaleY = 1,
        IReadOnlyList<Project.SavedThread>? stock = null) =>
        WritePdf(new PatternComposer(doc, analysis, stock), doc.Title, output, scaleX, scaleY);

    public static void WritePdf(IPageSource pages, string title, Stream output, double scaleX = 1, double scaleY = 1)
    {
        var pdf = new PdfWriter(title);
        for (int i = 0; i < pages.PageCount; i++)
        {
            var (w, h) = pages.PageSize(i);
            var s = pdf.BeginPage(w, h);
            bool scaled = Math.Abs(scaleX - 1) > 1e-6 || Math.Abs(scaleY - 1) > 1e-6;
            if (scaled) s.PushTransform(ScaleAboutCenter(w, h, scaleX, scaleY));
            pages.DrawPage(s, i);
            if (scaled) s.Pop();
        }
        pdf.Save(output);
    }

    /// <summary>Skalowanie strony względem jej środka (korekta drukarki nie przesuwa wzoru do rogu).</summary>
    public static Matrix3x2 ScaleAboutCenter(double width, double height, double scaleX, double scaleY)
    {
        var c = new Vector2((float)width / 2, (float)height / 2);
        return Matrix3x2.CreateTranslation(-c) * Matrix3x2.CreateScale((float)scaleX, (float)scaleY) * Matrix3x2.CreateTranslation(c);
    }

    /// <summary>
    /// SVG w rzeczywistym rozmiarze: każdy kolor jako osobna warstwa (Inkscape / Ink/Stitch / ploter), do tego warstwy
    /// konturów i numerów. Lustrzane odbicie — jak w ustawieniach wydruku.
    /// </summary>
    public static string ToSvg(PatternDocument doc, PatternAnalysis an)
    {
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.###", inv);
        double wmm = doc.Print.WidthMm, hmm = doc.Print.HeightMm(an.Width, an.Height);
        var composer = new PatternComposer(doc, an);
        var style = composer.PrintStyle();
        string transform = doc.Print.Mirror ? $" transform=\"matrix(-1 0 0 1 {an.Width} 0)\"" : "";

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" ")
          .Append($"width=\"{F(wmm)}mm\" height=\"{F(hmm)}mm\" viewBox=\"0 0 {an.Width} {an.Height}\">\n");
        sb.Append($"  <title>{Esc(doc.Title)}</title>\n");
        for (int c = 0; c < doc.Palette.Count && c < an.ColorPaths.Length; c++)
        {
            if (an.ColorPaths[c].IsEmpty) continue;
            var e = doc.Palette[c];
            var label = e.IsFabric ? $"{e.Name} (tkanina)" : $"{c + 1} {e.Name}";
            sb.Append($"  <g inkscape:groupmode=\"layer\" id=\"kolor-{c + 1}\" inkscape:label=\"{Esc(label)}\"{transform}>\n");
            sb.Append($"    <path fill=\"{doc.DisplayColor(c).Hex}\" fill-rule=\"evenodd\" d=\"{an.ColorPaths[c].ToSvgData()}\"/>\n");
            sb.Append("  </g>\n");
        }
        sb.Append($"  <g inkscape:groupmode=\"layer\" id=\"kontury\" inkscape:label=\"Kontury\"{transform}>\n");
        sb.Append($"    <path fill=\"none\" stroke=\"{PatternComposer.Ink.Hex}\" stroke-width=\"{F(style.LineWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"{an.Outline.ToSvgData()}\"/>\n");
        sb.Append("  </g>\n");

        // linie detali — kolorem nici (Ink/Stitch zrobi z nich ścieg prosty)
        if (doc.Lines.Count > 0)
        {
            var lineStyle = composer.PrintStyle(fullColor: true);
            sb.Append($"  <g inkscape:groupmode=\"layer\" id=\"linie\" inkscape:label=\"Linie detali\"{transform}>\n");
            var paths = composer.LinePaths;
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].IsEmpty) continue;
                int c = doc.Lines[i].Color;
                var color = c >= 0 && c < doc.Palette.Count ? doc.Palette[c].Color : PatternComposer.Ink;
                sb.Append($"    <path fill=\"none\" stroke=\"{color.Hex}\" stroke-width=\"{F(lineStyle.DetailWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"{paths[i].ToSvgData()}\"/>\n");
            }
            sb.Append("  </g>\n");
        }

        // kierunek ściegu — warstwa ukryta (podpowiedź, nie do wyszycia)
        sb.Append($"  <g inkscape:groupmode=\"layer\" id=\"kierunek\" inkscape:label=\"Kierunek ściegu\" style=\"display:none\"{transform}>\n");
        composer.DrawArrows(new SvgPaths(sb), style with { Numbers = false }, new RectF(0, 0, an.Width, an.Height));
        sb.Append("  </g>\n");

        // numery: SVG z napisami w odbiciu — liczymy pozycje sami, żeby tekst dało się normalnie edytować
        var numbers = new SvgNumbers(sb, doc.Print.Mirror ? an.Width : null);
        sb.Append("  <g inkscape:groupmode=\"layer\" id=\"numery\" inkscape:label=\"Numery\" style=\"display:inline\" font-family=\"Arial, Helvetica, sans-serif\" text-anchor=\"middle\">\n");
        composer.DrawNumbers(numbers, style, new RectF(0, 0, an.Width, an.Height));
        sb.Append("  </g>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";

    /// <summary>Zbiera tylko obrysy (bez przekształceń) jako elementy &lt;path&gt;.</summary>
    private sealed class SvgPaths(StringBuilder sb) : IDrawSurface
    {
        public void PushTransform(Matrix3x2 transform) { }
        public void PushClip(RectF rect) { }
        public void Pop() { }
        public void Fill(PathData path, Rgb color) { }
        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false) { }

        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null)
        {
            if (path.IsEmpty) return;
            sb.Append($"    <path fill=\"none\" stroke=\"{color.Hex}\" stroke-width=\"{width.ToString("0.###", CultureInfo.InvariantCulture)}\" ")
              .Append($"stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"{path.ToSvgData()}\"/>\n");
        }
    }

    /// <summary>Zbiera tylko napisy (numery) z kompozytora jako elementy &lt;text&gt;.</summary>
    private sealed class SvgNumbers(StringBuilder sb, float? mirrorWidth) : IDrawSurface
    {
        public void PushTransform(Matrix3x2 transform) { }
        public void PushClip(RectF rect) { }
        public void Pop() { }
        public void Fill(PathData path, Rgb color) { }
        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null) { }

        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false)
        {
            var inv = CultureInfo.InvariantCulture;
            if (mirrorWidth is { } w)
            {
                // tekst odbity jak cały wzór (do pisaków termotransferowych)
                sb.Append($"    <text transform=\"matrix(-1 0 0 1 {(w - baseline.X).ToString("0.##", inv)} {baseline.Y.ToString("0.##", inv)})\" ")
                  .Append($"font-size=\"{size.ToString("0.##", inv)}\" fill=\"{color.Hex}\">{Esc(text)}</text>\n");
            }
            else
            {
                sb.Append($"    <text x=\"{baseline.X.ToString("0.##", inv)}\" y=\"{baseline.Y.ToString("0.##", inv)}\" ")
                  .Append($"font-size=\"{size.ToString("0.##", inv)}\" fill=\"{color.Hex}\">{Esc(text)}</text>\n");
            }
        }
    }
}
