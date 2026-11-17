namespace Wyszyciuszek.Core.Imaging;

/// <summary>Obraz RGB (3 bajty na piksel, wiersz po wierszu). Przezroczyste piksele są już nałożone na tło.</summary>
public sealed class ImageRgb
{
    public ImageRgb(int width, int height, byte[] rgb)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Obraz musi mieć dodatni rozmiar.");
        if (rgb.Length != width * height * 3) throw new ArgumentException("Długość danych nie pasuje do rozmiaru obrazu.", nameof(rgb));
        Width = width;
        Height = height;
        Data = rgb;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public Rgb this[int x, int y]
    {
        get
        {
            int i = (y * Width + x) * 3;
            return new Rgb(Data[i], Data[i + 1], Data[i + 2]);
        }
        set
        {
            int i = (y * Width + x) * 3;
            Data[i] = value.R;
            Data[i + 1] = value.G;
            Data[i + 2] = value.B;
        }
    }

    public static ImageRgb Filled(int width, int height, Rgb color)
    {
        var img = new ImageRgb(width, height, new byte[width * height * 3]);
        for (int i = 0; i < width * height; i++)
        {
            img.Data[i * 3] = color.R;
            img.Data[i * 3 + 1] = color.G;
            img.Data[i * 3 + 2] = color.B;
        }
        return img;
    }

    /// <summary>
    /// Z pikseli BGRA z premultiplikowaną alfą (tak oddaje je Win2D) — przezroczystość nakładana na <paramref name="background"/>.
    /// </summary>
    public static ImageRgb FromBgraPremultiplied(int width, int height, ReadOnlySpan<byte> bgra, Rgb background)
    {
        if (bgra.Length < width * height * 4) throw new ArgumentException("Za mało danych pikseli.", nameof(bgra));
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < width * height; i++, j += 4)
        {
            int a = bgra[j + 3];
            int inv = 255 - a;
            rgb[i * 3] = (byte)Math.Min(255, bgra[j + 2] + (background.R * inv + 127) / 255);
            rgb[i * 3 + 1] = (byte)Math.Min(255, bgra[j + 1] + (background.G * inv + 127) / 255);
            rgb[i * 3 + 2] = (byte)Math.Min(255, bgra[j] + (background.B * inv + 127) / 255);
        }
        return new ImageRgb(width, height, rgb);
    }

    /// <summary>Piksele BGRA (alfa 255) — np. do pokazania obrazu w Win2D.</summary>
    public byte[] ToBgra()
    {
        var bgra = new byte[Width * Height * 4];
        for (int i = 0; i < Width * Height; i++)
        {
            bgra[i * 4] = Data[i * 3 + 2];
            bgra[i * 4 + 1] = Data[i * 3 + 1];
            bgra[i * 4 + 2] = Data[i * 3];
            bgra[i * 4 + 3] = 255;
        }
        return bgra;
    }

    /// <summary>Rozmiar po zmniejszeniu tak, by dłuższy bok miał najwyżej <paramref name="maxSide"/> pikseli.</summary>
    public static (int Width, int Height) FitSize(int width, int height, int maxSide)
    {
        if (width <= maxSide && height <= maxSide) return (width, height);
        double s = maxSide / (double)Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * s)), Math.Max(1, (int)Math.Round(height * s)));
    }

    /// <summary>
    /// Zmniejsza obraz (uśrednianie pól — bez schodków i migotania), tak by dłuższy bok miał najwyżej
    /// <paramref name="maxSide"/> pikseli. Mniejszego obrazu nie powiększa (zwraca kopię).
    /// </summary>
    public ImageRgb Downscale(int maxSide)
    {
        var (tw, th) = FitSize(Width, Height, maxSide);
        if (tw == Width && th == Height) return new ImageRgb(Width, Height, (byte[])Data.Clone());

        var result = new byte[tw * th * 3];
        Parallel.For(0, th, ty =>
        {
            int y0 = (int)((long)ty * Height / th), y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * Height / th));
            for (int tx = 0; tx < tw; tx++)
            {
                int x0 = (int)((long)tx * Width / tw), x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * Width / tw));
                long r = 0, g = 0, b = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * Width;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (row + x) * 3;
                        r += Data[i];
                        g += Data[i + 1];
                        b += Data[i + 2];
                    }
                }
                long n = (long)(y1 - y0) * (x1 - x0);
                int o = (ty * tw + tx) * 3;
                result[o] = (byte)((r + n / 2) / n);
                result[o + 1] = (byte)((g + n / 2) / n);
                result[o + 2] = (byte)((b + n / 2) / n);
            }
        });
        return new ImageRgb(tw, th, result);
    }
}

/// <summary>Obraz w L*a*b* (trzy płaszczyzny float) — na nim działa wygładzanie i dobór kolorów.</summary>
public sealed class LabImage
{
    public LabImage(int width, int height)
    {
        Width = width;
        Height = height;
        L = new float[width * height];
        A = new float[width * height];
        B = new float[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public float[] L { get; }
    public float[] A { get; }
    public float[] B { get; }

    public Lab this[int i] => new(L[i], A[i], B[i]);

    public static LabImage From(ImageRgb img)
    {
        var lab = new LabImage(img.Width, img.Height);
        // ten sam kolor powtarza się w obrazie bardzo często — pamięć podręczna oszczędza potęgowania
        Parallel.For(0, img.Height, y =>
        {
            var cache = new Dictionary<int, Lab>();
            for (int x = 0; x < img.Width; x++)
            {
                int i = y * img.Width + x;
                int key = img.Data[i * 3] << 16 | img.Data[i * 3 + 1] << 8 | img.Data[i * 3 + 2];
                if (!cache.TryGetValue(key, out var c))
                {
                    c = ColorMath.ToLab(img.Data[i * 3], img.Data[i * 3 + 1], img.Data[i * 3 + 2]);
                    if (cache.Count < 4096) cache[key] = c;
                }
                lab.L[i] = c.L;
                lab.A[i] = c.A;
                lab.B[i] = c.B;
            }
        });
        return lab;
    }
}
