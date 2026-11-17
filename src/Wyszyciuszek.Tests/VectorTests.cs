namespace Wyszyciuszek.Tests;

public class VectorTests
{
    private static TracedBoundaries Trace(byte[] labels, int w, int h, out Components comp)
    {
        comp = LabelOps.FindComponents(labels, w, h);
        return BoundaryTracer.Trace(comp.Map, w, h, comp.Count);
    }

    /// <summary>Każda granica wewnętrzna jest użyta przez oba obszary (raz wprost, raz odwrotnie), krawędź obrazu — raz.</summary>
    private static void AssertConsistent(TracedBoundaries t, Components comp)
    {
        var uses = new Dictionary<int, List<(int Region, bool Reversed)>>();
        for (int r = 0; r < comp.Count; r++)
        {
            Assert.NotEmpty(t.RegionLoops[r]);
            foreach (var loop in t.RegionLoops[r])
                foreach (var cr in loop)
                {
                    if (!uses.TryGetValue(cr.Chain, out var list)) uses[cr.Chain] = list = [];
                    list.Add((r, cr.Reversed));
                }
        }
        for (int i = 0; i < t.Chains.Count; i++)
        {
            var c = t.Chains[i];
            var u = uses[i];
            if (c.Left < 0)
            {
                Assert.Equal([(c.Right, false)], u);
            }
            else
            {
                Assert.Equal(2, u.Count);
                Assert.Contains((c.Right, false), u);
                Assert.Contains((c.Left, true), u);
            }
        }

        // pętle są ciągłe: koniec odcinka = początek następnego
        int w1 = t.Width + 1;
        for (int r = 0; r < comp.Count; r++)
            foreach (var loop in t.RegionLoops[r])
            {
                for (int k = 0; k < loop.Count; k++)
                {
                    var a = loop[k];
                    var b = loop[(k + 1) % loop.Count];
                    var ca = t.Chains[a.Chain];
                    var cb = t.Chains[b.Chain];
                    if (ca.Closed) { Assert.Single(loop); continue; }
                    int end = a.Reversed ? ca.Vertices[0] : ca.Vertices[^1];
                    int start = b.Reversed ? cb.Vertices[^1] : cb.Vertices[0];
                    Assert.Equal(end, start);
                }
            }
    }

    [Fact]
    public void Single_color_has_only_frame()
    {
        var labels = new byte[6 * 4];
        var t = Trace(labels, 6, 4, out var comp);
        Assert.All(t.Chains, c => Assert.Equal(-1, c.Left));
        Assert.Equal(4, t.Chains.Count); // cztery boki między rogami
        Assert.Single(t.RegionLoops[0]);
        AssertConsistent(t, comp);
    }

    [Fact]
    public void Two_halves_share_one_boundary()
    {
        var (labels, w, h) = TestData.Map(
            "0011",
            "0011",
            "0011");
        var t = Trace(labels, w, h, out var comp);
        var inner = t.Chains.Where(c => c.Left >= 0).ToList();
        Assert.Single(inner);
        Assert.Equal(4, inner[0].Vertices.Length); // pionowa linia x=2 od y=0 do y=3
        AssertConsistent(t, comp);
    }

    [Fact]
    public void Island_is_closed_chain_and_hole_of_outer_region()
    {
        var (labels, w, h) = TestData.Map(
            "00000",
            "01110",
            "01110",
            "00000");
        var t = Trace(labels, w, h, out var comp);
        int outer = comp.Map[0], island = comp.Map[1 * w + 1];
        var closed = t.Chains.Single(c => c.Closed);
        Assert.Equal(island, closed.Right);
        Assert.Equal(outer, closed.Left);
        Assert.Equal(2, t.RegionLoops[outer].Count); // obwód zewnętrzny + dziura
        Assert.Single(t.RegionLoops[island]);
        AssertConsistent(t, comp);
    }

