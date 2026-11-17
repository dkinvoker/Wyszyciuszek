using System.IO.Compression;
using System.Text;
using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek.Tests;

/// <summary>Wersja 1.2: autozapis, wąskie obszary i linie, kolejność szycia, linie do kalkowania, kalibracja drukarki.</summary>
public class AutosaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wyszyciuszek-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Autosave_roundtrip_and_listing()
    {
        var (labels, w, h) = TestData.Map("0011", "0011");
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red);
        doc.Title = "Kotek";
        var older = Autosave.FileFor(_dir, 1234, 638000000000000000);
        var newer = Autosave.FileFor(_dir, 77, 639000000000000000);
        Autosave.Save(older, doc, null);
        doc.Title = "Pies";
        Autosave.Save(newer, doc, @"C:\Wzory\pies.wyszycie");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(Path.Combine(_dir, "nie-moje.wyszycie"), "x"); // obca nazwa — pomijana
        File.WriteAllText(Path.Combine(_dir, "5-6.wyszycie"), "to nie zip"); // uszkodzony — pomijany

        var list = Autosave.List(_dir);
        Assert.Equal(2, list.Count);
        Assert.Equal(("Pies", @"C:\Wzory\pies.wyszycie", 77, 639000000000000000L), (list[0].Title, list[0].OriginalPath, list[0].ProcessId, list[0].ProcessStartTicks));
        Assert.Equal(("Kotek", (string?)null), (list[1].Title, list[1].OriginalPath));
        Assert.Equal(doc.Labels, ProjectFile.Load(list[0].Path).Labels);

        Autosave.Delete(newer);
        Assert.Single(Autosave.List(_dir));
        Assert.False(File.Exists(Path.ChangeExtension(newer, ".txt")));
        Assert.Empty(Autosave.List(Path.Combine(_dir, "brak")));
    }

    [Fact]
    public void Title_is_read_without_loading_the_whole_project()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "a.wyszycie");
        var (labels, w, h) = TestData.Map("01");
        var doc = TestData.Document(labels, w, h);
        doc.Title = "Żółw";
        ProjectFile.Save(doc, path);
        Assert.Equal("Żółw", ProjectFile.ReadTitle(path));
        Assert.Null(ProjectFile.ReadTitle(Path.Combine(_dir, "brak.wyszycie")));
    }
}

public class NarrowAndCenterlineTests
{
    /// <summary>Tło z poziomym paskiem 40×4 px koloru 1 i dużym kwadratem koloru 2.</summary>
    private static (byte[] Labels, int W, int H) Stripe()
    {
        int w = 60, h = 40;
        var labels = new byte[w * h];
        for (int y = 8; y < 12; y++)
            for (int x = 10; x < 50; x++) labels[y * w + x] = 1;
        for (int y = 20; y < 36; y++)
            for (int x = 20; x < 40; x++) labels[y * w + x] = 2;
        return (labels, w, h);
    }

    [Fact]
    public void Narrow_regions_are_found_in_real_size()
    {
        var (labels, w, h) = Stripe();
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Blue);
        doc.Print.WidthMm = 30; // 0,5 mm na piksel — pasek 4 px ma 2 mm
        var an = PatternAnalysis.Compute(labels, w, h, 3, 0);
        var narrow = NarrowRegions.Find(doc, an);
        var stripe = Assert.Single(narrow);
        Assert.Equal(1, an.Regions[stripe.Region].Color);
        Assert.True(stripe.Stripe);
        Assert.InRange(stripe.WidthMm, 1.5, 2.5);

