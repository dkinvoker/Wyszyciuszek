namespace Wyszyciuszek.Core.Pattern;

/// <summary>Skąd brać kolory wzoru.</summary>
public enum PaletteSource
{
    /// <summary>Dobierane automatycznie z obrazu.</summary>
    Automatic,
    /// <summary>Tylko z nici, które użytkownik ma w pudełku („Moje nici”).</summary>
    MyThreads,
}

/// <summary>Ustawienia upraszczania obrazu (suwaki w panelu „Upraszczanie”).</summary>
public sealed class SimplifyOptions
{
    /// <summary>Najwięcej kolorów (nici) we wzorze: 2–24.</summary>
    public int Colors { get; set; } = 6;

    /// <summary>Szczegółowość 1–10: jak małe plamy jeszcze zostają (1 — tylko duże, 10 — drobne detale).</summary>
    public int Detail { get; set; } = 5;

    /// <summary>Wygładzenie 0–10: usuwa fakturę i szum zdjęcia przed doborem kolorów i wyrównuje krawędzie plam.</summary>
    public int Smoothing { get; set; } = 5;

    /// <summary>Gładkość linii 0–10: jak mocno zaokrąglać kontury (0 — wiernie, 10 — miękkie łuki).</summary>
    public int LineSmoothness { get; set; } = 5;

    /// <summary>Dłuższy bok obrazu roboczego w pikselach (więcej = dokładniej, ale wolniej).</summary>
    public int WorkingSize { get; set; } = 800;

    public PaletteSource Source { get; set; } = PaletteSource.Automatic;

    /// <summary>
    /// Kolor, który zajmuje większość brzegów obrazka, od razu oznaczyć jako tkaninę (bez numeru, nie szyjemy).
    /// Domyślnie wyłączone — włącza się ręcznie (i zostaje zapamiętane dla kolejnych obrazków).
    /// </summary>
    public bool BackgroundAsFabric { get; set; }

    /// <summary>Ziarno losowania (inne = trochę inny dobór kolorów przy tych samych ustawieniach).</summary>
    public int Seed { get; set; } = 1;

    public SimplifyOptions Clone() => (SimplifyOptions)MemberwiseClone();

    public static readonly int[] WorkingSizes = [500, 800, 1200, 1600];

    /// <summary>Najmniejsza plama (w pikselach obrazu roboczego) dla danej szczegółowości.</summary>
    public int MinRegionArea(int width, int height)
    {
        // 1 → 1% obrazu, 10 → 0,003% (skala logarytmiczna)
        double t = (Math.Clamp(Detail, 1, 10) - 1) / 9.0;
        double fraction = Math.Pow(10, -2.0 - t * 2.5);
        return Math.Max(4, (int)Math.Round(width * height * fraction));
    }

    /// <summary>Parametry filtra bilateralnego i liczba przejść filtra większościowego dla danego wygładzenia.</summary>
    public (int Radius, float SigmaColor, int Passes, int ModePasses) SmoothingParameters()
    {
        int s = Math.Clamp(Smoothing, 0, 10);
        if (s == 0) return (0, 0, 0, 0);
        return (1 + s / 2, 6f + s * 1.6f, s >= 7 ? 2 : 1, (s + 2) / 3);
    }

    /// <summary>Przejścia filtra „najczęstszy kolor” o promieniu 2 (prostuje fale na granicach) — od średniego wygładzenia.</summary>
    public int PluralityPasses => Math.Clamp(Smoothing, 0, 10) switch { < 3 => 0, < 7 => 1, _ => 2 };

    /// <summary>Najmniejsza „grubość” plamy (promień w pikselach) — cieńsze paski wtapiają się w sąsiadów.</summary>
    public float MinRegionRadius => Math.Clamp(Detail, 1, 10) switch { >= 8 => 1.5f, >= 4 => 2.1f, _ => 2.6f };
}
