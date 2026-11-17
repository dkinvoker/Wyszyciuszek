using System.Globalization;

namespace Wyszyciuszek.Core.Imaging;

/// <summary>Kolor sRGB 8 bit na kanał (bez przezroczystości — haft jej nie zna).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb White => new(255, 255, 255);
    public static Rgb Black => new(0, 0, 0);

    /// <summary>Zapis „#RRGGBB”.</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Hex;

    /// <summary>Wczytuje „#RRGGBB”, „RRGGBB” albo skrót „#RGB”.</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length == 8) s = s[2..]; // #AARRGGBB — przezroczystość pomijamy
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Niepoprawny kolor: „{text}”.");

    /// <summary>Względna luminancja (0 = czarny, 1 = biały) wg sRGB.</summary>
    public double Luminance => 0.2126 * ColorMath.SrgbToLinear(R) + 0.7152 * ColorMath.SrgbToLinear(G) + 0.0722 * ColorMath.SrgbToLinear(B);

    /// <summary>Czy na tym kolorze lepiej pisać białym tekstem.</summary>
    public bool PrefersLightText => Luminance < 0.36;

    /// <summary>Mieszanka z innym kolorem: t = 0 → ten, t = 1 → drugi.</summary>
    public Rgb Lerp(Rgb other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(R + (other.R - R) * t),
            (byte)Math.Round(G + (other.G - G) * t),
            (byte)Math.Round(B + (other.B - B) * t));
    }

    public Lab ToLab() => ColorMath.ToLab(this);
}

/// <summary>Kolor w przestrzeni CIE L*a*b* (D65) — odległości odpowiadają temu, jak różnicę widzi oko.</summary>
public readonly record struct Lab(float L, float A, float B)
{
    public float DistanceSquared(Lab o)
    {
        float dl = L - o.L, da = A - o.A, db = B - o.B;
        return dl * dl + da * da + db * db;
    }

    public Rgb ToRgb() => ColorMath.ToRgb(this);
}
