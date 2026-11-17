using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Color = Windows.UI.Color;

namespace Wyszyciuszek;

/// <summary>Lista nici (paleta): numer = kolejność szycia, próbka z próbnikiem, nazwa, udział w wzorze, menu.</summary>
public sealed partial class MainWindow
{
    private sealed class PaletteRow
    {
        public required Border Root;
        public required Border Badge;
        public required TextBlock Number;
        public required Border SwatchFill;
        public required TextBox Name;
        public required TextBlock Stats;
        public required ColorPicker Picker;
        public required CheckBox Fabric;
        public bool PickerOpen;
        public int Index;
    }

    private readonly List<PaletteRow> _rows = [];
    private bool _updatingRows;

    private static SolidColorBrush Brush(Rgb c) => new(Color.FromArgb(255, c.R, c.G, c.B));
    private static SolidColorBrush Res(string key) => (SolidColorBrush)Application.Current.Resources[key];

    private void RebuildPalette()
    {
        PaletteList.Children.Clear();
        _rows.Clear();
        if (Doc is null) return;
        for (int i = 0; i < Doc.Palette.Count; i++)
        {
            var row = CreateRow(i);
            _rows.Add(row);
            PaletteList.Children.Add(row.Root);
        }
        UpdatePaletteRows();
    }

    private PaletteRow CreateRow(int index)
    {
        var number = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = number, VerticalAlignment = VerticalAlignment.Center };

