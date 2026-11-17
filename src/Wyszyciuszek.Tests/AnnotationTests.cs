using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek.Tests;

/// <summary>Wersja 1.1: tło jako tkanina, numeracja nici, linie detali, kierunek ściegu, zużycie nici.</summary>
public class BackgroundTests
{
    [Fact]
    public void Dominant_border_color_is_found()
    {
        var (labels, w, h) = TestData.Map(
            "00000",
            "01110",
            "01210",
            "00000");
        Assert.Equal(0, LabelOps.DominantBorderLabel(labels, w, h));

        // brzegi pół na pół — żaden kolor nie dominuje
        (labels, w, h) = TestData.Map(
            "0011",
            "0011",
            "0011");
        Assert.Equal(-1, LabelOps.DominantBorderLabel(labels, w, h, 0.6));
        Assert.Equal(-1, LabelOps.DominantBorderLabel([], 0, 0));
    }

    private static ImageRgb CircleOnCream(int w = 120, int h = 90)
    {
        var img = ImageRgb.Filled(w, h, TestData.Cream);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - w / 2f, dy = y - h / 2f;
                if (dx * dx + dy * dy < 30 * 30) img[x, y] = TestData.Red;
                if (Math.Abs(dx) < 8 && Math.Abs(dy) < 8) img[x, y] = TestData.Blue;
            }
        return img;
    }

    [Fact]
    public void Background_as_fabric_is_opt_in()
    {
        var img = CircleOnCream();
        Assert.False(new SimplifyOptions().BackgroundAsFabric); // domyślnie wyłączone

        var off = Simplifier.Run(img, new SimplifyOptions { Colors = 3 });
        Assert.DoesNotContain(off.Palette, p => p.IsFabric);

        var on = Simplifier.Run(img, new SimplifyOptions { Colors = 3, BackgroundAsFabric = true });
        var fabric = Assert.Single(on.Palette, p => p.IsFabric);
        Assert.True(ColorMath.DeltaE2000(fabric.Color, TestData.Cream) < 5);
    }

    [Fact]
    public void Background_toggle_on_existing_pattern_is_undoable()
    {
        var (labels, w, h) = TestData.Map(
            "00000",
            "01110",
            "00000");
        var s = new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red));
        Assert.Equal(0, s.SetBackgroundFabric(true));
        Assert.True(s.Document.Palette[0].IsFabric);
        Assert.Equal(0, s.SetBackgroundFabric(false));
        Assert.False(s.Document.Palette[0].IsFabric);
        s.Undo();
        Assert.True(s.Document.Palette[0].IsFabric);
    }

    [Fact]
    public void Fabric_does_not_take_a_thread_number()
    {
        var (labels, w, h) = TestData.Map("0123");
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Green, TestData.Blue);
        doc.Palette[1].IsFabric = true;
        Assert.Equal([1, 0, 2, 3], doc.ThreadNumbers());
        Assert.Equal("T", doc.ThreadLabel(1));
        Assert.Equal("2", doc.ThreadLabel(2));
        Assert.Equal("", doc.ThreadLabel(9));
    }
}

