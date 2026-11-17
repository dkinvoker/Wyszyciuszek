using System.IO.Compression;
using Wyszyciuszek.Core.Project;
using Wyszyciuszek.Core.Updates;

namespace Wyszyciuszek.Tests;

public class ProjectFileTests
{
    [Fact]
    public void Project_roundtrip_keeps_everything()
    {
        var (labels, w, h) = TestData.Map("0120", "2201");
        var doc = TestData.Document(labels, w, h, TestData.Cream, TestData.Red, TestData.Green);
        doc.Title = "Kotek ąę";
        doc.Palette[1].IsFabric = true;
        doc.Options.Colors = 9;
        doc.Options.Source = PaletteSource.MyThreads;
        doc.Print.WidthMm = 180;
        doc.Print.Mirror = true;
        doc.Print.Hoop = "r20";
        doc.SourceImage = [0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0, 1, 2, 3];

        using var ms = new MemoryStream();
        ProjectFile.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectFile.Load(ms);

        Assert.Equal(doc.Title, back.Title);
        Assert.Equal(doc.Labels, back.Labels);
        Assert.Equal((w, h), (back.Width, back.Height));
        Assert.Equal(doc.Palette.Select(p => (p.Color, p.Name, p.IsFabric)), back.Palette.Select(p => (p.Color, p.Name, p.IsFabric)));
        Assert.Equal(9, back.Options.Colors);
        Assert.Equal(PaletteSource.MyThreads, back.Options.Source);
        Assert.Equal(180, back.Print.WidthMm);
        Assert.True(back.Print.Mirror);
        Assert.Equal("r20", back.Print.Hoop);
        Assert.Equal(doc.SourceImage, back.SourceImage);
    }

    [Fact]
    public void Corrupt_project_is_rejected()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("project.json").Open()) s.Write("""{"format":1,"width":3,"height":3,"palette":[{"color":"#fff"}]}"""u8);
            using (var s = zip.CreateEntry("labels.bin").Open()) s.Write(new byte[4]);
        }
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => ProjectFile.Load(ms));
        Assert.ThrowsAny<Exception>(() => ProjectFile.Load(new MemoryStream([1, 2, 3])));
    }

    [Fact]
    public void Newer_format_asks_for_update()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("project.json").Open())
            s.Write("""{"format":99}"""u8);
        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => ProjectFile.Load(ms));
        Assert.Contains("nowsza", ex.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 }, ".png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0 }, ".bmp")]
    [InlineData(new byte[] { 1, 2 }, ".img")]
    public void Image_type_is_guessed_from_header(byte[] data, string ext) => Assert.Equal(ext, ProjectFile.GuessExtension(data));

    [Fact]
    public void Settings_roundtrip_and_recent_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wyszyciuszek-test-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var s = new AppSettings();
            s.MyThreads.Add(new SavedThread { Name = "Czerwona 321", Color = "#C8102E" });
            for (int i = 0; i < 10; i++) s.AddRecent($@"C:\wzory\{i}.wyszycie");
            s.AddRecent(@"C:\WZORY\3.wyszycie");
            s.Save(path);
            var back = AppSettings.Load(path);
            Assert.Equal("Czerwona 321", back.Threads[0].Name);
            Assert.Equal(new Rgb(0xC8, 0x10, 0x2E), back.Threads[0].Color);
            Assert.Equal(8, back.RecentFiles.Count);
            Assert.Equal(@"C:\WZORY\3.wyszycie", back.RecentFiles[0]);
            Assert.Single(back.RecentFiles, f => f.Equals(@"C:\wzory\3.wyszycie", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }

    [Fact]
    public void Broken_settings_file_gives_defaults()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ to nie json");
        try
        {
            Assert.Empty(AppSettings.Load(path).MyThreads);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class UpdateLogicTests
{
    [Theory]
    [InlineData("v1.0.17", 1, 0, 17)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("Wyszyciuszek 2.3.4", 2, 3, 4)]
    public void Parses_versions(string text, int a, int b, int c) => Assert.Equal(new Version(a, b, c), UpdateLogic.ParseVersion(text));

    [Fact]
    public void Compares_versions_numerically()
    {
        Assert.True(UpdateLogic.IsNewer(new Version(1, 0, 10), new Version(1, 0, 9)));
        Assert.True(UpdateLogic.IsNewer(new Version(1, 1, 0), new Version(1, 0, 99)));
        Assert.False(UpdateLogic.IsNewer(new Version(1, 0), new Version(1, 0, 0)));
        Assert.Null(UpdateLogic.ParseVersion("brak"));
    }

    [Fact]
    public void Reads_github_release_with_digest()
    {
        var hash = new string('a', 64);
        var json = $$"""
            {"tag_name":"v1.0.5","name":"Wyszyciuszek 1.0.5","body":"Zmiany","html_url":"https://github.com/x/y/releases/tag/v1.0.5",
             "draft":false,"prerelease":false,
             "assets":[{"name":"Wyszyciuszek.exe","browser_download_url":"https://example.invalid/W.exe","size":123,"digest":"sha256:{{hash}}"}]}
            """;
        var r = UpdateLogic.Parse(json)!;
        Assert.Equal(new Version(1, 0, 5), r.Version);
        Assert.Equal("1.0.5", r.DisplayVersion);
        Assert.True(r.CanInstall);
        Assert.Equal(hash, r.Sha256);
        Assert.Equal(123, r.AssetSize);
    }

    [Fact]
    public void Falls_back_to_sha_in_notes_and_ignores_prereleases()
    {
        var hash = new string('B', 64);
        Assert.Equal(hash.ToLowerInvariant(), UpdateLogic.ParseSha256(null, $"SHA-256: {hash}"));
        Assert.Null(UpdateLogic.Parse("""{"tag_name":"v9.0.0","prerelease":true,"assets":[]}"""));
        Assert.False(UpdateLogic.Parse("""{"tag_name":"v9.0.0","assets":[]}""")!.CanInstall);
    }
}
