namespace Wyszyciuszek.Core.Vector;

/// <summary>Odcinek granicy między dwoma obszarami (od węzła do węzła), jako ciąg wierzchołków siatki pikseli.</summary>
/// <param name="Vertices">Indeksy wierzchołków siatki (y·(szerokość+1)+x). Dla zamkniętej — bez powtórzenia pierwszego.</param>
/// <param name="Right">Obszar po prawej stronie (przy osi Y w dół) w kierunku zapisu.</param>
/// <param name="Left">Obszar po lewej stronie; −1 = poza obrazem (krawędź obrazu).</param>
/// <param name="Closed">Pętla bez węzłów (wyspa otoczona jednym obszarem).</param>
public sealed record RawChain(int[] Vertices, int Right, int Left, bool Closed);

/// <summary>Odwołanie do odcinka granicy w obwodzie obszaru (odwrócone, gdy obszar leży po lewej stronie odcinka).</summary>
public readonly record struct ChainRef(int Chain, bool Reversed);

/// <summary>Wynik śledzenia granic: wspólne odcinki i obwody (pętle) każdego obszaru zbudowane z tych odcinków.</summary>
public sealed class TracedBoundaries
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required List<RawChain> Chains { get; init; }
    /// <summary>Dla każdego obszaru — jego pętle (obwód zewnętrzny i dziury), każda jako ciąg odcinków.</summary>
    public required List<List<ChainRef>>[] RegionLoops { get; init; }
}

/// <summary>
/// Śledzi granice obszarów po krawędziach między pikselami. Każda granica między dwoma obszarami jest zapisana raz
/// (jako odcinek od węzła do węzła), a obwody obu obszarów używają tego samego odcinka. Dzięki temu po wygładzeniu
/// sąsiednie obszary nadal idealnie do siebie przylegają — bez szczelin i podwójnych linii na wzorze.
/// </summary>
public static class BoundaryTracer
{
    // kierunki: 0 = →, 1 = ↓, 2 = ←, 3 = ↑ (oś Y w dół)
    private static readonly int[] Dx = [1, 0, -1, 0];
    private static readonly int[] Dy = [0, 1, 0, -1];

    public static TracedBoundaries Trace(int[] regionMap, int w, int h, int regionCount, CancellationToken ct = default)
    {
        var tracer = new Tracer(regionMap, w, h, regionCount);
        tracer.Run(ct);
        return new TracedBoundaries { Width = w, Height = h, Chains = tracer.Chains, RegionLoops = tracer.Loops };
    }

    private sealed class Tracer
    {
        private readonly int[] _map;
        private readonly int _w, _h, _w1, _hEdges;
        private readonly bool[] _visited;
        private readonly int[] _edgeChain;
        private readonly List<int> _verts = [];
        private readonly List<int> _dirs = [];

        public readonly List<RawChain> Chains = [];
        public readonly List<List<ChainRef>>[] Loops;

        public Tracer(int[] map, int w, int h, int regionCount)
        {
            _map = map;
            _w = w;
            _h = h;
            _w1 = w + 1;
            _hEdges = (h + 1) * w;
            _visited = new bool[_w1 * (h + 1) * 4];
            _edgeChain = new int[_hEdges + h * _w1];
            Array.Fill(_edgeChain, -1);
            Loops = new List<List<ChainRef>>[regionCount];
            for (int i = 0; i < regionCount; i++) Loops[i] = [];
        }

        private int Pix(int x, int y) => x < 0 || y < 0 || x >= _w || y >= _h ? -1 : _map[y * _w + x];

        private int RightPixel(int x, int y, int d) => d switch
        {
            0 => Pix(x, y),
            1 => Pix(x - 1, y),
            2 => Pix(x - 1, y - 1),
            _ => Pix(x, y - 1),
        };

        private int LeftPixel(int x, int y, int d) => d switch
        {
            0 => Pix(x, y - 1),
            1 => Pix(x, y),
            2 => Pix(x - 1, y),
            _ => Pix(x - 1, y - 1),
        };

        private bool IsBoundary(int x, int y, int d, int region) => RightPixel(x, y, d) == region && LeftPixel(x, y, d) != region;

