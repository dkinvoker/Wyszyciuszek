using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Wyszyciuszek.Core.Output;

namespace Wyszyciuszek.Canvas;

/// <summary>Rasteryzacja kartek i samego wzoru (PNG, druk) tym samym kodem co PDF.</summary>
internal static class Raster
{
    private static int MaxPixels => Math.Min(16384, (int)CanvasDevice.GetSharedDevice().MaximumBitmapSizeInPixels);

    /// <summary>Strona wydruku w rozdzielczości <paramref name="dpi"/> (białe tło).</summary>
    public static CanvasRenderTarget RenderPage(PatternComposer composer, int page, float dpi)
    {
        var (wmm, hmm) = composer.PageSize(page);
        dpi = Math.Min(dpi, (float)(MaxPixels / (Math.Max(wmm, hmm) / 25.4)));
        int pw = (int)Math.Ceiling(wmm / 25.4 * dpi), ph = (int)Math.Ceiling(hmm / 25.4 * dpi);
        var rt = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), pw, ph, 96);
        using var ds = rt.CreateDrawingSession();
        ds.Clear(Colors.White);
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
        ds.Transform = Matrix3x2.CreateScale(dpi / 25.4f);
        using var cache = new GeometryCache();
        var surface = new Win2DSurface(ds, cache);
        try
        {
            composer.DrawPage(surface, page);
        }
        finally
        {
            surface.PopAll();
        }
        return rt;
    }

    /// <summary>
    /// Sam wzór w rzeczywistym rozmiarze (<paramref name="dpi"/>), z lustrem jak w ustawieniach (poza podglądem kolorów).
    /// </summary>
    public static CanvasRenderTarget RenderDesign(PatternComposer composer, bool fullColor, float dpi)
    {
        double wmm = composer.DesignWidthMm, hmm = composer.DesignHeightMm;
        dpi = Math.Min(dpi, (float)(MaxPixels / (Math.Max(wmm, hmm) / 25.4)));
        int pw = Math.Max(1, (int)Math.Round(wmm / 25.4 * dpi)), ph = Math.Max(1, (int)Math.Round(hmm / 25.4 * dpi));
        var rt = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), pw, ph, 96);
        using var ds = rt.CreateDrawingSession();
        ds.Clear(Colors.White);
        var toMm = fullColor ? Matrix3x2.CreateScale((float)(wmm / composer.DesignWidthPx)) : composer.DesignToOutputMm();
        ds.Transform = toMm * Matrix3x2.CreateScale(dpi / 25.4f);
        using var cache = new GeometryCache();
        var surface = new Win2DSurface(ds, cache);
        try
        {
            composer.DrawDesign(surface, composer.PrintStyle(fullColor));
        }
        finally
        {
            surface.PopAll();
        }
        return rt;
    }
}
