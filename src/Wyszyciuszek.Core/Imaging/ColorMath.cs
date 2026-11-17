namespace Wyszyciuszek.Core.Imaging;

/// <summary>Przeliczenia sRGB ↔ CIE L*a*b* (biel D65) i odległość kolorów CIEDE2000.</summary>
public static class ColorMath
{
    private const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;
    private const double Delta = 6.0 / 29.0;

    private static readonly float[] LinearLut = BuildLinearLut();

    private static float[] BuildLinearLut()
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            lut[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return lut;
    }

    /// <summary>Wartość kanału sRGB (0–255) → światło liniowe (0–1).</summary>
    public static double SrgbToLinear(byte c) => LinearLut[c];

    private static byte LinearToSrgb(double c)
    {
        c = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(Math.Max(c, 0), 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(c * 255), 0, 255);
    }

    private static double F(double t) => t > Delta * Delta * Delta ? Math.Cbrt(t) : t / (3 * Delta * Delta) + 4.0 / 29.0;

    private static double FInv(double t) => t > Delta ? t * t * t : 3 * Delta * Delta * (t - 4.0 / 29.0);

    public static Lab ToLab(Rgb c) => ToLab(c.R, c.G, c.B);

    public static Lab ToLab(byte r8, byte g8, byte b8)
    {
        double r = LinearLut[r8], g = LinearLut[g8], b = LinearLut[b8];
        double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;
        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return new Lab((float)(116 * fy - 16), (float)(500 * (fx - fy)), (float)(200 * (fy - fz)));
    }

    public static Rgb ToRgb(Lab lab)
    {
        double fy = (lab.L + 16) / 116.0;
        double fx = fy + lab.A / 500.0;
        double fz = fy - lab.B / 200.0;
        double x = Xn * FInv(fx), y = Yn * FInv(fy), z = Zn * FInv(fz);
        double r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        return new Rgb(LinearToSrgb(r), LinearToSrgb(g), LinearToSrgb(b));
    }

    /// <summary>
    /// Różnica kolorów CIEDE2000 (ΔE00): ~1 — ledwo widoczna, ~2–3 — widoczna z bliska, &gt;10 — wyraźnie inne kolory.
    /// Używana przy dopasowaniu nici, gdzie liczy się to, jak różnicę widzi człowiek.
    /// </summary>
    public static double DeltaE2000(Lab c1, Lab c2)
    {
        const double Pow25To7 = 6103515625.0; // 25^7
        double l1 = c1.L, a1 = c1.A, b1 = c1.B, l2 = c2.L, a2 = c2.A, b2 = c2.B;

        double cab1 = Math.Sqrt(a1 * a1 + b1 * b1), cab2 = Math.Sqrt(a2 * a2 + b2 * b2);
        double cBar = (cab1 + cab2) / 2;
        double cBar7 = Math.Pow(cBar, 7);
        double g = 0.5 * (1 - Math.Sqrt(cBar7 / (cBar7 + Pow25To7)));
        double ap1 = (1 + g) * a1, ap2 = (1 + g) * a2;
        double cp1 = Math.Sqrt(ap1 * ap1 + b1 * b1), cp2 = Math.Sqrt(ap2 * ap2 + b2 * b2);
        double hp1 = Hue(b1, ap1), hp2 = Hue(b2, ap2);

        double dL = l2 - l1;
        double dC = cp2 - cp1;
        double dhp;
        if (cp1 * cp2 == 0) dhp = 0;
        else
        {
            dhp = hp2 - hp1;
            if (dhp > 180) dhp -= 360;
            else if (dhp < -180) dhp += 360;
        }
        double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(Rad(dhp / 2));

        double lBar = (l1 + l2) / 2;
        double cpBar = (cp1 + cp2) / 2;
        double hBar;
        if (cp1 * cp2 == 0) hBar = hp1 + hp2;
        else if (Math.Abs(hp1 - hp2) <= 180) hBar = (hp1 + hp2) / 2;
        else if (hp1 + hp2 < 360) hBar = (hp1 + hp2 + 360) / 2;
        else hBar = (hp1 + hp2 - 360) / 2;

        double t = 1 - 0.17 * Math.Cos(Rad(hBar - 30)) + 0.24 * Math.Cos(Rad(2 * hBar))
                   + 0.32 * Math.Cos(Rad(3 * hBar + 6)) - 0.20 * Math.Cos(Rad(4 * hBar - 63));
        double dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        double cpBar7 = Math.Pow(cpBar, 7);
        double rc = 2 * Math.Sqrt(cpBar7 / (cpBar7 + Pow25To7));
        double lb50 = (lBar - 50) * (lBar - 50);
        double sl = 1 + 0.015 * lb50 / Math.Sqrt(20 + lb50);
        double sc = 1 + 0.045 * cpBar;
        double sh = 1 + 0.015 * cpBar * t;
        double rt = -Math.Sin(Rad(2 * dTheta)) * rc;

        double kl = dL / sl, kc = dC / sc, kh = dH / sh;
        return Math.Sqrt(kl * kl + kc * kc + kh * kh + rt * kc * kh);
    }

    public static double DeltaE2000(Rgb a, Rgb b) => DeltaE2000(a.ToLab(), b.ToLab());

    private static double Hue(double b, double a)
    {
        if (a == 0 && b == 0) return 0;
        double h = Math.Atan2(b, a) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
}