public class DetailLineTests
{
    private static EditorSession Session(params string[] rows)
    {
        var (labels, w, h) = TestData.Map(rows);
        return new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Green, TestData.Blue));
    }

    private static readonly Vector2[] Wobbly =
        [new(1, 1), new(2, 1.3f), new(3, 0.8f), new(4, 1.2f), new(5, 0.9f), new(6, 1.1f), new(7, 1)];

    [Fact]
    public void Smoothing_keeps_ends_and_drops_jitter()
    {
        var pts = DetailLine.Smooth(Wobbly);
        Assert.Equal(Wobbly[0], pts[0]);
        Assert.Equal(Wobbly[^1], pts[^1]);
        Assert.True(pts.Count < Wobbly.Length);
        Assert.Single(DetailLine.Smooth([new(1, 1), new(1.2f, 1.1f)])); // zbyt blisko — jeden punkt
    }

    [Fact]
    public void Line_hit_test_and_length()
    {
        var line = new DetailLine { Points = [new(0, 0), new(10, 0), new(10, 5)] };
        Assert.Equal(15, line.Length, 3);
        Assert.Equal(2, line.DistanceTo(new Vector2(5, 2)), 3);
        var doc = new PatternDocument { Lines = [line] };
        Assert.Equal(0, doc.HitLine(new Vector2(9, 3), 1.5f));
        Assert.Equal(-1, doc.HitLine(new Vector2(5, 3), 1.5f));
        Assert.False(line.ToPath().IsEmpty);
    }

    [Fact]
    public void Lines_are_added_deleted_recolored_and_undone()
    {
        var s = Session("01230000", "00000000", "00000000");
        int changes = 0;
        s.Changed += k => { if (k.HasFlag(ChangeKind.Annotations)) changes++; };

        Assert.False(s.AddLine([new(1, 1)], 1)); // samo kliknięcie
        Assert.False(s.AddLine(Wobbly, 9)); // nie ma takiej nici
        Assert.True(s.AddLine(Wobbly, 1));
        Assert.Single(s.Document.Lines);
        Assert.Equal(1, changes);

        Assert.True(s.SetLineColor(0, 2));
        Assert.False(s.SetLineColor(0, 2));
        Assert.Equal(2, s.Document.Lines[0].Color);

        Assert.True(s.DeleteLine(0));
        Assert.Empty(s.Document.Lines);
        s.Undo();
        Assert.Single(s.Document.Lines);
        s.Undo();
        Assert.Equal(1, s.Document.Lines[0].Color);
        s.Redo();
        Assert.Equal(2, s.Document.Lines[0].Color);
    }

    [Fact]
    public void Palette_changes_keep_line_threads()
    {
        var s = Session("0123", "0123");
        s.AddLine(Wobbly, 3); // niebieska linia
        s.AddLine(Wobbly, 1); // czerwona linia
        var blue = s.Document.Palette[3];

        s.MoveColor(3, 0); // niebieski na początek
        Assert.Same(blue, s.Document.Palette[s.Document.Lines[0].Color]);

        s.MergeColor(s.Document.Lines[1].Color, 0); // czerwony → niebieski
        Assert.All(s.Document.Lines, l => Assert.Same(blue, s.Document.Palette[l.Color]));
    }

    [Fact]
    public void Color_used_only_by_a_line_is_not_removed()
    {
        var s = Session("0011", "0011");
        s.Document.Palette.Add(new PaletteEntry { Color = TestData.Blue, Name = "Tylko linia" });
        s.Document.Palette.Add(new PaletteEntry { Color = TestData.Green, Name = "Nieużywany" });
        s.AddLine(Wobbly, 2);
        Assert.Equal(1, s.RemoveUnusedColors());
        Assert.Equal("Tylko linia", s.Document.Palette[s.Document.Lines[0].Color].Name);
    }

    [Fact]
    public void Resimplifying_rescales_lines_and_picks_nearest_thread()
    {
        var s = Session("0011", "0011");
        s.AddLine([new(0.5f, 0.5f), new(3.5f, 1.5f)], 1); // czerwona
        var bigger = new SimplifyResult(new byte[8 * 4], 8, 4,
        [
            new PaletteEntry { Color = TestData.Blue, Name = "Niebieski" },
            new PaletteEntry { Color = new Rgb(225, 35, 45), Name = "Prawie czerwony" },
        ]);
        s.ApplySimplified(bigger);
        var line = s.Document.Lines[0];
        Assert.Equal(1, line.Color);
        Assert.Equal(new Vector2(1, 1), line.Points[0]);
        Assert.Equal(new Vector2(7, 3), line.Points[^1]);
        s.Undo();
        Assert.Equal(new Vector2(0.5f, 0.5f), s.Document.Lines[0].Points[0]);
    }
}