        var swatchFill = new Border { CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), BorderThickness = new Thickness(1) };
        var swatch = new Button
        {
            Width = 42, Height = 30, Padding = new Thickness(3), MinWidth = 0, MinHeight = 0, Content = swatchFill,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(swatch, "Zmień kolor nici");

        var name = new TextBox { FontSize = 13, MinHeight = 30, Padding = new Thickness(8, 4, 6, 4), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
        ToolTipService.SetToolTip(name, "Nazwa nici (np. numer z katalogu producenta) — trafi do legendy");

        var stats = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("HaftTextDimBrush"), MinWidth = 34, TextAlignment = TextAlignment.Right };
        var more = new Button { Width = 28, Height = 28, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Content = new FontIcon { Glyph = "", FontSize = 13 } };
        ToolTipService.SetToolTip(more, "Więcej: połącz, przesuń, tkanina, usuń");

        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(badge);
        Grid.SetColumn(swatch, 1); grid.Children.Add(swatch);
        Grid.SetColumn(name, 2); grid.Children.Add(name);
        Grid.SetColumn(stats, 3); grid.Children.Add(stats);
        Grid.SetColumn(more, 4); grid.Children.Add(more);

        var root = new Border
        {
            Child = grid, Padding = new Thickness(4), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = Res("HaftBg1Brush"), BorderBrush = new SolidColorBrush(Colors.Transparent),
        };

        // próbnik koloru z paletą nici
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true, ColorSpectrumShape = ColorSpectrumShape.Box, IsColorPreviewVisible = false,
        };
        var fabric = new CheckBox { Content = "Tkanina — tego koloru nie szyję" };
        var row = new PaletteRow { Root = root, Badge = badge, Number = number, SwatchFill = swatchFill, Name = name, Stats = stats, Picker = picker, Fabric = fabric, Index = index };

        picker.ColorChanged += (_, e) =>
        {
            if (_updatingRows || _session is null) return;
            _session.SetColor(row.Index, new Rgb(e.NewColor.R, e.NewColor.G, e.NewColor.B));
        };
        fabric.Click += (_, _) => _session?.SetFabric(row.Index, fabric.IsChecked == true);

        var flyoutPanel = new StackPanel { Spacing = 10, Width = 320 };
        if (AppInfo.Settings.MyThreads.Count > 0)
        {
            flyoutPanel.Children.Add(new TextBlock { Text = "MOJE NICI", Style = (Style)Application.Current.Resources["SectionTitleStyle"] });
            flyoutPanel.Children.Add(SwatchGrid(AppInfo.Settings.Threads, t => ApplyThread(row.Index, t)));
        }
        flyoutPanel.Children.Add(new TextBlock { Text = "KATALOG KOLORÓW", Style = (Style)Application.Current.Resources["SectionTitleStyle"] });
        flyoutPanel.Children.Add(SwatchGrid(ThreadCatalog.All, t => ApplyThread(row.Index, t)));
        flyoutPanel.Children.Add(picker);
        flyoutPanel.Children.Add(fabric);
        var flyout = new Flyout { Content = new ScrollViewer { Content = flyoutPanel, MaxHeight = 640, Padding = new Thickness(0, 0, 12, 0) }, Placement = FlyoutPlacementMode.Left };
        flyout.Opening += (_, _) =>
        {
            row.PickerOpen = true;
            _updatingRows = true;
            if (Doc is not null && row.Index < Doc.Palette.Count)
            {
                var c = Doc.Palette[row.Index].Color;
                picker.Color = Color.FromArgb(255, c.R, c.G, c.B);
                fabric.IsChecked = Doc.Palette[row.Index].IsFabric;
            }
            _updatingRows = false;
        };
        flyout.Closed += (_, _) => row.PickerOpen = false;
        swatch.Flyout = flyout;

        name.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                _session?.Rename(row.Index, name.Text);
                View.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        };
        name.LostFocus += (_, _) => _session?.Rename(row.Index, name.Text);

        more.Flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        ((MenuFlyout)more.Flyout).Opening += (s, _) => FillRowMenu((MenuFlyout)s!, row.Index);

        root.Tapped += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, name)) return;
            // zaznaczona linia detalu dostaje klikniętą nić
            if (View.SelectedLine >= 0 && _session is not null && _session.SetLineColor(View.SelectedLine, row.Index)) UpdateHint();
            SetPaintColor(row.Index);
        };
        return row;
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, container)) return true;
        return false;
    }

    private Grid SwatchGrid(IReadOnlyList<ThreadColor> threads, Action<ThreadColor> pick)
    {
        const int Cols = 10;
        var g = new Grid { RowSpacing = 4, ColumnSpacing = 4 };
        for (int i = 0; i < Cols; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (int i = 0; i < threads.Count; i++)
        {
            var t = threads[i];
            var b = new Button
            {
                Width = 26, Height = 22, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, Background = Brush(t.Color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            };
            ToolTipService.SetToolTip(b, $"{t.Name} {t.Color.Hex}");
            b.Click += (_, _) => pick(t);
            if (i / Cols >= g.RowDefinitions.Count) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(b, i / Cols);
            Grid.SetColumn(b, i % Cols);
            g.Children.Add(b);
        }
        return g;
    }

    private void ApplyThread(int index, ThreadColor thread)
    {
        if (_session is null) return;
        _session.SetColor(index, thread.Color);
        _session.Rename(index, thread.Name);
        if (index < _rows.Count)
        {
            _updatingRows = true;
            _rows[index].Picker.Color = Color.FromArgb(255, thread.Color.R, thread.Color.G, thread.Color.B);
            _updatingRows = false;
        }
    }

    private void FillRowMenu(MenuFlyout menu, int index)
    {
        menu.Items.Clear();
        if (Doc is null || index >= Doc.Palette.Count) return;
        var palette = Doc.Palette;

        var paint = new MenuFlyoutItem { Text = "Maluj tą nicią", Icon = new FontIcon { Glyph = "" } };
        paint.Click += (_, _) => { SetPaintColor(index); SetTool(EditTool.Brush); };
        menu.Items.Add(paint);

        var merge = new MenuFlyoutSubItem { Text = "Połącz z…", Icon = new FontIcon { Glyph = "" } };
        for (int j = 0; j < palette.Count; j++)
        {
            if (j == index) continue;
            int target = j;
            var item = new MenuFlyoutItem { Text = $"{Doc.ThreadLabel(j)}. {palette[j].Name}" };
            item.Click += (_, _) =>
            {
                if (_session?.MergeColor(index, target) == true) _editedSinceSimplify = true;
            };
            merge.Items.Add(item);
        }
        merge.IsEnabled = palette.Count > 1;
        menu.Items.Add(merge);
        menu.Items.Add(new MenuFlyoutSeparator());

        var up = new MenuFlyoutItem { Text = "Szyj wcześniej (w górę)", Icon = new FontIcon { Glyph = "" }, IsEnabled = index > 0 };
        up.Click += (_, _) => { if (_session?.MoveColor(index, index - 1) == true) FollowPaint(index, index - 1); };
        menu.Items.Add(up);
        var down = new MenuFlyoutItem { Text = "Szyj później (w dół)", Icon = new FontIcon { Glyph = "" }, IsEnabled = index < palette.Count - 1 };
        down.Click += (_, _) => { if (_session?.MoveColor(index, index + 1) == true) FollowPaint(index, index + 1); };
        menu.Items.Add(down);

        var fabric = new ToggleMenuFlyoutItem { Text = "Tkanina — nie szyję", IsChecked = palette[index].IsFabric };
        fabric.Click += (_, _) => _session?.SetFabric(index, fabric.IsChecked);
        menu.Items.Add(fabric);
        menu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Usuń (zastąp najbliższą nicią)", Icon = new FontIcon { Glyph = "" }, IsEnabled = palette.Count > 1 };
        delete.Click += (_, _) =>
        {
            if (_session?.DeleteColor(index) == true) _editedSinceSimplify = true;
        };
        menu.Items.Add(delete);
    }

    /// <summary>Po przesunięciu nici wybrany kolor pędzla „jedzie” razem z nią.</summary>
    private void FollowPaint(int from, int to)
    {
        if (_paintColor == from) SetPaintColor(to);
        else if (_paintColor == to) SetPaintColor(from);
        else HighlightPaletteRow();
    }

    private void UpdatePaletteRows()
    {
        if (Doc is null) return;
        if (_rows.Count != Doc.Palette.Count)
        {
            RebuildPalette();
            return;
        }
        _updatingRows = true;
        try
        {
            var stats = AnalysisIsCurrent ? _analysis : null;
            var numbers = Doc.ThreadNumbers();
            var usage = stats is null ? null : ThreadEstimator.PerColor(Doc, stats);
            var stock = usage is null ? new Dictionary<int, StockCheck>() : ThreadStock.Check(Doc, usage, AppInfo.Settings.MyThreads).ToDictionary(c => c.Color);
            var weak = Fabrics.LowContrast(Doc).ToHashSet();
            double total = Math.Max(1, Doc.Width * (double)Doc.Height);
            for (int i = 0; i < _rows.Count; i++)
            {
                var e = Doc.Palette[i];
                var row = _rows[i];
                row.Index = i;
                row.Number.Text = e.IsFabric ? "T" : numbers[i].ToString();
                row.Badge.Background = e.IsFabric ? Res("HaftBg3Brush") : Res("HaftLinenBrush");
                row.Number.Foreground = e.IsFabric ? Res("HaftTextDimBrush") : new SolidColorBrush(Color.FromArgb(255, 21, 23, 26));
                ToolTipService.SetToolTip(row.Badge, e.IsFabric ? "Tkanina — nie szyjemy (bez numeru)" : $"Szyjesz jako {numbers[i]}.");
                row.SwatchFill.Background = Brush(Doc.DisplayColor(i));
                if (row.PickerOpen)
                {
                    var c = Color.FromArgb(255, e.Color.R, e.Color.G, e.Color.B);
                    if (row.Picker.Color != c) row.Picker.Color = c;
                }
                if (!ReferenceEquals(FocusManager.GetFocusedElement(RootHost.XamlRoot), row.Name) && row.Name.Text != e.Name) row.Name.Text = e.Name;
                row.Name.Opacity = e.IsFabric ? 0.6 : 1;
                if (stats is not null && i < stats.ColorArea.Length)
                {
                    double share = stats.ColorArea[i] / total;
                    bool isShort = stock.TryGetValue(i, out var sc) && sc.Short;
                    bool isWeak = weak.Contains(i);
                    var shareText = share < 0.001 && stats.ColorArea[i] > 0 ? "<0,1%" : $"{share * 100:0.#}%";
                    row.Stats.Text = isShort || isWeak ? "⚠ " + shareText : shareText;
                    row.Stats.Foreground = isShort || isWeak ? Res("HaftThreadBrush") : Res("HaftTextDimBrush");
                    double cm2 = stats.ColorArea[i] * Doc.MmPerPixel * Doc.MmPerPixel / 100;
                    var thread = usage is not null && usage[i].Meters > 0 ? $" · nici ok. {ThreadEstimator.Meters(usage[i].Meters)}" : "";
                    var lines = Doc.Lines.Count(l => l.Color == i) is var ln and > 0 ? $" · linie detali: {ln}" : "";
                    var spool = stock.ContainsKey(i)
                        ? sc.Short
                            ? $"\nNa szpulce {ThreadEstimator.Meters(sc.OnSpool)} — zabraknie ok. {ThreadEstimator.Meters(sc.Missing)}."
                            : $"\nNa szpulce {ThreadEstimator.Meters(sc.OnSpool)} — wystarczy."
                        : "";
                    var contrast = isWeak ? "\nSłabo widać ją na tej tkaninie." : "";
                    ToolTipService.SetToolTip(row.Stats, $"{stats.ColorRegionCount[i]} obszarów · ok. {cm2:0.#} cm²{lines}{thread}{spool}{contrast}");
                }
            }
            int stitched = Doc.Palette.Count(p => !p.IsFabric);
            PaletteSummary.Text = Doc.Palette.Count == 0 ? "" : stitched == Doc.Palette.Count ? $"{stitched}" : $"{stitched} + tkanina";
            UpdateFabricButton();
        }
        finally
        {
            _updatingRows = false;
        }
        HighlightPaletteRow();
    }

    private void HighlightPaletteRow()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            bool sel = i == _paintColor;
            _rows[i].Root.BorderBrush = sel ? Res("HaftThreadBrush") : new SolidColorBrush(Colors.Transparent);
            _rows[i].Root.Background = sel ? Res("HaftBg3Brush") : Res("HaftBg1Brush");
        }
    }

    // ================================================================== przyciski listy nici

    private void AddColor_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Doc is null) return;
        // pierwszy kolor z katalogu wyraźnie inny niż te, które już są
        var candidate = ThreadCatalog.All.FirstOrDefault(t => Doc.Palette.All(p => ColorMath.DeltaE2000(p.Color, t.Color) > 12)) ?? ThreadCatalog.All[0];
        int index = _session.AddColor(candidate.Color, candidate.Name);
        if (index < 0) return;
        SetPaintColor(index);
        SetTool(EditTool.Brush);
        ShowToast("Nowa nić", "Maluj nią pędzlem albo kliknij obszar wiaderkiem. Kolor zmienisz klikając próbkę.");
    }

    private void SortLayered_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_session.SortPalette(PaletteOrder.Layered))
            ShowToast("Nowa kolejność szycia", "Najpierw tło i szerokie plamy, potem to, co na nich leży, na końcu cienkie detale.", InfoBarSeverity.Success);
        else
            ShowToast("Kolejność już jest od spodu do wierzchu", "");
    }

    private void SortByArea_Click(object sender, RoutedEventArgs e) => _session?.SortPalette(byLightness: false);

    private void SortByLight_Click(object sender, RoutedEventArgs e) => _session?.SortPalette(byLightness: true);

    private void MatchThreads_Click(object sender, RoutedEventArgs e)
    {
        var threads = AppInfo.Settings.Threads;
        if (threads.Count == 0)
        {
            ShowToast("Brak nici w pudełku", "Najpierw dodaj swoje nici w „Moje nici”.", InfoBarSeverity.Warning);
            return;
        }
        _session?.MatchToThreads(threads);
    }

    private void RemoveUnused_Click(object sender, RoutedEventArgs e)
    {
        int n = _session?.RemoveUnusedColors() ?? 0;
        ShowToast(n > 0 ? $"Usunięto nieużywane kolory: {n}" : "Wszystkie kolory są używane", "");
    }
}
