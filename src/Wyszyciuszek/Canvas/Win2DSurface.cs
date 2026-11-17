using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Canvas;

/// <summary>
/// Pamięć geometrii Win2D dla dużych ścieżek wzoru (wypełnienia kolorów, kontury) — budowane raz, rysowane w każdej klatce.
/// Małe ścieżki pomocnicze (ramki, krzyżyki) są budowane na bieżąco.
/// </summary>
internal sealed class GeometryCache : IDisposable
{
    private const int CacheThreshold = 24;
    private readonly Dictionary<PathData, CanvasGeometry> _map = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PathData> _used = new(ReferenceEqualityComparer.Instance);

    public CanvasGeometry Get(ICanvasResourceCreator rc, PathData path, out bool owned)
    {
        if (path.Commands.Count < CacheThreshold)
        {
            owned = true;
            return Build(rc, path);
        }
        owned = false;
        _used.Add(path);
        if (!_map.TryGetValue(path, out var g)) _map[path] = g = Build(rc, path);
        return g;
    }

    /// <summary>
    /// Koniec klatki: zwalnia geometrie nieużyte w tej klatce (np. stare linie detali po zmianie), żeby pamięć nie rosła.
    /// </summary>
    public void EndFrame()
    {
        if (_map.Count > _used.Count)
        {
            foreach (var key in _map.Keys.Where(k => !_used.Contains(k)).ToList())
            {
                _map[key].Dispose();
                _map.Remove(key);
            }
        }
        _used.Clear();
    }

    public static CanvasGeometry Build(ICanvasResourceCreator rc, PathData path)
    {
        using var pb = new CanvasPathBuilder(rc);
        pb.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Alternate);
        bool open = false;
        foreach (var c in path.Commands)
        {
            switch (c.Op)
            {
                case PathOp.Move:
                    if (open) pb.EndFigure(CanvasFigureLoop.Open);
                    pb.BeginFigure(c.P1);
                    open = true;
                    break;
                case PathOp.Line:
                    if (open) pb.AddLine(c.P1);
                    break;
                case PathOp.Cubic:
                    if (open) pb.AddCubicBezier(c.P1, c.P2, c.P3);
                    break;
                case PathOp.Close:
                    if (open) pb.EndFigure(CanvasFigureLoop.Closed);
                    open = false;
                    break;
            }
        }
        if (open) pb.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(pb);
    }

    public void Clear()
    {
        foreach (var g in _map.Values) g.Dispose();
        _map.Clear();
        _used.Clear();
    }

    public void Dispose() => Clear();
}

/// <summary>Rysowanie poleceń <see cref="IDrawSurface"/> przez Win2D (podgląd, PNG, druk).</summary>
internal sealed class Win2DSurface : IDrawSurface
{
    private const float ArialAscent = 0.905f;
    private static readonly CanvasStrokeStyle RoundStroke = new()
    {
        StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round,
    };

    private readonly CanvasDrawingSession _ds;
    private readonly GeometryCache _cache;
    private readonly Stack<object> _stack = new();
    private readonly Dictionary<(int Size, bool Bold, TextAnchor Anchor), CanvasTextFormat> _formats = [];

    public Win2DSurface(CanvasDrawingSession ds, GeometryCache cache)
    {
        _ds = ds;
        _cache = cache;
    }

    public static Color ToColor(Rgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    public void PushTransform(Matrix3x2 transform)
    {
        _stack.Push(_ds.Transform);
        _ds.Transform = transform * _ds.Transform;
    }

    public void PushClip(RectF rect) => _stack.Push(_ds.CreateLayer(1f, new Rect(rect.X, rect.Y, Math.Max(0, rect.W), Math.Max(0, rect.H))));

    public void Pop()
    {
        if (_stack.Count == 0) return;
        switch (_stack.Pop())
        {
            case Matrix3x2 m: _ds.Transform = m; break;
            case CanvasActiveLayer layer: layer.Dispose(); break;
        }
    }

    /// <summary>Zamyka wszystkie otwarte warstwy (gdyby rysowanie przerwał wyjątek).</summary>
    public void PopAll()
    {
        while (_stack.Count > 0) Pop();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
    }

    public void Fill(PathData path, Rgb color)
    {
        if (path.IsEmpty) return;
        var g = _cache.Get(_ds, path, out bool owned);
        _ds.FillGeometry(g, ToColor(color));
        if (owned) g.Dispose();
    }

    public void Stroke(PathData path, Rgb color, float width, float[]? dash = null)
    {
        if (path.IsEmpty) return;
        var g = _cache.Get(_ds, path, out bool owned);
        if (dash is { Length: > 0 })
        {
            // wzór kreski Win2D jest w jednostkach grubości linii
            using var style = new CanvasStrokeStyle
            {
                CustomDashStyle = dash.Select(d => d / Math.Max(width, 1e-3f)).ToArray(),
                StartCap = CanvasCapStyle.Flat, EndCap = CanvasCapStyle.Flat,
            };
            _ds.DrawGeometry(g, ToColor(color), width, style);
        }
        else
        {
            _ds.DrawGeometry(g, ToColor(color), width, RoundStroke);
        }
        if (owned) g.Dispose();
    }

    public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false)
    {
        if (string.IsNullOrEmpty(text) || size <= 0) return;
        // czcionka w 1/100 jednostki, żeby nie tworzyć formatu dla każdej drobnej różnicy
        int key = Math.Max(1, (int)MathF.Round(size * 100));
        if (!_formats.TryGetValue((key, bold, anchor), out var format))
        {
            format = new CanvasTextFormat
            {
                FontFamily = "Arial",
                FontSize = key / 100f,
                FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap,
                VerticalAlignment = CanvasVerticalAlignment.Top,
                HorizontalAlignment = anchor switch
                {
                    TextAnchor.Middle => CanvasHorizontalAlignment.Center,
                    TextAnchor.End => CanvasHorizontalAlignment.Right,
                    _ => CanvasHorizontalAlignment.Left,
                },
                Options = CanvasDrawTextOptions.NoPixelSnap,
            };
            _formats[(key, bold, anchor)] = format;
        }
        _ds.DrawText(text, baseline.X, baseline.Y - ArialAscent * size, ToColor(color), format);
    }
}
