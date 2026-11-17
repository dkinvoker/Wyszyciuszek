using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Output;
using static Wyszyciuszek.Native;

namespace Wyszyciuszek.Output;

/// <summary>
/// Drukowanie przez okno „Drukuj” Windows i GDI w skali 1:1. Każda kartka jest rysowana tym samym kodem co PDF,
/// a orientacja i format papieru ustawiane są osobno dla każdej kartki (kafelki mogą być poziome, legenda pionowa).
/// </summary>
internal static class PrintService
{
    public sealed record Result(bool Printed, string? Error);

    private sealed class PrinterDc : IDisposable
    {
        public nint Hdc;
        public nint DevMode, DevNames;

        public void Dispose()
        {
            if (Hdc != 0) DeleteDC(Hdc);
            if (DevMode != 0) GlobalFree(DevMode);
            if (DevNames != 0) GlobalFree(DevNames);
        }
    }

    public static async Task<Result> PrintAsync(nint hwnd, PatternComposer composer, string title)
    {
        var pd = new PRINTDLGW
        {
            lStructSize = (uint)Unsafe.SizeOf<PRINTDLGW>(),
            hwndOwner = hwnd,
            Flags = PD_RETURNDC | PD_NOSELECTION | PD_USEDEVMODECOPIESANDCOLLATE | PD_HIDEPRINTTOFILE,
            nCopies = 1,
            nFromPage = 1, nToPage = (ushort)composer.PageCount, nMinPage = 1, nMaxPage = (ushort)composer.PageCount,
        };
        if (!PrintDlgW(ref pd))
        {
            uint err = CommDlgExtendedError();
            return new Result(false, err != 0 ? $"Nie udało się otworzyć okna drukowania (kod 0x{err:X})." : null);
        }
        using var dc = new PrinterDc { Hdc = pd.hDC, DevMode = pd.hDevMode, DevNames = pd.hDevNames };
        if (dc.Hdc == 0) return new Result(false, "Drukarka nie zwróciła kontekstu urządzenia.");

        // zakres stron z okna (PD_PAGENUMS = 0x2)
        int from = 0, to = composer.PageCount - 1;
        if ((pd.Flags & 0x2) != 0)
        {
            from = Math.Clamp(pd.nFromPage - 1, 0, composer.PageCount - 1);
            to = Math.Clamp(pd.nToPage - 1, from, composer.PageCount - 1);
        }
        return await PrintPagesAsync(dc, composer, title, from, to);
    }

    private static unsafe void SetupPage(PrinterDc dc, string paperId, bool landscape)
    {
        if (dc.DevMode == 0) return;
        nint dm = GlobalLock(dc.DevMode);
        if (dm == 0) return;
        try
        {
            short paper = paperId switch { "A3" => DMPAPER_A3, "A5" => DMPAPER_A5, "Letter" => DMPAPER_LETTER, _ => DMPAPER_A4 };
            *(uint*)((byte*)dm + DevModeFieldsOffset) |= DM_ORIENTATION | DM_PAPERSIZE;
            *(short*)((byte*)dm + DevModeOrientationOffset) = landscape ? DMORIENT_LANDSCAPE : DMORIENT_PORTRAIT;
            *(short*)((byte*)dm + DevModePaperSizeOffset) = paper;
            ResetDCW(dc.Hdc, dm);
        }
        finally
        {
            GlobalUnlock(dc.DevMode);
        }
    }

    private static async Task<Result> PrintPagesAsync(PrinterDc dc, PatternComposer composer, string title, int from, int to)
    {
        var docName = Marshal.StringToHGlobalUni(string.IsNullOrWhiteSpace(title) ? "Wyszyciuszek" : title);
        try
        {
            var info = new DOCINFOW { cbSize = Unsafe.SizeOf<DOCINFOW>(), lpszDocName = docName };
            var layout = composer.Layout;
            SetupPage(dc, layout.Paper.Id, layout.Landscape && !layout.IsLegendPage(from));
            if (StartDocW(dc.Hdc, ref info) <= 0) return new Result(false, "Drukarka odrzuciła zadanie drukowania.");
            bool ok = false;
            try
            {
                for (int page = from; page <= to; page++)
                {
                    var (wmm, hmm) = composer.PageSize(page);
                    if (page != from) SetupPage(dc, layout.Paper.Id, wmm > hmm);
                    if (StartPage(dc.Hdc) <= 0) return new Result(false, "Nie udało się rozpocząć strony.");
                    int dpiX = GetDeviceCaps(dc.Hdc, LOGPIXELSX), dpiY = GetDeviceCaps(dc.Hdc, LOGPIXELSY);
                    int offX = GetDeviceCaps(dc.Hdc, PHYSICALOFFSETX), offY = GetDeviceCaps(dc.Hdc, PHYSICALOFFSETY);
                    // raster 300 dpi wystarcza na linie i numery; GDI dopasowuje do rozdzielczości drukarki
                    using var rt = await Task.Run(() => Raster.RenderPage(composer, page, Math.Min(300, Math.Max(dpiX, 96))));
                    Blit(dc.Hdc, rt, (int)Math.Round(wmm / 25.4 * dpiX), (int)Math.Round(hmm / 25.4 * dpiY), -offX, -offY);
                    if (EndPage(dc.Hdc) <= 0) return new Result(false, "Drukarka przerwała drukowanie strony.");
                }
                ok = true;
            }
            finally
            {
                if (ok) EndDoc(dc.Hdc);
                else AbortDoc(dc.Hdc);
            }
            return new Result(true, null);
        }
        finally
        {
            Marshal.FreeHGlobal(docName);
        }
    }

    private static unsafe void Blit(nint hdc, CanvasRenderTarget rt, int destW, int destH, int x, int y)
    {
        int w = (int)rt.SizeInPixels.Width, h = (int)rt.SizeInPixels.Height;
        byte[] bgra = rt.GetPixelBytes();
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = w,
            biHeight = -h, // od góry
            biPlanes = 1,
            biBitCount = 32,
        };
        SetStretchBltMode(hdc, HALFTONE);
        SetBrushOrgEx(hdc, 0, 0, 0);
        fixed (byte* p = bgra)
            StretchDIBits(hdc, x, y, destW, destH, 0, 0, w, h, p, &bmi, DIB_RGB_COLORS, SRCCOPY);
    }
}
