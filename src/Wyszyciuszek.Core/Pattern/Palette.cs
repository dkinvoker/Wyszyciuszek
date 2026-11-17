using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Kolor wzoru = jedna nić. Kolejność na liście to kolejność szycia (i numery na wzorze).</summary>
public sealed class PaletteEntry
{
    public Rgb Color { get; set; }
    /// <summary>Nazwa nici, np. „Czerwień” albo „Madeira 1147” — to, co ma się wydrukować w legendzie.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ten kolor to tkanina — nie szyjemy go (na wzorze bez numeru).</summary>
    public bool IsFabric { get; set; }

    public PaletteEntry Clone() => new() { Color = Color, Name = Name, IsFabric = IsFabric };

    public override string ToString() => $"{Name} {Color}";
}

/// <summary>Nić z katalogu albo z pudełka użytkownika.</summary>
public sealed record ThreadColor(string Name, Rgb Color);

/// <summary>Wbudowany katalog nazwanych kolorów (ogólne nazwy, bez numerów producentów).</summary>
public static class ThreadCatalog
{
    public static IReadOnlyList<ThreadColor> All { get; } =
    [
        new("Biel", Rgb.Parse("#FFFFFF")),
        new("Ecru", Rgb.Parse("#F2EAD8")),
        new("Kość słoniowa", Rgb.Parse("#FFF4D6")),
        new("Szarość jasna", Rgb.Parse("#C8C8C8")),
        new("Szarość", Rgb.Parse("#8C8C8C")),
        new("Grafit", Rgb.Parse("#4A4A4A")),
        new("Czerń", Rgb.Parse("#141414")),
        new("Cytrynowy", Rgb.Parse("#FFF35C")),
        new("Żółty", Rgb.Parse("#FFD21F")),
        new("Słonecznikowy", Rgb.Parse("#F7B500")),
        new("Musztardowy", Rgb.Parse("#D4A017")),
        new("Morelowy", Rgb.Parse("#FFB27A")),
        new("Pomarańczowy", Rgb.Parse("#F57C1F")),
        new("Rdzawy", Rgb.Parse("#B7410E")),
        new("Łososiowy", Rgb.Parse("#FA8F7A")),
        new("Koralowy", Rgb.Parse("#F25C54")),
        new("Czerwony", Rgb.Parse("#D7262E")),
        new("Karminowy", Rgb.Parse("#A3132A")),
        new("Bordowy", Rgb.Parse("#6E1428")),
        new("Pudrowy róż", Rgb.Parse("#F4C6CF")),
        new("Różowy", Rgb.Parse("#F07FA6")),
        new("Malinowy", Rgb.Parse("#D2286E")),
        new("Fuksja", Rgb.Parse("#C2187E")),
        new("Lawendowy", Rgb.Parse("#C3B1E1")),
        new("Wrzosowy", Rgb.Parse("#9E6AA8")),
        new("Fioletowy", Rgb.Parse("#6A2C91")),
        new("Śliwkowy", Rgb.Parse("#4B1F48")),
        new("Błękit jasny", Rgb.Parse("#B9DDF5")),
        new("Błękitny", Rgb.Parse("#5DADE2")),
        new("Lazurowy", Rgb.Parse("#2E86C1")),
        new("Kobaltowy", Rgb.Parse("#1F4FA8")),
        new("Granatowy", Rgb.Parse("#1B2A55")),
        new("Turkusowy", Rgb.Parse("#2BB3B1")),
        new("Morski", Rgb.Parse("#1D7874")),
        new("Miętowy", Rgb.Parse("#A8E6CF")),
        new("Seledynowy", Rgb.Parse("#B5D99C")),
        new("Limonkowy", Rgb.Parse("#9ACD32")),
        new("Trawiasty", Rgb.Parse("#4CAF50")),
        new("Zielony", Rgb.Parse("#228B3B")),
        new("Butelkowy", Rgb.Parse("#0F5132")),
        new("Oliwkowy", Rgb.Parse("#7A7A2E")),
        new("Khaki", Rgb.Parse("#B3A66B")),
        new("Beżowy", Rgb.Parse("#D9C3A0")),
        new("Piaskowy", Rgb.Parse("#C9A86A")),
        new("Karmelowy", Rgb.Parse("#B97A3D")),
        new("Brązowy", Rgb.Parse("#7B4A26")),
        new("Czekoladowy", Rgb.Parse("#4A2C1A")),
        new("Cielisty", Rgb.Parse("#F1C9A5")),
        new("Brzoskwiniowy", Rgb.Parse("#F6B99B")),
        new("Złoty", Rgb.Parse("#C9A227")),
        new("Srebrny", Rgb.Parse("#B8BCC2")),
    ];

    /// <summary>Najbliższy kolor z listy (CIEDE2000).</summary>
    public static ThreadColor Nearest(Rgb color, IReadOnlyList<ThreadColor>? list = null)
    {
        list ??= All;
        if (list.Count == 0) throw new ArgumentException("Pusta lista nici.", nameof(list));
        var lab = color.ToLab();
        ThreadColor best = list[0];
        double bestD = double.MaxValue;
        foreach (var t in list)
        {
            double d = ColorMath.DeltaE2000(lab, t.Color.ToLab());
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    /// <summary>Opisowa nazwa koloru (najbliższa z katalogu).</summary>
    public static string NameFor(Rgb color) => Nearest(color).Name;
}
