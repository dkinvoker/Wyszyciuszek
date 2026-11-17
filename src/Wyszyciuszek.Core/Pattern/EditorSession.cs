using System.Numerics;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Pattern;

/// <summary>Co się zmieniło w projekcie.</summary>
[Flags]
public enum ChangeKind
{
    None = 0,
    /// <summary>Mapa kolorów (trzeba przeliczyć kontury).</summary>
    Labels = 1,
    /// <summary>Kolor, nazwa albo „tkanina” któregoś wpisu palety.</summary>
    PaletteColors = 2,
    /// <summary>Liczba albo kolejność kolorów palety.</summary>
    PaletteStructure = 4,
    /// <summary>Ustawienia wydruku.</summary>
    Print = 8,
    /// <summary>Linie detali albo kierunki ściegu (konturów nie trzeba przeliczać).</summary>
    Annotations = 16,
    /// <summary>Wszystko naraz (nowy projekt, cofnięcie).</summary>
    All = Labels | PaletteColors | PaletteStructure | Print | Annotations,
}

/// <summary>
/// Edycja projektu z historią (cofnij/ponów). Każda operacja zapisuje migawkę mapy kolorów, palety, linii detali
/// i kierunków ściegu, więc da się wrócić także po ponownym uproszczeniu obrazu. Niezależna od interfejsu — testowana
/// jednostkowo.
/// </summary>
public sealed class EditorSession
{
    private const int MaxHistory = 60;
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);
    private const ChangeKind Everything = ChangeKind.Labels | ChangeKind.PaletteColors | ChangeKind.PaletteStructure | ChangeKind.Annotations;

    private sealed record Snapshot(
        int Width, int Height, byte[] Labels, PaletteEntry[] Palette, DetailLine[] Lines, StitchHint[] Hints, Rgb? Fabric,
        string? Key, DateTime Time);

    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    public EditorSession(PatternDocument document)
    {
        Document = document;
    }

    public PatternDocument Document { get; }

    /// <summary>Ścieżka pliku projektu (null — jeszcze niezapisany).</summary>
    public string? FilePath { get; set; }

    public bool IsDirty { get; private set; }

    public event Action<ChangeKind>? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Zegar do łączenia kolejnych zmian (podmieniany w testach).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public void MarkSaved() => IsDirty = false;

    /// <summary>Projekt ma niezapisane zmiany (np. przywrócony z autozapisu).</summary>
    public void MarkDirty() => IsDirty = true;

    private Snapshot Capture(string? key) => new(
        Document.Width, Document.Height, (byte[])Document.Labels.Clone(),
        Document.Palette.Select(p => p.Clone()).ToArray(),
        Document.Lines.Select(l => l.Clone()).ToArray(),
        Document.StitchHints.Select(h => h.Clone()).ToArray(),
        Document.FabricColor,
        key, Clock());

    /// <summary>Zapamiętuje stan przed zmianą. Zmiany z tym samym kluczem w krótkim odstępie łączą się w jeden krok.</summary>
    private void PushUndo(string? coalesceKey = null)
    {
        var now = Clock();
        if (coalesceKey is not null && _undo.Count > 0 && _undo[^1].Key == coalesceKey && now - _undo[^1].Time < CoalesceWindow)
        {
            _undo[^1] = _undo[^1] with { Time = now };
        }
        else
        {
            _undo.Add(Capture(coalesceKey));
            if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        }
        _redo.Clear();
    }

    private void Restore(Snapshot s)
    {
        Document.Width = s.Width;
        Document.Height = s.Height;
        Document.Labels = s.Labels;
        Document.Palette = s.Palette.Select(p => p.Clone()).ToList();
        Document.Lines = s.Lines.Select(l => l.Clone()).ToList();
        Document.StitchHints = s.Hints.Select(h => h.Clone()).ToList();
        Document.FabricColor = s.Fabric;
    }

    private void Raise(ChangeKind kind)
    {
        IsDirty = true;
        Changed?.Invoke(kind);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Capture(null));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Raise(Everything);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Capture(null));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Raise(Everything);
        return true;
    }

    // ------------------------------------------------------------------ upraszczanie

    /// <summary>Podmienia wzór na nowo uproszczony (do cofnięcia — ręczne poprawki da się odzyskać przez Ctrl+Z).</summary>
    public void ApplySimplified(SimplifyResult result)
    {
        if (Document.HasPattern) PushUndo();

        // linie i kierunki zostają: inna dokładność = inna skala obrazu roboczego, a nici dobieramy najbliższe nowe
        if (Document.Width > 0 && result.Width > 0 && result.Width != Document.Width)
        {
            float k = result.Width / (float)Document.Width;
            foreach (var l in Document.Lines)
                for (int i = 0; i < l.Points.Count; i++) l.Points[i] *= k;
            foreach (var h in Document.StitchHints) h.Point *= k;
        }
        if (Document.Lines.Count > 0 && result.Palette.Count > 0)
        {
            var newLab = result.Palette.Select(p => p.Color.ToLab()).ToArray();
            foreach (var l in Document.Lines)
            {
                var own = l.Color >= 0 && l.Color < Document.Palette.Count ? Document.Palette[l.Color].Color.ToLab() : newLab[0];
                l.Color = Enumerable.Range(0, newLab.Length)
                    .OrderBy(i => result.Palette[i].IsFabric ? 1 : 0)
                    .ThenBy(i => ColorMath.DeltaE2000(own, newLab[i]))
                    .First();
            }
        }

        Document.Width = result.Width;
        Document.Height = result.Height;
        Document.Labels = result.Labels;
        Document.Palette = result.Palette;
        Raise(Everything);
    }

    // ------------------------------------------------------------------ malowanie

    /// <summary>Maluje pociągnięcie pędzla kolorem <paramref name="color"/>. Zwraca false, gdy nic się nie zmieniło.</summary>
    public bool PaintStroke(IReadOnlyList<Vector2> points, float radius, int color)
    {
        if (!ValidColor(color) || points.Count == 0) return false;
        var labels = (byte[])Document.Labels.Clone();
        if (LabelOps.PaintStroke(labels, Document.Width, Document.Height, points, radius, (byte)color) == 0) return false;
        PushUndo();
        Document.Labels = labels;
        Raise(ChangeKind.Labels);
        return true;
    }

    /// <summary>Wiaderko: cały obszar dostaje kolor <paramref name="color"/> (sąsiednie obszary tego koloru się łączą).</summary>
    public bool FillRegion(Components components, int region, int color)
    {
        if (!ValidColor(color) || region < 0 || region >= components.Count || components.Label[region] == color) return false;
        if (components.Map.Length != Document.Labels.Length) return false;
        PushUndo();
        var labels = (byte[])Document.Labels.Clone();
        LabelOps.FillRegion(labels, components, region, (byte)color);
        Document.Labels = labels;
        Raise(ChangeKind.Labels);
        return true;
    }

    /// <summary>
    /// Wtapia w sąsiadów plamy mniejsze niż <paramref name="minArea"/> pikseli i paski cieńsze niż 2·<paramref name="minRadius"/>.
    /// Zwraca liczbę usuniętych plam.
    /// </summary>
    public int RemoveSmallRegions(int minArea, float minRadius = 0)
    {
        var labels = (byte[])Document.Labels.Clone();
        var lab = Document.Palette.Select(p => p.Color.ToLab()).ToList();
        int n = LabelOps.RemoveThinRegions(labels, Document.Width, Document.Height, minRadius, lab)
                + LabelOps.RemoveSmallRegions(labels, Document.Width, Document.Height, minArea, lab);
        if (n == 0) return 0;
        PushUndo();
        Document.Labels = labels;
        Raise(ChangeKind.Labels);
        return n;
    }

    /// <summary>Wyrównuje poszarpane krawędzie (filtr większościowy).</summary>
    public bool SmoothEdges(int passes = 1)
    {
        var labels = LabelOps.ModeFilter(Document.Labels, Document.Width, Document.Height, passes);
        if (labels.AsSpan().SequenceEqual(Document.Labels)) return false;
        PushUndo();
        Document.Labels = labels;
        Raise(ChangeKind.Labels);
        return true;
    }

    // ------------------------------------------------------------------ linie detali

    /// <summary>
    /// Dodaje linię detalu z pociągnięcia myszą (wygładzoną). Zwraca false dla samego kliknięcia albo złego koloru.
    /// </summary>
    public bool AddLine(IReadOnlyList<Vector2> rawPoints, int color)
    {
        if (!ValidColor(color) || rawPoints.Count < 2) return false;
        var points = DetailLine.Smooth(rawPoints);
        var max = new Vector2(Document.Width, Document.Height);
        for (int i = 0; i < points.Count; i++) points[i] = Vector2.Clamp(points[i], Vector2.Zero, max);
        var line = new DetailLine { Points = points, Color = color };
        if (points.Count < 2 || line.Length < 1.5f) return false;
        PushUndo();
        Document.Lines.Add(line);
        Raise(ChangeKind.Annotations);
        return true;
    }

    /// <summary>
    /// Zamienia obszary na linie detali w ich kolorze: środkowa linia każdej plamy zostaje jako linia, a sama plama
    /// wtapia się w sąsiadów. Jeden krok cofania. Zwraca liczbę nowych linii.
    /// </summary>
    public int ConvertRegionsToLines(Components components, IReadOnlyCollection<int> regions)
    {
        if (components.Map.Length != Document.Labels.Length || regions.Count == 0) return 0;
        var labels = (byte[])Document.Labels.Clone();
        var lines = new List<DetailLine>();
        foreach (int r in regions.Distinct())
        {
            if (r < 0 || r >= components.Count || !ValidColor(components.Label[r])) continue;
            // jedna plama może dać kilka linii (np. trzy wąsy zbiegające się w jednym miejscu)
            var found = Vector.Centerline.Lines(components, r)
                .Select(points => new DetailLine { Points = points, Color = components.Label[r] })
                .Where(l => l.Points.Count >= 2 && l.Length >= 1.5f)
                .ToList();
            if (found.Count == 0) continue;
            if (LabelOps.DissolveRegion(labels, components, r) == 0) continue; // cały obraz — nie ma w co wtopić
            lines.AddRange(found);
        }
        if (lines.Count == 0) return 0;
        PushUndo();
        Document.Labels = labels;
        Document.Lines.AddRange(lines);
        Raise(ChangeKind.Labels | ChangeKind.Annotations);
        return lines.Count;
    }

    public bool DeleteLine(int index)
    {
        if (index < 0 || index >= Document.Lines.Count) return false;
        PushUndo();
        Document.Lines.RemoveAt(index);
        Raise(ChangeKind.Annotations);
        return true;
    }

    public bool SetLineColor(int index, int color)
    {
        if (index < 0 || index >= Document.Lines.Count || !ValidColor(color) || Document.Lines[index].Color == color) return false;
        PushUndo();
        Document.Lines[index].Color = color;
        Raise(ChangeKind.Annotations);
        return true;
    }

    // ------------------------------------------------------------------ kierunek ściegu

    /// <summary>
    /// Ustawia kierunek ściegu obszaru pod <paramref name="point"/> (stopnie) albo — dla null — przywraca kierunek
    /// automatyczny. Wcześniejsze ustawienia tego obszaru są zastępowane.
    /// </summary>
    public bool SetStitchDirection(Components components, Vector2 point, float? angle)
    {
        if (components.Map.Length != Document.Labels.Length) return false;
        int RegionAt(Vector2 p)
        {
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
            return x < 0 || y < 0 || x >= components.Width || y >= components.Height ? -1 : components.Map[y * components.Width + x];
        }
        int region = RegionAt(point);
        if (region < 0) return false;
        var same = Document.StitchHints.Where(h => RegionAt(h.Point) == region).ToList();
        if (angle is null && same.Count == 0) return false;
        PushUndo();
        Document.StitchHints.RemoveAll(same.Contains);
        if (angle is { } a) Document.StitchHints.Add(new StitchHint { Point = point, Angle = StitchPlan.Normalize(a) });
        Raise(ChangeKind.Annotations);
        return true;
    }

    // ------------------------------------------------------------------ paleta

    private bool ValidColor(int index) => index >= 0 && index < Document.Palette.Count;

    /// <summary>Zmienia kolor nici (przeciąganie w próbniku łączy się w jeden krok cofania).</summary>
    public void SetColor(int index, Rgb color)
    {
        if (!ValidColor(index) || Document.Palette[index].Color == color) return;
        PushUndo($"color:{index}");
        Document.Palette[index].Color = color;
        Raise(ChangeKind.PaletteColors);
    }

    public void Rename(int index, string name)
    {
        name = name.Trim();
        if (!ValidColor(index) || Document.Palette[index].Name == name) return;
        PushUndo($"name:{index}");
        Document.Palette[index].Name = name;
        Raise(ChangeKind.PaletteColors);
    }

    public void SetFabric(int index, bool fabric)
    {
        if (!ValidColor(index) || Document.Palette[index].IsFabric == fabric) return;
        PushUndo();
        Document.Palette[index].IsFabric = fabric;
        Raise(ChangeKind.PaletteColors);
    }

    /// <summary>
    /// Kolor tła (dominujący na brzegach obrazka) oznacza jako tkaninę albo zdejmuje to oznaczenie.
    /// Zwraca indeks koloru tła albo −1, gdy obrazek nie ma wyraźnego tła.
    /// </summary>
    /// <summary>
    /// Ustala kolor materiału (null — bez). Jeśli żaden kolor wzoru nie jest jeszcze tkaniną, a któryś jest prawie
    /// taki jak materiał — dostaje oznaczenie „tkanina” (np. białe tło na białym płótnie). Zwraca indeks tak
    /// oznaczonego koloru albo −1.
    /// </summary>
    public int SetFabricColor(Rgb? color)
    {
        if (Document.FabricColor == color) return -1;
        PushUndo("fabric"); // przeciąganie w próbniku koloru = jeden krok cofania
        Document.FabricColor = color;
        int marked = -1;
        if (color is { } c && !Document.Palette.Any(p => p.IsFabric) && Fabrics.Closest(Document, c) is var i and >= 0)
        {
            Document.Palette[i].IsFabric = true;
            marked = i;
        }
        Raise(ChangeKind.PaletteColors);
        return marked;
    }

    public int SetBackgroundFabric(bool fabric)
    {
        if (!Document.HasPattern) return -1;
        int bg = LabelOps.DominantBorderLabel(Document.Labels, Document.Width, Document.Height);
        if (!ValidColor(bg)) return -1;
        SetFabric(bg, fabric);
        return bg;
    }

    /// <summary>Nowy kolor na końcu palety. Zwraca jego indeks (−1, gdy paleta jest pełna).</summary>
    public int AddColor(Rgb color, string? name = null)
    {
        if (Document.Palette.Count >= 255) return -1;
        PushUndo();
        Document.Palette.Add(new PaletteEntry { Color = color, Name = string.IsNullOrWhiteSpace(name) ? ThreadCatalog.NameFor(color) : name });
        Raise(ChangeKind.PaletteStructure | ChangeKind.PaletteColors);
        return Document.Palette.Count - 1;
    }

    /// <summary>Łączy kolor <paramref name="from"/> z <paramref name="into"/> (wszystkie jego plamy i linie dostają drugi kolor).</summary>
    public bool MergeColor(int from, int into)
    {
        if (!ValidColor(from) || !ValidColor(into) || from == into) return false;
        PushUndo();
        var labels = (byte[])Document.Labels.Clone();
        var map = IdentityMap();
        map[from] = (byte)into;
        LabelOps.Remap(labels, map);
        RemapLines(map);
        Document.Labels = labels;
        RemoveFromPalette(from);
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors | ChangeKind.Annotations);
        return true;
    }

    /// <summary>Usuwa kolor — jego plamy przejmuje najbliższy kolorem. Nie usuwa ostatniego koloru.</summary>
    public bool DeleteColor(int index)
    {
        if (!ValidColor(index) || Document.Palette.Count < 2) return false;
        var own = Document.Palette[index].Color.ToLab();
        int nearest = Enumerable.Range(0, Document.Palette.Count).Where(i => i != index)
            .MinBy(i => ColorMath.DeltaE2000(own, Document.Palette[i].Color.ToLab()));
        return MergeColor(index, nearest);
    }

    /// <summary>Przesuwa kolor na liście (zmienia kolejność szycia i numery).</summary>
    public bool MoveColor(int index, int newIndex)
    {
        newIndex = Math.Clamp(newIndex, 0, Document.Palette.Count - 1);
        if (!ValidColor(index) || index == newIndex) return false;
        var order = Enumerable.Range(0, Document.Palette.Count).ToList();
        order.RemoveAt(index);
        order.Insert(newIndex, index);
        ApplyOrder(order);
        return true;
    }

    /// <summary>Sortuje paletę: od największej powierzchni albo od najjaśniejszego koloru.</summary>
    public void SortPalette(bool byLightness) => SortPalette(byLightness ? PaletteOrder.Lightness : PaletteOrder.Area);

    /// <summary>Układa paletę (tkanina zawsze pierwsza — nie szyjemy jej). Zwraca false, gdy kolejność już taka była.</summary>
    public bool SortPalette(PaletteOrder mode)
    {
        var order = StitchOrder.Compute(Document.Labels, Document.Width, Document.Height, Document.Palette, mode);
        if (order.SequenceEqual(Enumerable.Range(0, order.Count))) return false;
        ApplyOrder(order);
        return true;
    }

    /// <summary>Zamienia kolory palety na najbliższe nici z listy (kolor i nazwa).</summary>
    public bool MatchToThreads(IReadOnlyList<ThreadColor> threads)
    {
        if (threads.Count == 0) return false;
        PushUndo();
        foreach (var p in Document.Palette)
        {
            if (p.IsFabric) continue;
            var t = ThreadCatalog.Nearest(p.Color, threads);
            p.Color = t.Color;
            p.Name = t.Name;
        }
        Raise(ChangeKind.PaletteColors);
        return true;
    }

    /// <summary>Usuwa kolory, które nie występują już na wzorze ani w liniach detali (np. po zamalowaniu).</summary>
    public int RemoveUnusedColors()
    {
        var hist = LabelOps.Histogram(Document.Labels, Document.Palette.Count);
        var used = Enumerable.Range(0, Document.Palette.Count).Where(i => hist[i] > 0 || Document.Lines.Any(l => l.Color == i)).ToList();
        int removed = Document.Palette.Count - used.Count;
        if (removed == 0 || used.Count == 0) return 0;
        PushUndo();
        var map = new byte[256];
        for (int i = 0; i < used.Count; i++) map[used[i]] = (byte)i;
        var labels = (byte[])Document.Labels.Clone();
        LabelOps.Remap(labels, map);
        RemapLines(map);
        Document.Labels = labels;
        Document.Palette = used.Select(i => Document.Palette[i]).ToList();
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors | ChangeKind.Annotations);
        return removed;
    }

    /// <summary>Nowa kolejność palety: order[nowy indeks] = stary indeks.</summary>
    private void ApplyOrder(List<int> order)
    {
        PushUndo();
        var map = IdentityMap();
        for (int i = 0; i < order.Count; i++) map[order[i]] = (byte)i;
        var labels = (byte[])Document.Labels.Clone();
        LabelOps.Remap(labels, map);
        RemapLines(map);
        Document.Labels = labels;
        Document.Palette = order.Select(i => Document.Palette[i]).ToList();
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors | ChangeKind.Annotations);
    }

    /// <summary>Usuwa wpis palety (już nieużywany) i przesuwa indeksy kolejnych.</summary>
    private void RemoveFromPalette(int index)
    {
        var map = IdentityMap();
        for (int i = index + 1; i < Document.Palette.Count; i++) map[i] = (byte)(i - 1);
        LabelOps.Remap(Document.Labels, map);
        RemapLines(map);
        Document.Palette.RemoveAt(index);
    }

    private void RemapLines(byte[] map)
    {
        foreach (var l in Document.Lines)
            if (l.Color >= 0 && l.Color < map.Length) l.Color = map[l.Color];
    }

    private static byte[] IdentityMap()
    {
        var map = new byte[256];
        for (int i = 0; i < 256; i++) map[i] = (byte)i;
        return map;
    }

    // ------------------------------------------------------------------ wydruk

    /// <summary>Zmiana ustawień wydruku (bez historii cofania — to tylko opcje).</summary>
    public void UpdatePrint(Action<PrintSettings> change)
    {
        change(Document.Print);
        Raise(ChangeKind.Print);
    }
}