    [Fact]
    public void Diagonal_touch_and_ring_are_handled()
    {
        var (labels, w, h) = TestData.Map(
            "111111",
            "100001",
            "101001",
            "100101",
            "100001",
            "111111");
        var t = Trace(labels, w, h, out var comp);
        AssertConsistent(t, comp);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Random_maps_trace_consistently(int seed)
    {
        var labels = TestData.RandomMap(23, 17, 3, seed);
        var t = Trace(labels, 23, 17, out var comp);
        AssertConsistent(t, comp);
    }

    [Fact]
    public void Analysis_builds_paths_and_hit_test()
    {
        var (labels, w, h) = TestData.Map(
            "0000000000",
            "0111100000",
            "0111100220",
            "0111100220",
            "0000000000");
        var an = PatternAnalysis.Compute(labels, w, h, 3, smoothness: 5);
        Assert.Equal(3, an.RegionCount);
        Assert.Equal([34, 12, 4], an.ColorArea);
        Assert.All(an.ColorPaths, p => Assert.False(p.IsEmpty));
        Assert.False(an.Outline.IsEmpty);
        Assert.False(an.Frame.IsEmpty);
        Assert.Equal(an.Components.Map[2 * w + 2], an.HitTest(new Vector2(2.5f, 2.5f)));
        Assert.Equal(-1, an.HitTest(new Vector2(-1, 0)));

        // granice obszaru 1 leżą w okolicy pikseli 1..5 × 1..4
        var b = an.Regions[an.HitTest(new Vector2(2.5f, 2.5f))].Bounds;
        Assert.InRange(b.X, 0.5f, 1.6f);
        Assert.InRange(b.Right, 4.4f, 5.5f);
    }

    [Fact]
    public void Smoothed_island_keeps_its_size()
    {
        // koło o promieniu 10 px — po wygładzeniu pole nadal ok. πr²
        int w = 40, h = 40;
        var labels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if ((x + 0.5 - 20) * (x + 0.5 - 20) + (y + 0.5 - 20) * (y + 0.5 - 20) < 100) labels[y * w + x] = 1;
        var an = PatternAnalysis.Compute(labels, w, h, 2, smoothness: 10);
        var b = an.Regions[an.HitTest(new Vector2(20, 20))].Bounds;
        Assert.InRange(b.W, 18.5f, 22.5f);
        Assert.InRange(b.H, 18.5f, 22.5f);
    }

    [Fact]
    public void Analysis_survives_noisy_map()
    {
        var labels = TestData.RandomMap(64, 48, 5, seed: 11);
        var an = PatternAnalysis.Compute(labels, 64, 48, 5, smoothness: 7);
        Assert.Equal(64 * 48, an.ColorArea.Sum());
        Assert.Equal(an.RegionCount, an.ColorRegionCount.Sum());
    }

    [Fact]
    public void Densify_puts_points_on_edge_midpoints()
    {
        int w1 = 4;
        int V(int x, int y) => y * w1 + x;
        var open = PatternAnalysis.Densify([V(0, 0), V(1, 0), V(1, 1)], w1, closed: false);
        Assert.Equal([new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 0.5f), new Vector2(1, 1)], open);
        var closed = PatternAnalysis.Densify([V(0, 0), V(1, 0), V(1, 1), V(0, 1)], w1, closed: true);
        Assert.Equal(4, closed.Length);
        Assert.Equal(new Vector2(0.5f, 0), closed[0]);
    }

    [Fact]
    public void Simplify_removes_collinear_points_and_keeps_corners()
    {
        Vector2[] pts = [new(0, 0), new(1, 0.01f), new(2, 0), new(3, 0), new(3, 1), new(3, 2)];
        var s = Curves.Simplify(pts, 0.1f, closed: false);
        Assert.Equal([new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 2)], s);
    }

    [Fact]
    public void CatmullRom_passes_through_points()
    {
        Vector2[] pts = [new(0, 0), new(4, 2), new(8, 0), new(12, 3)];
        var segs = Curves.CatmullRom(pts, closed: false);
        Assert.Equal(3, segs.Length);
        for (int i = 0; i < segs.Length; i++)
        {
            Assert.Equal(pts[i], segs[i].Start);
            Assert.Equal(pts[i + 1], segs[i].End);
        }
        Assert.Equal(4, Curves.CatmullRom(pts, closed: true).Length);
    }

    [Fact]
    public void Taubin_keeps_open_ends_fixed()
    {
        Vector2[] pts = [new(0, 0), new(1, 1), new(2, 0), new(3, 1), new(4, 0)];
        var s = Curves.Taubin(pts, 5, closed: false);
        Assert.Equal(pts[0], s[0]);
        Assert.Equal(pts[^1], s[^1]);
        Assert.True(Math.Abs(s[2].Y - 0.5f) < Math.Abs(pts[2].Y - 0.5f)); // zygzak się wypłaszcza
    }

    [Fact]
    public void PathData_bounds_and_svg()
    {
        var p = Shapes.Rect(1, 2, 3, 4);
        Assert.Equal(new RectF(1, 2, 3, 4), p.Bounds);
        Assert.Equal("M1 2L4 2L4 6L1 6Z", p.ToSvgData());
    }
}