public class StitchDirectionTests
{
    [Theory]
    [InlineData(40, 6, 0)]   // poziomy pasek
    [InlineData(6, 40, 90)]  // pionowy pasek
    public void Automatic_direction_follows_the_long_axis(int bw, int bh, float expected)
    {
        int w = 50, h = 50;
        var labels = new byte[w * h];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++) labels[(5 + y) * w + 5 + x] = 1;
        var an = PatternAnalysis.Compute(labels, w, h, 2, 0);
        var bar = an.Regions.Single(r => r.Color == 1);
        Assert.InRange(AngleDiff(expected, bar.Axis), 0f, 2f);
    }

    [Fact]
    public void Round_and_diagonal_shapes()
    {
        int w = 60, h = 60;
        var labels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if ((x - 15) * (x - 15) + (y - 15) * (y - 15) < 100) labels[y * w + x] = 1; // koło
                if (Math.Abs((x - 40) - (y - 40)) < 3 && x is > 28 and < 55) labels[y * w + x] = 2; // ukośny pasek ↘
            }
        var an = PatternAnalysis.Compute(labels, w, h, 3, 0);
        Assert.Equal(45, an.Regions.Single(r => r.Color == 1).Axis, 1);
        Assert.InRange(AngleDiff(45, an.Regions.Single(r => r.Color == 2).Axis), 0f, 5f);
    }

    /// <summary>Różnica kierunków (kąt i kąt+180° to ten sam kierunek).</summary>
    private static float AngleDiff(float a, float b)
    {
        float d = Math.Abs(a - b) % 180;
        return Math.Min(d, 180 - d);
    }

    [Fact]
    public void Angles_are_normalized()
    {
        Assert.Equal(0, StitchPlan.Normalize(180));
        Assert.Equal(170, StitchPlan.Normalize(-10));
        Assert.Equal(30, StitchPlan.Normalize(390));
        Assert.Equal(135, StitchPlan.AngleOf(new Vector2(5, 5), new Vector2(0, 10)));
        Assert.Equal(90, StitchPlan.AngleOf(new Vector2(0, 0), new Vector2(0, -3)));
    }

    [Fact]
    public void Manual_direction_replaces_previous_and_can_be_reset()
    {
        var (labels, w, h) = TestData.Map(
            "000111",
            "000111",
            "000111");
        var s = new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red));
        var an = PatternAnalysis.Compute(s.Document.Labels, w, h, 2, 0);

        Assert.True(s.SetStitchDirection(an.Components, new Vector2(4.5f, 1.5f), 30));
        Assert.True(s.SetStitchDirection(an.Components, new Vector2(5.5f, 0.5f), 120)); // ten sam obszar
        var hint = Assert.Single(s.Document.StitchHints);
        Assert.Equal(120, hint.Angle);

        var dirs = StitchPlan.Directions(an, s.Document.StitchHints);
        int red = an.HitTest(new Vector2(4.5f, 1.5f)), cream = an.HitTest(new Vector2(0.5f, 0.5f));
        Assert.Equal(new StitchDirection(120, true), dirs[red]);
        Assert.False(dirs[cream].Manual);

        Assert.False(s.SetStitchDirection(an.Components, new Vector2(0.5f, 0.5f), null)); // nic do przywrócenia
        Assert.True(s.SetStitchDirection(an.Components, new Vector2(3.5f, 2.5f), null));
        Assert.Empty(s.Document.StitchHints);
        s.Undo();
        Assert.Single(s.Document.StitchHints);
        Assert.False(s.SetStitchDirection(an.Components, new Vector2(-1, 0), 10)); // poza wzorem
    }
}

