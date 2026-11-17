using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Project;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cli = Environment.GetCommandLineArgs();
        if (cli.Skip(1).Any(a => a.StartsWith("--export-", StringComparison.OrdinalIgnoreCase)))
        {
            _ = RunCliAsync(cli);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        // dwuklik na .wyszycie w Eksploratorze — rejestracja w tle, żeby nie opóźniać okna
        _ = Task.Run(FileAssociation.EnsureRegistered);
    }

    /// <summary>
    /// Tryb bez okna (skrypty, testy):
    ///   Wyszyciuszek.exe obraz.jpg|projekt.wyszycie [--colors 6] [--detail 5] [--smooth 5] [--lines 5] [--quality 800]
    ///       [--my-threads] [--background] [--seed 1] [--width-cm 15] [--paper A4] [--hoop r15] [--mirror] [--no-numbers] [--no-arrows] [--no-legend] [--tint 0.25]
    ///       [--export-pdf wzor.pdf] [--export-png wzor.png] [--export-colors-png kolory.png] [--export-svg wzor.svg]
    ///       [--export-project wzor.wyszycie] [--dpi 300]
    /// Błąd: kod wyjścia 1 i opis w %TEMP%\Wyszyciuszek-cli-error.txt.
    /// </summary>
    private static async Task RunCliAsync(string[] cli)
    {
        int code = 0;
        try
        {
            string? Arg(string name)
            {
                int i = Array.FindIndex(cli, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                return i > 0 && i + 1 < cli.Length ? cli[i + 1] : null;
            }
            bool Flag(string name) => cli.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            int? Int(string name) => int.TryParse(Arg(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            double? Num(string name) => double.TryParse(Arg(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            string[] flags = ["--mirror", "--no-numbers", "--no-legend", "--my-threads", "--background", "--no-arrows"];
            string? input = null;
            for (int i = 1; i < cli.Length; i++)
            {
                if (flags.Contains(cli[i], StringComparer.OrdinalIgnoreCase)) continue;
                if (cli[i].StartsWith("--", StringComparison.Ordinal)) { i++; continue; }
                input = cli[i];
                break;
            }
            if (input is null) throw new ArgumentException("Podaj obrazek albo projekt .wyszycie.");
            input = Path.GetFullPath(input);

            PatternDocument doc;
            if (input.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                doc = ProjectFile.Load(input);
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(input);
                using var image = await ImageLoader.DecodeAsync(bytes);
                var options = new SimplifyOptions
                {
                    Colors = Int("--colors") ?? 6, Detail = Int("--detail") ?? 5, Smoothing = Int("--smooth") ?? 5,
                    LineSmoothness = Int("--lines") ?? 5, WorkingSize = Int("--quality") ?? 800, Seed = Int("--seed") ?? 1,
                    Source = Flag("--my-threads") ? PaletteSource.MyThreads : PaletteSource.Automatic,
                    BackgroundAsFabric = Flag("--background"),
                };
                var working = Simplifier.PrepareWorkingImage(image.Pixels, options);
                var result = Simplifier.Run(working, options, AppInfo.Settings.Threads);
                doc = new PatternDocument
                {
                    Title = Path.GetFileNameWithoutExtension(input), SourceImage = bytes, Options = options,
                    Width = result.Width, Height = result.Height, Labels = result.Labels, Palette = result.Palette,
                };
            }
            if (Int("--lines") is { } lines) doc.Options.LineSmoothness = lines;
            var p = doc.Print;
            if (Num("--width-cm") is { } wcm) p.WidthMm = wcm * 10;
            if (Arg("--paper") is { } paper) p.Paper = paper;
            if (Arg("--hoop") is { } hoop) p.Hoop = hoop;
            if (Num("--tint") is { } tint) p.Tint = tint;
            if (Flag("--mirror")) p.Mirror = true;
            if (Flag("--no-numbers")) p.Numbers = false;
            if (Flag("--no-legend")) p.Legend = false;
            if (Flag("--no-arrows")) p.StitchArrows = false;

            var analysis = PatternAnalysis.Compute(doc.Labels, doc.Width, doc.Height, doc.Palette.Count, doc.Options.LineSmoothness);
            var composer = new PatternComposer(doc, analysis);
            float dpi = (float)(Num("--dpi") ?? 300);

            if (Arg("--export-pdf") is { } pdf)
            {
                using var fs = File.Create(Path.GetFullPath(pdf));
                Exporters.WritePdf(doc, analysis, fs);
            }
            if (Arg("--export-svg") is { } svg) await File.WriteAllTextAsync(Path.GetFullPath(svg), Exporters.ToSvg(doc, analysis));
            if (Arg("--export-png") is { } png)
            {
                using var rt = Raster.RenderDesign(composer, fullColor: false, dpi);
                await rt.SaveAsync(Path.GetFullPath(png), CanvasBitmapFileFormat.Png);
            }
            if (Arg("--export-colors-png") is { } cpng)
            {
                using var rt = Raster.RenderDesign(composer, fullColor: true, dpi);
                await rt.SaveAsync(Path.GetFullPath(cpng), CanvasBitmapFileFormat.Png);
            }
            if (Arg("--export-page-png") is { } ppng)
            {
                // podgląd kartki wydruku (pierwsza strona, a z --page N — wybrana)
                int page = Math.Clamp((Int("--page") ?? 1) - 1, 0, composer.PageCount - 1);
                using var rt = Raster.RenderPage(composer, page, Math.Min(dpi, 150));
                await rt.SaveAsync(Path.GetFullPath(ppng), CanvasBitmapFileFormat.Png);
            }
            if (Arg("--export-project") is { } proj) ProjectFile.Save(doc, Path.GetFullPath(proj));
        }
        catch (Exception ex)
        {
            code = 1;
            try { await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "Wyszyciuszek-cli-error.txt"), ex.ToString()); } catch { }
        }
        Environment.Exit(code);
    }
}
