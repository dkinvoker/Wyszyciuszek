namespace Wyszyciuszek.Core.Pattern;

/// <summary>Ustawienia wydruku wzoru.</summary>
public sealed class PrintSettings
{
    /// <summary>Szerokość wzoru na tkaninie (mm). Wysokość wynika z proporcji obrazu.</summary>
    public double WidthMm { get; set; } = 150;
    public string Paper { get; set; } = "A4";
    /// <summary>Tamborek (identyfikator z <see cref="Output.Hoops"/>) — pusty = bez tamborka.</summary>
    public string Hoop { get; set; } = "";
    /// <summary>Lustrzane odbicie (pisaki/ołówki termotransferowe — po przeprasowaniu wzór wraca do normy).</summary>
    public bool Mirror { get; set; }
    /// <summary>Numery nici w obszarach.</summary>
    public bool Numbers { get; set; } = true;
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

/// <summary>Projekt: oryginalny obraz, mapa kolorów (po upraszczaniu i ręcznych poprawkach), paleta i ustawienia.</summary>
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
    public SimplifyOptions Options { get; set; } = new();
    public PrintSettings Print { get; set; } = new();

    public bool HasPattern => Width > 0 && Height > 0 && Labels.Length == Width * Height && Palette.Count > 0;

    /// <summary>Rozmiar jednego piksela obrazu roboczego na tkaninie (mm).</summary>
    public double MmPerPixel => Width > 0 ? Print.WidthMm / Width : 0;

    public double HeightMm => Print.HeightMm(Width, Height);
}