        doc.Print.WidthMm = 120; // ten sam wzór 4× większy — pasek ma już 8 mm
        Assert.Empty(NarrowRegions.Find(doc, an));
    }

    [Fact]
    public void Centerline_runs_along_the_stripe_to_its_ends()
    {
        var (labels, w, h) = Stripe();
        var comp = LabelOps.FindComponents(labels, w, h);
        int stripe = comp.Map[10 * w + 30];
        var line = Centerline.Of(comp, stripe);
        Assert.True(line.Count >= 2);
        var xs = line.Select(p => p.X).ToList();
        Assert.InRange(xs.Min(), 9, 13);
        Assert.InRange(xs.Max(), 47, 51);
        Assert.All(line, p => Assert.InRange(p.Y, 9, 11));
    }

    [Fact]
    public void Branching_stripes_give_a_line_per_branch()
    {
        // „wachlarz” trzech wąsów zbiegających się w jednym punkcie (lewy koniec)
        int w = 70, h = 50;
        var labels = new byte[w * h];
        void Thick(float x0, float y0, float x1, float y1)
        {
            for (float t = 0; t <= 1; t += 0.002f)
            {
                int cx = (int)(x0 + (x1 - x0) * t), cy = (int)(y0 + (y1 - y0) * t);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) labels[(cy + dy) * w + cx + dx] = 1;
            }
        }
        Thick(8, 25, 62, 6);
        Thick(8, 25, 62, 25);
        Thick(8, 25, 62, 44);
        var comp = LabelOps.FindComponents(labels, w, h);
        var lines = Centerline.Lines(comp, comp.Map[25 * w + 30]);
        // najdłuższa linia biegnie przez dwa wąsy, trzeci jest osobną gałęzią (bez drobnych ząbków szkieletu)
        Assert.InRange(lines.Count, 2, 3);
        // każdy koniec wachlarza (prawa strona) jest końcem którejś linii
        foreach (var tipY in new[] { 6f, 25f, 44f })
            Assert.Contains(lines, l => Vector2.Distance(l[0], new Vector2(62, tipY)) < 4 || Vector2.Distance(l[^1], new Vector2(62, tipY)) < 4);
    }

    [Fact]
    public void Dissolved_stripe_splits_between_its_neighbours()
    {
        // pasek koloru 2 między kolorem 0 (u góry) a 1 (na dole)
        var (labels, w, h) = TestData.Map(
            "00000000",
            "00000000",
            "22222222",
            "22222222",
            "11111111",
            "11111111");
        var comp = LabelOps.FindComponents(labels, w, h);
        int changed = LabelOps.DissolveRegion(labels, comp, comp.Map[2 * w]);
        Assert.Equal(16, changed);
        Assert.DoesNotContain((byte)2, labels);
        Assert.Equal(0, labels[2 * w + 3]); // górna połowa paska → górny sąsiad
        Assert.Equal(1, labels[3 * w + 3]); // dolna → dolny
    }

    [Fact]
    public void Converting_regions_to_lines_is_one_undo_step()
    {
        var (labels, w, h) = Stripe();
        var s = new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Blue));
        var comp = LabelOps.FindComponents(s.Document.Labels, w, h);
        int stripe = comp.Map[10 * w + 30];
        Assert.Equal(1, s.ConvertRegionsToLines(comp, [stripe]));
        Assert.DoesNotContain((byte)1, s.Document.Labels);
        var line = Assert.Single(s.Document.Lines);
        Assert.Equal(1, line.Color);
        Assert.True(line.Length > 30);

        s.Undo();
        Assert.Empty(s.Document.Lines);
        Assert.Contains((byte)1, s.Document.Labels);

        // obraz z jednym kolorem — nie ma w co wtopić obszaru
        var one = new EditorSession(TestData.Document(new byte[16], 4, 4, TestData.Cream));
        Assert.Equal(0, one.ConvertRegionsToLines(LabelOps.FindComponents(one.Document.Labels, 4, 4), [0]));
    }
}

public class StitchOrderTests
{
    private static List<PaletteEntry> Palette(int n) =>
        Enumerable.Range(0, n).Select(i => new PaletteEntry { Color = new Rgb((byte)(i * 50), 100, 100), Name = $"K{i}" }).ToList();

    [Fact]
    public void Things_lying_on_top_are_stitched_later_even_if_bigger()
    {
        // tło 0, duży kwadrat 1, w nim kwadrat 2; po tle rozrzucone małe kropki 3 (razem większe niż 2)
        int w = 40, h = 40;
        var labels = new byte[w * h];
        void Rect(int x0, int y0, int x1, int y1, byte c)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) labels[y * w + x] = c;
        }
        Rect(14, 14, 30, 30, 1);
        Rect(19, 19, 25, 25, 2);
        for (int i = 0; i < 6; i++) Rect(3 + i * 6, 4, 5 + i * 6, 6, 3);
        for (int i = 0; i < 6; i++) Rect(3 + i * 6, 34, 5 + i * 6, 36, 3);

        var order = StitchOrder.Layered(labels, w, h, Palette(4));
        Assert.Equal([0, 1, 2, 3], order); // powierzchnią byłoby 0, 1, 3, 2
        Assert.Equal([0, 1, 3, 2], StitchOrder.Compute(labels, w, h, Palette(4), PaletteOrder.Area));
    }

    [Fact]
    public void Thin_outline_goes_after_what_it_surrounds()
    {
        // tło 0, plama 1 otoczona cienkim (2 px) konturem 2 o większej powierzchni niż plama
        int w = 30, h = 30;
        var labels = new byte[w * h];
        for (int y = 9; y < 21; y++)
            for (int x = 9; x < 21; x++) labels[y * w + x] = 2;
        for (int y = 11; y < 19; y++)
            for (int x = 11; x < 19; x++) labels[y * w + x] = 1;

        var palette = Palette(3);
        Assert.Equal([0, 1, 2], StitchOrder.Layered(labels, w, h, palette));

        palette[2].IsFabric = true; // tkanina zawsze pierwsza
        Assert.Equal(2, StitchOrder.Layered(labels, w, h, palette)[0]);
    }

    [Fact]
    public void Thin_lines_cutting_a_blob_do_not_go_under_it()
    {
        // tło 0, duże koło 1 przecięte cienkimi kreskami 2 (jak wąsy kota) — kawałki koła między kreskami
        // nie mogą sprawić, że kreski „leżą pod” kołem
        int w = 60, h = 60;
        var labels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dx = x - 30, dy = y - 30;
                if (dx * dx + dy * dy < 24 * 24) labels[y * w + x] = 1;
                if ((Math.Abs(y - 24) < 1 || Math.Abs(y - 34) < 1) && x is > 2 and < 57) labels[y * w + x] = 2;
            }
        Assert.Equal([0, 1, 2], StitchOrder.Layered(labels, w, h, Palette(3)));
    }

    [Fact]
    public void Session_sorts_by_layers()
    {
        var (labels, w, h) = TestData.Map(
            "1111111",
            "1100011",
            "1102011",
            "1100011",
            "1111111");
        var s = new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Blue));
        var red = s.Document.Palette[1];
        Assert.True(s.SortPalette(PaletteOrder.Layered));
        Assert.Same(red, s.Document.Palette[0]); // czerwona rama dookoła — spód
        Assert.False(s.SortPalette(PaletteOrder.Layered));
    }
}

