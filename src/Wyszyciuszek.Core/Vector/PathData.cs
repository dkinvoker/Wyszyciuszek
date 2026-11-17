using System.Globalization;
using System.Numerics;
using System.Text;

namespace Wyszyciuszek.Core.Vector;

public enum PathOp : byte { Move, Line, Cubic, Close }

/// <summary>Polecenie ścieżki. Move/Line — punkt w P1; Cubic — punkty kontrolne P1, P2 i koniec P3.</summary>
public readonly record struct PathCmd(PathOp Op, Vector2 P1, Vector2 P2 = default, Vector2 P3 = default);

/// <summary>Prostokąt (x, y, szerokość, wysokość).</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public Vector2 Center => new(X + W / 2, Y + H / 2);
    public bool IsEmpty => W <= 0 || H <= 0;
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X <= Right && p.Y <= Bottom;
    public bool Intersects(RectF o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public RectF Inflate(float d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);

    public static RectF FromPoints(Vector2 min, Vector2 max) => new(min.X, min.Y, max.X - min.X, max.Y - min.Y);

    public RectF Union(RectF o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        return FromPoints(Vector2.Min(new Vector2(X, Y), new Vector2(o.X, o.Y)), Vector2.Max(new Vector2(Right, Bottom), new Vector2(o.Right, o.Bottom)));
    }
}

/// <summary>Ścieżka wektorowa (kilka figur z odcinków i krzywych Béziera) — wspólna dla podglądu, PDF, SVG i druku.</summary>
public sealed class PathData
{
    private readonly List<PathCmd> _commands = [];
    private RectF? _bounds;

    public IReadOnlyList<PathCmd> Commands => _commands;
    public bool IsEmpty => _commands.Count == 0;

    public void MoveTo(Vector2 p) { _commands.Add(new PathCmd(PathOp.Move, p)); _bounds = null; }
    public void LineTo(Vector2 p) { _commands.Add(new PathCmd(PathOp.Line, p)); _bounds = null; }
    public void CubicTo(Vector2 c1, Vector2 c2, Vector2 p) { _commands.Add(new PathCmd(PathOp.Cubic, c1, c2, p)); _bounds = null; }
    public void Close() => _commands.Add(new PathCmd(PathOp.Close, default));

    public void Append(PathData other)
    {
        _commands.AddRange(other._commands);
        _bounds = null;
    }

    /// <summary>Prostokąt obejmujący wszystkie punkty (z kontrolnymi — wystarcza do przycinania i hit-testu).</summary>
    public RectF Bounds => _bounds ??= ComputeBounds();

    private RectF ComputeBounds()
    {
        if (_commands.Count == 0) return default;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var c in _commands)
        {
            switch (c.Op)
            {
                case PathOp.Move:
                case PathOp.Line:
                    min = Vector2.Min(min, c.P1); max = Vector2.Max(max, c.P1);
                    break;
                case PathOp.Cubic:
                    min = Vector2.Min(Vector2.Min(min, c.P1), Vector2.Min(c.P2, c.P3));
                    max = Vector2.Max(Vector2.Max(max, c.P1), Vector2.Max(c.P2, c.P3));
                    break;
            }
        }
        return min.X > max.X ? default : RectF.FromPoints(min, max);
    }

    /// <summary>Atrybut „d” dla SVG (współrzędne z dokładnością do 0,01).</summary>
    public string ToSvgData()
    {
        var sb = new StringBuilder(_commands.Count * 24);
        foreach (var c in _commands)
        {
            switch (c.Op)
            {
                case PathOp.Move: sb.Append('M').Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)); break;
                case PathOp.Line: sb.Append('L').Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)); break;
                case PathOp.Cubic:
                    sb.Append('C').Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)).Append(' ')
                      .Append(F(c.P2.X)).Append(' ').Append(F(c.P2.Y)).Append(' ')
                      .Append(F(c.P3.X)).Append(' ').Append(F(c.P3.Y));
                    break;
                case PathOp.Close: sb.Append('Z'); break;
            }
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
