using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Project;
using Wyszyciuszek.Core.Vector;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wyszyciuszek;

public sealed partial class MainWindow : Window
{
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _simplifyTimer;
    private readonly DispatcherQueueTimer _toastTimer;

    private EditorSession? _session;
    private LoadedImage? _image;
    private ImageRgb? _working;
    private int _workingSize;

    private PatternAnalysis? _analysis;
    private byte[]? _analysisLabels;
    private int _analysisVersion, _simplifyVersion;
    private CancellationTokenSource? _analysisCts, _simplifyCts;
    private bool _simplifying, _analyzing;

    private int _paintColor;
    private bool _editedSinceSimplify;
    private bool _loadingUi;
    private bool _dialogOpen;
    private bool _closeConfirmed;

    private PatternDocument? Doc => _session?.Document;

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        _simplifyTimer = DispatcherQueue.CreateTimer();
        _simplifyTimer.Interval = TimeSpan.FromMilliseconds(350);
        _simplifyTimer.Tick += (t, _) => { t.Stop(); _ = SimplifyAsync(); };
        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(6);
        _toastTimer.Tick += (t, _) => { t.Stop(); Toast.IsOpen = false; };

        SetupWindow();
        SetupControls();
        SetupView();
        SetupUpdates();

        VersionText.Text = AppInfo.Version;
        UpdateAll();
        ShowRecentFiles();

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
            DispatcherQueue.TryEnqueue(async () => await OpenPathAsync(args[1]));
    }

    // ================================================================== okno

    private void SetupWindow()
    {
        Title = AppInfo.Name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var tb = AppWindow.TitleBar;
        tb.PreferredHeightOption = TitleBarHeightOption.Tall;
        var text = Windows.UI.Color.FromArgb(255, 238, 231, 216);
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = text;
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 120, 115, 105);
        tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 47, 53, 59);
        tb.ButtonHoverForegroundColor = text;
        tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 226, 87, 76);
        tb.ButtonPressedForegroundColor = Colors.White;

        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch { }

        double scale = Math.Max(1, Native.GetDpiForWindow(_hwnd)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(1440 * scale), area.Width - 40);
        int h = Math.Min((int)(900 * scale), area.Height - 40);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = (int)(980 * scale);
            op.PreferredMinimumHeight = (int)(620 * scale);
        }

        AppWindow.Closing += OnAppWindowClosing;
        Closed += (_, _) => View.Shutdown();
        RootHost.Loaded += (_, _) => UpdateCaptionInset();
        AppWindow.Changed += (_, _) => UpdateCaptionInset();
        RootHost.KeyDown += Root_KeyDown;
    }

    private void UpdateCaptionInset()
    {
        var xr = RootHost.XamlRoot;
        if (xr is null) return;
        double inset = AppWindow.TitleBar.RightInset / xr.RasterizationScale;
        if (inset > 0) CaptionColumn.Width = new GridLength(inset + 8);
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || _session is null || !_session.IsDirty) return;
        args.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ================================================================== płótno

    private void SetupView()
    {
        View.ZoomChanged += () => ZoomText.Content = $"{View.ZoomPercent:0}%";
        View.FilesDropped += async paths => await OpenPathAsync(paths[0]);
        View.HoverChanged += _ => UpdateHint();
        View.ColorPicked += c =>
        {
            if (Doc is null || c >= Doc.Palette.Count) return;
            SetPaintColor(c);
            if (View.Tool == EditTool.Picker) SetTool(EditTool.Brush);
        };
        View.RegionSelected += r =>
        {
            if (!AnalysisIsCurrent || r < 0) { UpdateHint(); return; }
            SetPaintColor(_analysis!.Regions[r].Color);
            UpdateHint();
        };
        View.FillRequested += r =>
        {
            if (_session is null || !AnalysisIsCurrent || _paintColor >= Doc!.Palette.Count) return;
            if (_session.FillRegion(_analysis!.Components, r, _paintColor)) _editedSinceSimplify = true;
        };
        View.StrokeFinished += (points, radius, color) =>
        {
            if (_session is null || color >= Doc!.Palette.Count || !_session.PaintStroke(points, radius, color))
            {
                View.ClearStrokePreview();
                return;
            }
            _editedSinceSimplify = true;
        };
    }

    /// <summary>Czy kontury liczono z obecnej mapy kolorów (inaczej kliknięcia trafiłyby w nieaktualne obszary).</summary>
    private bool AnalysisIsCurrent => _analysis is not null && Doc is not null && ReferenceEquals(_analysisLabels, Doc.Labels);

    // ================================================================== sesja

    private void StartSession(PatternDocument doc, LoadedImage? image, string? filePath)
    {
        _simplifyCts?.Cancel();
        _analysisCts?.Cancel();
        _simplifyTimer.Stop();
        if (!ReferenceEquals(_image, image)) _image?.Dispose();
        _image = image;
        _working = null;
        _workingSize = 0;
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session = new EditorSession(doc) { FilePath = filePath };
        _session.Changed += OnSessionChanged;
        _analysis = null;
        _analysisLabels = null;
        _paintColor = 0;
        _editedSinceSimplify = false;

        View.SetDocument(doc, image?.Bitmap);
        View.SetAnalysis(null);
        EmptyState.Visibility = Visibility.Collapsed;
        View.Focus(FocusState.Programmatic); // skróty od razu działają, bez ramki fokusu na przycisku
        LoadOptionsToUi();
        RebuildPalette();
        UpdateAll();
        if (doc.HasPattern) RequestAnalysis();
    }

    private void OnSessionChanged(ChangeKind kind)
    {
        if (kind.HasFlag(ChangeKind.Labels))
        {
            View.CheckSize();
            RequestAnalysis();
        }
        if (kind.HasFlag(ChangeKind.PaletteStructure)) RebuildPalette();
        else if (kind.HasFlag(ChangeKind.PaletteColors)) UpdatePaletteRows();
        if (Doc is not null && _paintColor >= Doc.Palette.Count) _paintColor = Math.Max(0, Doc.Palette.Count - 1);
        if (kind.HasFlag(ChangeKind.Print)) View.RefreshComposer();
        View.Invalidate();
        UpdatePaintSwatch();
        UpdateTitle();
        UpdateUndo();
        UpdatePrintInfo();
    }

    /// <summary>Przelicza kontury w tle (najnowsza zmiana wygrywa).</summary>
    private async void RequestAnalysis()
    {
        if (Doc is not { HasPattern: true } doc)
        {
            View.SetAnalysis(null);
            return;
        }
        var labels = doc.Labels;
        int w = doc.Width, h = doc.Height, colors = doc.Palette.Count, smooth = doc.Options.LineSmoothness;
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();
        int version = ++_analysisVersion;
        _analyzing = true;
        UpdateBusy();
        try
        {
            var an = await Task.Run(() => PatternAnalysis.Compute(labels, w, h, colors, smooth, cts.Token), cts.Token);
            if (version != _analysisVersion || !ReferenceEquals(Doc, doc)) return;
            _analysis = an;
            _analysisLabels = labels;
            View.SetAnalysis(an);
            UpdatePaletteRows();
            UpdateStats();
            UpdatePrintInfo();
            UpdateHint();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się narysować konturów", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (version == _analysisVersion)
            {
                _analyzing = false;
                UpdateBusy();
            }
        }
    }

    /// <summary>Upraszcza obraz od nowa z bieżącymi ustawieniami (do cofnięcia).</summary>
    private async Task SimplifyAsync()
    {
        if (_session is null || _image is null) return;
        var doc = _session.Document;
        var options = doc.Options.Clone();
        var threads = AppInfo.Settings.Threads;
        if (options.Source == PaletteSource.MyThreads && threads.Count < 2)
        {
            ShowToast("Brak nici w pudełku", "Dodaj co najmniej dwie nici w „Moje nici” — na razie kolory biorę z obrazka.", InfoBarSeverity.Warning);
            options.Source = PaletteSource.Automatic;
        }
        var image = _image;
        var cachedWorking = _workingSize == options.WorkingSize ? _working : null;

        _simplifyCts?.Cancel();
        var cts = _simplifyCts = new CancellationTokenSource();
        int version = ++_simplifyVersion;
        _simplifying = true;
        UpdateBusy();
        try
        {
            var (result, working) = await Task.Run(() =>
            {
                var w = cachedWorking ?? Simplifier.PrepareWorkingImage(image.Pixels, options);
                return (Simplifier.Run(w, options, threads, cts.Token), w);
            }, cts.Token);
            if (version != _simplifyVersion || !ReferenceEquals(_session?.Document, doc)) return;
            _working = working;
            _workingSize = options.WorkingSize;
            bool hadEdits = _editedSinceSimplify;
            _session!.ApplySimplified(result);
            _editedSinceSimplify = false;
            if (hadEdits) ShowToast("Uproszczono od nowa", "Ręczne poprawki przywrócisz przyciskiem Cofnij (Ctrl+Z).", InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się uprościć obrazka", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (version == _simplifyVersion)
            {
                _simplifying = false;
                UpdateBusy();
            }
        }
    }

    private void ScheduleSimplify()
    {
        _simplifyTimer.Stop();
        _simplifyTimer.Start();
    }

    // ================================================================== otwieranie i zapis

    private async Task OpenPathAsync(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
                await LoadProjectAsync(path);
            else if (ImageLoader.IsImagePath(path))
                await LoadImageAsync(await File.ReadAllBytesAsync(path), Path.GetFileNameWithoutExtension(path));
            else
                ShowToast("Nieznany typ pliku", "Otwieram obrazki (PNG, JPG, BMP, GIF, TIFF, WEBP) i projekty .wyszycie.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się otworzyć pliku", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task LoadImageAsync(byte[] bytes, string title)
    {
        if (!await ConfirmDiscardAsync()) return;
        LoadedImage image;
        try
        {
            BusyText.Text = "Wczytuję obrazek…";
            BusyCard.Visibility = Visibility.Visible;
            image = await ImageLoader.DecodeAsync(bytes);
        }
        catch (Exception ex)
        {
            UpdateBusy();
            ShowToast("Nie udało się wczytać obrazka", ex.Message, InfoBarSeverity.Error);
            return;
        }
        var settings = AppInfo.Settings;
        var options = settings.LastOptions.Clone();
        options.Seed = 1;
        var (ww, wh) = ImageRgb.FitSize(image.Pixels.Width, image.Pixels.Height, options.WorkingSize);
        var doc = new PatternDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Nowy wzór" : title,
            SourceImage = bytes,
            Options = options,
            Print = settings.LastPrint.Clone(),
            Width = ww,
            Height = wh,
            Labels = new byte[ww * wh],
        };
        StartSession(doc, image, null);
        View.Mode = ViewMode.Colors;
        SyncViewToggles();
        await SimplifyAsync();
    }

    private async Task LoadProjectAsync(string path)
    {
        if (!await ConfirmDiscardAsync()) return;
        var doc = await Task.Run(() => ProjectFile.Load(path));
        LoadedImage? image = null;
        if (doc.SourceImage is { Length: > 0 } src)
        {
            try { image = await ImageLoader.DecodeAsync(src); }
            catch { /* projekt działa bez oryginału — tylko bez ponownego upraszczania */ }
        }
        StartSession(doc, image, path);
        _session!.MarkSaved();
        AppInfo.Settings.AddRecent(path);
        AppInfo.SaveSettings();
        UpdateTitle();
        if (image is null && doc.SourceImage is not null)
            ShowToast("Brak oryginału", "Nie udało się odczytać oryginalnego obrazka — wzór można edytować, ale nie uprościć od nowa.", InfoBarSeverity.Warning);
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_session is null || !_session.Document.HasPattern) return false;
        var path = _session.FilePath;
        if (saveAs || path is null)
        {
            path = await PickSaveAsync(_session.Document.Title, "Projekt Wyszyciuszka", ProjectFile.Extension);
            if (path is null) return false;
        }
        try
        {
            var doc = _session.Document;
            await Task.Run(() => ProjectFile.Save(doc, path));
            _session.FilePath = path;
            _session.MarkSaved();
            AppInfo.Settings.AddRecent(path);
            AppInfo.SaveSettings();
            UpdateTitle();
            ShowToast("Zapisano projekt", path, InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się zapisać", ex.Message, InfoBarSeverity.Error);
            return false;
        }
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                if (items.OfType<StorageFile>().FirstOrDefault(f => ImageLoader.IsImagePath(f.Path) || f.Path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) is { } file)
                {
                    await OpenPathAsync(file.Path);
                    return;
                }
            }
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var bytes = await ImageLoader.ReadAllAsync(stream);
                await LoadImageAsync(bytes, "Obrazek ze schowka");
                return;
            }
            ShowToast("Schowek nie zawiera obrazka", "Skopiuj obrazek (np. z przeglądarki: prawy przycisk → Kopiuj obraz) i spróbuj jeszcze raz.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się wkleić", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowRecentFiles()
    {
        RecentPanel.Children.Clear();
        var recent = AppInfo.Settings.RecentFiles.Where(File.Exists).Take(5).ToList();
        if (recent.Count == 0) return;
        RecentPanel.Children.Add(new TextBlock
        {
            Text = "OSTATNIE PROJEKTY", Style = (Style)Application.Current.Resources["SectionTitleStyle"],
            Margin = new Thickness(0, 8, 0, 2), HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (var path in recent)
        {
            var b = new HyperlinkButton { Content = Path.GetFileNameWithoutExtension(path), HorizontalAlignment = HorizontalAlignment.Center };
            ToolTipService.SetToolTip(b, path);
            b.Click += async (_, _) => await OpenPathAsync(path);
            RecentPanel.Children.Add(b);
        }
    }

    // ================================================================== przyciski — pliki

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync(ImageLoader.Extensions, pictures: true);
        if (path is not null) await OpenPathAsync(path);
    }

    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync([ProjectFile.Extension]);
        if (path is not null) await OpenPathAsync(path);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(IsKeyDown(VirtualKey.Shift));

    private void Undo_Click(object sender, RoutedEventArgs e) => _session?.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => _session?.Redo();

    // ================================================================== narzędzia i widok

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<EditTool>(tag, out var tool)) SetTool(tool);
    }

    private void SetTool(EditTool tool)
    {
        View.Tool = tool;
        ToolSelect.IsChecked = tool == EditTool.Select;
        ToolFill.IsChecked = tool == EditTool.Fill;
        ToolBrush.IsChecked = tool == EditTool.Brush;
        ToolPicker.IsChecked = tool == EditTool.Picker;
        BrushPanel.Visibility = tool == EditTool.Brush ? Visibility.Visible : Visibility.Collapsed;
        // malowanie na oryginale nic nie pokazuje — przełącz na kolory
        if (tool is EditTool.Brush or EditTool.Fill && View.Mode is ViewMode.Original or ViewMode.Overlay)
        {
            View.Mode = ViewMode.Colors;
            SyncViewToggles();
        }
        UpdateHint();
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<ViewMode>(tag, out var mode)) SetViewMode(mode);
    }

    private void SetViewMode(ViewMode mode)
    {
        View.Mode = mode;
        SyncViewToggles();
        UpdateHint();
    }

    private void SyncViewToggles()
    {
        ViewOriginal.IsChecked = View.Mode == ViewMode.Original;
        ViewCompare.IsChecked = View.Mode == ViewMode.Compare;
        ViewColors.IsChecked = View.Mode == ViewMode.Colors;
        ViewPattern.IsChecked = View.Mode == ViewMode.Pattern;
        ViewOverlay.IsChecked = View.Mode == ViewMode.Overlay;
    }

    private void OutlineToggle_Click(object sender, RoutedEventArgs e) => View.OutlinesInColors = OutlineToggle.IsChecked == true;

    private void BrushSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        View.BrushRadius = (float)e.NewValue;
        UpdateBrushText();
    }

    private void UpdateBrushText()
    {
        if (BrushSizeText is null) return;
        var mm = Doc is { MmPerPixel: > 0 } d ? $" · {View.BrushRadius * 2 * d.MmPerPixel:0.#} mm" : "";
        BrushSizeText.Text = $"{View.BrushRadius * 2:0} px{mm}";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => View.ZoomBy(1.25f);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => View.ZoomBy(0.8f);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => View.FitToView();
    private void ZoomActual_Click(object sender, RoutedEventArgs e) => View.ZoomActual();

    private void SetPaintColor(int index)
    {
        if (Doc is null || index < 0 || index >= Doc.Palette.Count) return;
        _paintColor = index;
        View.PaintColor = index;
        UpdatePaintSwatch();
        HighlightPaletteRow();
    }

    private void UpdatePaintSwatch()
    {
        View.PaintColor = _paintColor;
        if (Doc is null || _paintColor >= Doc.Palette.Count)
        {
            PaintSwatch.Background = new SolidColorBrush(Colors.Transparent);
            PaintNumber.Text = "";
            return;
        }
        var c = Doc.Palette[_paintColor].Color;
        PaintSwatch.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, c.R, c.G, c.B));
        PaintNumber.Text = (_paintColor + 1).ToString();
        PaintNumber.Foreground = new SolidColorBrush(c.PrefersLightText ? Colors.White : Colors.Black);
    }

    // ================================================================== klawiatura

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _dialogOpen) return;
        var focused = FocusManager.GetFocusedElement(RootHost.XamlRoot);
        bool typing = focused is TextBox or NumberBox or AutoSuggestBox;
        bool ctrl = IsKeyDown(VirtualKey.Control), shift = IsKeyDown(VirtualKey.Shift);

        if (ctrl)
        {
            switch (e.Key)
            {
                case VirtualKey.O: e.Handled = true; if (shift) OpenProject_Click(this, new RoutedEventArgs()); else OpenImage_Click(this, new RoutedEventArgs()); return;
                case VirtualKey.S: e.Handled = true; await SaveAsync(shift); return;
                case VirtualKey.P: e.Handled = true; Print_Click(this, new RoutedEventArgs()); return;
                case VirtualKey.V when !typing: e.Handled = true; await PasteAsync(); return;
                case VirtualKey.Z when !typing: e.Handled = true; if (shift) _session?.Redo(); else _session?.Undo(); return;
                case VirtualKey.Y when !typing: e.Handled = true; _session?.Redo(); return;
                case VirtualKey.Number0: e.Handled = true; View.FitToView(); return;
            }
            return;
        }
        if (typing) return;
        switch (e.Key)
        {
            case VirtualKey.V: SetTool(EditTool.Select); break;
            case VirtualKey.G: SetTool(EditTool.Fill); break;
            case VirtualKey.B: SetTool(EditTool.Brush); break;
            case VirtualKey.I: SetTool(EditTool.Picker); break;
            case VirtualKey.Number1: SetViewMode(ViewMode.Original); break;
            case VirtualKey.Number2: SetViewMode(ViewMode.Compare); break;
            case VirtualKey.Number3: SetViewMode(ViewMode.Colors); break;
            case VirtualKey.Number4: SetViewMode(ViewMode.Pattern); break;
            case VirtualKey.Number5: SetViewMode(ViewMode.Overlay); break;
            case VirtualKey.F: View.FitToView(); break;
            case (VirtualKey)187: case VirtualKey.Add: View.ZoomBy(1.25f); break; // „=/+”
            case (VirtualKey)189: case VirtualKey.Subtract: View.ZoomBy(0.8f); break; // „-”
            case (VirtualKey)219: BrushSlider.Value = Math.Max(BrushSlider.Minimum, BrushSlider.Value / 1.25); break; // [
            case (VirtualKey)221: BrushSlider.Value = Math.Min(BrushSlider.Maximum, BrushSlider.Value * 1.25); break; // ]
            case VirtualKey.Escape: View.SelectedRegion = -1; UpdateHint(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ================================================================== stan interfejsu

    private void UpdateAll()
    {
        UpdateTitle();
        UpdateUndo();
        UpdateStats();
        UpdatePrintInfo();
        UpdatePaintSwatch();
        UpdateHint();
        UpdateBusy();
        bool has = Doc?.HasPattern == true;
        SaveButton.IsEnabled = has;
        ExportButton.IsEnabled = has;
        PrintButton.IsEnabled = has;
        bool any = _session is not null;
        ToolRail.Visibility = ViewBar.Visibility = ZoomBar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        SidePanel.IsEnabled = any;
        SidePanel.Opacity = any ? 1 : 0.5;
    }

    private void UpdateTitle()
    {
        if (_session is null)
        {
            DocTitleText.Text = "";
            Title = AppInfo.Name;
            return;
        }
        var name = _session.FilePath is { } p ? Path.GetFileName(p) : _session.Document.Title;
        DocTitleText.Text = (_session.IsDirty ? "● " : "") + name;
        Title = $"{name} — {AppInfo.Name}";
        bool has = Doc?.HasPattern == true;
        SaveButton.IsEnabled = has;
        ExportButton.IsEnabled = has;
        PrintButton.IsEnabled = has;
    }

    private void UpdateUndo()
    {
        UndoButton.IsEnabled = _session?.CanUndo == true;
        RedoButton.IsEnabled = _session?.CanRedo == true;
    }

    private void UpdateBusy()
    {
        bool busy = _simplifying || _analyzing;
        BusyText.Text = _simplifying ? "Upraszczam…" : "Rysuję kontury…";
        BusyCard.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStats()
    {
        if (Doc is not { HasPattern: true } d)
        {
            StatsText.Text = "";
            SizeText.Text = "";
            return;
        }
        var regions = _analysis is null ? "" : $" · {_analysis.RegionCount} obszarów";
        StatsText.Text = $"Obraz roboczy {d.Width}×{d.Height} px · {d.Palette.Count(p => !p.IsFabric)} nici{regions}";
        SizeText.Text = $"Wzór {Core.Output.PatternComposer.Cm(d.Print.WidthMm)} × {Core.Output.PatternComposer.Cm(d.HeightMm)} cm";
    }

    private void UpdateHint()
    {
        if (_session is null)
        {
            HintText.Text = "Otwórz albo wklej obrazek, żeby zacząć.";
            return;
        }
        string toolHint = View.Tool switch
        {
            EditTool.Select => "Kliknij obszar, by zobaczyć jego nić. Środkowy przycisk lub spacja — przesuwanie, kółko — powiększenie.",
            EditTool.Fill => "Kliknij obszar, by nadać mu wybraną nić. Prawy przycisk — weź kolor z wzoru.",
            EditTool.Brush => "Maluj wybraną nicią, by poprawić kształty. [ i ] zmieniają rozmiar pędzla. Prawy przycisk — pipeta.",
            _ => "Kliknij na wzorze, by wybrać nić do malowania.",
        };
        int region = View.SelectedRegion;
        if (AnalysisIsCurrent && Doc is { } d)
        {
            var an = _analysis!;
            int r = region >= 0 && region < an.RegionCount ? region : -1;
            if (r >= 0 && an.Regions[r].Color < d.Palette.Count)
            {
                var info = an.Regions[r];
                var e = d.Palette[info.Color];
                double cm2 = info.Area * d.MmPerPixel * d.MmPerPixel / 100;
                HintText.Text = $"Zaznaczony obszar: nić {info.Color + 1} „{e.Name}”{(e.IsFabric ? " (tkanina)" : "")} · ok. {cm2:0.#} cm² — wiaderkiem (G) nadasz mu inną nić.";
                return;
            }
        }
        HintText.Text = toolHint;
    }

    // ================================================================== komunikaty i dialogi

    private void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Toast.Content = null;
        Toast.Title = title;
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 12 : 6);
        _toastTimer.Start();
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try
        {
            dialog.XamlRoot = RootHost.XamlRoot;
            dialog.RequestedTheme = ElementTheme.Dark;
            if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) && style is Style s) dialog.Style = s;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_session is null || !_session.IsDirty) return true;
        var dlg = new ContentDialog
        {
            Title = "Niezapisane zmiany",
            Content = $"Zapisać projekt „{_session.Document.Title}”?",
            PrimaryButtonText = "Zapisz",
            SecondaryButtonText = "Nie zapisuj",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await ShowDialogAsync(dlg) switch
        {
            ContentDialogResult.Primary => await SaveAsync(false),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private async Task<string?> PickOpenAsync(IEnumerable<string> extensions, bool pictures = false)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = pictures ? PickerLocationId.PicturesLibrary : PickerLocationId.DocumentsLibrary,
                ViewMode = pictures ? PickerViewMode.Thumbnail : PickerViewMode.List,
            };
            foreach (var x in extensions) picker.FileTypeFilter.Add(x);
            var result = await picker.PickSingleFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się otworzyć okna wyboru pliku", ex.Message, InfoBarSeverity.Error);
            return null;
        }
    }

    private async Task<string?> PickSaveAsync(string suggestedName, string typeName, string extension)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SafeFileName(suggestedName),
            };
            picker.FileTypeChoices.Add(typeName, new List<string> { extension });
            var result = await picker.PickSaveFileAsync();
            if (result is null) return null;
            var path = result.Path;
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) path += extension;
            return path;
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się otworzyć okna zapisu", ex.Message, InfoBarSeverity.Error);
            return null;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "Wzór" : s;
    }
}
