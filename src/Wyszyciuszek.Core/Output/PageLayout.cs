namespace Wyszyciuszek.Core.Output;

/// <summary>Prostokąt w milimetrach.</summary>
public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>Jedna strona z kawałkiem wzoru.</summary>
/// <param name="Area">Fragment wzoru (mm, od lewego górnego rogu wzoru) drukowany na tej stronie.</param>
/// <param name="PageX">Gdzie na stronie (mm) wypada lewy górny róg tego fragmentu.</param>
public sealed record PageTile(int Index, string Label, int Column, int Row, RectD Area, double PageX, double PageY);

/// <summary>
/// Rozkład wzoru na kartki w skali 1:1. Duży wzór dzielony jest na kafelki z zakładką (te same znaczniki na sąsiednich
/// kartkach — po nałożeniu i sklejeniu wychodzi cały wzór). Orientacja kartki dobierana tak, żeby kartek było jak najmniej.
/// </summary>
public sealed class PageLayout
{
    public const double Margin = 10;
    public const double Header = 10;
    public const double Overlap = 15;

    public required PaperSize Paper { get; init; }
    public required bool Landscape { get; init; }
    public required double PageWidth { get; init; }
    public required double PageHeight { get; init; }
    public required double DesignWidth { get; init; }
    public required double DesignHeight { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required IReadOnlyList<PageTile> Tiles { get; init; }
    public required bool LegendPage { get; init; }

    public int PageCount => Tiles.Count + (LegendPage ? 1 : 0);

    public bool IsLegendPage(int index) => LegendPage && index == Tiles.Count;

    /// <summary>Rozmiar strony o danym numerze (legenda zawsze pionowo).</summary>
    public (double Width, double Height) PageSize(int index) =>
        IsLegendPage(index) ? (Paper.Width, Paper.Height) : (PageWidth, PageHeight);

    public PageTile? TileAt(int column, int row) => Tiles.FirstOrDefault(t => t.Column == column && t.Row == row);

    public static PageLayout Compute(double designWidth, double designHeight, PaperSize paper, bool legend)
    {
        if (designWidth <= 0 || designHeight <= 0) throw new ArgumentOutOfRangeException(nameof(designWidth));
        var portrait = Grid(designWidth, designHeight, paper.Width, paper.Height);
        var landscape = Grid(designWidth, designHeight, paper.Height, paper.Width);
        bool useLandscape = landscape.Cols * landscape.Rows < portrait.Cols * portrait.Rows;
        var (cols, rows) = useLandscape ? landscape : portrait;
        double pw = useLandscape ? paper.Height : paper.Width, ph = useLandscape ? paper.Width : paper.Height;
        double aw = pw - 2 * Margin, ah = ph - 2 * Margin - Header;

        var tiles = new List<PageTile>();
        // równe kafelki z zakładką dokładnie Overlap — ostatni kończy się na krawędzi wzoru
        double tileW = cols > 1 ? (designWidth + (cols - 1) * Overlap) / cols : designWidth;
        double tileH = rows > 1 ? (designHeight + (rows - 1) * Overlap) / rows : designHeight;
        double stepX = tileW - Overlap, stepY = tileH - Overlap;
        bool single = cols == 1 && rows == 1;
        // kafelek na środku pola zadruku (wszystkie kartki wyglądają tak samo)
        double px = (pw - tileW) / 2;
        double py = Margin + Header + (ah - tileH) / 2;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var area = new RectD(c * stepX, r * stepY, tileW, tileH);
                tiles.Add(new PageTile(tiles.Count, single ? "" : $"{ColumnName(c)}{r + 1}", c, r, area, px, py));
            }

        return new PageLayout
        {
            Paper = paper, Landscape = useLandscape, PageWidth = pw, PageHeight = ph,
            DesignWidth = designWidth, DesignHeight = designHeight, Columns = cols, Rows = rows, Tiles = tiles, LegendPage = legend,
        };
    }

    private static (int Cols, int Rows) Grid(double dw, double dh, double pw, double ph)
    {
        double aw = pw - 2 * Margin, ah = ph - 2 * Margin - Header;
        int Count(double size, double avail) => size <= avail + 1e-6 ? 1 : (int)Math.Ceiling((size - Overlap) / (avail - Overlap) - 1e-9);
        return (Count(dw, aw), Count(dh, ah));
    }

    /// <summary>A, B, …, Z, AA, AB…</summary>
    public static string ColumnName(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            index--;
            s = (char)('A' + index % 26) + s;
            index /= 26;
        }
        return s;
    }
}
