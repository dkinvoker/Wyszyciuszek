using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Project;
using Color = Windows.UI.Color;

namespace Wyszyciuszek;

/// <summary>„Moje nici” — nici, które użytkownik ma w pudełku (wzór może używać tylko ich).</summary>
public sealed partial class MainWindow
{
    private async void MyThreads_Click(object sender, RoutedEventArgs e)
    {
        var list = AppInfo.Settings.MyThreads.Select(t => new SavedThread { Name = t.Name, Color = t.Color }).ToList();
        var rows = new StackPanel { Spacing = 4 };
        var empty = new TextBlock { Style = (Style)Application.Current.Resources["HintTextStyle"], Text = "Pudełko jest puste — dodaj pierwszą nić przyciskami powyżej." };

        void Rebuild()
        {
            rows.Children.Clear();
            empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in list) rows.Children.Add(ThreadRow(t, list, Rebuild));
        }

        void Add(string name, Rgb color)
        {
            if (list.Any(t => Rgb.TryParse(t.Color, out var c) && c == color && t.Name == name)) return;
            list.Add(new SavedThread { Name = name, Color = color.Hex });
            Rebuild();
        }

        var addNew = new Button { Content = "Nowa nić" };
        addNew.Click += (_, _) => Add($"Nić {list.Count + 1}", new Rgb(200, 60, 60));

        var catalog = new Button { Content = "Z katalogu…" };
        catalog.Flyout = new Flyout
        {
            Content = SwatchGrid(ThreadCatalog.All, t => Add(t.Name, t.Color)),
            Placement = FlyoutPlacementMode.Bottom,
        };

        var fromProject = new Button { Content = "Kolory z tego wzoru", IsEnabled = Doc?.Palette.Count > 0 };
        fromProject.Click += (_, _) =>
        {
            foreach (var p in Doc?.Palette ?? []) if (!p.IsFabric) Add(p.Name, p.Color);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(addNew);
        buttons.Children.Add(catalog);
        buttons.Children.Add(fromProject);

        var content = new StackPanel { Spacing = 12, Width = 460 };
        content.Children.Add(new TextBlock
        {
            Style = (Style)Application.Current.Resources["HintTextStyle"],
            Text = "Wpisz nici, które masz w pudełku (nazwa może zawierać numer producenta). Potem w panelu „Upraszczanie” wybierz " +
                   "„Kolory: z moich nici” — wzór użyje tylko tych kolorów. Możesz też zamienić kolory gotowego wzoru na najbliższe nici z pudełka.",
        });
        content.Children.Add(buttons);
        content.Children.Add(empty);
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 380, Padding = new Thickness(0, 0, 14, 0) });
        Rebuild();

        var dlg = new ContentDialog
        {
            Title = "Moje nici",
            Content = content,
            PrimaryButtonText = "Zapisz",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return;

        AppInfo.Settings.MyThreads = list.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList();
        AppInfo.SaveSettings();
        RebuildPalette(); // próbniki kolorów pokazują „moje nici”
        if (Doc?.Options.Source == PaletteSource.MyThreads) ScheduleSimplify();
        ShowToast("Zapisano nici", $"W pudełku: {AppInfo.Settings.MyThreads.Count}", InfoBarSeverity.Success);
    }

    private Grid ThreadRow(SavedThread thread, List<SavedThread> list, Action rebuild)
    {
        var color = Rgb.TryParse(thread.Color, out var c) ? c : Rgb.Black;
        var fill = new Border { CornerRadius = new CornerRadius(4), Background = Brush(color), BorderBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), BorderThickness = new Thickness(1) };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Box, Color = Color.FromArgb(255, color.R, color.G, color.B),
        };
        picker.ColorChanged += (_, e) =>
        {
            var rgb = new Rgb(e.NewColor.R, e.NewColor.G, e.NewColor.B);
            thread.Color = rgb.Hex;
            fill.Background = Brush(rgb);
        };
        var swatch = new Button
        {
            Width = 44, Height = 30, Padding = new Thickness(3), MinWidth = 0, MinHeight = 0, Content = fill,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Flyout = new Flyout { Content = picker, Placement = FlyoutPlacementMode.Left },
        };
        var name = new TextBox { Text = thread.Name, PlaceholderText = "np. Czerwień 321" };
        name.TextChanged += (_, _) => thread.Name = name.Text;
        var delete = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(delete, "Usuń z pudełka");
        delete.Click += (_, _) =>
        {
            list.Remove(thread);
            rebuild();
        };

        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(swatch);
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        Grid.SetColumn(delete, 2);
        g.Children.Add(delete);
        return g;
    }
}