public class ThreadEstimatorTests
{
    [Fact]
    public void Usage_grows_with_area_and_skips_fabric()
    {
        var (labels, w, h) = TestData.Map(
            "0000111111",
            "0000111111");
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red);
        doc.Print.WidthMm = 100; // 10 mm na piksel
        var an = PatternAnalysis.Compute(labels, w, h, 2, 0);
        var u = ThreadEstimator.PerColor(doc, an);
        Assert.Equal(8, u[0].FillCm2, 3);  // 8 px × 100 mm²
        Assert.Equal(12, u[1].FillCm2, 3);
        Assert.Equal(1.5, u[1].Meters / u[0].Meters, 3);
        Assert.True(u[0].Minutes > ThreadEstimator.ColorChangeMinutes);

        doc.Palette[0].IsFabric = true;
        u = ThreadEstimator.PerColor(doc, an);
        Assert.Equal(0, u[0].Meters);

        // linia detalu w kolorze tkaniny też jest szyta
        doc.Lines.Add(new DetailLine { Points = [new(0, 0), new(5, 0)], Color = 0 });
        u = ThreadEstimator.PerColor(doc, an);
        Assert.Equal(50, u[0].LineMm, 3);
        Assert.True(u[0].Meters > 0);
        Assert.Equal(1, ThreadEstimator.Total(u).Lines);
    }

    [Theory]
    [InlineData(3.2, "3 min")]
    [InlineData(42, "40 min")]
    [InlineData(70, "1 h 10 min")]
    [InlineData(360, "6 h")]
    public void Duration_is_rounded_for_people(double minutes, string text) => Assert.Equal(text, ThreadEstimator.Duration(minutes));

    [Fact]
    public void Meters_text() => Assert.Equal(("<0,1 m", "2,5 m", "26 m"), (ThreadEstimator.Meters(0.01), ThreadEstimator.Meters(2.5), ThreadEstimator.Meters(25.6)));
}

public class AnnotationOutputTests
{
    private static (PatternDocument Doc, PatternAnalysis An) Sample()
    {
        var img = TestData.Shapes(160, 120);
        var r = Simplifier.Run(img, new SimplifyOptions { Colors = 4, Smoothing = 0 });
        var doc = new PatternDocument
        {
            Title = "Linie", Width = r.Width, Height = r.Height, Labels = r.Labels, Palette = r.Palette,
            Print = new PrintSettings { WidthMm = 200 },
        };
        doc.Lines.Add(new DetailLine { Points = [new(20, 60), new(60, 50), new(100, 70)], Color = 1 });
        doc.StitchHints.Add(new StitchHint { Point = new Vector2(80, 54), Angle = 10 });
        return (doc, PatternAnalysis.Compute(doc.Labels, doc.Width, doc.Height, doc.Palette.Count, 5));
    }

    [Fact]
    public void Pattern_has_arrows_dashed_lines_and_line_numbers()
    {
        var (doc, an) = Sample();
        var composer = new PatternComposer(doc, an);
        var rec = new Recorder();
        composer.DrawDesign(rec, composer.PrintStyle());
        Assert.True(rec.ArrowStrokes > 0);
        Assert.Equal(1, rec.DashedStrokes);
        Assert.Contains(doc.ThreadLabel(1), rec.Texts);

        doc.Print.StitchArrows = false;
        rec = new Recorder();
        new PatternComposer(doc, an).DrawDesign(rec, new PatternComposer(doc, an).PrintStyle());
        Assert.Equal(0, rec.ArrowStrokes);

        // podgląd w kolorach: linia kolorem nici, bez numerów
        rec = new Recorder();
        composer.DrawDesign(rec, composer.PrintStyle(fullColor: true));
        Assert.Contains(doc.Palette[1].Color, rec.StrokeColors);
        Assert.Empty(rec.Texts);
    }

    [Fact]
    public void Manual_direction_is_shown_even_in_a_narrow_region()
    {
        int w = 60, h = 20;
        var labels = new byte[w * h];
        for (int y = 8; y < 12; y++)
            for (int x = 10; x < 50; x++) labels[y * w + x] = 1; // pasek 40×4 px
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red);
        doc.Print.WidthMm = 60; // 1 mm na piksel
        var an = PatternAnalysis.Compute(labels, w, h, 2, 0);

