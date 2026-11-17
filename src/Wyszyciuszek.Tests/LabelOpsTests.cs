namespace Wyszyciuszek.Tests;

public class LabelOpsTests
{
    [Fact]
    public void Components_use_edge_connectivity_only()
    {
        // dwie jedynki stykające się tylko rogiem to dwa osobne obszary
        var (labels, w, h) = TestData.Map(
            "100",
            "010",
            "000");
        var comp = LabelOps.FindComponents(labels, w, h);
        Assert.Equal(3, comp.Count);
        Assert.Equal(7, comp.Area[comp.Map[2]]);
        Assert.NotEqual(comp.Map[0], comp.Map[4]);
    }

    [Fact]
    public void ModeFilter_removes_single_pixel_specks()
    {
        var (labels, w, h) = TestData.Map(
            "00000",
            "00100",
            "00000",
            "22222");
        var r = LabelOps.ModeFilter(labels, w, h, 1);
        Assert.Equal(0, r[1 * w + 2]);
        Assert.All(r[(3 * w)..], v => Assert.Equal(2, v)); // dolny pas nienaruszony
    }

    [Fact]
    public void RemoveSmallRegions_merges_into_neighbor_with_longest_border()
    {
        var (labels, w, h) = TestData.Map(
            "000000",
            "001100",
            "001100",
            "222222",
            "222222");
        // plama 1 (4 px) graniczy z 0 na 6 krawędziach i z 2 na 2 — trafia do 0
        int merged = LabelOps.RemoveSmallRegions(labels, w, h, minArea: 5);
        Assert.Equal(1, merged);
        Assert.DoesNotContain((byte)1, labels);
        Assert.Equal(0, labels[1 * w + 2]);
    }

    [Fact]
    public void RemoveSmallRegions_keeps_large_regions_and_whole_image()
    {
        var labels = new byte[100];
        Assert.Equal(0, LabelOps.RemoveSmallRegions(labels, 10, 10, minArea: 1000));
        Assert.All(labels, v => Assert.Equal(0, v));
    }

    [Fact]
    public void RemoveSmallRegions_cleans_random_noise_completely()
    {
        var labels = TestData.RandomMap(60, 40, 4, seed: 9);
        LabelOps.RemoveSmallRegions(labels, 60, 40, minArea: 30, maxPasses: 30);
        var comp = LabelOps.FindComponents(labels, 60, 40);
        Assert.All(Enumerable.Range(0, comp.Count), r => Assert.True(comp.Area[r] >= 30 || comp.Count == 1));
    }

    [Fact]
    public void RemoveThinRegions_removes_slivers_but_keeps_thick_shapes()
    {
        var (labels, w, h) = TestData.Map(
            "0000000000",
            "0000000000",
            "1111111111",
            "0000000000",
            "0022220000",
            "0022220000",
            "0022220000",
            "0022220000",
            "0000000000");
        int merged = LabelOps.RemoveThinRegions(labels, w, h, 1.5f);
        Assert.Equal(1, merged);
        Assert.DoesNotContain((byte)1, labels); // pasek grubości 1 px zniknął
        Assert.Equal(16, labels.Count(l => l == 2)); // kwadrat 4×4 został
    }

    [Fact]
    public void PluralityFilter_straightens_wavy_boundary_and_keeps_big_areas()
    {
        int w = 40, h = 20;
        var labels = new byte[w * h];
        // granica na wysokości 10 z „ząbkami” co czwarty piksel
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                labels[y * w + x] = (byte)(y > 10 || (y == 10 && x % 4 != 0) ? 1 : 0);
        var r = LabelOps.PluralityFilter(labels, w, h, 2, 1);
        for (int x = 2; x < w - 2; x++) Assert.Equal(r[10 * w + 2], r[10 * w + x]); // jeden równy wiersz
        Assert.Equal(0, r[2 * w + 20]);
        Assert.Equal(1, r[17 * w + 20]);
    }

    [Fact]
    public void LabelPoints_are_inside_and_far_from_edges()
    {
        var (labels, w, h) = TestData.Map(
            "0000000000",
            "0111111110",
            "0111111110",
            "0111111110",
            "0111111110",
            "0000000000");
        var comp = LabelOps.FindComponents(labels, w, h);
        var points = LabelOps.LabelPoints(comp);
        int inner = comp.Map[2 * w + 3];
        var (p, radius) = points[inner];
        Assert.Equal(inner, comp.Map[(int)p.Y * w + (int)p.X]);
        Assert.True(radius >= 2, $"promień {radius}");
        Assert.InRange(p.Y, 2, 4);
    }

    [Fact]
    public void PaintStroke_paints_line_with_round_brush()
    {
        var labels = new byte[20 * 10];
        int changed = LabelOps.PaintStroke(labels, 20, 10, [new Vector2(2, 5), new Vector2(17, 5)], 1.5f, 3);
        Assert.True(changed > 30);
        Assert.Equal(3, labels[5 * 20 + 10]);
        Assert.Equal(0, labels[0]);
        Assert.Equal(0, labels[9 * 20 + 10]);
    }

    [Fact]
    public void PaintCircle_tiny_brush_still_paints_one_pixel()
    {
        var labels = new byte[25];
        Assert.Equal(1, LabelOps.PaintCircle(labels, 5, 5, new Vector2(2.9f, 2.1f), 0.2f, 4));
        Assert.Equal(4, labels[2 * 5 + 2]);
    }

    [Fact]
    public void FillRegion_changes_only_that_component()
    {
        var (labels, w, h) = TestData.Map(
            "1001",
            "1001");
        var comp = LabelOps.FindComponents(labels, w, h);
        LabelOps.FillRegion(labels, comp, comp.Map[0], 2);
        Assert.Equal([2, 0, 0, 1, 2, 0, 0, 1], labels);
    }

    [Fact]
    public void Histogram_counts_colors()
    {
        var (labels, _, _) = TestData.Map("0112", "2222");
        Assert.Equal([1, 2, 5], LabelOps.Histogram(labels, 3));
    }
}
