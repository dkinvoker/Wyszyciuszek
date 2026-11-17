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
        var doc = d.Snapshot(); // z liniami detali, kierunkami ściegu i kolorem tkaniny
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
        var s = AppInfo.Settings;
        double sx = s.CalibratePdf ? s.PrintScaleX : 1, sy = s.CalibratePdf ? s.PrintScaleY : 1;
        await RunExportAsync(path, () =>
        {
            using var fs = File.Create(path);
            Exporters.WritePdf(doc, an, fs, sx, sy, AppInfo.Settings.MyThreads);
            return Task.CompletedTask;
        });
    }

    // ================================================================== kalibracja drukarki

    /// <summary>
    /// Kalibracja (jak w Kasetowniku): strona testowa z miarkami 100 mm, pomiar linijką, zapis korekty skali,
    /// którą potem stosuje każdy wydruk (i opcjonalnie PDF).
    /// </summary>
    private async void CalibratePrinter_Click(object sender, RoutedEventArgs e)
    {
        var s = AppInfo.Settings;
        NumberBox Box(string header) => new()
        {
            Header = header, Value = CalibrationPage.RulerMm, Minimum = 80, Maximum = 120, SmallChange = 0.5, LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var horizontal = Box("Miarka pozioma (mm)");
        var vertical = Box("Miarka pionowa (mm)");
        var pdf = new CheckBox { Content = "Stosuj też w PDF (gdy PDF drukuję na tej samej drukarce)", IsChecked = s.CalibratePdf };
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Res("HaftTextDimBrush"),
            Text = $"Obecna korekta: {s.PrintCalibrationText}",
        };
        var printTest = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = "1. Wydrukuj stronę testową" };
        printTest.Click += async (_, _) =>
        {
            printTest.IsEnabled = false;
            try
            {
                // strona testowa zawsze bez korekty — mierzymy, ile drukarka naprawdę drukuje
                var r = await PrintService.PrintAsync(_hwnd, new CalibrationPage(), "Wyszyciuszek — kalibracja drukarki");
                status.Text = r.Error ?? (r.Printed ? "Wydrukowano. Zmierz obie miarki i wpisz wyniki." : status.Text);
            }
            finally
            {
                printTest.IsEnabled = true;
            }
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(vertical, 1);
        grid.Children.Add(horizontal);
        grid.Children.Add(vertical);

        var panel = new StackPanel { Spacing = 12, Width = 440 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Text = "Drukarki często drukują odrobinę za mało albo za dużo (np. 98–99%), mimo skali 100%. Wydrukuj stronę " +
                   "testową, zmierz linijką obie miarki (powinny mieć 100 mm) i wpisz wyniki — Wyszyciuszek będzie to " +
                   "poprawiał przy każdym wydruku, żeby wzór miał dokładnie zadany rozmiar.",
        });
        panel.Children.Add(printTest);
        panel.Children.Add(new TextBlock { Text = "2. Wpisz zmierzone długości:", FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(grid);
        panel.Children.Add(pdf);
        panel.Children.Add(status);

        var dlg = new ContentDialog
        {
            Title = "Kalibracja drukarki",
            Content = panel,
            PrimaryButtonText = "Zapisz kalibrację",
            SecondaryButtonText = s.HasPrintCalibration ? "Usuń kalibrację" : "",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await ShowDialogAsync(dlg);
        if (result == ContentDialogResult.Secondary)
        {
            s.PrintScaleX = s.PrintScaleY = 1;
            AppInfo.SaveSettings();
            UpdatePrintInfo();
            ShowToast("Kalibracja usunięta", "Wydruki bez korekty skali.");
            return;
        }
        if (result != ContentDialogResult.Primary) return;
        double lh = horizontal.Value, lv = vertical.Value;
        if (double.IsNaN(lh) || double.IsNaN(lv) || lh < 80 || lv < 80 || lh > 120 || lv > 120)
        {
            ShowToast("Nieprawidłowe pomiary", "Kalibracja nie została zapisana — wpisz długości miarek w milimetrach (ok. 100).", InfoBarSeverity.Warning);
            return;
        }
        s.PrintScaleX = CalibrationPage.ScaleFromMeasurement(lh);
        s.PrintScaleY = CalibrationPage.ScaleFromMeasurement(lv);
        s.CalibratePdf = pdf.IsChecked == true;
        AppInfo.SaveSettings();
        UpdatePrintInfo();
        ShowToast("Zapisano kalibrację drukarki", $"Korekta: {s.PrintCalibrationText}. Sprawdź kwadrat 1 cm na następnym wydruku.", InfoBarSeverity.Success);
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
        var composer = new PatternComposer(doc, an, AppInfo.Settings.MyThreads);
        BusyText.Text = "Drukuję…";
        try
        {
            var s = AppInfo.Settings;
            var result = await PrintService.PrintAsync(_hwnd, composer, doc.Title, s.PrintScaleX, s.PrintScaleY);
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
