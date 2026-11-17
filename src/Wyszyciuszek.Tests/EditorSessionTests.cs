namespace Wyszyciuszek.Tests;

public class EditorSessionTests
{
    private static EditorSession Session(params string[] rows)
    {
        var (labels, w, h) = TestData.Map(rows);
        return new EditorSession(TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Green, TestData.Blue));
    }

    [Fact]
    public void Paint_is_undoable_and_redoable()
    {
        var s = Session("0000", "0000", "0000");
        var before = (byte[])s.Document.Labels.Clone();
        ChangeKind seen = ChangeKind.None;
        s.Changed += k => seen |= k;

        Assert.False(s.PaintStroke([new Vector2(1.5f, 1.5f)], 0.6f, 0));
        Assert.False(s.CanUndo); // malowanie kolorem, który już tam jest, niczego nie zmienia

        var doc = s.Document;
        doc.Palette.Add(new PaletteEntry { Color = TestData.Red, Name = "Czerwony" });
        Assert.True(s.PaintStroke([new Vector2(1.5f, 1.5f)], 0.6f, 1));
        Assert.True(s.CanUndo);
        Assert.True(seen.HasFlag(ChangeKind.Labels));
        Assert.True(s.IsDirty);
        var painted = (byte[])doc.Labels.Clone();

        Assert.True(s.Undo());
        Assert.Equal(before, doc.Labels);
        Assert.True(s.Redo());
        Assert.Equal(painted, doc.Labels);
    }

    [Fact]
    public void Fill_region_recolors_and_merges_with_same_color_neighbors()
    {
        var s = Session(
            "0011",
            "0221",
            "0011");
        var comp = LabelOps.FindComponents(s.Document.Labels, 4, 3);
        int region2 = comp.Map[1 * 4 + 1];
        Assert.True(s.FillRegion(comp, region2, 1));
        var after = LabelOps.FindComponents(s.Document.Labels, 4, 3);
        Assert.Equal(2, after.Count); // dwójki dołączyły do jedynek
        Assert.False(s.FillRegion(after, after.Map[3], 1)); // już ten kolor
    }

    [Fact]
    public void Merge_color_removes_palette_entry_and_shifts_indices()
    {
        var s = Session("0123", "0123");
        Assert.True(s.MergeColor(1, 3));
        var d = s.Document;
        Assert.Equal(3, d.Palette.Count);
        Assert.Equal(TestData.Blue, d.Palette[2].Color);
        Assert.Equal([0, 2, 1, 2, 0, 2, 1, 2], d.Labels);
        s.Undo();
        Assert.Equal(4, d.Palette.Count);
        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3], d.Labels);
    }

    [Fact]
    public void Delete_color_merges_into_nearest_color()
    {
        var s = Session("0123");
        s.Document.Palette[3].Color = new Rgb(225, 35, 45); // prawie czerwony
        Assert.True(s.DeleteColor(3));
        Assert.Equal([0, 1, 2, 1], s.Document.Labels);
        Assert.False(Session("0000").DeleteColor(0)); // ostatniego koloru nie usuwamy
    }

    [Fact]
    public void Move_and_sort_keep_pixels_colors()
    {
        var s = Session("0112", "2222");
        var colorsOfPixels = s.Document.Labels.Select(l => s.Document.Palette[l].Color).ToArray();
        Assert.True(s.MoveColor(2, 0));
        Assert.Equal(TestData.Green, s.Document.Palette[0].Color);
        Assert.Equal(colorsOfPixels, s.Document.Labels.Select(l => s.Document.Palette[l].Color));

        s.SortPalette(byLightness: false);
        Assert.Equal(TestData.Green, s.Document.Palette[0].Color); // największa powierzchnia
        Assert.Equal(colorsOfPixels, s.Document.Labels.Select(l => s.Document.Palette[l].Color));

        s.Document.Palette[2].IsFabric = true;
        s.SortPalette(byLightness: true);
        Assert.True(s.Document.Palette[0].IsFabric); // tkanina zawsze na początku
        Assert.Equal(colorsOfPixels, s.Document.Labels.Select(l => s.Document.Palette[l].Color));
    }

    [Fact]
    public void Color_changes_in_quick_succession_are_one_undo_step()
    {
        var s = Session("01");
        var t = new DateTime(2026, 1, 1);
        s.Clock = () => t;
        s.SetColor(1, new Rgb(1, 2, 3));
        t += TimeSpan.FromMilliseconds(300);
        s.SetColor(1, new Rgb(4, 5, 6));
        t += TimeSpan.FromMilliseconds(300);
        s.SetColor(1, new Rgb(7, 8, 9));
        s.Undo();
        Assert.Equal(TestData.Red, s.Document.Palette[1].Color);
        Assert.False(s.CanUndo);

        t += TimeSpan.FromSeconds(10);
        s.SetColor(1, new Rgb(1, 1, 1));
        t += TimeSpan.FromSeconds(10);
        s.SetColor(1, new Rgb(2, 2, 2));
        s.Undo();
        Assert.Equal(new Rgb(1, 1, 1), s.Document.Palette[1].Color);
    }

    [Fact]
    public void Apply_simplified_can_be_undone()
    {
        var s = Session("0101");
        var r = new SimplifyResult([0, 0, 0, 0, 0, 0], 3, 2, [new PaletteEntry { Color = Rgb.White, Name = "Biel" }]);
        s.ApplySimplified(r);
        Assert.Equal(3, s.Document.Width);
        Assert.Single(s.Document.Palette);
        s.Undo();
        Assert.Equal(4, s.Document.Width);
        Assert.Equal(2, s.Document.Palette.Count);
        Assert.Equal([0, 1, 0, 1], s.Document.Labels);
    }

    [Fact]
    public void Remove_unused_colors_and_cleanup()
    {
        var s = Session("0002", "0000");
        Assert.Equal(1, s.RemoveUnusedColors()); // kolor 1 nieużywany
        Assert.Equal(2, s.Document.Palette.Count);
        Assert.Equal(1, s.Document.Labels[3]);
        Assert.Equal(1, s.RemoveSmallRegions(2));
        Assert.All(s.Document.Labels, l => Assert.Equal(0, l));
    }

    [Fact]
    public void Match_to_threads_renames_and_recolors_but_keeps_fabric()
    {
        var s = Session("0123");
        s.SetFabric(0, true);
        Assert.True(s.MatchToThreads([new ThreadColor("Moja czerwień", new Rgb(200, 0, 0)), new ThreadColor("Moja zieleń", new Rgb(0, 150, 0))]));
        Assert.Equal(TestData.Cream, s.Document.Palette[0].Color);
        Assert.Equal("Moja czerwień", s.Document.Palette[1].Name);
        Assert.Equal(new Rgb(0, 150, 0), s.Document.Palette[2].Color);
    }

    [Fact]
    public void Print_changes_raise_event_without_history()
    {
        var s = Session("0");
        ChangeKind seen = ChangeKind.None;
        s.Changed += k => seen = k;
        s.UpdatePrint(p => p.WidthMm = 200);
        Assert.Equal(ChangeKind.Print, seen);
        Assert.Equal(200, s.Document.Print.WidthMm);
        Assert.False(s.CanUndo);
    }
}
