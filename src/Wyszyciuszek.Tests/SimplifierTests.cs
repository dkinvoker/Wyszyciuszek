namespace Wyszyciuszek.Tests;

public class SimplifierTests
{
    [Fact]
    public void Simplifies_noisy_shapes_to_clean_regions()
    {
        var img = TestData.Shapes(160, 120, noise: 30);
        var r = Simplifier.Run(img, new SimplifyOptions { Colors = 4, Detail = 5, Smoothing = 5 });
        Assert.Equal(160 * 120, r.Labels.Length);
        Assert.Equal(4, r.Palette.Count);
        // szum zniknął: zostały cztery obszary (tło, koło, pas, kwadrat)
        var comp = LabelOps.FindComponents(r.Labels, r.Width, r.Height);
        Assert.Equal(4, comp.Count);
        // paleta od największej powierzchni — tło pierwsze
        Assert.True(ColorMath.DeltaE2000(r.Palette[0].Color, TestData.Cream) < 6);
    }

    [Fact]
    public void Same_options_give_same_result()
    {
        var img = TestData.Shapes(100, 80, noise: 40);
        var o = new SimplifyOptions { Colors = 5 };
        var a = Simplifier.Run(img, o);
        var b = Simplifier.Run(img, o);
        Assert.Equal(a.Labels, b.Labels);
        Assert.Equal(a.Palette.Select(p => p.Color), b.Palette.Select(p => p.Color));
    }

    [Fact]
    public void Lower_detail_removes_small_shapes()
    {
        var img = TestData.Shapes(160, 120);
        // mała kropka 3×3 px
        for (int y = 100; y < 103; y++)
            for (int x = 140; x < 143; x++) img[x, y] = new Rgb(250, 220, 0);
        var detailed = Simplifier.Run(img, new SimplifyOptions { Colors = 5, Detail = 10, Smoothing = 0 });
        var coarse = Simplifier.Run(img, new SimplifyOptions { Colors = 5, Detail = 1, Smoothing = 0 });
        Assert.True(LabelOps.FindComponents(detailed.Labels, 160, 120).Count > LabelOps.FindComponents(coarse.Labels, 160, 120).Count);
    }

    [Fact]
    public void MyThreads_mode_uses_only_threads_from_the_box()
    {
        var img = TestData.Shapes(120, 90, noise: 10);
        ThreadColor[] threads =
        [
            new("Biała", new Rgb(255, 255, 255)),
            new("Krwista", new Rgb(200, 20, 30)),
            new("Trawa", new Rgb(40, 170, 70)),
            new("Morze", new Rgb(20, 50, 190)),
            new("Fiolet", new Rgb(110, 40, 140)),
        ];
        var r = Simplifier.Run(img, new SimplifyOptions { Colors = 4, Source = PaletteSource.MyThreads }, threads);
        Assert.All(r.Palette, p => Assert.Contains(threads, t => t.Color == p.Color && t.Name == p.Name));
        Assert.DoesNotContain(r.Palette, p => p.Name == "Fiolet");
    }

    [Fact]
    public void Normalize_drops_unused_and_sorts_by_area()
    {
        var (labels, _, _) = TestData.Map("2220", "2223");
        var palette = Enumerable.Range(0, 4).Select(i => new PaletteEntry { Name = $"P{i}" }).ToList();
        Simplifier.Normalize(labels, palette);
        Assert.Equal(["P2", "P0", "P3"], palette.Select(p => p.Name));
        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 2], labels);
    }

    [Fact]
    public void Names_are_made_unique()
    {
        var palette = new List<PaletteEntry> { new() { Name = "Zielony" }, new() { Name = "Zielony" }, new() { Name = "zielony" }, new() { Name = "Biel" } };
        Simplifier.MakeNamesUnique(palette);
        Assert.Equal(["Zielony", "Zielony 2", "zielony 3", "Biel"], palette.Select(p => p.Name));
    }

    [Theory]
    [InlineData(1, 800, 600, 4800)]
    [InlineData(10, 800, 600, 15)]
    public void MinRegionArea_follows_detail(int detail, int w, int h, int expected)
    {
        Assert.Equal(expected, new SimplifyOptions { Detail = detail }.MinRegionArea(w, h));
    }

    [Fact]
    public void Smoothing_zero_means_no_filtering()
    {
        Assert.Equal((0, 0f, 0, 0), new SimplifyOptions { Smoothing = 0 }.SmoothingParameters());
        Assert.True(new SimplifyOptions { Smoothing = 10 }.SmoothingParameters().Radius > new SimplifyOptions { Smoothing = 2 }.SmoothingParameters().Radius);
    }
}
