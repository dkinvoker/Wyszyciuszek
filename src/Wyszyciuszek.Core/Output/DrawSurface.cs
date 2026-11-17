using System.Numerics;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Output;

public enum TextAnchor { Start, Middle, End }

/// <summary>
/// Minimalne „płótno”, na którym rysuje <see cref="PatternComposer"/>. Te same polecenia trafiają do PDF, do podglądu
/// (Win2D) i do drukarki — wydruk wygląda dokładnie jak podgląd. Oś Y w dół.
/// </summary>
public interface IDrawSurface
{
    /// <summary>Dokłada przekształcenie (do zdjęcia przez <see cref="Pop"/>).</summary>
    void PushTransform(Matrix3x2 transform);
    /// <summary>Ogranicza rysowanie do prostokąta (do zdjęcia przez <see cref="Pop"/>).</summary>
    void PushClip(RectF rect);
    void Pop();
    /// <summary>Wypełnia ścieżkę (zasada parzystości — dziury są osobnymi figurami).</summary>
    void Fill(PathData path, Rgb color);
    void Stroke(PathData path, Rgb color, float width, float[]? dash = null);
    /// <summary>Tekst (Helvetica/Arial) — <paramref name="baseline"/> to punkt na linii bazowej.</summary>
    void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false);
}

/// <summary>Kształty pomocnicze.</summary>
public static class Shapes
{
    public static PathData Rect(float x, float y, float w, float h)
    {
        var p = new PathData();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + w, y));
        p.LineTo(new Vector2(x + w, y + h));
        p.LineTo(new Vector2(x, y + h));
        p.Close();
        return p;
    }

    public static PathData Rect(RectD r) => Rect((float)r.X, (float)r.Y, (float)r.W, (float)r.H);

    public static PathData Line(Vector2 a, Vector2 b)
    {
        var p = new PathData();
        p.MoveTo(a);
        p.LineTo(b);
        return p;
    }

    public static PathData Ellipse(Vector2 c, float rx, float ry)
    {
        const float K = 0.5522848f;
        var p = new PathData();
        p.MoveTo(new Vector2(c.X + rx, c.Y));
        p.CubicTo(new Vector2(c.X + rx, c.Y + ry * K), new Vector2(c.X + rx * K, c.Y + ry), new Vector2(c.X, c.Y + ry));
        p.CubicTo(new Vector2(c.X - rx * K, c.Y + ry), new Vector2(c.X - rx, c.Y + ry * K), new Vector2(c.X - rx, c.Y));
        p.CubicTo(new Vector2(c.X - rx, c.Y - ry * K), new Vector2(c.X - rx * K, c.Y - ry), new Vector2(c.X, c.Y - ry));
        p.CubicTo(new Vector2(c.X + rx * K, c.Y - ry), new Vector2(c.X + rx, c.Y - ry * K), new Vector2(c.X + rx, c.Y));
        p.Close();
        return p;
    }

    public static PathData Circle(Vector2 c, float r) => Ellipse(c, r, r);

    /// <summary>Krzyżyk z kółkiem — znacznik do nakładania sąsiednich kartek.</summary>
    public static PathData Crosshair(Vector2 c, float size)
    {
        var p = new PathData();
        p.MoveTo(new Vector2(c.X - size, c.Y));
        p.LineTo(new Vector2(c.X + size, c.Y));
        p.MoveTo(new Vector2(c.X, c.Y - size));
        p.LineTo(new Vector2(c.X, c.Y + size));
        p.Append(Circle(c, size * 0.55f));
        return p;
    }
}

/// <summary>Szerokości znaków Helvetica (= Arial), żeby PDF i podgląd mierzyły tekst tak samo.</summary>
public static class FontMetrics
{
    // znaki 32–126, w 1/1000 wysokości czcionki (dane z plików AFM Helvetica i Helvetica-Bold)
    private static readonly short[] Regular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly short[] Bold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    public static float CharWidth(char c, bool bold)
    {
        if (c >= 32 && c <= 126) return (bold ? Bold : Regular)[c - 32];
        return c switch
        {
            'ą' or 'ę' => 556,
            'ć' or 'ś' => bold ? 556 : 500,
            'ł' => bold ? 278 : 222,
            'ń' or 'ó' => bold ? 611 : 556,
            'ź' or 'ż' => 500,
            'Ą' => bold ? 722 : 667,
            'Ć' or 'Ń' or 'Ó' or 'Ø' => c == 'Ó' || c == 'Ø' ? 778 : 722,
            'Ę' or 'Ś' => 667,
            'Ł' => bold ? 611 : 556,
            'Ź' or 'Ż' => 611,
            '×' => 584,
            '·' => 278,
            '°' => 400,
            '–' => 556,
            '—' or '…' => 1000,
            '„' or '”' or '“' => bold ? 500 : 333,
            '’' => bold ? 278 : 222,
            '•' => 350,
            '²' => 333,
            _ => 556,
        };
    }

    public static float Measure(string text, float size, bool bold = false)
    {
        float w = 0;
        foreach (var c in text) w += CharWidth(c, bold);
        return w * size / 1000f;
    }
}
