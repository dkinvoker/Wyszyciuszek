using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Canvas;

public enum ViewMode { Original, Compare, Colors, Pattern, Overlay }

public enum EditTool { Select, Fill, Brush, Picker, Line, Direction }

/// <summary>
/// Płótno: oryginał, porównanie, kolory, wzór do druku, kontury na oryginale. Zoom kółkiem, przesuwanie środkowym
/// przyciskiem albo spacją, narzędzia: zaznaczanie, wiaderko, pędzel, pipeta (też prawy przycisk), linia detalu
/// i kierunek ściegu.
/// </summary>
public sealed partial class PatternView : UserControl
{
    private const float MinZoom = 0.05f, MaxZoom = 64f;

    private static readonly Color Workbench = Color.FromArgb(255, 18, 20, 23);
    private static readonly Color Weave = Color.FromArgb(10, 255, 255, 255);
    private static readonly Color Thread = Color.FromArgb(255, 226, 87, 76);
    private static readonly Color Wood = Color.FromArgb(255, 201, 155, 99);

    private readonly CanvasControl _canvas;
    private readonly GeometryCache _cache = new();
    private readonly CanvasStrokeStyle _dash = new() { DashStyle = CanvasDashStyle.Dash, DashCap = CanvasCapStyle.Round };
    private readonly CanvasStrokeStyle _round = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };

    private PatternDocument? _doc;
    private PatternAnalysis? _analysis;
    private PatternComposer? _composer;
    private CanvasBitmap? _source;

    private float _zoom = 1;
    private Vector2 _pan;
    private bool _needsFit = true;
    private float _split = 0.5f;
    private int _hover = -1;
    private int _hoverLine = -1;
    private Vector2? _pointerWorld;
    private bool _spaceDown;
    private bool _shiftDown;

    private enum DragKind { None, Pan, Brush, Split, Line, Direction, Minimap }
    private DragKind _drag;
    private Vector2 _dragStart, _panStart, _directionStart;
    private readonly List<Vector2> _stroke = [];
    private int _strokeColor;

    private ViewMode _mode = ViewMode.Colors;
    private EditTool _tool = EditTool.Select;
    private int _selected = -1;
    private int _selectedLine = -1;
    private bool _outlinesInColors;
    private bool _showArrows;

    /// <summary>Kliknięto obszar narzędziem zaznaczania (−1 = tło poza wzorem).</summary>
    public event Action<int>? RegionSelected;
    /// <summary>Kliknięto linię detalu narzędziem zaznaczania.</summary>
    public event Action<int>? LineSelected;
    /// <summary>Wiaderko na obszarze.</summary>
    public event Action<int>? FillRequested;
    /// <summary>Pipeta: kolor (indeks palety) pod kursorem.</summary>
    public event Action<int>? ColorPicked;
    /// <summary>Koniec pociągnięcia pędzla: punkty (piksele obrazu roboczego), promień, kolor.</summary>
    public event Action<IReadOnlyList<Vector2>, float, int>? StrokeFinished;
    /// <summary>Narysowano linię detalu: punkty (piksele obrazu roboczego) i kolor.</summary>
    public event Action<IReadOnlyList<Vector2>, int>? LineFinished;
    /// <summary>Kierunek ściegu: punkt w obszarze i kąt w stopniach (null = przywróć automatyczny).</summary>
    public event Action<Vector2, float?>? DirectionSet;
    public event Action<int>? HoverChanged;
    public event Action? ZoomChanged;
    public event Action<IReadOnlyList<string>>? FilesDropped;

    public PatternView()
    {
        _canvas = new CanvasControl { ClearColor = Workbench };
        _canvas.Draw += OnDraw;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCaptureLost += (_, _) => EndDrag(commit: _drag is DragKind.Brush or DragKind.Line);
        _canvas.PointerExited += (_, _) =>
        {
            _pointerWorld = null;
            if (_drag == DragKind.None)
            {
                SetHover(-1);
                _hoverLine = -1;
            }
            _canvas.Invalidate();
        };
        _canvas.PointerWheelChanged += OnWheel;
        _canvas.SizeChanged += (_, _) => { if (_needsFit) FitToView(); else _canvas.Invalidate(); };
        Content = _canvas;

        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Otwórz w Wyszyciuszku";
            }
        };
        Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0) FilesDropped?.Invoke(paths);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Shift && !_shiftDown)
            {
                _shiftDown = true;
                if (_drag == DragKind.Line) _canvas.Invalidate();
            }
            if (e.Key != VirtualKey.Space || _spaceDown) return;
            _spaceDown = true;
            UpdateCursor();
            e.Handled = true;
        };
        KeyUp += (_, e) =>
        {
            if (e.Key == VirtualKey.Shift)
            {
                _shiftDown = false;
                if (_drag == DragKind.Line) _canvas.Invalidate();
            }
            if (e.Key != VirtualKey.Space) return;
            _spaceDown = false;
            UpdateCursor();
        };
    }

    // ================================================================== stan

    public ViewMode Mode
    {
        get => _mode;
        set { _mode = value; _canvas.Invalidate(); }
    }

    public EditTool Tool
    {
        get => _tool;
        set { _tool = value; UpdateCursor(); _canvas.Invalidate(); }
    }

    /// <summary>Kolor pędzla, wiaderka i linii (indeks palety).</summary>
    public int PaintColor { get; set; }

    /// <summary>Promień pędzla w pikselach obrazu roboczego.</summary>
    public float BrushRadius { get; set; } = 6;

    public bool OutlinesInColors
    {
        get => _outlinesInColors;
        set { _outlinesInColors = value; _canvas.Invalidate(); }
    }

    /// <summary>Strzałki kierunku ściegu w widoku kolorów.</summary>
    public bool ShowArrows
    {
        get => _showArrows;
        set { _showArrows = value; _canvas.Invalidate(); }
    }

    public int SelectedRegion
    {
        get => _selected;
        set { _selected = value; _canvas.Invalidate(); }
    }

    public int SelectedLine
    {
        get => _selectedLine;
        set { _selectedLine = value; _canvas.Invalidate(); }
    }

    private IReadOnlyList<int>? _warnings;

    /// <summary>Obszary do wyróżnienia na czerwono (np. za wąskie na wypełnienie); null — bez wyróżnienia.</summary>
    public IReadOnlyList<int>? WarningRegions
    {
        get => _warnings;
        set { _warnings = value; _canvas.Invalidate(); }
    }

    public bool HasContent => _doc is not null;

    private int _fitWidth, _fitHeight;

    public void SetDocument(PatternDocument? doc, CanvasBitmap? source)
    {
        _doc = doc;
        _source = source;
        _selected = -1;
        _selectedLine = -1;
        _hover = -1;
        _hoverLine = -1;
        _fitWidth = _fitHeight = -1;
        CheckSize();
        _canvas.Invalidate();
    }

    /// <summary>Po zmianie rozmiaru obrazu roboczego (np. inna dokładność) — dopasowanie widoku od nowa.</summary>
    public void CheckSize()
    {
        int w = _doc?.Width ?? 0, h = _doc?.Height ?? 0;
        if (w == _fitWidth && h == _fitHeight) return;
        _fitWidth = w;
        _fitHeight = h;
        _needsFit = true;
        FitToView();
    }

    public void SetAnalysis(PatternAnalysis? analysis)
    {
        CheckSize();
        _cache.Clear();
        _analysis = analysis;
        _warnings = null; // numery obszarów się zmieniły — okno ustawi je od nowa
        _composer = analysis is not null && _doc is not null && analysis.Width == _doc.Width ? new PatternComposer(_doc, analysis) : null;
        if (_selected >= (analysis?.RegionCount ?? 0)) _selected = -1;
        _hover = -1;
        _stroke.Clear();
        _canvas.Invalidate();
    }

    /// <summary>Ustawienia wydruku, linie albo kierunki się zmieniły — nowy kompozytor (kontury zostają).</summary>
    public void RefreshComposer()
    {
        if (_analysis is not null && _doc is not null && _analysis.Width == _doc.Width) _composer = new PatternComposer(_doc, _analysis);
        if (_doc is not null && _selectedLine >= _doc.Lines.Count) _selectedLine = -1;
        if (_doc is not null && _hoverLine >= _doc.Lines.Count) _hoverLine = -1;
        _stroke.Clear();
        _canvas.Invalidate();
    }

    public void ClearStrokePreview()
    {
        _stroke.Clear();
        _canvas.Invalidate();
    }

    public void Invalidate() => _canvas.Invalidate();

    /// <summary>Usuwa kontrolkę Win2D z drzewa (zalecane przy zamykaniu okna).</summary>
    public void Shutdown()
    {
        _cache.Clear();
        _canvas.RemoveFromVisualTree();
    }

    // ================================================================== widok

    /// <summary>Powiększenie: 100% = jeden piksel obrazu roboczego na punkt ekranu.</summary>
    public double ZoomPercent => _zoom * 100;

    private Vector2 ToWorld(Vector2 p) => (p - _pan) / _zoom;
    private Vector2 ToScreen(Vector2 w) => w * _zoom + _pan;

    public void FitToView()
    {
        var size = _canvas.ActualSize;
        if (_doc is null || size.X < 50 || size.Y < 50 || _doc.Width == 0)
        {
            _canvas.Invalidate();
            return;
        }
        _needsFit = false;
        // miejsce na pływające paski: narzędzia z lewej, widoki u góry, powiększenie u dołu
        const float left = 84, right = 28, top = 70, bottom = 70;
        _zoom = Math.Clamp(Math.Min((size.X - left - right) / _doc.Width, (size.Y - top - bottom) / _doc.Height), MinZoom, MaxZoom);
        _pan = new Vector2(left + (size.X - left - right - _doc.Width * _zoom) / 2, top + (size.Y - top - bottom - _doc.Height * _zoom) / 2);
        ZoomChanged?.Invoke();
        _canvas.Invalidate();
    }

    public void ZoomBy(float factor, Vector2? screenAnchor = null)
    {
        var anchor = screenAnchor ?? _canvas.ActualSize / 2;
        var world = ToWorld(anchor);
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _pan = anchor - world * _zoom;
        _needsFit = false;
        ZoomChanged?.Invoke();
        _canvas.Invalidate();
    }

    /// <summary>Ustawia powiększenie na 100% (piksel w piksel).</summary>
    public void ZoomActual() => ZoomBy(1 / _zoom);

    // ================================================================== rysowanie

    /// <summary>Piksele obrazu roboczego na milimetr tkaniny.</summary>
    private float PxPerMm => _doc is { MmPerPixel: > 0 } d ? (float)(1 / d.MmPerPixel) : 4;

    /// <summary>Strzałki widać w widoku kolorów po włączeniu przełącznika i zawsze przy narzędziu „kierunek”.</summary>
    private bool ArrowsVisible => _showArrows || _tool == EditTool.Direction;

    /// <summary>Styl widoku „kolory nici”: kolory, linie detali w kolorze nici, opcjonalnie kontury i strzałki.</summary>
    private DesignStyle ColorsStyle() => new()
    {
        FullColor = true, Numbers = false, Outline = _outlinesInColors, LineWidth = 1.2f / _zoom,
        Lines = true, DetailWidth = Math.Max(0.8f * PxPerMm, 2.2f / _zoom),
        Arrows = ArrowsVisible, ArrowWidth = 1.6f / _zoom, MinArrow = 18 / _zoom, MaxArrow = Math.Max(14 * PxPerMm, 40 / _zoom),
        EmphasizeManual = _tool == EditTool.Direction,
    };

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        DrawWeave(ds, sender.ActualSize);
        if (_doc is null || _doc.Width == 0) return;

        int w = _doc.Width, h = _doc.Height;
        var world = Matrix3x2.CreateScale(_zoom) * Matrix3x2.CreateTranslation(_pan);
        var screenRect = new Rect(_pan.X, _pan.Y, w * _zoom, h * _zoom);

        // miękki cień pod „tkaniną”
        for (int i = 6; i >= 1; i--)
        {
            var r = new Rect(screenRect.X - i + 3, screenRect.Y - i + 6, screenRect.Width + 2 * i, screenRect.Height + 2 * i);
            ds.FillRoundedRectangle(r, i + 2, i + 2, Color.FromArgb((byte)(10 + (6 - i) * 6), 0, 0, 0));
        }

        ds.Transform = world;
        DrawScene(ds, w, h);
        ds.Transform = Matrix3x2.Identity;
        if (_mode == ViewMode.Compare) DrawSplitHandle(ds, screenRect);
        DrawBrushPreview(ds);
        DrawLinePreview(ds);
        DrawDirectionPreview(ds);
        DrawMinimap(ds, sender.ActualSize, w, h);
        DrawLoupe(ds, sender.ActualSize, w, h);
        _cache.EndFrame();
    }

    /// <summary>Treść widoku (oryginał, kolory, wzór…) z tamborkiem i podświetleniami — w bieżącym przekształceniu.</summary>
    private void DrawScene(CanvasDrawingSession ds, int w, int h)
    {
        var surface = new Win2DSurface(ds, _cache);
        try
        {
            switch (_mode)
            {
                case ViewMode.Original:
                    DrawSource(ds, w, h);
                    break;
                case ViewMode.Overlay:
                    DrawSource(ds, w, h);
                    if (_analysis is not null) surface.Stroke(_analysis.Outline, new(226, 87, 76), 1.6f / _zoom);
                    _composer?.DrawLines(surface, ColorsStyle());
                    break;
                case ViewMode.Colors:
                    DrawColors(ds, surface, w, h);
                    break;
                case ViewMode.Pattern:
                    if (_composer is not null)
                    {
                        var style = _composer.PrintStyle();
                        if (_tool == EditTool.Direction) style = style with { Arrows = true, EmphasizeManual = true };
                        _composer.DrawDesign(surface, style);
                    }
                    else
                    {
                        DrawColors(ds, surface, w, h);
                    }
                    break;
                case ViewMode.Compare:
                    float sx = _split * w;
                    surface.PushClip(new RectF(0, 0, sx, h));
                    DrawSource(ds, w, h);
                    surface.Pop();
                    surface.PushClip(new RectF(sx, 0, w - sx, h));
                    DrawColors(ds, surface, w, h);
                    surface.Pop();
                    break;
            }
            DrawHoop(ds, w, h);
            DrawHighlights(ds, surface);
        }
        finally
        {
            surface.PopAll();
        }
    }

    // ================================================================== minimapa i lupa

    private const float MinimapMax = 170, LoupeRadius = 95, LoupeFactor = 3;
    private Rect _minimapRect = Rect.Empty;
    private bool _showLoupe, _wasOverMinimap;

    /// <summary>Lupa przy kursorze — 3× większe powiększenie, do drobnych poprawek.</summary>
    public bool ShowLoupe
    {
        get => _showLoupe;
        set { _showLoupe = value; _canvas.Invalidate(); }
    }

    /// <summary>Czy cały obrazek mieści się w oknie (wtedy minimapa niepotrzebna).</summary>
    private bool WholeImageVisible(Vector2 size, int w, int h)
    {
        var a = ToWorld(Vector2.Zero);
        var b = ToWorld(size);
        return a.X <= 0.5f && a.Y <= 0.5f && b.X >= w - 0.5f && b.Y >= h - 0.5f;
    }

    /// <summary>Podgląd całości w lewym dolnym rogu z ramką widocznego fragmentu — klik albo przeciąganie przesuwa widok.</summary>
    private void DrawMinimap(CanvasDrawingSession ds, Vector2 size, int w, int h)
    {
        _minimapRect = Rect.Empty;
        if (WholeImageVisible(size, w, h) || size.X < 300 || size.Y < 260) return;
        float k = Math.Min(MinimapMax / w, MinimapMax / h);
        float mw = w * k, mh = h * k;
        var origin = new Vector2(16, size.Y - 16 - mh);
        ds.FillRoundedRectangle(new Rect(origin.X - 6, origin.Y - 6, mw + 12, mh + 12), 8, 8, Color.FromArgb(235, 28, 31, 35));
        ds.DrawRoundedRectangle(new Rect(origin.X - 6, origin.Y - 6, mw + 12, mh + 12), 8, 8, Color.FromArgb(255, 58, 64, 71), 1);

        ds.Transform = Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(origin);
        var surface = new Win2DSurface(ds, _cache);
        try
        {
            if (_composer is null || _mode is ViewMode.Original) DrawSource(ds, w, h);
            else _composer.DrawDesign(surface, new DesignStyle { FullColor = true, Outline = false, Numbers = false, DetailWidth = 1.5f / k });
        }
        finally
        {
            surface.PopAll();
            ds.Transform = Matrix3x2.Identity;
        }

        // widoczny fragment
        var a = Vector2.Max(ToWorld(Vector2.Zero), Vector2.Zero);
        var b = Vector2.Min(ToWorld(size), new Vector2(w, h));
        if (b.X > a.X && b.Y > a.Y)
        {
            var r = new Rect(origin.X + a.X * k, origin.Y + a.Y * k, (b.X - a.X) * k, (b.Y - a.Y) * k);
            ds.DrawRectangle(r, Color.FromArgb(200, 0, 0, 0), 3);
            ds.DrawRectangle(r, Thread, 1.5f);
        }
        _minimapRect = new Rect(origin.X, origin.Y, mw, mh);
    }

    /// <summary>Punkt na minimapie → punkt obrazu; widok ustawia się tak, by był na środku.</summary>
    private void CenterOnMinimap(Vector2 screen)
    {
        if (_doc is null || _minimapRect.IsEmpty) return;
        float k = (float)(_minimapRect.Width / _doc.Width);
        var world = new Vector2((float)((screen.X - _minimapRect.X) / k), (float)((screen.Y - _minimapRect.Y) / k));
        world = Vector2.Clamp(world, Vector2.Zero, new Vector2(_doc.Width, _doc.Height));
        _pan = _canvas.ActualSize / 2 - world * _zoom;
        _needsFit = false;
        ZoomChanged?.Invoke();
    }

    private bool InMinimap(Vector2 screen) => !_minimapRect.IsEmpty && _minimapRect.Contains(new Point(screen.X, screen.Y));

    /// <summary>Lupa: koło obok kursora z tym samym widokiem w 3× większym powiększeniu (z pędzlem i rysowaną linią).</summary>
    private void DrawLoupe(CanvasDrawingSession ds, Vector2 size, int w, int h)
    {
        if (!_showLoupe || _pointerWorld is not { } pw || _spaceDown || _drag is DragKind.Pan or DragKind.Split or DragKind.Minimap) return;
        var cursor = ToScreen(pw);
        if (InMinimap(cursor)) return;
        const float gap = 28;
        var center = cursor + new Vector2(LoupeRadius + gap, -(LoupeRadius + gap));
        if (center.X + LoupeRadius > size.X - 8) center.X = cursor.X - LoupeRadius - gap;
        if (center.Y - LoupeRadius < 8) center.Y = cursor.Y + LoupeRadius + gap;

        float savedZoom = _zoom;
        var savedPan = _pan;
        _zoom = Math.Min(_zoom * LoupeFactor, MaxZoom * 2);
        _pan = center - pw * _zoom;
        try
        {
            ds.FillCircle(center, LoupeRadius, Workbench);
            using var clip = CanvasGeometry.CreateCircle(ds, center, LoupeRadius);
            using (ds.CreateLayer(1f, clip))
            {
                ds.Transform = Matrix3x2.CreateScale(_zoom) * Matrix3x2.CreateTranslation(_pan);
                DrawScene(ds, w, h);
                ds.Transform = Matrix3x2.Identity;
                DrawBrushPreview(ds);
                DrawLinePreview(ds);
                DrawDirectionPreview(ds);
                if (_tool != EditTool.Brush)
                {
                    // krzyżyk w punkcie kursora
                    ds.DrawLine(center.X - 9, center.Y, center.X + 9, center.Y, Color.FromArgb(200, 0, 0, 0), 3);
                    ds.DrawLine(center.X, center.Y - 9, center.X, center.Y + 9, Color.FromArgb(200, 0, 0, 0), 3);
                    ds.DrawLine(center.X - 8, center.Y, center.X + 8, center.Y, Colors.White, 1.2f);
                    ds.DrawLine(center.X, center.Y - 8, center.X, center.Y + 8, Colors.White, 1.2f);
                }
            }
        }
        finally
        {
            _zoom = savedZoom;
            _pan = savedPan;
            ds.Transform = Matrix3x2.Identity;
        }
        ds.DrawCircle(center, LoupeRadius + 1.5f, Color.FromArgb(220, 0, 0, 0), 4);
        ds.DrawCircle(center, LoupeRadius, Thread, 2.5f);
        using var label = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontSize = 11, HorizontalAlignment = Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment.Center };
        ds.DrawText($"{LoupeFactor:0}×", center.X, center.Y + LoupeRadius - 20, Colors.White, label);
    }

    private void DrawWeave(CanvasDrawingSession ds, Vector2 size)
    {
        // delikatny splot płótna w tle
        for (float x = 0; x < size.X; x += 5) ds.DrawLine(x, 0, x, size.Y, Weave, 1);
        for (float y = 0; y < size.Y; y += 5) ds.DrawLine(0, y, size.X, y, Weave, 1);
    }

    private void DrawSource(CanvasDrawingSession ds, int w, int h)
    {
        if (_source is null)
        {
            ds.FillRectangle(0, 0, w, h, Color.FromArgb(255, 60, 60, 60));
            return;
        }
        var src = _source.Bounds;
        var interpolation = _zoom * w / src.Width >= 1.5 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.HighQualityCubic;
        ds.DrawImage(_source, new Rect(0, 0, w, h), src, 1, interpolation);
    }

    private void DrawColors(CanvasDrawingSession ds, Win2DSurface surface, int w, int h)
    {
        if (_composer is null)
        {
            DrawSource(ds, w, h);
            return;
        }
        _composer.DrawDesign(surface, ColorsStyle());
    }

    private void DrawHoop(CanvasDrawingSession ds, int w, int h)
    {
        if (_doc is null || Hoops.Get(_doc.Print.Hoop) is not { } hoop || _doc.MmPerPixel <= 0) return;
        float k = (float)(1 / _doc.MmPerPixel);
        var c = new Vector2(w / 2f, h / 2f);
        float rx = (float)hoop.Width / 2 * k, ry = (float)hoop.Height / 2 * k;
        // obrys tamborka (drewno) — prostokątny obrócony tak, by lepiej pasował do wzoru
        if (!hoop.Round && (w > h) != (hoop.Width > hoop.Height)) (rx, ry) = (ry, rx);
        float ring = 6 / _zoom;
        if (hoop.Round)
        {
            ds.DrawEllipse(c, rx + ring / 2, ry + ring / 2, Color.FromArgb(150, 201, 155, 99), ring);
            ds.DrawEllipse(c, rx, ry, Wood, 1.5f / _zoom, _dash);
        }
        else
        {
            var rr = new Rect(c.X - rx - ring / 2, c.Y - ry - ring / 2, 2 * rx + ring, 2 * ry + ring);
            ds.DrawRoundedRectangle(rr, 6 * k, 6 * k, Color.FromArgb(150, 201, 155, 99), ring);
            ds.DrawRoundedRectangle(new Rect(c.X - rx, c.Y - ry, 2 * rx, 2 * ry), 5 * k, 5 * k, Wood, 1.5f / _zoom, _dash);
        }
    }

    private void DrawHighlights(CanvasDrawingSession ds, Win2DSurface surface)
    {
        if (_analysis is null) return;
        // obszary z ostrzeżeniem (np. za wąskie na wypełnienie) — półprzezroczysta czerwień
        if (_warnings is { Count: > 0 } warn && _mode != ViewMode.Original)
        {
            var red = Color.FromArgb(255, 255, 70, 70);
            var fill = Color.FromArgb(115, 255, 70, 70);
            foreach (int r in warn)
            {
                if (r < 0 || r >= _analysis.RegionCount) continue;
                var g = _cache.Get(ds, _analysis.RegionPaths[r], out bool owned);
                ds.FillGeometry(g, fill);
                ds.DrawGeometry(g, red, 2f / _zoom);
                if (owned) g.Dispose();
            }
        }
        if (_selected >= 0 && _selected < _analysis.RegionCount)
        {
            var p = _analysis.RegionPaths[_selected];
            surface.Stroke(p, new(255, 255, 255), 4f / _zoom);
            surface.Stroke(p, new(226, 87, 76), 2f / _zoom, [6f / _zoom, 4f / _zoom]);
        }
        bool showHover = _drag == DragKind.None && _tool is EditTool.Select or EditTool.Fill or EditTool.Picker or EditTool.Direction
                         && !_spaceDown && _mode != ViewMode.Original;
        if (showHover && _hoverLine < 0 && _hover >= 0 && _hover < _analysis.RegionCount && _hover != _selected)
        {
            var p = _analysis.RegionPaths[_hover];
            surface.Stroke(p, new(18, 20, 23), 3.5f / _zoom);
            surface.Stroke(p, new(255, 255, 255), 1.5f / _zoom);
        }

        // linie detali: zaznaczona i pod kursorem
        if (_composer is null || _doc is null) return;
        var lines = _composer.LinePaths;
        if (_selectedLine >= 0 && _selectedLine < lines.Length)
        {
            surface.Stroke(lines[_selectedLine], new(255, 255, 255), 5f / _zoom);
            surface.Stroke(lines[_selectedLine], new(226, 87, 76), 2.5f / _zoom, [6f / _zoom, 4f / _zoom]);
        }
        if (_tool == EditTool.Select && _drag == DragKind.None && _hoverLine >= 0 && _hoverLine < lines.Length && _hoverLine != _selectedLine)
        {
            surface.Stroke(lines[_hoverLine], new(18, 20, 23), 5f / _zoom);
            surface.Stroke(lines[_hoverLine], new(255, 255, 255), 2f / _zoom);
        }
    }

    private void DrawSplitHandle(CanvasDrawingSession ds, Rect screenRect)
    {
        float x = (float)(screenRect.X + _split * screenRect.Width);
        ds.DrawLine(x, (float)screenRect.Y, x, (float)screenRect.Bottom, Color.FromArgb(255, 255, 255, 255), 2);
        float cy = (float)(screenRect.Y + screenRect.Height / 2);
        ds.FillCircle(x, cy, 14, Thread);
        ds.DrawCircle(x, cy, 14, Color.FromArgb(255, 255, 255, 255), 2);
        ds.DrawLine(x - 6, cy - 4, x - 9, cy, Colors.White, 2, _round);
        ds.DrawLine(x - 9, cy, x - 6, cy + 4, Colors.White, 2, _round);
        ds.DrawLine(x + 6, cy - 4, x + 9, cy, Colors.White, 2, _round);
        ds.DrawLine(x + 9, cy, x + 6, cy + 4, Colors.White, 2, _round);
    }

    private static class Colors
    {
        public static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    }

    private Color PaletteColor(int index)
    {
        if (_doc is null || index < 0 || index >= _doc.Palette.Count) return Thread;
        var c = _doc.Palette[index].Color;
        return Color.FromArgb(255, c.R, c.G, c.B);
    }

    private void DrawBrushPreview(CanvasDrawingSession ds)
    {
        if (_doc is null) return;
        float r = BrushRadius * _zoom;
        if (_drag == DragKind.Brush && _stroke.Count > 0)
        {
            var color = PaletteColor(_strokeColor);
            if (_stroke.Count == 1) ds.FillCircle(ToScreen(_stroke[0]), r, color);
            for (int i = 1; i < _stroke.Count; i++) ds.DrawLine(ToScreen(_stroke[i - 1]), ToScreen(_stroke[i]), color, 2 * r, _round);
        }
        else if (_drag == DragKind.None && _stroke.Count > 0 && _tool == EditTool.Brush)
        {
            // pociągnięcie czeka na przeliczenie wzoru
            var color = PaletteColor(_strokeColor);
            if (_stroke.Count == 1) ds.FillCircle(ToScreen(_stroke[0]), r, color);
            for (int i = 1; i < _stroke.Count; i++) ds.DrawLine(ToScreen(_stroke[i - 1]), ToScreen(_stroke[i]), color, 2 * r, _round);
        }
        if (_tool == EditTool.Brush && _pointerWorld is { } pw && !_spaceDown && _drag != DragKind.Pan)
        {
            var p = ToScreen(pw);
            ds.DrawCircle(p, r, Color.FromArgb(200, 0, 0, 0), 3);
            ds.DrawCircle(p, r, Colors.White, 1.2f);
        }
    }

    /// <summary>Punkty rysowanej linii (z Shift — prosta od początku do kursora).</summary>
    private List<Vector2> LinePoints()
    {
        if (_shiftDown && _stroke.Count > 1) return [_stroke[0], _stroke[^1]];
        return _stroke;
    }

    private void DrawLinePreview(CanvasDrawingSession ds)
    {
        if (_drag != DragKind.Line || _stroke.Count == 0) return;
        var pts = LinePoints();
        float width = Math.Max(0.8f * PxPerMm * _zoom, 2.2f);
        var color = PaletteColor(_strokeColor);
        for (int i = 1; i < pts.Count; i++)
        {
            ds.DrawLine(ToScreen(pts[i - 1]), ToScreen(pts[i]), Color.FromArgb(160, 0, 0, 0), width + 2, _round);
        }
        for (int i = 1; i < pts.Count; i++) ds.DrawLine(ToScreen(pts[i - 1]), ToScreen(pts[i]), color, width, _round);
    }

    private void DrawDirectionPreview(CanvasDrawingSession ds)
    {
        if (_drag != DragKind.Direction || _pointerWorld is not { } pw) return;
        var a = ToScreen(_directionStart);
        var b = ToScreen(pw);
        var d = b - a;
        float len = d.Length();
        ds.FillCircle(a, 4, Thread);
        if (len < 6) return;
        var dir = d / len;
        var start = a - dir * len; // kierunek obowiązuje w obie strony
        ds.DrawLine(start, b, Color.FromArgb(200, 0, 0, 0), 5, _round);
        ds.DrawLine(start, b, Thread, 2.5f, _round);
        foreach (var (tip, back) in new[] { (b, -dir), (start, dir) })
        {
            var n = new Vector2(-back.Y, back.X);
            ds.DrawLine(tip, tip + back * 12 + n * 7, Thread, 2.5f, _round);
            ds.DrawLine(tip, tip + back * 12 - n * 7, Thread, 2.5f, _round);
        }
    }

    // ================================================================== wskaźnik

    private void SetHover(int region)
    {
        if (region == _hover) return;
        _hover = region;
        HoverChanged?.Invoke(region);
        _canvas.Invalidate();
    }

    private int RegionAt(Vector2 world) => _analysis?.HitTest(world) ?? -1;

    private int LineAt(Vector2 world) => _doc?.HitLine(world, 6 / _zoom) ?? -1;

    private int ColorAt(Vector2 world)
    {
        if (_doc is null || !_doc.HasPattern) return -1;
        int x = (int)MathF.Floor(world.X), y = (int)MathF.Floor(world.Y);
        if (x < 0 || y < 0 || x >= _doc.Width || y >= _doc.Height) return -1;
        return _doc.Labels[y * _doc.Width + x];
    }

    private bool NearSplit(Vector2 screen)
    {
        if (_mode != ViewMode.Compare || _doc is null) return false;
        float x = _pan.X + _split * _doc.Width * _zoom;
        return Math.Abs(screen.X - x) < 16;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_canvas);
        var pos = pt.Position.ToVector2();
        var props = pt.Properties;
        Focus(FocusState.Pointer);
        _shiftDown = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        if (_doc is null) return;
        var world = ToWorld(pos);

        if (props.IsLeftButtonPressed && InMinimap(pos))
        {
            // minimapa: klik albo przeciąganie przesuwa widok
            CenterOnMinimap(pos);
            BeginDrag(DragKind.Minimap, pos, e);
        }
        else if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
        {
            BeginDrag(DragKind.Pan, pos, e);
        }
        else if (props.IsRightButtonPressed)
        {
            // prawy przycisk = pipeta w każdym narzędziu
            int c = ColorAt(world);
            if (c >= 0) ColorPicked?.Invoke(c);
        }
        else if (props.IsLeftButtonPressed)
        {
            if (NearSplit(pos))
            {
                BeginDrag(DragKind.Split, pos, e);
            }
            else if ((e.KeyModifiers & VirtualKeyModifiers.Menu) != 0 || _tool == EditTool.Picker)
            {
                int c = ColorAt(world);
                if (c >= 0) ColorPicked?.Invoke(c);
            }
            else if (_tool == EditTool.Select)
            {
                int line = _mode == ViewMode.Original ? -1 : LineAt(world);
                if (line >= 0)
                {
                    _selected = -1;
                    SelectedLine = line;
                    LineSelected?.Invoke(line);
                }
                else
                {
                    _selectedLine = -1;
                    SelectedRegion = RegionAt(world);
                    RegionSelected?.Invoke(_selected);
                }
            }
            else if (_tool == EditTool.Fill)
            {
                int r = RegionAt(world);
                if (r >= 0) FillRequested?.Invoke(r);
            }
            else if (_tool is EditTool.Brush or EditTool.Line)
            {
                _stroke.Clear();
                _stroke.Add(world);
                _strokeColor = PaintColor;
                BeginDrag(_tool == EditTool.Brush ? DragKind.Brush : DragKind.Line, pos, e);
            }
            else if (_tool == EditTool.Direction && RegionAt(world) >= 0 && ColorAt(world) is var c && c >= 0 && c < _doc.Palette.Count && !_doc.Palette[c].IsFabric)
            {
                // tkaniny nie szyjemy — kierunek dla niej nic nie znaczy
                _directionStart = world;
                BeginDrag(DragKind.Direction, pos, e);
            }
        }
        e.Handled = true;
    }

    private void BeginDrag(DragKind kind, Vector2 pos, PointerRoutedEventArgs e)
    {
        _drag = kind;
        _dragStart = pos;
        _panStart = _pan;
        _canvas.CapturePointer(e.Pointer);
        UpdateCursor();
        _canvas.Invalidate();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(_canvas).Position.ToVector2();
        var world = ToWorld(pos);
        _pointerWorld = world;
        switch (_drag)
        {
            case DragKind.Pan:
                _pan = _panStart + (pos - _dragStart);
                _needsFit = false;
                break;
            case DragKind.Minimap:
                CenterOnMinimap(pos);
                break;
            case DragKind.Split when _doc is not null:
                _split = Math.Clamp((pos.X - _pan.X) / (_doc.Width * _zoom), 0, 1);
                break;
            case DragKind.Brush:
            case DragKind.Line:
                _shiftDown = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
                if (Vector2.Distance(world, _stroke[^1]) * _zoom >= 1.5f) _stroke.Add(world);
                break;
            case DragKind.Direction:
                break;
            default:
                int line = _tool == EditTool.Select && _mode != ViewMode.Original ? LineAt(world) : -1;
                if (line != _hoverLine) _hoverLine = line;
                SetHover(RegionAt(world));
                bool overMinimap = InMinimap(pos);
                if (overMinimap) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                else if (_mode == ViewMode.Compare || _wasOverMinimap) UpdateCursor(NearSplit(pos));
                _wasOverMinimap = overMinimap;
                break;
        }
        _canvas.Invalidate();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None) return;
        var pos = e.GetCurrentPoint(_canvas).Position.ToVector2();
        var world = ToWorld(pos);
        _pointerWorld = world;
        _shiftDown = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        if (_drag is DragKind.Brush or DragKind.Line) _stroke.Add(world);
        // najpierw zatwierdzenie: zwolnienie myszy od razu wywołuje PointerCaptureLost (które tylko anuluje)
        EndDrag(commit: true);
        _canvas.ReleasePointerCapture(e.Pointer);
    }

    private void EndDrag(bool commit)
    {
        var kind = _drag;
        _drag = DragKind.None;
        switch (kind)
        {
            case DragKind.Brush:
                if (commit && _stroke.Count > 0) StrokeFinished?.Invoke(_stroke.ToArray(), BrushRadius, _strokeColor);
                else _stroke.Clear();
                break;
            case DragKind.Line:
                var points = LinePoints().ToArray();
                _stroke.Clear();
                if (commit && points.Length > 1) LineFinished?.Invoke(points, _strokeColor);
                break;
            case DragKind.Direction when commit && _pointerWorld is { } end:
                // krótkie kliknięcie = kierunek automatyczny
                if (Vector2.Distance(end, _directionStart) * _zoom < 6) DirectionSet?.Invoke(_directionStart, null);
                else DirectionSet?.Invoke(_directionStart, StitchPlan.AngleOf(_directionStart, end));
                break;
        }
        UpdateCursor();
        _canvas.Invalidate();
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_canvas);
        int delta = pt.Properties.MouseWheelDelta;
        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
        {
            _pan.X += delta * 0.6f;
            _needsFit = false;
            _canvas.Invalidate();
        }
        else
        {
            ZoomBy(MathF.Pow(1.0015f, delta), pt.Position.ToVector2());
        }
        e.Handled = true;
    }

    private void UpdateCursor(bool nearSplit = false)
    {
        var shape = _drag == DragKind.Pan || _spaceDown ? InputSystemCursorShape.SizeAll
            : nearSplit || _drag == DragKind.Split ? InputSystemCursorShape.SizeWestEast
            : _tool switch
            {
                EditTool.Select => InputSystemCursorShape.Arrow,
                EditTool.Fill => InputSystemCursorShape.Hand,
                _ => InputSystemCursorShape.Cross,
            };
        ProtectedCursor = InputSystemCursor.Create(shape);
    }
}
