using System.Globalization;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;
using Color = Windows.UI.Color;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wyszyciuszek;

/// <summary>
/// Tryb projektora: wzór na pełnym ekranie w skali 1:1 — do odrysowania prosto na tkaninie z projektora nad stołem
/// albo z ekranu (tabletu, monitora) użytego jak podświetlana szyba. Skalę kalibruje się raz dla każdego ekranu:
/// mierzy się linijką wyświetlony kwadrat.
/// </summary>
internal sealed partial class ProjectorWindow : Window
{
    private readonly nint _hwnd;
    private readonly UserControl _host;
    private readonly CanvasControl _canvas;
    private readonly GeometryCache _cache = new();
    private readonly Border _bar, _notice, _calCard;
    private readonly TextBlock _info, _noticeText, _calText;
    private readonly NumberBox _measured;
    private readonly ToggleButton _mirrorButton, _numbersButton, _arrowsButton, _darkButton;
    private readonly Button _threadButton;
    private readonly DispatcherQueueTimer _hideTimer;

    private PatternDocument? _doc;
    private PatternComposer? _composer;
    private PatternAnalysis? _analysis;
    /// <summary>Obszary już odrysowane na tkaninie (kliknięte) — przygaszone.</summary>
    private readonly HashSet<int> _done = [];
    private bool _grid;
    /// <summary>Ostatnie przekształcenie piksele wzoru → ekran (do trafiania kliknięciem w obszar).</summary>
    private Matrix3x2 _designToScreen = Matrix3x2.Identity;
    private ToggleButton? _gridButton;

    private bool _dark = AppInfo.Settings.ProjectorDark;
    private bool _mirror, _numbers = true, _arrows;
    private int _rotation; // ćwierćobroty
    private int _highlight = -1;
    private Vector2 _offsetMm;

    private string _screenKey = "";
    private string _screenName = "";
    private double _pxPerMm = 96 / 25.4; // fizyczne piksele na milimetr
    private bool _calibrated;
    private bool _calibrating;
    private double _pxPerMmBeforeCalibration;
    private bool _updatingMeasured;

    private bool _dragging;
    private Vector2 _dragStart, _offsetStart;

