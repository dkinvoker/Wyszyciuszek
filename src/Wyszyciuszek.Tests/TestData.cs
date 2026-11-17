namespace Wyszyciuszek.Tests;

/// <summary>Sztuczne obrazy i mapy kolorów do testów.</summary>
internal static class TestData
{
    public static readonly Rgb Red = new(220, 30, 40);
    public static readonly Rgb Green = new(30, 160, 60);
    public static readonly Rgb Blue = new(30, 60, 200);
    public static readonly Rgb Cream = new(245, 238, 220);

    /// <summary>Kremowe tło, czerwone koło na środku, niebieski pas na dole, zielony kwadrat w rogu.</summary>
    public static ImageRgb Shapes(int w = 160, int h = 120, int noise = 0, int seed = 7)
    {
        var img = ImageRgb.Filled(w, h, Cream);
        var rng = new Random(seed);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Rgb c = Cream;
                float dx = x - w * 0.5f, dy = y - h * 0.45f;
                if (dx * dx + dy * dy < (h * 0.25f) * (h * 0.25f)) c = Red;
                if (y > h * 0.8f) c = Blue;
                if (x < w * 0.2f && y < h * 0.25f) c = Green;
                if (noise > 0)
                {
                    int n() => rng.Next(-noise, noise + 1);
                    c = new Rgb((byte)Math.Clamp(c.R + n(), 0, 255), (byte)Math.Clamp(c.G + n(), 0, 255), (byte)Math.Clamp(c.B + n(), 0, 255));
                }
                img[x, y] = c;
            }
        return img;
    }

    /// <summary>Mapa kolorów z rysunku: każdy znak to kolor ('0'…'9', 'a'…).</summary>
    public static (byte[] Labels, int W, int H) Map(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var labels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                labels[y * w + x] = (byte)(char.IsDigit(c) ? c - '0' : c - 'a' + 10);
            }
        return (labels, w, h);
    }

    /// <summary>Losowa mapa z kilkoma kolorami (do testów odporności).</summary>
    public static byte[] RandomMap(int w, int h, int colors, int seed)
    {
        var rng = new Random(seed);
        var labels = new byte[w * h];
        for (int i = 0; i < labels.Length; i++) labels[i] = (byte)rng.Next(colors);
        return labels;
    }

    public static PatternDocument Document(byte[] labels, int w, int h, params Rgb[] colors)
    {
        int count = labels.Max() + 1;
        var palette = Enumerable.Range(0, count).Select(i => new PaletteEntry
        {
            Color = i < colors.Length ? colors[i] : new Rgb((byte)(i * 40), (byte)(255 - i * 30), (byte)(i * 70)),
            Name = $"Kolor {i + 1}",
        }).ToList();
        return new PatternDocument { Title = "Test", Width = w, Height = h, Labels = labels, Palette = palette };
    }
}
