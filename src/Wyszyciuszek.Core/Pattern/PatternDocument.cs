using System.Numerics;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Kreska konturów na wzorze do odrysowania.</summary>
public enum OutlineStyle
{
    /// <summary>Ciągła — najwyraźniejsza.</summary>
    Solid,
    /// <summary>Przerywana — pod pisak znikający i kalkę (łatwo prowadzić, mniej tuszu).</summary>
    Dashed,
    /// <summary>Kropki — najmniej widoczne po przeniesieniu, np. na stabilizatorze rozpuszczalnym.</summary>
    Dotted,
}

/// <summary>Jak gęsto wypełnia się obszary (do szacunku nici i czasu).</summary>
public enum FillDensity
{
    /// <summary>Gęsto, „malowanie nicią” — rzędy co ok. 0,5 mm, tkaniny nie widać.</summary>
    Dense,
    /// <summary>Średnio — rzędy co ok. 1 mm, tkanina lekko prześwituje.</summary>
    Medium,
    /// <summary>Szkicowo — luźne rzędy co ok. 2,5 mm, jak kreskowanie ołówkiem.</summary>
    Sketch,
}

/// <summary>Ustawienia wydruku wzoru.</summary>
public sealed class PrintSettings
{
    /// <summary>Gęstość wypełnienia — do szacunku nici i czasu szycia.</summary>
    public FillDensity Fill { get; set; } = FillDensity.Dense;
    /// <summary>Kreska konturów.</summary>
    public OutlineStyle Outline { get; set; } = OutlineStyle.Solid;
    /// <summary>Szare linie i numery zamiast czarnych (słabiej przebijają przez tkaninę i nici).</summary>
    public bool GrayLines { get; set; }
    /// <summary>Szerokość wzoru na tkaninie (mm). Wysokość wynika z proporcji obrazu.</summary>
    public double WidthMm { get; set; } = 150;
    public string Paper { get; set; } = "A4";
    /// <summary>Tamborek (identyfikator z <see cref="Output.Hoops"/>) — pusty = bez tamborka.</summary>
    public string Hoop { get; set; } = "";
    /// <summary>Lustrzane odbicie (pisaki/ołówki termotransferowe — po przeprasowaniu wzór wraca do normy).</summary>
    public bool Mirror { get; set; }
    /// <summary>Numery nici w obszarach.</summary>
    public bool Numbers { get; set; } = true;
    /// <summary>Strzałki kierunku ściegu w obszarach.</summary>
    public bool StitchArrows { get; set; } = true;
    /// <summary>Lekkie wypełnienie kolorem nici (0 = białe tło, 1 = pełny kolor).</summary>
    public double Tint { get; set; } = 0.25;
    public double LineWidthMm { get; set; } = 0.4;
    /// <summary>Strona z legendą (kolory, nazwy, kolejność szycia).</summary>
    public bool Legend { get; set; } = true;
    /// <summary>Znaczniki środka na krawędziach wzoru (do wyśrodkowania w tamborku).</summary>
    public bool CenterMarks { get; set; } = true;
    /// <summary>Ramka na krawędzi obrazu.</summary>
    public bool Frame { get; set; }

    public double HeightMm(int width, int height) => width <= 0 ? 0 : WidthMm * height / width;

    public PrintSettings Clone() => (PrintSettings)MemberwiseClone();

    public const double MinWidthMm = 20, MaxWidthMm = 2000;
}

/// <summary>
/// Projekt: oryginalny obraz, mapa kolorów (po upraszczaniu i ręcznych poprawkach), paleta, linie detali,
/// kierunki ściegu i ustawienia.
/// </summary>
public sealed class PatternDocument
{
    public string Title { get; set; } = "Nowy wzór";
    /// <summary>Oryginalny plik obrazu (PNG/JPG…), z którego powstał wzór.</summary>
    public byte[]? SourceImage { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Indeks palety dla każdego piksela obrazu roboczego.</summary>
    public byte[] Labels { get; set; } = [];
    public List<PaletteEntry> Palette { get; set; } = [];
    /// <summary>Linie detali (przeszycie samą linią).</summary>
    public List<DetailLine> Lines { get; set; } = [];
    /// <summary>Ręcznie ustawione kierunki ściegu.</summary>
    public List<StitchHint> StitchHints { get; set; } = [];
    /// <summary>
    /// Kolor materiału, na którym się szyje (null — nieustalony). W podglądzie kolory „tkanina” mają ten kolor,
    /// a nici słabo na nim widoczne dostają ostrzeżenie.
    /// </summary>
    public Rgb? FabricColor { get; set; }

    /// <summary>Kolor, jakim widać nić albo tkaninę: tkanina z ustalonym kolorem materiału — ten kolor.</summary>
    public Rgb DisplayColor(int index) =>
        index < 0 || index >= Palette.Count ? Rgb.Black
        : Palette[index].IsFabric && FabricColor is { } f ? f : Palette[index].Color;
    public SimplifyOptions Options { get; set; } = new();
    public PrintSettings Print { get; set; } = new();

    public bool HasPattern => Width > 0 && Height > 0 && Labels.Length == Width * Height && Palette.Count > 0;

    /// <summary>
    /// Niezależna kopia do pracy w tle (eksport, druk, autozapis) — edycja w oknie w tym czasie jej nie zmieni.
    /// </summary>
    public PatternDocument Snapshot() => new()
    {
        Title = Title, SourceImage = SourceImage, Width = Width, Height = Height, Labels = (byte[])Labels.Clone(),
        Palette = Palette.Select(p => p.Clone()).ToList(), Lines = Lines.Select(l => l.Clone()).ToList(),
        StitchHints = StitchHints.Select(h => h.Clone()).ToList(), FabricColor = FabricColor,
        Options = Options.Clone(), Print = Print.Clone(),
    };

    /// <summary>Rozmiar jednego piksela obrazu roboczego na tkaninie (mm).</summary>
    public double MmPerPixel => Width > 0 ? Print.WidthMm / Width : 0;

    public double HeightMm => Print.HeightMm(Width, Height);

    /// <summary>
    /// Numery nici na wzorze: kolejne liczby dla nici do szycia, 0 dla tkaniny (tkanina nie zabiera numeru —
    /// pierwsza szyta nić to zawsze „1”).
    /// </summary>
    public int[] ThreadNumbers()
    {
        var n = new int[Palette.Count];
        int k = 0;
        for (int i = 0; i < n.Length; i++) n[i] = Palette[i].IsFabric ? 0 : ++k;
        return n;
    }

    /// <summary>Numer nici jako tekst („3”) albo „T” dla tkaniny.</summary>
    public string ThreadLabel(int index)
    {
        if (index < 0 || index >= Palette.Count) return "";
        int number = ThreadNumbers()[index];
        return number == 0 ? "T" : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Najbliższa linia detalu w odległości <paramref name="tolerance"/> (piksele) albo −1.</summary>
    public int HitLine(Vector2 point, float tolerance)
    {
        int best = -1;
        float bestD = tolerance;
        for (int i = 0; i < Lines.Count; i++)
        {
            float d = Lines[i].DistanceTo(point);
            if (d <= bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }
}
