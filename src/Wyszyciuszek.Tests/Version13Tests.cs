using System.Text;
using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek.Tests;

/// <summary>Wersja 1.3: „Moje nici” w CSV, zapas na szpulce, gęstość wypełnienia, kolor tkaniny, migawka projektu.</summary>
public class ThreadCsvTests
{
    [Fact]
    public void Csv_roundtrip_keeps_names_colors_and_meters()
    {
        var threads = new List<SavedThread>
        {
            new() { Name = "Czerwień 321", Color = "#D7262E", SpoolMeters = 180 },
            new() { Name = "Żółć; „cytrynowa”", Color = "#FFF35C" },
            new() { Name = "Szary \"melanż\"", Color = "#8C8C8C", SpoolMeters = 12.5 },
        };
        var sw = new StringWriter();
        ThreadCsv.Write(sw, threads);
        var text = sw.ToString();
        Assert.StartsWith(ThreadCsv.Header, text);
        Assert.Contains("12,5", text); // polski przecinek dziesiętny

        var back = ThreadCsv.Read(new StringReader(text));
        Assert.Equal(threads.Select(t => (t.Name, t.Color, t.SpoolMeters)), back.Select(t => (t.Name, t.Color, t.SpoolMeters)));
    }

    [Fact]
    public void Csv_reading_is_forgiving()
    {
        var csv = "﻿nazwa,kolor,metry\nBiel,ffffff,\nCzerń\t#141414\t200\n,#123456,5\nZły kolor;#zz;1\n\nGranat;#1B2A55;12.5\n";
        var t = ThreadCsv.Read(new StringReader(csv));
        Assert.Equal(["Biel", "Czerń", "Granat"], t.Select(x => x.Name));
        Assert.Equal("#FFFFFF", t[0].Color);
        Assert.Null(t[0].SpoolMeters);
        Assert.Equal(200, t[1].SpoolMeters);
        Assert.Equal(12.5, t[2].SpoolMeters);
    }

    [Fact]
    public void Merge_adds_new_and_updates_meters()
    {
        var box = new List<SavedThread> { new() { Name = "Biel", Color = "#FFFFFF", SpoolMeters = 10 } };
        int added = ThreadCsv.Merge(box,
        [
            new() { Name = "biel", Color = "#ffffff", SpoolMeters = 400 },
            new() { Name = "Czerń", Color = "#141414" },
        ]);
        Assert.Equal(1, added);
        Assert.Equal(2, box.Count);
        Assert.Equal(400, box[0].SpoolMeters);
    }

    [Fact]
    public void Csv_file_has_bom_for_excel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nici-{Guid.NewGuid():N}.csv");
        try
        {
            ThreadCsv.Write(path, [new SavedThread { Name = "Łososiowy", Color = "#FA8F7A" }]);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal("Łososiowy", ThreadCsv.Read(path).Single().Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class StockAndDensityTests
{
    private static (PatternDocument Doc, PatternAnalysis An) TwoColors()
    {
        var (labels, w, h) = TestData.Map(
            "0000111111",
            "0000111111");
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red);
        doc.Palette[1].Name = "Czerwień 321";
        doc.Print.WidthMm = 100;
        return (doc, PatternAnalysis.Compute(labels, w, h, 2, 0));
    }

    [Fact]
    public void Sparser_fill_needs_less_thread()
    {
        var (doc, an) = TwoColors();
        double dense = ThreadEstimator.PerColor(doc, an)[1].Meters;
        doc.Print.Fill = FillDensity.Medium;
        double medium = ThreadEstimator.PerColor(doc, an)[1].Meters;
        doc.Print.Fill = FillDensity.Sketch;
        double sketch = ThreadEstimator.PerColor(doc, an)[1].Meters;
        Assert.True(dense > medium && medium > sketch);
        Assert.Equal(2, dense / medium, 3);
        Assert.Contains("szkicowe", ThreadEstimator.Describe(FillDensity.Sketch));
    }

    [Fact]
    public void Shortage_is_found_for_threads_from_the_box()
    {
        var (doc, an) = TwoColors();
        var usage = ThreadEstimator.PerColor(doc, an);
        double need = usage[1].Meters;
        var box = new List<SavedThread>
        {
            new() { Name = "czerwień 321", Color = "#000000", SpoolMeters = need - 5 }, // ta sama nazwa, inny kolor
            new() { Name = "Krem", Color = TestData.Cream.Hex, SpoolMeters = 1 },
        };
        var check = Assert.Single(ThreadStock.Check(doc, usage, box)); // kremowy to „Kolor 1”, nie pasuje nazwą
        Assert.True(check.Short);
        Assert.Equal(5, check.Missing, 6);

        box[0].SpoolMeters = need + 50;
        Assert.False(ThreadStock.Check(doc, usage, box).Single().Short);
        Assert.Empty(ThreadStock.Check(doc, usage, null));

        // dokładne dopasowanie (nazwa i kolor) wygrywa z samą nazwą
        box.Add(new SavedThread { Name = "Czerwień 321", Color = TestData.Red.Hex, SpoolMeters = 1 });
        Assert.Same(box[2], ThreadStock.Match(doc.Palette[1], box));
    }

    [Fact]
    public void Legend_warns_about_missing_thread()
    {
        var (doc, an) = TwoColors();
        var need = ThreadEstimator.PerColor(doc, an)[1].Meters;
        var box = new List<SavedThread> { new() { Name = "Czerwień 321", Color = TestData.Red.Hex, SpoolMeters = need / 2 } };
        var composer = new PatternComposer(doc, an, box);
        var rec = new TextRecorder();
        composer.DrawPage(rec, composer.PageCount - 1); // legenda
        Assert.Contains(rec.Texts, t => t.StartsWith("Zabraknie nici z pudełka: Czerwień 321"));
        Assert.Contains(rec.Texts, t => t.Contains("zabraknie ok."));
    }

    internal sealed class TextRecorder : IDrawSurface
    {
        public List<string> Texts { get; } = [];
        public List<Rgb> Fills { get; } = [];
        public void PushTransform(Matrix3x2 transform) { }
        public void PushClip(RectF rect) { }
        public void Pop() { }
        public void Fill(PathData path, Rgb color) => Fills.Add(color);
        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null) { }
        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false) => Texts.Add(text);
    }
}

