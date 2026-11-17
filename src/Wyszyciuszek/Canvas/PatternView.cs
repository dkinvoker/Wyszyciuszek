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
using Wyszyciuszek.Core.Output;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Canvas;

public enum ViewMode { Original, Compare, Colors, Pattern, Overlay }

public enum EditTool { Select, Fill, Brush, Picker }

/// <summary>
/// Płótno: oryginał, porównanie, kolory, wzór do druku, kontury na oryginale. Zoom kółkiem, przesuwanie środkowym
/// przyciskiem albo spacją, narzędzia: zaznaczanie, wiaderko, pędzel, pipeta (też prawy przycisk).
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
    private Vector2? _pointerWorld;
    private bool _spaceDown;

    private enum DragKind { None, Pan, Brush, Split }
    private DragKind _drag;
    private Vector2 _dragStart, _panStart;
    private readonly List<Vector2> _stroke = [];
    private int _strokeColor;

    private ViewMode _mode = ViewMode.Colors;
    private EditTool _tool = EditTool.Select;
    private int _selected = -1;
    private bool _outlinesInColors;

    /// <summary>Kliknięto obszar narzędziem zaznaczania (−1 = tło poza wzorem).</summary>
    public event Action<int>? RegionSelected;
    /// <summary>Wiaderko na obszarze.</summary>
    public event Action<int>? FillRequested;
    /// <summary>Pipeta: kolor (indeks palety) pod kursorem.</summary>
    public event Action<int>? ColorPicked;
    /// <summary>Koniec pociągnięcia pędzla: punkty (piksele obrazu roboczego), promień, kolor.</summary>
    public event Action<IReadOnlyList<Vector2>, float, int>? StrokeFinished;
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
        _canvas.PointerCaptureLost += (_, _) => EndDrag(commit: _drag == DragKind.Brush);
        _canvas.PointerExited += (_, _) =>
        {
            _pointerWorld = null;
            if (_drag == DragKind.None) SetHover(-1);
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
            if (e.Key != VirtualKey.Space || _spaceDown) return;
            _spaceDown = true;
            UpdateCursor();
            e.Handled = true;
        };
        KeyUp += (_, e) =>
        {
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

    /// <summary>Kolor pędzla i wiaderka (indeks palety).</summary>
    public int PaintColor { get; set; }

    /// <summary>Promień pędzla w pikselach obrazu roboczego.</summary>
    public float BrushRadius { get; set; } = 6;

    public bool OutlinesInColors
    {
        get => _outlinesInColors;
        set { _outlinesInColors = value; _canvas.Invalidate(); }
    }

    public int SelectedRegion
    {
        get => _selected;
        set { _selected = value; _canvas.Invalidate(); }
    }

    public bool HasContent => _doc is not null;

    private int _fitWidth, _fitHeight;

    public void SetDocument(PatternDocument? doc, CanvasBitmap? source)
    {
        _doc = doc;
        _source = source;
        _selected = -1;
        _hover = -1;
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
        _composer = analysis is not null && _doc is not null && analysis.Width == _doc.Width ? new PatternComposer(_doc, analysis) : null;
        if (_selected >= (analysis?.RegionCount ?? 0)) _selected = -1;
        _hover = -1;
        _stroke.Clear();
        _canvas.Invalidate();
    }

    /// <summary>Ustawienia wydruku się zmieniły (grubość linii, numery…) — nowy kompozytor.</summary>
    public void RefreshComposer()
    {
        if (_analysis is not null && _doc is not null && _analysis.Width == _doc.Width) _composer = new PatternComposer(_doc, _analysis);
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
                    break;
                case ViewMode.Colors:
                    DrawColors(ds, surface, w, h);
                    break;
                case ViewMode.Pattern:
                    if (_composer is not null) _composer.DrawDesign(surface, _composer.PrintStyle());
                    else DrawColors(ds, surface, w, h);
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

        ds.Transform = Matrix3x2.Identity;
        if (_mode == ViewMode.Compare) DrawSplitHandle(ds, screenRect);
        DrawBrushPreview(ds);
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
        _composer.DrawDesign(surface, new DesignStyle
        {
            FullColor = true, Numbers = false, Outline = _outlinesInColors, LineWidth = 1.2f / _zoom,
        });
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
        if (_selected >= 0 && _selected < _analysis.RegionCount)
        {
            var p = _analysis.RegionPaths[_selected];
            surface.Stroke(p, new(255, 255, 255), 4f / _zoom);
            surface.Stroke(p, new(226, 87, 76), 2f / _zoom, [6f / _zoom, 4f / _zoom]);
        }
        bool showHover = _drag == DragKind.None && _tool != EditTool.Brush && !_spaceDown && _mode != ViewMode.Original;
        if (showHover && _hover >= 0 && _hover < _analysis.RegionCount && _hover != _selected)
        {
            var p = _analysis.RegionPaths[_hover];
            surface.Stroke(p, new(18, 20, 23), 3.5f / _zoom);
            surface.Stroke(p, new(255, 255, 255), 1.5f / _zoom);
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

    private void DrawBrushPreview(CanvasDrawingSession ds)
    {
        if (_doc is null) return;
        float r = BrushRadius * _zoom;
        if (_stroke.Count > 0 && _strokeColor >= 0 && _strokeColor < _doc.Palette.Count)
        {
            var c = _doc.Palette[_strokeColor].Color;
            var color = Color.FromArgb(255, c.R, c.G, c.B);
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

    // ================================================================== wskaźnik

    private void SetHover(int region)
    {
        if (region == _hover) return;
        _hover = region;
        HoverChanged?.Invoke(region);
        _canvas.Invalidate();
    }

    private int RegionAt(Vector2 world) => _analysis?.HitTest(world) ?? -1;

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
        if (_doc is null) return;
        var world = ToWorld(pos);

        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
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
                SelectedRegion = RegionAt(world);
                RegionSelected?.Invoke(_selected);
            }
            else if (_tool == EditTool.Fill)
            {
                int r = RegionAt(world);
                if (r >= 0) FillRequested?.Invoke(r);
            }
            else if (_tool == EditTool.Brush)
            {
                _stroke.Clear();
                _stroke.Add(world);
                _strokeColor = PaintColor;
                BeginDrag(DragKind.Brush, pos, e);
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
            case DragKind.Split when _doc is not null:
                _split = Math.Clamp((pos.X - _pan.X) / (_doc.Width * _zoom), 0, 1);
                break;
            case DragKind.Brush:
                if (Vector2.Distance(world, _stroke[^1]) * _zoom >= 1.5f) _stroke.Add(world);
                break;
            default:
                SetHover(RegionAt(world));
                if (_mode == ViewMode.Compare) UpdateCursor(NearSplit(pos));
                break;
        }
        _canvas.Invalidate();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None) return;
        var pos = e.GetCurrentPoint(_canvas).Position.ToVector2();
        if (_drag == DragKind.Brush) _stroke.Add(ToWorld(pos));
        _canvas.ReleasePointerCapture(e.Pointer);
        EndDrag(commit: true);
    }

    private void EndDrag(bool commit)
    {
        var kind = _drag;
        _drag = DragKind.None;
        if (kind == DragKind.Brush)
        {
            if (commit && _stroke.Count > 0) StrokeFinished?.Invoke(_stroke.ToArray(), BrushRadius, _strokeColor);
            else _stroke.Clear();
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
