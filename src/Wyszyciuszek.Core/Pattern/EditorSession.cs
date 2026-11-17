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
    /// <summary>Wszystko naraz (nowy projekt, cofnięcie).</summary>
    All = Labels | PaletteColors | PaletteStructure | Print,
}

/// <summary>
/// Edycja projektu z historią (cofnij/ponów). Każda operacja zapisuje migawkę mapy kolorów i palety, więc da się wrócić
/// także po ponownym uproszczeniu obrazu. Niezależna od interfejsu — testowana jednostkowo.
/// </summary>
public sealed class EditorSession
{
    private const int MaxHistory = 60;
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);

    private sealed record Snapshot(int Width, int Height, byte[] Labels, PaletteEntry[] Palette, string? Key, DateTime Time);

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

    private Snapshot Capture(string? key) =>
        new(Document.Width, Document.Height, (byte[])Document.Labels.Clone(), Document.Palette.Select(p => p.Clone()).ToArray(), key, Clock());

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
        Raise(ChangeKind.Labels | ChangeKind.PaletteColors | ChangeKind.PaletteStructure);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Capture(null));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Raise(ChangeKind.Labels | ChangeKind.PaletteColors | ChangeKind.PaletteStructure);
        return true;
    }

    // ------------------------------------------------------------------ upraszczanie

    /// <summary>Podmienia wzór na nowo uproszczony (do cofnięcia — ręczne poprawki da się odzyskać przez Ctrl+Z).</summary>
    public void ApplySimplified(SimplifyResult result)
    {
        if (Document.HasPattern) PushUndo();
        Document.Width = result.Width;
        Document.Height = result.Height;
        Document.Labels = result.Labels;
        Document.Palette = result.Palette;
        Raise(ChangeKind.Labels | ChangeKind.PaletteColors | ChangeKind.PaletteStructure);
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

    /// <summary>Nowy kolor na końcu palety. Zwraca jego indeks (−1, gdy paleta jest pełna).</summary>
    public int AddColor(Rgb color, string? name = null)
    {
        if (Document.Palette.Count >= 255) return -1;
        PushUndo();
        Document.Palette.Add(new PaletteEntry { Color = color, Name = string.IsNullOrWhiteSpace(name) ? ThreadCatalog.NameFor(color) : name });
        Raise(ChangeKind.PaletteStructure | ChangeKind.PaletteColors);
        return Document.Palette.Count - 1;
    }

    /// <summary>Łączy kolor <paramref name="from"/> z <paramref name="into"/> (wszystkie jego plamy dostają drugi kolor).</summary>
    public bool MergeColor(int from, int into)
    {
        if (!ValidColor(from) || !ValidColor(into) || from == into) return false;
        PushUndo();
        var labels = (byte[])Document.Labels.Clone();
        var map = IdentityMap();
        map[from] = (byte)into;
        LabelOps.Remap(labels, map);
        Document.Labels = labels;
        RemoveFromPalette(from);
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors);
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
    public void SortPalette(bool byLightness)
    {
        var hist = LabelOps.Histogram(Document.Labels, Document.Palette.Count);
        var order = Enumerable.Range(0, Document.Palette.Count)
            .OrderBy(i => Document.Palette[i].IsFabric ? 0 : 1) // tkanina zawsze pierwsza — nie szyjemy jej
            .ThenByDescending(i => byLightness ? Document.Palette[i].Color.Luminance : hist[i])
            .ToList();
        if (order.SequenceEqual(Enumerable.Range(0, order.Count))) return;
        ApplyOrder(order);
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

    /// <summary>Usuwa kolory, które nie występują już na wzorze (np. po zamalowaniu).</summary>
    public int RemoveUnusedColors()
    {
        var hist = LabelOps.Histogram(Document.Labels, Document.Palette.Count);
        var used = Enumerable.Range(0, Document.Palette.Count).Where(i => hist[i] > 0).ToList();
        int removed = Document.Palette.Count - used.Count;
        if (removed == 0 || used.Count == 0) return 0;
        PushUndo();
        var map = new byte[256];
        for (int i = 0; i < used.Count; i++) map[used[i]] = (byte)i;
        var labels = (byte[])Document.Labels.Clone();
        LabelOps.Remap(labels, map);
        Document.Labels = labels;
        Document.Palette = used.Select(i => Document.Palette[i]).ToList();
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors);
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
        Document.Labels = labels;
        Document.Palette = order.Select(i => Document.Palette[i]).ToList();
        Raise(ChangeKind.Labels | ChangeKind.PaletteStructure | ChangeKind.PaletteColors);
    }

    /// <summary>Usuwa wpis palety (już nieużywany) i przesuwa indeksy kolejnych.</summary>
    private void RemoveFromPalette(int index)
    {
        var map = IdentityMap();
        for (int i = index + 1; i < Document.Palette.Count; i++) map[i] = (byte)(i - 1);
        LabelOps.Remap(Document.Labels, map);
        Document.Palette.RemoveAt(index);
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
