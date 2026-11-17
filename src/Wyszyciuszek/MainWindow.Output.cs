using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;
using Wyszyciuszek.Output;

namespace Wyszyciuszek;

/// <summary>Eksport (PDF, PNG, SVG) i druk.</summary>
public sealed partial class MainWindow
{
    private const float ExportDpi = 300;

    /// <summary>Migawka projektu do eksportu (użytkownik może dalej edytować w trakcie zapisu) i aktualne kontury.</summary>
    private async Task<(PatternDocument Doc, PatternAnalysis Analysis)?> SnapshotAsync()
    {
        if (Doc is not { HasPattern: true } d) return null;
        var doc = new PatternDocument
        {
            Title = d.Title, Width = d.Width, Height = d.Height, Labels = d.Labels,
            Palette = d.Palette.Select(p => p.Clone()).ToList(), Options = d.Options.Clone(), Print = d.Print.Clone(),
        };
        var an = AnalysisIsCurrent
            ? _analysis!
            : await Task.Run(() => PatternAnalysis.Compute(doc.Labels, doc.Width, doc.Height, doc.Palette.Count, doc.Options.LineSmoothness));
        return (doc, an);
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (await SnapshotAsync() is not { } snap) return;
        var (doc, an) = snap;
        var path = await PickSaveAsync(doc.Title, "Dokument PDF", ".pdf");
        if (path is null) return;
        await RunExportAsync(path, () =>
        {
            using var fs = File.Create(path);
            Exporters.WritePdf(doc, an, fs);
            return Task.CompletedTask;
        });
    }

    private async void ExportPatternPng_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(fullColor: false);

    private async void ExportColorsPng_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(fullColor: true);

    private async Task ExportPngAsync(bool fullColor)
    {
        if (await SnapshotAsync() is not { } snap) return;
        var (doc, an) = snap;
        var path = await PickSaveAsync(doc.Title + (fullColor ? " — kolory" : " — wzór"), "Obraz PNG", ".png");
        if (path is null) return;
        await RunExportAsync(path, async () =>
        {
            using var rt = Raster.RenderDesign(new PatternComposer(doc, an), fullColor, ExportDpi);
            await rt.SaveAsync(path, CanvasBitmapFileFormat.Png);
        });
    }

    private async void ExportSvg_Click(object sender, RoutedEventArgs e)
    {
        if (await SnapshotAsync() is not { } snap) return;
        var (doc, an) = snap;
        var path = await PickSaveAsync(doc.Title, "Grafika SVG", ".svg");
        if (path is null) return;
        await RunExportAsync(path, () => File.WriteAllTextAsync(path, Exporters.ToSvg(doc, an)));
    }

    private async Task RunExportAsync(string path, Func<Task> work)
    {
        BusyText.Text = "Zapisuję…";
        BusyCard.Visibility = Visibility.Visible;
        try
        {
            await Task.Run(work);
            ShowSavedToast(path);
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się zapisać pliku", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            UpdateBusy();
        }
    }

    /// <summary>Komunikat „zapisano” z przyciskiem otwierającym plik.</summary>
    private void ShowSavedToast(string path)
    {
        ShowToast("Zapisano", Path.GetFileName(path), InfoBarSeverity.Success);
        var open = new Button { Content = "Otwórz" };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
            Toast.IsOpen = false;
        };
        var folder = new Button { Content = "Pokaż w folderze", Margin = new Thickness(6, 0, 0, 0) };
        folder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
            Toast.IsOpen = false;
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(open);
        panel.Children.Add(folder);
        Toast.Content = panel;
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || await SnapshotAsync() is not { } snap) return;
        var (doc, an) = snap;
        var composer = new PatternComposer(doc, an);
        BusyText.Text = "Drukuję…";
        try
        {
            var result = await PrintService.PrintAsync(_hwnd, composer, doc.Title);
            if (result.Error is not null) ShowToast("Nie udało się wydrukować", result.Error, InfoBarSeverity.Error);
            else if (result.Printed)
                ShowToast("Wysłano do drukarki", "Sprawdź kwadrat 1 cm w rogu kartki — jeśli ma inny rozmiar, wyłącz w drukarce „dopasuj do strony”.", InfoBarSeverity.Success);
        }
        finally
        {
            UpdateBusy();
        }
    }
}