        int Arrows()
        {
            var c = new PatternComposer(doc, an);
            var rec = new Recorder();
            c.DrawArrows(rec, c.PrintStyle(), new RectF(0, 0, w, h));
            return rec.ArrowStrokes;
        }

        Assert.Equal(1, Arrows()); // tło ma strzałkę, wąski pasek — nie
        doc.StitchHints.Add(new StitchHint { Point = new Vector2(30, 10), Angle = 90 });
        Assert.Equal(2, Arrows()); // ręcznie ustawiony kierunek widać zawsze
    }

    [Fact]
    public void Pdf_and_svg_include_lines()
    {
        var (doc, an) = Sample();
        using var ms = new MemoryStream();
        Exporters.WritePdf(doc, an, ms);
        var pdf = string.Concat(PdfStreams(ms.ToArray()));
        Assert.Contains(" 0 J [", pdf); // linia kreska-kropka z płaskimi końcami
        Assert.Contains(" 1 J [] 0 d", pdf); // kontury ciągłe z okrągłymi

        var svg = Exporters.ToSvg(doc, an);
        Assert.Contains("id=\"linie\"", svg);
        Assert.Contains($"stroke=\"{doc.Palette[1].Color.Hex}\"", svg);
    }

    [Fact]
    public void Project_keeps_lines_and_directions_and_stays_v1_without_them()
    {
        var (doc, _) = Sample();
        using var ms = new MemoryStream();
        ProjectFile.Save(doc, ms);
        Assert.Equal(2, FormatOf(ms));
        ms.Position = 0;
        var back = ProjectFile.Load(ms);
        var line = Assert.Single(back.Lines);
        Assert.Equal(1, line.Color);
        Assert.Equal(doc.Lines[0].Points, line.Points);
        var hint = Assert.Single(back.StitchHints);
        Assert.Equal((new Vector2(80, 54), 10f), (hint.Point, hint.Angle));

        doc.Lines.Clear();
        doc.StitchHints.Clear();
        using var plain = new MemoryStream();
        ProjectFile.Save(doc, plain);
        Assert.Equal(1, FormatOf(plain)); // otworzy go też wersja 1.0
    }

    /// <summary>Rozpakowane strumienie PDF (treść stron).</summary>
    private static IEnumerable<string> PdfStreams(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        int at = 0;
        while ((at = text.IndexOf("stream\n", at, StringComparison.Ordinal)) >= 0)
        {
            int start = at + 7, end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) yield break;
            string content;
            try
            {
                using var z = new ZLibStream(new MemoryStream(pdf, start, end - start), CompressionMode.Decompress);
                using var r = new StreamReader(z, Encoding.Latin1);
                content = r.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                content = "";
            }
            yield return content;
            at = end;
        }
    }

    private static int FormatOf(MemoryStream ms)
    {
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        using var s = zip.GetEntry("project.json")!.Open();
        using var json = JsonDocument.Parse(s);
        return json.RootElement.GetProperty("format").GetInt32();
    }

    [Fact]
    public void Wrap_splits_long_text()
    {
        var lines = PatternComposer.Wrap(string.Join(" ", Enumerable.Repeat("nić", 40)), 3.4f, 60);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(FontMetrics.Measure(l, 3.4f) <= 60));
        Assert.Single(PatternComposer.Wrap("krótko", 3.4f, 60));
    }

    private sealed class Recorder : IDrawSurface
    {
        public List<string> Texts { get; } = [];
        public List<Rgb> StrokeColors { get; } = [];
        public int ArrowStrokes, DashedStrokes;

        public void PushTransform(Matrix3x2 transform) { }
        public void PushClip(RectF rect) { }
        public void Pop() { }
        public void Fill(PathData path, Rgb color) { }

        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null)
        {
            StrokeColors.Add(color);
            if (color == PatternComposer.Accent) ArrowStrokes++;
            if (dash is { Length: > 0 }) DashedStrokes++;
        }

        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false) => Texts.Add(text);
    }
}
