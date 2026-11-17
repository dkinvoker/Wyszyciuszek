using System.Numerics;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Output;

/// <summary>Zestaw stron do druku albo PDF (wzór z legendą, strona kalibracji…).</summary>
public interface IPageSource
{
    int PageCount { get; }
    /// <summary>Format papieru (A4, A3…) — do ustawienia drukarki.</summary>
    string PaperId { get; }
    /// <summary>Rozmiar strony w mm (szerszy niż wysoki = poziomo).</summary>
    (double Width, double Height) PageSize(int index);
    void DrawPage(IDrawSurface s, int index);
}

/// <summary>
/// Strona testowa do kalibracji drukarki: dwie miarki po 100 mm (pozioma i pionowa) i kwadrat 5 cm. Zmierzone długości
/// dają korektę skali, o którą drukarka „gubi” albo „dokłada” rozmiar mimo druku w 100%.
/// </summary>
public sealed class CalibrationPage : IPageSource
{
    public const double RulerMm = 100;
    private const float RulerX = 40, HorizontalY = 92, VerticalTop = 120;

    private static readonly Rgb Gray = new(110, 110, 110);

    public int PageCount => 1;
    public string PaperId => "A4";
    public (double Width, double Height) PageSize(int index) => (210, 297);

    /// <summary>Korekta skali z pomiaru: wydrukowano <paramref name="measuredMm"/> zamiast 100 mm → mnożnik.</summary>
    public static double ScaleFromMeasurement(double measuredMm) => measuredMm > 0 ? RulerMm / measuredMm : 1;

    public void DrawPage(IDrawSurface s, int index)
    {
        var ink = PatternComposer.Ink;
        s.Text("Kalibracja drukarki", new Vector2(20, 28), 7, ink, bold: true);
        string[] lines =
        [
            "1. Wydrukuj tę stronę w skali 100% (wyłącz „dopasuj do strony”) na papierze, na którym drukujesz wzory.",
            "2. Zmierz linijką obie miarki — każda powinna mieć dokładnie 100 mm (od kreski 0 do kreski 10).",
            "3. Wpisz zmierzone długości w oknie „Kalibracja drukarki” w Wyszyciuszku.",
            "Kolejne wydruki (i, jeśli zechcesz, PDF-y) będą powiększane albo zmniejszane tak, żeby wzór miał dokładny rozmiar.",
        ];
        float y = 40;
        foreach (var line in lines)
            foreach (var part in PatternComposer.Wrap(line, 3.4f, 170))
            {
                s.Text(part, new Vector2(20, y), 3.4f, Gray);
                y += 5;
            }

        // miarka pozioma
        Ruler(s, new Vector2(RulerX, HorizontalY), Vector2.UnitX, -Vector2.UnitY);
        s.Text("miarka pozioma — 100 mm", new Vector2(RulerX + 50, HorizontalY + 9), 3.2f, Gray, TextAnchor.Middle);

        // miarka pionowa
        Ruler(s, new Vector2(RulerX, VerticalTop), Vector2.UnitY, Vector2.UnitX);
        s.Text("miarka pionowa — 100 mm", new Vector2(RulerX - 6, VerticalTop + 110), 3.2f, Gray);

        // kwadrat 5 cm — szybkie sprawdzenie „na oko” po kalibracji
        float sx = 100, sy = 140;
        s.Stroke(Shapes.Rect(sx, sy, 50, 50), ink, 0.3f);
        s.Text("kwadrat 5 × 5 cm", new Vector2(sx + 25, sy + 27), 3.4f, Gray, TextAnchor.Middle);
        s.Text("Po kalibracji ten kwadrat i kwadraty 1 cm na wzorach mają dokładny rozmiar.", new Vector2(sx - 5, sy + 58), 3, Gray);
    }

    /// <summary>Miarka 100 mm: kreski co 1 mm, dłuższe co 5 i 10 mm, z opisem centymetrów po stronie <paramref name="side"/>.</summary>
    private static void Ruler(IDrawSurface s, Vector2 start, Vector2 along, Vector2 side)
    {
        var ink = PatternComposer.Ink;
        var path = new Vector.PathData();
        path.MoveTo(start);
        path.LineTo(start + along * (float)RulerMm);
        for (int mm = 0; mm <= RulerMm; mm++)
        {
            float len = mm % 10 == 0 ? 6 : mm % 5 == 0 ? 4 : 2.2f;
            var p = start + along * mm;
            path.MoveTo(p);
            path.LineTo(p + side * len);
            if (mm % 10 == 0)
            {
                var label = side * 9 + (side.Y < 0 ? Vector2.Zero : new Vector2(0, 1.2f));
                s.Text((mm / 10).ToString(System.Globalization.CultureInfo.InvariantCulture), p + label, 3, ink,
                    side.Y < 0 ? TextAnchor.Middle : TextAnchor.Start);
            }
        }
        s.Stroke(path, ink, 0.2f);
    }
}
