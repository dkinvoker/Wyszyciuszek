using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Color = Windows.UI.Color;

namespace Wyszyciuszek;

/// <summary>Kolor tkaniny: gotowe materiały, własny kolor, „bez koloru”.</summary>
public sealed partial class MainWindow
{

    private void UpdateFabricButton()
    {
        if (Doc?.FabricColor is { } f)
        {
            FabricSwatch.Background = Brush(f);
            FabricText.Text = Fabrics.NameFor(f);
        }
        else
        {
            FabricSwatch.Background = new SolidColorBrush(Colors.Transparent);
            FabricText.Text = "nie wybrano — kolory jak na obrazku";
        }
    }

    private void FabricFlyout_Opening(object? sender, object e)
    {
        var panel = new StackPanel { Spacing = 10, Width = 300 };
        panel.Children.Add(new TextBlock { Text = "MATERIAŁ", Style = (Style)Application.Current.Resources["SectionTitleStyle"] });
        panel.Children.Add(new TextBlock
        {
            Style = (Style)Application.Current.Resources["HintTextStyle"], FontSize = 12,
            Text = "Podgląd pokaże nici na tym tle, a nici, które się z nim zlewają, dostaną ostrzeżenie. " +
                   "Kolor wzoru prawie taki jak materiał zostanie oznaczony jako tkanina.",
        });

        var grid = new Grid { RowSpacing = 6, ColumnSpacing = 6 };
        const int Cols = 7;
        for (int i = 0; i < Cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (int i = 0; i < Fabrics.All.Count; i++)
        {
            var f = Fabrics.All[i];
            var b = new Button
            {
                Width = 34, Height = 26, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, Background = Brush(f.Color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            };
            ToolTipService.SetToolTip(b, f.Name);
            b.Click += (_, _) => ApplyFabric(f.Color);
            if (i / Cols >= grid.RowDefinitions.Count) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(b, i / Cols);
            Grid.SetColumn(b, i % Cols);
            grid.Children.Add(b);
        }
        panel.Children.Add(grid);

        var current = Doc?.FabricColor ?? Fabrics.All[0].Color;
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Box, IsColorPreviewVisible = false,
            Color = Color.FromArgb(255, current.R, current.G, current.B),
        };
        picker.ColorChanged += (_, args) =>
        {
            ApplyFabric(new Rgb(args.NewColor.R, args.NewColor.G, args.NewColor.B), quiet: true);
        };
        var custom = new Expander { Header = "Własny kolor", HorizontalAlignment = HorizontalAlignment.Stretch, Content = picker };
        panel.Children.Add(custom);

        var none = new Button { Content = "Bez koloru (jak na obrazku)", HorizontalAlignment = HorizontalAlignment.Stretch };
        none.Click += (_, _) =>
        {
            ApplyFabric(null);
            FabricFlyout.Hide();
        };
        panel.Children.Add(none);
        FabricFlyout.Content = new ScrollViewer { Content = panel, MaxHeight = 640, Padding = new Thickness(0, 0, 12, 0) };
    }

    /// <summary>Nowy kolor tkaniny (do cofnięcia); kolor wzoru prawie taki jak materiał zostaje tkaniną.</summary>
    private void ApplyFabric(Rgb? color, bool quiet = false)
    {
        if (_session is null) return;
        int marked = _session.SetFabricColor(color);
        UpdateFabricButton();
        if (quiet) return;
        if (marked >= 0)
            ShowToast("Tkanina ustawiona", $"Kolor „{Doc!.Palette[marked].Name}” jest prawie taki jak materiał — oznaczyłem go jako tkaninę (nie szyjemy go).", InfoBarSeverity.Success);
        else if (color is not null && Doc is { } d && Fabrics.LowContrast(d) is { Count: > 0 } weak)
            ShowToast("Uwaga na kontrast", "Słabo widać na tym materiale: " + string.Join(", ", weak.Select(i => d.Palette[i].Name)) + ".", InfoBarSeverity.Warning);
    }
}