public class TracingAndCalibrationTests
{
    private static (PatternDocument Doc, PatternAnalysis An) Sample()
    {
        var r = Simplifier.Run(TestData.Shapes(160, 120), new SimplifyOptions { Colors = 4, Smoothing = 0 });
        var doc = new PatternDocument { Title = "Kalka", Width = r.Width, Height = r.Height, Labels = r.Labels, Palette = r.Palette };
        return (doc, PatternAnalysis.Compute(doc.Labels, doc.Width, doc.Height, doc.Palette.Count, 5));
    }

    [Fact]
    public void Tracing_lines_are_dashed_dotted_or_gray()
    {
        var (doc, an) = Sample();
        var style = new PatternComposer(doc, an).PrintStyle();
        Assert.Null(style.OutlineDash);
        Assert.Equal(PatternComposer.Ink, style.Ink);

        doc.Print.Outline = OutlineStyle.Dashed;
        doc.Print.GrayLines = true;
        var c = new PatternComposer(doc, an);
        style = c.PrintStyle();
        Assert.NotNull(style.OutlineDash);
        Assert.Equal(PatternComposer.TraceGray, style.Ink);
        Assert.Null(c.PrintStyle(fullColor: true).OutlineDash); // podgląd w kolorach bez kresek

        doc.Print.Outline = OutlineStyle.Dotted;
        var dots = new PatternComposer(doc, an).PrintStyle().OutlineDash!;
        Assert.True(dots[0] < dots[1]); // krótkie kropki, dłuższe przerwy
    }

    [Fact]
    public void Calibration_scale_and_page()
    {
        Assert.Equal(100 / 98.0, CalibrationPage.ScaleFromMeasurement(98), 6);
        Assert.Equal(1, CalibrationPage.ScaleFromMeasurement(0));

        var page = new CalibrationPage();
        Assert.Equal(1, page.PageCount);
        Assert.Equal((210.0, 297.0), page.PageSize(0));
        using var ms = new MemoryStream();
        Exporters.WritePdf(page, "Kalibracja", ms);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(ms.ToArray(), 0, 4));
    }

    [Fact]
    public void Pdf_can_include_printer_correction()
    {
        var (doc, an) = Sample();
        string Content(double sx, double sy)
        {
            using var ms = new MemoryStream();
            Exporters.WritePdf(doc, an, ms, sx, sy);
            var bytes = ms.ToArray();
            var text = Encoding.Latin1.GetString(bytes);
            int start = text.IndexOf("stream\n", StringComparison.Ordinal) + 7;
            int end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            using var z = new ZLibStream(new MemoryStream(bytes, start, end - start), CompressionMode.Decompress);
            return new StreamReader(z, Encoding.Latin1).ReadToEnd();
        }
        Assert.DoesNotContain("1.02 0 0 0.99", Content(1, 1));
        Assert.Contains("q 1.02 0 0 0.99 ", Content(1.02, 0.99));

        var m = Exporters.ScaleAboutCenter(200, 100, 1.1, 1.1);
        Assert.Equal(new Vector2(100, 50), Vector2.Transform(new Vector2(100, 50), m)); // środek zostaje na miejscu
    }
}
