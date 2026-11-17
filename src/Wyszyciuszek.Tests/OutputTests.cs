using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Wyszyciuszek.Tests;

public class OutputTests
{
    private static readonly PaperSize A4 = Papers.Get("A4");

    [Fact]
    public void Small_design_fits_on_one_centered_page()
    {
        var l = PageLayout.Compute(150, 100, A4, legend: true);
        Assert.Single(l.Tiles);
        Assert.Equal(2, l.PageCount);
        Assert.False(l.Landscape);
        var t = l.Tiles[0];
        Assert.Equal("", t.Label);
        Assert.Equal((210 - 150) / 2.0, t.PageX, 6);
        Assert.Equal(new RectD(0, 0, 150, 100), t.Area);
        Assert.True(l.IsLegendPage(1));
        Assert.Equal((210.0, 297.0), l.PageSize(1));
    }

    [Fact]
    public void Wide_design_switches_to_landscape()
    {
        var l = PageLayout.Compute(250, 150, A4, legend: false);
        Assert.True(l.Landscape);
        Assert.Single(l.Tiles);
        Assert.Equal((297.0, 210.0), l.PageSize(0));
    }

    [Theory]
    [InlineData(400, 300)]
    [InlineData(600, 600)]
    [InlineData(1000, 250)]
    public void Large_design_is_tiled_with_overlap_covering_everything(double w, double h)
    {
        var l = PageLayout.Compute(w, h, A4, legend: false);
        Assert.True(l.Tiles.Count > 1);
        Assert.Equal(l.Columns * l.Rows, l.Tiles.Count);
        foreach (var t in l.Tiles)
        {
            // kafelek mieści się w polu zadruku
            Assert.True(t.PageX + t.Area.W <= l.PageWidth - PageLayout.Margin + 1e-6);
            Assert.True(t.PageY + t.Area.H <= l.PageHeight - PageLayout.Margin + 1e-6);
            Assert.True(t.Area.Right <= w + 1e-6 && t.Area.Bottom <= h + 1e-6);
            Assert.True(t.PageX >= PageLayout.Margin - 1e-6 && t.PageY >= PageLayout.Margin + PageLayout.Header - 1e-6);
            // zakładka z sąsiadem dokładnie Overlap
            if (l.TileAt(t.Column + 1, t.Row) is { } right) Assert.Equal(PageLayout.Overlap, t.Area.Right - right.Area.X, 6);
            if (l.TileAt(t.Column, t.Row + 1) is { } below) Assert.Equal(PageLayout.Overlap, t.Area.Bottom - below.Area.Y, 6);
        }
        // pokrycie całego wzoru
        Assert.Equal(0, l.Tiles.Min(t => t.Area.X));
        Assert.Equal(w, l.Tiles.Max(t => t.Area.Right), 6);
        Assert.Equal(h, l.Tiles.Max(t => t.Area.Bottom), 6);
        Assert.Equal("A1", l.Tiles[0].Label);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    public void Column_names(int i, string name) => Assert.Equal(name, PageLayout.ColumnName(i));

    [Fact]
    public void Hoops_fit_and_max_width()
    {
        var round = Hoops.Get("r15")!;
        Assert.True(round.Fits(90, 90));
        Assert.False(round.Fits(140, 50));
        var rect = Hoops.Get("13x18")!;
        Assert.True(rect.Fits(160, 110)); // obrócony
        Assert.Equal(110, rect.MaxWidthFor(1), 6);
        Assert.Equal(130 / Math.Sqrt(2), round.MaxWidthFor(1), 6);
        Assert.Null(Hoops.Get(""));
    }

    private static (PatternDocument Doc, PatternAnalysis An) Sample(double widthMm = 120)
    {
        var img = TestData.Shapes(160, 120);
        var r = Simplifier.Run(img, new SimplifyOptions { Colors = 4, Smoothing = 0 });
        var doc = new PatternDocument
        {
            Title = "Żółć gęślą — test", Width = r.Width, Height = r.Height, Labels = r.Labels, Palette = r.Palette,
            Print = new PrintSettings { WidthMm = widthMm },
        };
        return (doc, PatternAnalysis.Compute(doc.Labels, doc.Width, doc.Height, doc.Palette.Count, 5));
    }

    [Fact]
    public void Pdf_is_well_formed_with_valid_xref()
    {
        var (doc, an) = Sample(500); // kilka kartek + legenda
        using var ms = new MemoryStream();
        Exporters.WritePdf(doc, an, ms);
        var bytes = ms.ToArray();
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);

        int pages = new PatternComposer(doc, an).PageCount;
        Assert.Contains($"/Count {pages}", text);
        Assert.Equal(pages, Regex.Matches(text, "/Type /Page ").Count);

        // tabela xref wskazuje dokładnie na początki obiektów
        int startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[startxref..]);
        var offsets = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (int i = 0; i < offsets.Count; i++) Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);
    }

    [Fact]
    public void Pdf_text_encodes_polish_letters()
    {
        var s = Encoding.ASCII.GetString(PdfText.LiteralString("Żółć (a\\b)"));
        Assert.Equal(@"(\317\363\303\301 \(a\\b\))", s);
        Assert.Equal((byte)'?', PdfText.Encode('€'));
    }

    [Fact]
    public void Svg_is_valid_xml_with_layer_per_color()
    {
        var (doc, an) = Sample();
        var svg = Exporters.ToSvg(doc, an);
        var x = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var layers = x.Root!.Elements(ns + "g").ToList();
        Assert.Equal(doc.Palette.Count + 3, layers.Count); // kolory + kontury + kierunek (ukryty) + numery
        Assert.Contains(layers, l => l.Attribute("id")?.Value == "kierunek" && l.Attribute("style")?.Value == "display:none");
        Assert.Equal("120mm", x.Root.Attribute("width")!.Value);
        Assert.NotEmpty(layers[^1].Elements(ns + "text"));

        doc.Print.Mirror = true;
        Assert.Contains("matrix(-1 0 0 1 160 0)", Exporters.ToSvg(doc, an));
    }

    [Fact]
    public void Composer_numbers_skip_fabric_and_tiny_regions()
    {
        var (doc, an) = Sample();
        var composer = new PatternComposer(doc, an);
        var rec = new RecordingSurface();
        composer.DrawDesign(rec, composer.PrintStyle());
        int stitched = an.Regions.Count(r => !doc.Palette[r.Color].IsFabric);
        Assert.Equal(stitched, rec.Texts.Count);

        // tkanina nie ma numeru i nie zabiera go — pierwsza szyta nić to dalej „1”
        doc.Palette[0].IsFabric = true;
        rec = new RecordingSurface();
        composer.DrawDesign(rec, composer.PrintStyle());
        int stitchedNow = an.Regions.Count(r => !doc.Palette[r.Color].IsFabric);
        Assert.Equal(stitchedNow, rec.Texts.Count);
        Assert.Contains("1", rec.Texts);
        Assert.DoesNotContain(rec.Texts, t => t == doc.Palette.Count.ToString());
        Assert.Equal(0, composer.CountUnnumbered(composer.PrintStyle()));

        // przy maleńkim wzorze numery się nie mieszczą
        doc.Print.WidthMm = 8;
        var tiny = new PatternComposer(doc, an);
        Assert.True(tiny.CountUnnumbered(tiny.PrintStyle()) > 0);
    }

    [Fact]
    public void Crosshairs_appear_on_both_neighbouring_tiles()
    {
        var (doc, an) = Sample(450);
        var composer = new PatternComposer(doc, an);
        Assert.True(composer.Layout.Tiles.Count > 1);
        foreach (var (x, y) in composer.Crosshairs())
            Assert.True(composer.Layout.Tiles.Count(t => t.Area.Contains(x, y)) >= 2, $"krzyżyk ({x:0.0}, {y:0.0}) tylko na jednej kartce");
    }

    [Fact]
    public void Every_page_draws_something()
    {
        var (doc, an) = Sample(400);
        var composer = new PatternComposer(doc, an);
        for (int i = 0; i < composer.PageCount; i++)
        {
            var rec = new RecordingSurface();
            composer.DrawPage(rec, i);
            Assert.True(rec.Paths > 5);
            Assert.Equal(0, rec.Depth); // każde Push ma swoje Pop
        }
    }

    [Fact]
    public void Long_titles_are_shortened_to_fit()
    {
        var t = PatternComposer.Fit(new string('W', 200), 4, true, 50);
        Assert.EndsWith("…", t);
        Assert.True(FontMetrics.Measure(t, 4, true) <= 50);
        Assert.Equal("Kot", PatternComposer.Fit("Kot", 4, true, 50));
    }

    [Fact]
    public void Font_metrics_measure_like_helvetica()
    {
        Assert.Equal(5.56f, FontMetrics.Measure("0", 10), 3);
        Assert.Equal(FontMetrics.Measure("Zażółć", 10), FontMetrics.Measure("Zazolc", 10), 0);
        Assert.True(FontMetrics.Measure("W", 10, bold: true) > FontMetrics.Measure("i", 10, bold: true));
    }

    private sealed class RecordingSurface : IDrawSurface
    {
        public List<string> Texts { get; } = [];
        public int Paths;
        public int Depth;

        public void PushTransform(Matrix3x2 transform) => Depth++;
        public void PushClip(RectF rect) => Depth++;
        public void Pop() => Depth--;
        public void Fill(PathData path, Rgb color) => Paths++;
        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null) => Paths++;
        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false) => Texts.Add(text);
    }
}
