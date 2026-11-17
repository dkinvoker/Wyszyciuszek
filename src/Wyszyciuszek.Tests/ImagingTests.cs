namespace Wyszyciuszek.Tests;

public class ImagingTests
{
    [Fact]
    public void Downscale_keeps_aspect_and_averages()
    {
        // lewa połowa czarna, prawa biała → po zmniejszeniu 2× nadal pół na pół
        var img = ImageRgb.Filled(100, 50, Rgb.Black);
        for (int y = 0; y < 50; y++)
            for (int x = 50; x < 100; x++) img[x, y] = Rgb.White;
        var small = img.Downscale(20);
        Assert.Equal(20, small.Width);
        Assert.Equal(10, small.Height);
        Assert.Equal(Rgb.Black, small[0, 5]);
        Assert.Equal(Rgb.White, small[19, 5]);
    }

    [Fact]
    public void Downscale_does_not_enlarge()
    {
        var img = ImageRgb.Filled(30, 20, Rgb.White);
        var r = img.Downscale(800);
        Assert.Equal((30, 20), (r.Width, r.Height));
        Assert.NotSame(img.Data, r.Data);
    }

    [Fact]
    public void FitSize_limits_longer_side() => Assert.Equal((800, 600), ImageRgb.FitSize(1600, 1200, 800));

    [Fact]
    public void Premultiplied_transparency_goes_onto_background()
    {
        // piksel 1: w pełni przezroczysty, piksel 2: pół-przezroczysta czerwień (premultiplikowana), piksel 3: kryjący niebieski
        byte[] bgra = [0, 0, 0, 0, 0, 0, 128, 128, 255, 0, 0, 255];
        var img = ImageRgb.FromBgraPremultiplied(3, 1, bgra, Rgb.White);
        Assert.Equal(Rgb.White, img[0, 0]);
        Assert.Equal(new Rgb(255, 127, 127), img[1, 0]);
        Assert.Equal(new Rgb(0, 0, 255), img[2, 0]);
    }

    [Fact]
    public void Bgra_roundtrip()
    {
        var img = TestData.Shapes(20, 10);
        var back = ImageRgb.FromBgraPremultiplied(20, 10, img.ToBgra(), Rgb.Black);
        Assert.Equal(img.Data, back.Data);
    }

    [Fact]
    public void Bilateral_removes_noise_but_keeps_edges()
    {
        var noisy = TestData.Shapes(80, 60, noise: 25);
        var lab = LabImage.From(noisy);
        var smooth = Filters.Bilateral(lab, 3, 15);

        double Spread(LabImage im, int x0, int y0)
        {
            var vals = new List<float>();
            for (int y = y0; y < y0 + 6; y++)
                for (int x = x0; x < x0 + 6; x++) vals.Add(im.L[y * im.Width + x]);
            double mean = vals.Average();
            return vals.Average(v => (v - mean) * (v - mean));
        }
        // tło (prawy górny róg) — szum mniejszy
        Assert.True(Spread(smooth, 66, 2) < Spread(lab, 66, 2) / 3);
        // krawędź między tłem a niebieskim pasem zostaje wyraźna
        float above = smooth.L[45 * 80 + 40], below = smooth.L[55 * 80 + 40];
        Assert.True(above - below > 40, $"krawędź rozmyta: {above} vs {below}");
    }

    [Fact]
    public void KMeans_finds_distinct_colors_sorted_by_area()
    {
        var img = TestData.Shapes(120, 90);
        var centers = Quantizer.KMeans(LabImage.From(img), 4);
        Assert.Equal(4, centers.Length);
        var rgb = centers.Select(c => c.ToRgb()).ToList();
        // największa powierzchnia — kremowe tło
        Assert.True(ColorMath.DeltaE2000(rgb[0], TestData.Cream) < 2);
        foreach (var expected in new[] { TestData.Red, TestData.Green, TestData.Blue })
            Assert.Contains(rgb, c => ColorMath.DeltaE2000(c, expected) < 2);
    }

    [Fact]
    public void KMeans_returns_fewer_colors_for_flat_image()
    {
        var img = ImageRgb.Filled(40, 40, TestData.Green);
        var centers = Quantizer.KMeans(LabImage.From(img), 6);
        Assert.Single(centers);
    }

    [Fact]
    public void KMeans_is_deterministic()
    {
        var lab = LabImage.From(TestData.Shapes(90, 70, noise: 30));
        Assert.Equal(Quantizer.KMeans(lab, 5, seed: 4), Quantizer.KMeans(lab, 5, seed: 4));
    }

    [Fact]
    public void Assign_picks_nearest_palette_color()
    {
        var img = TestData.Shapes(40, 30);
        Lab[] palette = [TestData.Cream.ToLab(), TestData.Red.ToLab(), TestData.Green.ToLab(), TestData.Blue.ToLab()];
        var labels = Quantizer.Assign(LabImage.From(img), palette);
        Assert.Equal(2, labels[0]); // zielony róg
        Assert.Equal(3, labels[29 * 40 + 20]); // niebieski pas
        Assert.Equal(1, labels[13 * 40 + 20]); // czerwone koło
        Assert.Equal(0, labels[2 * 40 + 38]); // tło
    }

    [Fact]
    public void MatchToThreads_uses_distinct_threads_for_distinct_shades()
    {
        Lab[] colors = [new Rgb(40, 140, 60).ToLab(), new Rgb(150, 210, 90).ToLab()];
        Lab[] threads = [new Rgb(34, 139, 59).ToLab(), new Rgb(154, 205, 50).ToLab(), new Rgb(215, 38, 46).ToLab()];
        Assert.Equal([0, 1], Quantizer.MatchToThreads(colors, threads));
    }

    [Fact]
    public void MatchToThreads_merges_when_no_close_alternative()
    {
        // dwa odcienie zieleni, ale tylko jedna zielona nić — drugi nie dostaje czerwonej
        Lab[] colors = [new Rgb(40, 140, 60).ToLab(), new Rgb(60, 160, 80).ToLab()];
        Lab[] threads = [new Rgb(34, 139, 59).ToLab(), new Rgb(215, 38, 46).ToLab()];
        Assert.Equal([0], Quantizer.MatchToThreads(colors, threads));
    }
}
