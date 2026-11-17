using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Pattern;

namespace Wyszyciuszek;

/// <summary>Panel boczny: upraszczanie i wydruk.</summary>
public sealed partial class MainWindow
{
    private void SetupControls()
    {
        _loadingUi = true;
        HoopCombo.Items.Add(new ComboBoxItem { Content = "Bez tamborka", Tag = "" });
        foreach (var h in Hoops.All) HoopCombo.Items.Add(new ComboBoxItem { Content = h.Name, Tag = h.Id });
        foreach (var p in Papers.All) PaperCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        _loadingUi = false;
        LoadOptionsToUi();
        Closed += (_, _) => AppInfo.SaveSettings();
    }

    /// <summary>Ustawienia projektu (albo domyślne) → suwaki i pola.</summary>
    private void LoadOptionsToUi()
    {
        _loadingUi = true;
        try
        {
            var o = Doc?.Options ?? AppInfo.Settings.LastOptions;
            var p = Doc?.Print ?? AppInfo.Settings.LastPrint;
            ColorsSlider.Value = o.Colors;
            DetailSlider.Value = o.Detail;
            SmoothingSlider.Value = o.Smoothing;
            LinesSlider.Value = o.LineSmoothness;
            SourceCombo.SelectedIndex = o.Source == PaletteSource.MyThreads ? 1 : 0;
            int q = Array.IndexOf(SimplifyOptions.WorkingSizes, o.WorkingSize);
            QualityCombo.SelectedIndex = q >= 0 ? q : 1;
            BackgroundCheck.IsChecked = o.BackgroundAsFabric;
            UpdateSliderLabels();

            WidthBox.Value = Math.Round(p.WidthMm / 10, 1);
            HeightBox.Value = Doc is { Width: > 0 } d ? Math.Round(d.HeightMm / 10, 1) : double.NaN;
            HoopCombo.SelectedIndex = Math.Max(0, HoopCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == p.Hoop));
            PaperCombo.SelectedIndex = Math.Max(0, PaperCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == p.Paper));
            MirrorToggle.IsOn = p.Mirror;
            OutlineCombo.SelectedIndex = (int)p.Outline;
            FillCombo.SelectedIndex = (int)p.Fill;
            GrayCheck.IsChecked = p.GrayLines;
            NumbersCheck.IsChecked = p.Numbers;
            LegendCheck.IsChecked = p.Legend;
            CenterMarksCheck.IsChecked = p.CenterMarks;
            FrameCheck.IsChecked = p.Frame;
            ArrowsCheck.IsChecked = p.StitchArrows;
            TintSlider.Value = Math.Round(p.Tint * 100);
            LineWidthSlider.Value = p.LineWidthMm;
            FitHoopButton.IsEnabled = Hoops.Get(p.Hoop) is not null;