    public ProjectorWindow(nint ownerHwnd)
    {
        Title = "Wyszyciuszek — projektor";
        _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch { }

        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCaptureLost += (_, _) => _dragging = false;

        // pasek narzędzi (chowa się po chwili bez ruchu myszą)
        _mirrorButton = Toggle("Lustro", "Odbicie lustrzane (M) — np. przy projekcji od spodu", () => { _mirror = !_mirror; Sync(); });
        _numbersButton = Toggle("Numery", "Numery nici (N)", () => { _numbers = !_numbers; Sync(); });
        _arrowsButton = Toggle("Kierunki", "Strzałki kierunku ściegu (K)", () => { _arrows = !_arrows; Sync(); });
        _darkButton = Toggle("Ciemne tło", "Jasne linie na czarnym (projektor) albo ciemne na białym — ekran jak podświetlana szyba (I)", () =>
        {
            _dark = !_dark;
            AppInfo.Settings.ProjectorDark = _dark;
            Sync();
        });
        _threadButton = Btn("Nić: wszystkie", "Wyróżnij obszary jednej nici (Tab — następna, Shift+Tab — poprzednia)", () => CycleThread(1));
        _info = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0), Opacity = 0.75 };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(Btn("Kalibruj", "Ustaw skalę 1:1 dla tego ekranu (C)", StartCalibration));
        tools.Children.Add(Btn("Obróć", "Obróć o 90° (R)", () => { _rotation = (_rotation + 1) % 4; Sync(); }));
        tools.Children.Add(_mirrorButton);
        tools.Children.Add(_numbersButton);
        tools.Children.Add(_arrowsButton);
        tools.Children.Add(_darkButton);
        tools.Children.Add(_threadButton);
        _gridButton = Toggle("Siatka", "Siatka co 1 cm (G)", () => { _grid = !_grid; Sync(); });
        tools.Children.Add(_gridButton);
        tools.Children.Add(Btn("Wyczyść odhaczone", "Klik w obszar odhacza go jako odrysowany; tu zdejmiesz wszystkie odhaczenia (Delete)", () => { _done.Clear(); Sync(); }));
        tools.Children.Add(Btn("Środek", "Wyśrodkuj wzór (0)", () => { _offsetMm = Vector2.Zero; Sync(); }));
        tools.Children.Add(Btn("Inny ekran", "Przenieś na następny ekran / projektor (S)", NextScreen));
        tools.Children.Add(Btn("Zamknij", "Wyjdź z trybu projektora (Esc)", Close));
        tools.Children.Add(_info);
        _bar = Card(tools, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 18));

        _noticeText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
        _notice = Card(_noticeText, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 18, 0, 0));
        _notice.Visibility = Visibility.Collapsed;

        // kalibracja
        _calText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        _measured = new NumberBox
        {
            Header = "Zmierzony bok kwadratu (cm)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 0.1, LargeChange = 1, Minimum = 0.5, Maximum = 100, Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
        };
        _measured.ValueChanged += (_, e) => ApplyMeasurement(e.NewValue);
        var calButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var done = new Button { Content = "Gotowe (Enter)", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        done.Click += (_, _) => FinishCalibration(save: true);
        var cancel = new Button { Content = "Anuluj (Esc)" };
        cancel.Click += (_, _) => FinishCalibration(save: false);
        calButtons.Children.Add(done);
        calButtons.Children.Add(cancel);
        var calPanel = new StackPanel { Spacing = 10 };
        calPanel.Children.Add(new TextBlock { Text = "KALIBRACJA EKRANU", Style = (Style)Application.Current.Resources["SectionTitleStyle"] });
        calPanel.Children.Add(_calText);
        calPanel.Children.Add(_measured);
        calPanel.Children.Add(calButtons);
        _calCard = Card(calPanel, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(24, 24, 0, 0));
        _calCard.Padding = new Thickness(16, 14, 16, 14);
        _calCard.Visibility = Visibility.Collapsed;

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_notice);
        root.Children.Add(_calCard);
        root.Children.Add(_bar);
        _host = new UserControl { Content = root, IsTabStop = true, RequestedTheme = ElementTheme.Dark, UseSystemFocusVisuals = false };
        _host.PreviewKeyDown += OnPreviewKeyDown;
        _host.PointerMoved += (_, _) => ShowBar();
        _host.Loaded += (_, _) =>
        {
            _host.Focus(FocusState.Programmatic);
            if (_host.XamlRoot is { } xr) xr.Changed += (_, _) => _canvas.Invalidate();
        };
        Content = _host;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromSeconds(4);
        _hideTimer.Tick += (t, _) =>
        {
            t.Stop();
            if (!_calibrating) _bar.Visibility = Visibility.Collapsed;
        };

        AppWindow.Changed += (_, e) => { if (e.DidPositionChange || e.DidSizeChange) UpdateScreen(); };
        Closed += (_, _) =>
        {
            _hideTimer.Stop();
            AppInfo.SaveSettings();
            _cache.Clear();
            _canvas.RemoveFromVisualTree();
        };

        // od razu na innym ekranie niż okno główne (zwykle to projektor albo drugi monitor)
        var monitors = Native.Monitors();
        var ownerMonitor = Native.MonitorFromWindow(ownerHwnd, Native.MONITOR_DEFAULTTONEAREST);
        var target = monitors.FirstOrDefault(m => m.Handle != ownerMonitor);
        if (target.Handle == 0 && monitors.Count > 0) target = monitors.FirstOrDefault(m => m.Handle == ownerMonitor);
        if (target.Handle != 0) MoveTo(target);
        else AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

        UpdateScreen();
        Sync();
        ShowBar();
    }

    // ================================================================== dane

    /// <summary>Nowy stan wzoru (po każdej zmianie w oknie głównym).</summary>
    public void SetPattern(PatternDocument? doc, PatternAnalysis? analysis)
    {
        // nowe kontury = nowe numery obszarów — odhaczenia tracą sens
        if (!ReferenceEquals(analysis, _analysis)) _done.Clear();
        _analysis = analysis;
        _doc = doc;
        _composer = doc is not null && analysis is not null && analysis.Width == doc.Width && analysis.Height == doc.Height
            ? new PatternComposer(doc, analysis)
            : null;
        if (_doc is null || _highlight >= _doc.Palette.Count) _highlight = -1;
        Sync();
    }

    // ================================================================== ekran i skala

    private void MoveTo(Native.Monitor monitor)
    {
        var b = monitor.Bounds;
        AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        AppWindow.MoveAndResize(new RectInt32(b.Left + 60, b.Top + 60, Math.Max(400, (b.Right - b.Left) / 2), Math.Max(300, (b.Bottom - b.Top) / 2)));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    private void NextScreen()
    {
        var monitors = Native.Monitors();
        if (monitors.Count < 2)
        {
            ShowNotice("Jest tylko jeden ekran. Podłącz projektor albo drugi monitor (Windows: Win+P → Rozszerz).");
            return;
        }
        var current = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        int i = monitors.FindIndex(m => m.Handle == current);
        MoveTo(monitors[(i + 1) % monitors.Count]);
        _offsetMm = Vector2.Zero;
        UpdateScreen();
        Sync();
    }

    /// <summary>Po przeniesieniu okna: który to ekran i jaka skala (zapamiętana kalibracja albo dane z monitora).</summary>
    private void UpdateScreen()
    {
        if (_calibrating) return;
        var handle = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (Native.Describe(handle) is not { } m) return;
        int w = m.Bounds.Right - m.Bounds.Left, h = m.Bounds.Bottom - m.Bounds.Top;
        var key = $"{m.Device} {w}x{h}";
        if (key == _screenKey) return;
        _screenKey = key;
        _screenName = $"{m.Device.TrimStart('\\', '.')} {w}×{h}";
        if (AppInfo.Settings.ScreenScale.TryGetValue(key, out var saved) && saved > 0.5)
        {
            _pxPerMm = saved;
            _calibrated = true;
        }
        else
        {
            double dpi = Native.RawDpi(handle);
            _pxPerMm = (dpi > 0 ? dpi : 96) / 25.4;
            _calibrated = false;
        }
        Sync();
    }

    private double RasterScale => _host.XamlRoot?.RasterizationScale ?? 1;

    /// <summary>Punkty ekranu (DIP) na milimetr.</summary>
    private float DipPerMm => (float)(_pxPerMm / RasterScale);

    // ================================================================== kalibracja

    /// <summary>Bok kwadratu kalibracyjnego (mm): jak największy, który wygodnie mieści się na ekranie.</summary>
    private double CalibrationSide()
    {
        var size = _canvas.ActualSize;
        float room = Math.Min(size.X, size.Y) * 0.62f;
        foreach (var mm in new[] { 150.0, 100, 50, 20, 10 })
            if (mm * DipPerMm <= room) return mm;
        return 10;
    }

    private void StartCalibration()
    {
        if (_calibrating) return;
        _calibrating = true;
        _pxPerMmBeforeCalibration = _pxPerMm;
        ResetMeasuredBox();
        Sync();
    }

    private void ResetMeasuredBox()
    {
        _updatingMeasured = true;
        _measured.Value = CalibrationSide() / 10;
        _updatingMeasured = false;
    }

    /// <summary>Wpisany zmierzony bok → poprawiona skala (kwadrat od razu rysuje się we właściwym rozmiarze).</summary>
    private void ApplyMeasurement(double cm)
    {
        if (_updatingMeasured || !_calibrating || double.IsNaN(cm)) return;
        double side = CalibrationSide();
        double measuredMm = cm * 10;
        if (measuredMm < side * 0.2 || measuredMm > side * 5) return;
        _pxPerMm *= side / measuredMm;
        ResetMeasuredBox();
        Sync();
    }

    private void FinishCalibration(bool save)
    {
        if (!_calibrating) return;
        _calibrating = false;
        if (save)
        {
            AppInfo.Settings.ScreenScale[_screenKey] = Math.Round(_pxPerMm, 4);
            AppInfo.SaveSettings();
            _calibrated = true;
        }
        else
        {
            _pxPerMm = _pxPerMmBeforeCalibration;
        }
        _host.Focus(FocusState.Programmatic);
        Sync();
        ShowBar();
    }

    // ================================================================== rysowanie

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var paper = _dark ? new Rgb(0, 0, 0) : Rgb.White;
        var ink = _dark ? new Rgb(240, 240, 240) : PatternComposer.Ink;
        ds.Clear(Win2DSurface.ToColor(paper));
        var size = sender.ActualSize;
        float dip = DipPerMm;

        if (_calibrating)
        {
            DrawCalibration(ds, size, dip, ink);
            return;
        }
        if (_composer is null || _doc is null)
        {
            using var f = new CanvasTextFormat { FontSize = 20, HorizontalAlignment = CanvasHorizontalAlignment.Center };
            ds.DrawText("Wzór się przygotowuje…", size.X / 2, size.Y / 2 - 12, Win2DSurface.ToColor(ink), f);
            return;
        }

        int w = _composer.DesignWidthPx, h = _composer.DesignHeightPx;
        float mmPerPx = (float)(_composer.DesignWidthMm / w);
        float k = 1 / mmPerPx; // piksele wzoru na mm
        // piksele wzoru → mm (środek wzoru w zerze) → lustro → obrót → przesunięcie → punkty ekranu
        var m = Matrix3x2.CreateTranslation(-w / 2f, -h / 2f) * Matrix3x2.CreateScale(mmPerPx);
        if (_mirror) m *= Matrix3x2.CreateScale(-1, 1);
        m *= Matrix3x2.CreateRotation(_rotation * MathF.PI / 2);
        m *= Matrix3x2.CreateTranslation(_offsetMm) * Matrix3x2.CreateScale(dip) * Matrix3x2.CreateTranslation(size / 2);
        ds.Transform = m;
        _designToScreen = m;

        float minLine = 1.4f / (dip * mmPerPx); // co najmniej ~1,4 punktu ekranu
        var style = new DesignStyle
        {
            Paper = paper, Ink = ink, Tint = 0, Outline = true, Numbers = _numbers, Lines = true,
            LineWidth = Math.Max(0.35f * k, minLine), MinFont = 1.8f * k, MaxFont = 5f * k,
            DetailDash = [2.4f * k, 0.9f * k, 0.35f * k, 0.9f * k],
            Arrows = _arrows, ArrowColor = _dark ? new Rgb(255, 120, 110) : PatternComposer.Accent,
            ArrowWidth = Math.Max(0.3f * k, minLine), MinArrow = 4.5f * k, MaxArrow = 14f * k,
            Highlight = _highlight,
        };
        var surface = new Win2DSurface(ds, _cache);
        try
        {
            _composer.DrawDesign(surface, style);
            if (_grid) DrawGrid(surface, w, h, k, minLine);
            DrawDone(ds, surface, paper, style.LineWidth);
            // obrys wzoru i krzyżyk w środku — do ułożenia tamborka
            var frameColor = _dark ? new Rgb(110, 110, 110) : new Rgb(150, 150, 150);
            surface.Stroke(Shapes.Rect(0, 0, w, h), frameColor, minLine, [3 * k, 2 * k]);
            surface.Stroke(Shapes.Crosshair(new Vector2(w / 2f, h / 2f), 5 * k), frameColor, minLine);
        }
        finally
        {
            surface.PopAll();
        }
        ds.Transform = Matrix3x2.Identity;
        _cache.EndFrame();
    }

    /// <summary>Siatka co 1 cm (od lewego górnego rogu wzoru), z wyraźniejszą linią co 5 cm — do mierzenia i prostowania.</summary>
    private void DrawGrid(Win2DSurface surface, int w, int h, float k, float minLine)
    {
        var faint = _dark ? new Rgb(52, 52, 52) : new Rgb(218, 218, 218);
        var strong = _dark ? new Rgb(80, 80, 80) : new Rgb(190, 190, 190);
        float step = 10 * k; // 10 mm w pikselach wzoru
        float margin = 2 * step;
        var thin = new PathData();
        var thick = new PathData();
        for (int i = (int)Math.Floor(-margin / step); i * step <= w + margin; i++)
        {
            var target = i % 5 == 0 ? thick : thin;
            target.MoveTo(new Vector2(i * step, -margin));
            target.LineTo(new Vector2(i * step, h + margin));
        }
        for (int j = (int)Math.Floor(-margin / step); j * step <= h + margin; j++)
        {
            var target = j % 5 == 0 ? thick : thin;
            target.MoveTo(new Vector2(-margin, j * step));
            target.LineTo(new Vector2(w + margin, j * step));
        }
        surface.Stroke(thin, faint, minLine);
        surface.Stroke(thick, strong, minLine);
    }

    /// <summary>Odhaczone obszary: lekko zabarwione i z przygaszonym konturem — widać, co już jest na tkaninie.</summary>
    private void DrawDone(CanvasDrawingSession ds, Win2DSurface surface, Rgb paper, float lineWidth)
    {
        if (_done.Count == 0 || _analysis is null) return;
        var tint = _dark ? Color.FromArgb(70, 60, 180, 90) : Color.FromArgb(70, 60, 170, 90);
        var dim = _dark ? new Rgb(75, 75, 75) : new Rgb(200, 200, 200);
        foreach (int r in _done)
        {
            if (r < 0 || r >= _analysis.RegionCount) continue;
            var path = _analysis.RegionPaths[r];
            var g = _cache.Get(ds, path, out bool owned);
            ds.FillGeometry(g, tint);
            if (owned) g.Dispose();
            surface.Stroke(path, paper, lineWidth * 1.8f); // zgaś jasną linię…
            surface.Stroke(path, dim, lineWidth);          // …i narysuj przygaszoną
        }
    }

    /// <summary>Kliknięcie (bez przesuwania) w obszar — odhacza go albo zdejmuje odhaczenie.</summary>
    private void ToggleDone(Vector2 screen)
    {
        if (_analysis is null || _doc is null || !Matrix3x2.Invert(_designToScreen, out var toDesign)) return;
        int r = _analysis.HitTest(Vector2.Transform(screen, toDesign));
        if (r < 0) return;
        var color = _analysis.Regions[r].Color;
        if (color < _doc.Palette.Count && _doc.Palette[color].IsFabric) return; // tkaniny się nie odrysowuje
        if (!_done.Remove(r)) _done.Add(r);
        Sync();
    }

    private void DrawCalibration(CanvasDrawingSession ds, Vector2 size, float dip, Rgb ink)
    {
        double sideMm = CalibrationSide();
        float side = (float)(sideMm * dip);
        var c = size / 2;
        var tl = c - new Vector2(side / 2);
        var color = Win2DSurface.ToColor(ink);
        var accent = _dark ? Color.FromArgb(255, 255, 120, 110) : Win2DSurface.ToColor(PatternComposer.Accent);
        ds.DrawRectangle(tl.X, tl.Y, side, side, color, 2);
        // podziałka co 1 cm (co 5 cm dłuższa) wzdłuż górnej i lewej krawędzi
        for (int i = 0; i <= (int)(sideMm / 10); i++)
        {
            float t = (float)(i * 10 * dip);
            float len = i % 5 == 0 ? 16 : 9;
            ds.DrawLine(tl.X + t, tl.Y, tl.X + t, tl.Y + len, color, 1.5f);
            ds.DrawLine(tl.X, tl.Y + t, tl.X + len, tl.Y + t, color, 1.5f);
        }
        ds.DrawLine(c.X - 12, c.Y, c.X + 12, c.Y, accent, 1.5f);
        ds.DrawLine(c.X, c.Y - 12, c.X, c.Y + 12, accent, 1.5f);
        using var f = new CanvasTextFormat { FontSize = 22, HorizontalAlignment = CanvasHorizontalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        ds.DrawText($"{sideMm / 10:0.#} cm", c.X, tl.Y + side + 12, color, f);
        _calText.Text =
            $"Zmierz linijką bok kwadratu — na ekranie albo na tkaninie pod projektorem. Powinien mieć dokładnie {sideMm / 10:0.#} cm. " +
            "Wpisz zmierzony wynik poniżej, a kwadrat od razu się poprawi; zmierz jeszcze raz i kliknij Gotowe. " +
            "Strzałki ↑ ↓ zmieniają skalę ręcznie (z Shift — dokładniej).";
    }

    // ================================================================== interfejs

    private void Sync()
    {
        _mirrorButton.IsChecked = _mirror;
        _numbersButton.IsChecked = _numbers;
        _arrowsButton.IsChecked = _arrows;
        _darkButton.IsChecked = _dark;
        if (_gridButton is not null) _gridButton.IsChecked = _grid;
        if (_doc is not null && _highlight >= 0 && _highlight < _doc.Palette.Count)
            _threadButton.Content = $"Nić {_doc.ThreadLabel(_highlight)}: {PatternComposer.Fit(_doc.Palette[_highlight].Name, 4, false, 60)}";
        else
            _threadButton.Content = "Nić: wszystkie";
        _info.Text = $"1:1 · {_pxPerMm.ToString("0.00", CultureInfo.GetCultureInfo("pl-PL"))} px/mm · {_screenName}"
                     + (_rotation != 0 ? $" · obrót {_rotation * 90}°" : "")
                     + (_done.Count > 0 ? $" · odhaczone: {_done.Count}" : " · klik w obszar = odhacz");

        _calCard.Visibility = _calibrating ? Visibility.Visible : Visibility.Collapsed;
        if (_calibrating) _bar.Visibility = Visibility.Collapsed;
        if (!_calibrating && !_calibrated)
            ShowNotice("Ten ekran nie jest jeszcze skalibrowany, więc rozmiar może się różnić od rzeczywistego. Kliknij Kalibruj (C) i zmierz linijką wyświetlony kwadrat.", autoHide: false);
        else if (_notice.Tag is "calibration")
            _notice.Visibility = Visibility.Collapsed;
        _canvas.Invalidate();
    }

    private void ShowNotice(string text, bool autoHide = true)
    {
        _noticeText.Text = text;
        _notice.Tag = autoHide ? null : "calibration";
        _notice.Visibility = Visibility.Visible;
        if (!autoHide) return;
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(5);
        t.Tick += (s, _) =>
        {
            s.Stop();
            if (_notice.Tag is null) _notice.Visibility = Visibility.Collapsed;
            Sync();
        };
        t.Start();
    }

    private void ShowBar()
    {
        if (_calibrating) return;
        _bar.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void CycleThread(int step)
    {
        if (_doc is null) return;
        var stitched = Enumerable.Range(0, _doc.Palette.Count).Where(i => !_doc.Palette[i].IsFabric).ToList();
        if (stitched.Count == 0) return;
        int pos = stitched.IndexOf(_highlight); // −1 = wszystkie
        pos += step;
        if (pos >= stitched.Count) pos = -1;
        else if (pos < -1) pos = stitched.Count - 1;
        _highlight = pos < 0 ? -1 : stitched[pos];
        Sync();
    }

    private ToggleButton Toggle(string text, string tooltip, Action click)
    {
        var b = new ToggleButton { Content = text, Padding = new Thickness(10, 5, 10, 5), MinHeight = 32, CornerRadius = new CornerRadius(6) };
        ToolTipService.SetToolTip(b, tooltip);
        b.Click += (_, _) =>
        {
            click();
            _host.Focus(FocusState.Programmatic);
        };
        return b;
    }

    private Button Btn(string text, string tooltip, Action click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources["BarButtonStyle"] };
        ToolTipService.SetToolTip(b, tooltip);
        b.Click += (_, _) =>
        {
            click();
            _host?.Focus(FocusState.Programmatic);
        };
        return b;
    }

    private static Border Card(UIElement child, HorizontalAlignment h, VerticalAlignment v, Thickness margin) => new()
    {
        Child = child, HorizontalAlignment = h, VerticalAlignment = v, Margin = margin,
        Style = (Style)Application.Current.Resources["FloatingCardStyle"], Padding = new Thickness(8, 6, 8, 6),
    };

    // ================================================================== mysz i klawiatura

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _host.Focus(FocusState.Pointer);
        if (_calibrating || !e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragStart = e.GetCurrentPoint(_canvas).Position.ToVector2();
        _offsetStart = _offsetMm;
        _canvas.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowBar();
        if (!_dragging) return;
        var pos = e.GetCurrentPoint(_canvas).Position.ToVector2();
        _offsetMm = _offsetStart + (pos - _dragStart) / DipPerMm;
        _canvas.Invalidate();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        var pos = e.GetCurrentPoint(_canvas).Position.ToVector2();
        _canvas.ReleasePointerCapture(e.Pointer);
        // bez przesunięcia = kliknięcie: odhacz obszar (przesunięcie wzoru wraca do stanu sprzed wciśnięcia)
        if (Vector2.Distance(pos, _dragStart) < 5)
        {
            _offsetMm = _offsetStart;
            ToggleDone(pos);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var focused = _host.XamlRoot is { } xr ? FocusManager.GetFocusedElement(xr) : null;
        bool typing = focused is TextBox or NumberBox;

        if (_calibrating)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape: FinishCalibration(save: false); break;
                case VirtualKey.Enter when !typing: FinishCalibration(save: true); break;
                case VirtualKey.Up when !typing: _pxPerMm *= shift ? 1.001 : 1.005; ResetMeasuredBox(); Sync(); break;
                case VirtualKey.Down when !typing: _pxPerMm /= shift ? 1.001 : 1.005; ResetMeasuredBox(); Sync(); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        if (typing) return;

        float step = shift ? 1 : 5; // mm
        switch (e.Key)
        {
            case VirtualKey.Escape: Close(); break;
            case VirtualKey.Left: _offsetMm.X -= step; break;
            case VirtualKey.Right: _offsetMm.X += step; break;
            case VirtualKey.Up: _offsetMm.Y -= step; break;
            case VirtualKey.Down: _offsetMm.Y += step; break;
            case VirtualKey.Number0: case VirtualKey.NumberPad0: case VirtualKey.Home: _offsetMm = Vector2.Zero; break;
            case VirtualKey.R: _rotation = (_rotation + (shift ? 3 : 1)) % 4; break;
            case VirtualKey.M: _mirror = !_mirror; break;
            case VirtualKey.N: _numbers = !_numbers; break;
            case VirtualKey.K: _arrows = !_arrows; break;
            case VirtualKey.G: _grid = !_grid; break;
            case VirtualKey.Delete: case VirtualKey.Back: _done.Clear(); break;
            case VirtualKey.I:
                _dark = !_dark;
                AppInfo.Settings.ProjectorDark = _dark;
                break;
            case VirtualKey.Tab: CycleThread(shift ? -1 : 1); break;
            case VirtualKey.C: StartCalibration(); break;
            case VirtualKey.S: NextScreen(); break;
            default: return;
        }
        e.Handled = true;
        Sync();
        ShowBar();
    }
}
