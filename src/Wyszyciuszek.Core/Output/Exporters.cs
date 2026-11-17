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
    /// <summary>Wszystkie kartki (i legenda) jako wektorowy PDF w skali 1:1.</summary>
    public static void WritePdf(PatternDocument doc, PatternAnalysis analysis, Stream output)
    {
        var composer = new PatternComposer(doc, analysis);
        var pdf = new PdfWriter(doc.Title);
        for (int i = 0; i < composer.PageCount; i++)
        {
            var (w, h) = composer.PageSize(i);
            composer.DrawPage(pdf.BeginPage(w, h), i);
        }
        pdf.Save(output);
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
            sb.Append($"    <path fill=\"{e.Color.Hex}\" fill-rule=\"evenodd\" d=\"{an.ColorPaths[c].ToSvgData()}\"/>\n");
            sb.Append("  </g>\n");
        }
        sb.Append($"  <g inkscape:groupmode=\"layer\" id=\"kontury\" inkscape:label=\"Kontury\"{transform}>\n");
        sb.Append($"    <path fill=\"none\" stroke=\"{PatternComposer.Ink.Hex}\" stroke-width=\"{F(style.LineWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"{an.Outline.ToSvgData()}\"/>\n");
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