            // bez oryginału (projekt bez obrazka) nie da się uprościć od nowa
            bool canSimplify = _image is not null || _session is null;
            ColorsSlider.IsEnabled = DetailSlider.IsEnabled = SmoothingSlider.IsEnabled = SourceCombo.IsEnabled = QualityCombo.IsEnabled = canSimplify;
        }
        finally
        {
            _loadingUi = false;
        }
        UpdateBrushText();
    }

    private void UpdateSliderLabels()
    {
        // podczas InitializeComponent część pól jeszcze nie istnieje
        if (ColorsValue is not null && ColorsSlider is not null) ColorsValue.Text = ((int)ColorsSlider.Value).ToString();
        if (DetailValue is not null && DetailSlider is not null) DetailValue.Text = ((int)DetailSlider.Value).ToString();
        if (SmoothingValue is not null && SmoothingSlider is not null) SmoothingValue.Text = ((int)SmoothingSlider.Value).ToString();
        if (LinesValue is not null && LinesSlider is not null) LinesValue.Text = ((int)LinesSlider.Value).ToString();
    }

    private void RememberOptions()
    {
        if (Doc is null) return;
        AppInfo.Settings.LastOptions = Doc.Options.Clone();
        AppInfo.Settings.LastPrint = Doc.Print.Clone();
    }

    // ================================================================== upraszczanie

    private void SimplifySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ColorsValue is null) return; // jeszcze trwa InitializeComponent
        UpdateSliderLabels();
        if (_loadingUi || Doc is null) return;
        Doc.Options.Colors = (int)ColorsSlider.Value;
        Doc.Options.Detail = (int)DetailSlider.Value;
        Doc.Options.Smoothing = (int)SmoothingSlider.Value;
        RememberOptions();
        ScheduleSimplify();
    }

    private void LinesSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (LinesValue is null) return;
        UpdateSliderLabels();
        if (_loadingUi || Doc is null) return;
        Doc.Options.LineSmoothness = (int)LinesSlider.Value;
        RememberOptions();
        RequestAnalysis(); // tylko kontury — mapa kolorów zostaje
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || Doc is null) return;
        Doc.Options.Source = SourceCombo.SelectedIndex == 1 ? PaletteSource.MyThreads : PaletteSource.Automatic;
        RememberOptions();
        ScheduleSimplify();
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || Doc is null || QualityCombo.SelectedIndex < 0) return;
        Doc.Options.WorkingSize = SimplifyOptions.WorkingSizes[QualityCombo.SelectedIndex];
        RememberOptions();
        ScheduleSimplify();
    }

    /// <summary>
    /// „Tło to tkanina” — opcja zapamiętywana dla kolejnych obrazków. Na gotowym wzorze działa od razu (do cofnięcia),
    /// bez ponownego upraszczania.
    /// </summary>
    private void BackgroundCheck_Click(object sender, RoutedEventArgs e)
    {
        bool on = BackgroundCheck.IsChecked == true;
        if (Doc is null)
        {
            AppInfo.Settings.LastOptions.BackgroundAsFabric = on;
            return;
        }
        Doc.Options.BackgroundAsFabric = on;
        RememberOptions();
        if (_session is null || !Doc.HasPattern) return;
        int bg = _session.SetBackgroundFabric(on);
        if (bg < 0)
        {
            if (on) ShowToast("Nie widzę jednolitego tła", "Żaden kolor nie zajmuje większości brzegów obrazka. Tkaninę możesz wskazać ręcznie na liście nici (menu ⋯ przy nici).");
            return;
        }
        var name = Doc.Palette[bg].Name;
        ShowToast(on ? $"Tło „{name}” to teraz tkanina" : $"„{name}” znowu do szycia",
            on ? "Ten kolor nie ma numeru i nie trzeba go szyć. Cofniesz to Ctrl+Z albo odznaczając opcję." : "Kolor tła dostał z powrotem numer nici.",
            InfoBarSeverity.Success);
    }

    private async void Reroll_Click(object sender, RoutedEventArgs e)
    {
        if (Doc is null) return;
        Doc.Options.Seed++;
        await SimplifyAsync();
    }

    private void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Doc is not { HasPattern: true } d) return;
        int n = _session.RemoveSmallRegions(d.Options.MinRegionArea(d.Width, d.Height), d.Options.MinRegionRadius);
        ShowToast(n > 0 ? $"Usunięto drobne plamki: {n}" : "Nie ma drobnych plamek", n > 0 ? "Wtopiły się w sąsiednie obszary. Mniejszą szczegółowością usuniesz większe." : "");
    }

    private void SmoothEdges_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.SmoothEdges(1) == false) ShowToast("Brzegi są już równe", "");
    }

    // ================================================================== wydruk

    private void UpdatePrint(Action<PrintSettings> change)
    {
        if (_loadingUi || _session is null) return;
        _session.UpdatePrint(change);
        RememberOptions();
    }

    private void WidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingUi || Doc is not { Width: > 0 } d || double.IsNaN(args.NewValue)) return;
        double mm = Math.Clamp(args.NewValue * 10, PrintSettings.MinWidthMm, PrintSettings.MaxWidthMm);
        UpdatePrint(p => p.WidthMm = mm);
        SyncSizeBoxes(updateWidth: false);
    }

    private void HeightBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingUi || Doc is not { Width: > 0 } d || double.IsNaN(args.NewValue)) return;
        double mm = Math.Clamp(args.NewValue * 10 * d.Width / d.Height, PrintSettings.MinWidthMm, PrintSettings.MaxWidthMm);
        UpdatePrint(p => p.WidthMm = mm);
        SyncSizeBoxes(updateHeight: false);
    }

    private void SyncSizeBoxes(bool updateWidth = true, bool updateHeight = true)
    {
        if (Doc is not { Width: > 0 } d) return;
        // nie nadpisujemy pola, w którym ktoś właśnie pisze
        var focused = FocusManager.GetFocusedElement(RootHost.XamlRoot) as DependencyObject;
        bool InBox(NumberBox box) => focused is not null && IsInside(focused, box);
        _loadingUi = true;
        if (updateWidth && !InBox(WidthBox)) WidthBox.Value = Math.Round(d.Print.WidthMm / 10, 1);
        if (updateHeight && !InBox(HeightBox)) HeightBox.Value = Math.Round(d.HeightMm / 10, 1);
        _loadingUi = false;
        UpdateStats();
        UpdateBrushText();
    }

    private void HoopCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var id = (HoopCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FitHoopButton.IsEnabled = Hoops.Get(id) is not null;
        UpdatePrint(p => p.Hoop = id);
        View.Invalidate();
    }

    private void FitHoop_Click(object sender, RoutedEventArgs e)
    {
        if (Doc is not { Width: > 0 } d || Hoops.Get(d.Print.Hoop) is not { } hoop) return;
        double mm = Math.Floor(hoop.MaxWidthFor(d.Height / (double)d.Width) / 5) * 5; // równe pół centymetra
        UpdatePrint(p => p.WidthMm = Math.Max(PrintSettings.MinWidthMm, mm));
        SyncSizeBoxes();
    }

    private void PaperCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var id = (PaperCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "A4";
        UpdatePrint(p => p.Paper = id);
    }

    private void PrintToggle_Toggled(object sender, RoutedEventArgs e) => UpdatePrint(p => p.Mirror = MirrorToggle.IsOn);

    private void PrintCheck_Changed(object sender, RoutedEventArgs e) => UpdatePrint(p =>
    {
        p.Numbers = NumbersCheck.IsChecked == true;
        p.Legend = LegendCheck.IsChecked == true;
        p.CenterMarks = CenterMarksCheck.IsChecked == true;
        p.Frame = FrameCheck.IsChecked == true;
        p.StitchArrows = ArrowsCheck.IsChecked == true;
        p.GrayLines = GrayCheck.IsChecked == true;
    });

    private void PrintSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TintSlider is null || LineWidthSlider is null) return;
        UpdatePrint(p =>
        {
            p.Tint = TintSlider.Value / 100;
            p.LineWidthMm = LineWidthSlider.Value;
        });
    }

    private void UpdatePrintInfo()
    {
        if (Doc is not { HasPattern: true } d || _analysis is null || _analysis.Width != d.Width || _analysis.Height != d.Height)
        {
            PrintInfo.Text = Doc is null ? "" : "Wzór się przygotowuje…";
            return;
        }
        var composer = new PatternComposer(d, _analysis);
        var l = composer.Layout;
        var lines = new List<string>();
        int n = l.Tiles.Count;
        var sheets = n == 1 ? "1 kartka" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? $"{n} kartki" : $"{n} kartek";
        lines.Add($"Wydruk: {sheets} {l.Paper.Id}{(l.Landscape ? " poziomo" : "")}"
                  + (n > 1 ? $" ({l.Columns}×{l.Rows}) do sklejenia" : "")
                  + (l.LegendPage ? " + legenda" : ""));
        if (Hoops.Get(d.Print.Hoop) is { } hoop)
        {
            lines.Add(hoop.Fits(d.Print.WidthMm, d.HeightMm)
                ? $"✓ Mieści się w tamborku ({hoop.Name.ToLowerInvariant()})."
                : $"⚠ Za duży na tamborek ({hoop.Name.ToLowerInvariant()}) — zmniejsz albo kliknij dopasowanie.");
        }
        if (d.Print.Numbers)
        {
            int un = composer.CountUnnumbered(composer.PrintStyle());
            if (un > 0) lines.Add($"{un} małych obszarów bez numeru — powiększ wzór albo zmniejsz szczegóły.");
        }
        var perColor = ThreadEstimator.PerColor(d, _analysis);
        var usage = ThreadEstimator.Total(perColor);
        if (usage.Meters > 0)
            lines.Add($"Nici ok. {ThreadEstimator.Meters(usage.Meters)} · szycie ok. {ThreadEstimator.Duration(usage.Minutes)} (orientacyjnie, {ThreadEstimator.Describe(d.Print.Fill)}).");
        var shortages = ThreadStock.Check(d, perColor, AppInfo.Settings.MyThreads).Where(c => c.Short).ToList();
        if (shortages.Count > 0)
            lines.Add("⚠ Zabraknie nici z pudełka: " + string.Join(", ", shortages.Select(c => $"{c.Name} (ok. {ThreadEstimator.Meters(c.Missing)})")) + ".");
        var weak = Fabrics.LowContrast(d);
        if (weak.Count > 0)
            lines.Add("⚠ Słabo widać na tkaninie: " + string.Join(", ", weak.Select(i => d.Palette[i].Name)) + " — wybierz ciemniejszą albo jaśniejszą nić.");
        lines.Add($"1 piksel obrazu roboczego = {d.MmPerPixel:0.00} mm na tkaninie.");
        PrintInfo.Text = string.Join("\n", lines);
        CalibrationLinkText.Text = AppInfo.Settings.HasPrintCalibration
            ? $"Kalibracja drukarki: {AppInfo.Settings.PrintCalibrationText}…"
            : "Kalibracja drukarki…";
        UpdateNarrowPanel(d, _analysis);
        SyncSizeBoxes();
    }

    private void FillCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FillCombo.SelectedIndex < 0) return;
        var fill = (FillDensity)FillCombo.SelectedIndex;
        UpdatePrint(p => p.Fill = fill);
        UpdatePaletteRows(); // metry przy niciach
    }

    private void OutlineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineCombo.SelectedIndex < 0) return;
        var style = (OutlineStyle)OutlineCombo.SelectedIndex;
        UpdatePrint(p => p.Outline = style);
    }

    // ================================================================== za wąskie na wypełnienie

    private List<NarrowRegion> _narrow = [];

    private void UpdateNarrowPanel(PatternDocument d, Core.Vector.PatternAnalysis an)
    {
        _narrow = NarrowRegions.Find(d, an);
        if (_narrow.Count == 0)
        {
            NarrowPanel.Visibility = Visibility.Collapsed;
            NarrowShowToggle.IsChecked = false;
            View.WarningRegions = null;
            return;
        }
        int stripes = _narrow.Count(n => n.Stripe);
        NarrowText.Text = $"⚠ {Plural(_narrow.Count, "obszar", "obszary", "obszarów")} węższe niż {NarrowRegions.DefaultMinWidthMm:0} mm " +
                          "— trudno je wypełnić ściegiem. Przeszyj je linią, powiększ wzór albo zmniejsz szczegóły.";
        NarrowConvertButton.Content = stripes > 0 ? $"Paski na linie ({stripes})" : "Paski na linie";
        NarrowConvertButton.IsEnabled = stripes > 0;
        NarrowPanel.Visibility = Visibility.Visible;
        View.WarningRegions = NarrowShowToggle.IsChecked == true ? _narrow.Select(n => n.Region).ToList() : null;
    }

    private static string Plural(int n, string one, string few, string many) =>
        $"{n} " + (n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many);

    private void NarrowShow_Click(object sender, RoutedEventArgs e)
    {
        bool show = NarrowShowToggle.IsChecked == true;
        View.WarningRegions = show ? _narrow.Select(n => n.Region).ToList() : null;
        if (show && View.Mode is ViewMode.Original or ViewMode.Overlay) SetViewMode(ViewMode.Colors);
    }

    private void NarrowConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !AnalysisIsCurrent) return;
        var stripes = _narrow.Where(n => n.Stripe).Select(n => n.Region).ToList();
        int made = _session.ConvertRegionsToLines(_analysis!.Components, stripes);
        if (made > 0)
        {
            _editedSinceSimplify = true;
            ShowToast($"Zamieniono na linie: {made}", "Wąskie paski są teraz liniami detali w swoich kolorach. Cofniesz to Ctrl+Z.", InfoBarSeverity.Success);
        }
        else
        {
            ShowToast("Nic do zamiany", "Nie znalazłem wąskich pasków, które dałoby się zamienić na linię.");
        }
    }

    /// <summary>Shift+L: zaznaczony obszar → linia detalu.</summary>
    private void ConvertSelectedToLine()
    {
        int r = View.SelectedRegion;
        if (_session is null || !AnalysisIsCurrent || r < 0 || r >= _analysis!.RegionCount)
        {
            ShowToast("Zaznacz obszar", "Kliknij obszar strzałką (V), a potem Shift+L zamieni go na linię detalu.");
            return;
        }
        if (_session.ConvertRegionsToLines(_analysis.Components, [r]) > 0)
        {
            View.SelectedRegion = -1;
            _editedSinceSimplify = true;
            ShowToast("Obszar zamieniony na linię", "Jego środkiem biegnie teraz linia detalu w tym samym kolorze. Cofniesz to Ctrl+Z.", InfoBarSeverity.Success);
        }
    }
}
