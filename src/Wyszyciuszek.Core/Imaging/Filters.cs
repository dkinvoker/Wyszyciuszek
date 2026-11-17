namespace Wyszyciuszek.Core.Imaging;

/// <summary>Filtry wygładzające, które zachowują krawędzie (jednolite plamy koloru zamiast „szumu” zdjęcia).</summary>
public static class Filters
{
    /// <summary>
    /// Filtr bilateralny w L*a*b*: uśrednia piksele z okolicy, ale tylko te o podobnym kolorze — faktury
    /// i szum znikają, a granice między plamami zostają ostre.
    /// </summary>
    /// <param name="radius">Promień okna w pikselach (0 = bez zmian).</param>
    /// <param name="sigmaColor">Jak bardzo różne kolory jeszcze się mieszają (ΔE, typowo 6–25).</param>
    public static LabImage Bilateral(LabImage src, int radius, float sigmaColor, CancellationToken ct = default)
    {
        if (radius <= 0) return src;
        int w = src.Width, h = src.Height;
        var dst = new LabImage(w, h);
        float sigmaSpace = Math.Max(1f, radius / 1.5f);
        int size = 2 * radius + 1;
        var spatial = new float[size * size];
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                float d2 = dx * dx + dy * dy;
                spatial[(dy + radius) * size + dx + radius] = d2 > radius * radius + radius ? 0 : MathF.Exp(-d2 / (2 * sigmaSpace * sigmaSpace));
            }
        float colorFactor = -1f / (2 * sigmaColor * sigmaColor);

        var options = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, h, options, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float l0 = src.L[i], a0 = src.A[i], b0 = src.B[i];
                float sw = 0, sl = 0, sa = 0, sb = 0;
                int yMin = Math.Max(0, y - radius), yMax = Math.Min(h - 1, y + radius);
                int xMin = Math.Max(0, x - radius), xMax = Math.Min(w - 1, x + radius);
                for (int yy = yMin; yy <= yMax; yy++)
                {
                    int krow = (yy - y + radius) * size + radius - x;
                    int row = yy * w;
                    for (int xx = xMin; xx <= xMax; xx++)
                    {
                        float ws = spatial[krow + xx];
                        if (ws == 0) continue;
                        int j = row + xx;
                        float dl = src.L[j] - l0, da = src.A[j] - a0, db = src.B[j] - b0;
                        float wt = ws * MathF.Exp((dl * dl + da * da + db * db) * colorFactor);
                        sw += wt;
                        sl += wt * src.L[j];
                        sa += wt * src.A[j];
                        sb += wt * src.B[j];
                    }
                }
                dst.L[i] = sl / sw;
                dst.A[i] = sa / sw;
                dst.B[i] = sb / sw;
            }
        });
        return dst;
    }
}
