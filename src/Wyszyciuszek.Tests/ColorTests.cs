namespace Wyszyciuszek.Tests;

public class ColorTests
{
    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("ff8000", 255, 128, 0)]
    [InlineData("#f80", 255, 136, 0)]
    [InlineData("#80FF8000", 255, 128, 0)]
    public void Rgb_parses_hex(string text, byte r, byte g, byte b)
    {
        Assert.True(Rgb.TryParse(text, out var c));
        Assert.Equal(new Rgb(r, g, b), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("zzzzzz")]
    [InlineData(null)]
    public void Rgb_rejects_garbage(string? text) => Assert.False(Rgb.TryParse(text, out _));

    [Fact]
    public void Rgb_hex_roundtrip() => Assert.Equal("#0A0B0C", Rgb.Parse(new Rgb(10, 11, 12).Hex).Hex);

    [Fact]
    public void Lab_of_white_and_red_match_reference()
    {
        var white = Rgb.White.ToLab();
        Assert.Equal(100, white.L, 1);
        Assert.Equal(0, white.A, 1);
        Assert.Equal(0, white.B, 1);

        var red = new Rgb(255, 0, 0).ToLab();
        Assert.Equal(53.24, red.L, 1);
        Assert.Equal(80.09, red.A, 1);
        Assert.Equal(67.20, red.B, 1);
    }

    [Fact]
    public void Lab_roundtrip_is_lossless_for_many_colors()
    {
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            var c = new Rgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            var back = c.ToLab().ToRgb();
            Assert.InRange(Math.Abs(c.R - back.R), 0, 1);
            Assert.InRange(Math.Abs(c.G - back.G), 0, 1);
            Assert.InRange(Math.Abs(c.B - back.B), 0, 1);
        }
    }

    // wartości referencyjne z: G. Sharma, W. Wu, E. Dalal, „The CIEDE2000 Color-Difference Formula” (2005)
    [Theory]
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
    [InlineData(50, 3.1571, -77.2803, 50, 0, -82.7485, 2.8615)]
    [InlineData(50, 0, 0, 50, -1, 2, 2.3669)]
    [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void DeltaE2000_matches_reference(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var d = ColorMath.DeltaE2000(new Lab((float)l1, (float)a1, (float)b1), new Lab((float)l2, (float)a2, (float)b2));
        Assert.Equal(expected, d, 3);
    }

    [Fact]
    public void DeltaE2000_is_symmetric_and_zero_for_same_color()
    {
        var a = new Rgb(12, 200, 99).ToLab();
        var b = new Rgb(240, 10, 77).ToLab();
        Assert.Equal(0, ColorMath.DeltaE2000(a, a), 6);
        Assert.Equal(ColorMath.DeltaE2000(a, b), ColorMath.DeltaE2000(b, a), 6);
    }

    [Fact]
    public void Lerp_and_text_contrast()
    {
        Assert.Equal(new Rgb(128, 128, 128), Rgb.Black.Lerp(Rgb.White, 0.5).Lerp(new Rgb(128, 128, 128), 1));
        Assert.True(Rgb.Black.PrefersLightText);
        Assert.False(Rgb.White.PrefersLightText);
        Assert.False(new Rgb(255, 230, 0).PrefersLightText); // żółty — czarny tekst
    }

    [Fact]
    public void Catalog_nearest_finds_obvious_names()
    {
        Assert.Equal("Biel", ThreadCatalog.NameFor(new Rgb(250, 250, 250)));
        Assert.Equal("Czerń", ThreadCatalog.NameFor(new Rgb(5, 5, 5)));
        Assert.Equal("Czerwony", ThreadCatalog.NameFor(new Rgb(215, 40, 45)));
    }
}