        /// <summary>Węzeł: styk ≥3 obszarów (licząc „poza obrazem”), styk po przekątnej albo róg obrazu.</summary>
        private bool IsJunction(int v)
        {
            int x = v % _w1, y = v / _w1;
            if ((x == 0 || x == _w) && (y == 0 || y == _h)) return true;
            int a = Pix(x - 1, y - 1), b = Pix(x, y - 1), c = Pix(x - 1, y), d = Pix(x, y);
            if (a == d && b == c && a != b) return true;
            int distinct = 1;
            if (b != a) distinct++;
            if (c != a && c != b) distinct++;
            if (d != a && d != b && d != c) distinct++;
            return distinct >= 3;
        }

        private int EdgeId(int v0, int v1)
        {
            int x0 = v0 % _w1, y0 = v0 / _w1, x1 = v1 % _w1, y1 = v1 / _w1;
            return y0 == y1 ? y0 * _w + Math.Min(x0, x1) : _hEdges + Math.Min(y0, y1) * _w1 + x0;
        }

        public void Run(CancellationToken ct)
        {
            // każda pętla (obwód lub dziura) zawiera krawędź „→” będącą górnym bokiem któregoś piksela obszaru
            for (int y = 0; y < _h; y++)
            {
                if ((y & 63) == 0) ct.ThrowIfCancellationRequested();
                for (int x = 0; x < _w; x++)
                {
                    int r = _map[y * _w + x];
                    if (y > 0 && _map[(y - 1) * _w + x] == r) continue;
                    if (_visited[(y * _w1 + x) * 4]) continue;
                    TraceLoop(r, x, y);
                }
            }
        }

        private void TraceLoop(int region, int sx, int sy)
        {
            _verts.Clear();
            _dirs.Clear();
            int x = sx, y = sy, d = 0;
            int guard = _visited.Length;
            do
            {
                _visited[(y * _w1 + x) * 4 + d] = true;
                _verts.Add(y * _w1 + x);
                _dirs.Add(d);
                x += Dx[d];
                y += Dy[d];
                // obszar po prawej: najpierw skręt w prawo, potem prosto, potem w lewo
                int right = (d + 1) & 3, left = (d + 3) & 3;
                if (IsBoundary(x, y, right, region)) d = right;
                else if (IsBoundary(x, y, d, region)) { }
                else if (IsBoundary(x, y, left, region)) d = left;
                else throw new InvalidOperationException("Przerwana granica obszaru — błąd śledzenia konturu.");
                if (--guard < 0) throw new InvalidOperationException("Granica obszaru się nie zamyka.");
            } while (x != sx || y != sy || d != 0);

            int n = _verts.Count;
            var junctions = new List<int>();
            for (int i = 0; i < n; i++) if (IsJunction(_verts[i])) junctions.Add(i);

            var loop = new List<ChainRef>(Math.Max(1, junctions.Count));
            if (junctions.Count == 0)
            {
                loop.Add(AddSegment(region, 0, n, closed: true));
            }
            else
            {
                for (int k = 0; k < junctions.Count; k++)
                {
                    int a = junctions[k], b = junctions[(k + 1) % junctions.Count];
                    int len = (b - a + n) % n;
                    if (len == 0) len = n;
                    loop.Add(AddSegment(region, a, len, closed: false));
                }
            }
            Loops[region].Add(loop);
        }

        private ChainRef AddSegment(int region, int start, int length, bool closed)
        {
            int n = _verts.Count;
            int first = EdgeId(_verts[start], _verts[(start + 1) % n]);
            if (_edgeChain[first] >= 0) return new ChainRef(_edgeChain[first], Reversed: true);

            int id = Chains.Count;
            var vs = new int[closed ? length : length + 1];
            for (int i = 0; i < vs.Length; i++) vs[i] = _verts[(start + i) % n];
            for (int i = 0; i < length; i++) _edgeChain[EdgeId(_verts[(start + i) % n], _verts[(start + i + 1) % n])] = id;
            int v0 = _verts[start];
            int leftRegion = LeftPixel(v0 % _w1, v0 / _w1, _dirs[start]);
            Chains.Add(new RawChain(vs, region, leftRegion, closed));
            return new ChainRef(id, Reversed: false);
        }
    }
}