public class FabricColorTests
{
    private static EditorSession Session()
    {
        var (labels, w, h) = TestData.Map(
            "000000",
            "011220",
            "000000");
        return new EditorSession(TestData.Document(labels, w, h, new Rgb(250, 249, 245), TestData.Red, new Rgb(240, 240, 236)));
    }

    [Fact]
    public void Fabric_color_marks_matching_background_and_is_undoable()
    {
        var s = Session();
        var white = Fabrics.All.Single(f => f.Name == "Biel").Color;
        Assert.Equal(0, s.SetFabricColor(white)); // prawie białe tło → tkanina
        Assert.True(s.Document.Palette[0].IsFabric);
        Assert.Equal(white, s.Document.DisplayColor(0));
        Assert.Equal(TestData.Red, s.Document.DisplayColor(1));
        Assert.Equal(-1, s.SetFabricColor(white)); // bez zmian

        // jasnoszara nić na białym płótnie słabo widoczna, czerwona — dobrze
        Assert.Equal([2], Fabrics.LowContrast(s.Document));

        s.Undo();
        Assert.Null(s.Document.FabricColor);
        Assert.False(s.Document.Palette[0].IsFabric);
    }

    [Fact]
    public void Existing_fabric_is_kept_and_far_colors_are_not_marked()
    {
        var s = Session();
        Assert.Equal(-1, s.SetFabricColor(Fabrics.All.Single(f => f.Name == "Granat").Color)); // nic podobnego do granatu
        Assert.DoesNotContain(s.Document.Palette, p => p.IsFabric);
        Assert.Empty(Fabrics.LowContrast(s.Document));
        Assert.Equal("Granat", Fabrics.NameFor(s.Document.FabricColor!.Value));
        Assert.Equal("#123456", Fabrics.NameFor(Rgb.Parse("#123456")));
    }

    [Fact]
    public void Colors_preview_paints_fabric_in_fabric_color()
    {
        var s = Session();
        var linen = Fabrics.All.Single(f => f.Name == "Len naturalny").Color;
        s.Document.Palette[0].IsFabric = true;
        s.SetFabricColor(linen);
        var an = PatternAnalysis.Compute(s.Document.Labels, s.Document.Width, s.Document.Height, 3, 0);
        var rec = new StockAndDensityTests.TextRecorder();
        new PatternComposer(s.Document, an).DrawDesign(rec, new DesignStyle { FullColor = true, Numbers = false, Outline = false });
        Assert.Contains(linen, rec.Fills);
        Assert.DoesNotContain(new Rgb(250, 249, 245), rec.Fills);

        var svg = Exporters.ToSvg(s.Document, an);
        Assert.Contains($"fill=\"{linen.Hex}\"", svg);
    }

    [Fact]
    public void Project_keeps_fabric_color_and_snapshot_copies_everything()
    {
        var s = Session();
        var doc = s.Document;
        doc.FabricColor = Rgb.Parse("#4B6384");
        doc.Lines.Add(new DetailLine { Points = [new(0, 0), new(5, 1)], Color = 1 });
        doc.StitchHints.Add(new StitchHint { Point = new Vector2(2, 1), Angle = 30 });
        doc.Print.Fill = FillDensity.Sketch;

        using var ms = new MemoryStream();
        ProjectFile.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectFile.Load(ms);
        Assert.Equal(doc.FabricColor, back.FabricColor);
        Assert.Equal(FillDensity.Sketch, back.Print.Fill);

        var copy = doc.Snapshot();
        Assert.Equal(doc.FabricColor, copy.FabricColor);
        Assert.Single(copy.Lines);
        Assert.Single(copy.StitchHints);
        Assert.NotSame(doc.Labels, copy.Labels);
        Assert.NotSame(doc.Lines[0], copy.Lines[0]);
        doc.Labels[0] = 2;
        doc.Palette[0].Name = "zmiana";
        Assert.NotEqual(2, copy.Labels[0]);
        Assert.NotEqual("zmiana", copy.Palette[0].Name);
    }
}
