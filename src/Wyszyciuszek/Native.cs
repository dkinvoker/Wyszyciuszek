using System.Runtime.InteropServices;

namespace Wyszyciuszek;

/// <summary>Minimalny zestaw Win32 (GDI, okno drukowania, powłoka) — przez LibraryImport, więc działa z Native AOT.</summary>
internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PRINTDLGW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hDevMode;
        public nint hDevNames;
        public nint hDC;
        public uint Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public nint hInstance;
        public nint lCustData;
        public nint lpfnPrintHook;
        public nint lpfnSetupHook;
        public nint lpPrintTemplateName;
        public nint lpSetupTemplateName;
        public nint hPrintTemplate;
        public nint hSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DOCINFOW
    {
        public int cbSize;
        public nint lpszDocName;
        public nint lpszOutput;
        public nint lpszDatatype;
        public uint fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    public const uint PD_NOSELECTION = 0x4;
    public const uint PD_NOPAGENUMS = 0x8;
    public const uint PD_RETURNDC = 0x100;
    public const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x40000;
    public const uint PD_HIDEPRINTTOFILE = 0x100000;

    public const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90;
    public const int PHYSICALWIDTH = 110, PHYSICALHEIGHT = 111, PHYSICALOFFSETX = 112, PHYSICALOFFSETY = 113;
    public const int HALFTONE = 4;
    public const uint DIB_RGB_COLORS = 0, SRCCOPY = 0x00CC0020;

    // DEVMODEW: dmFields @72, dmOrientation @76, dmPaperSize @78
    public const int DevModeFieldsOffset = 72, DevModeOrientationOffset = 76, DevModePaperSizeOffset = 78;
    public const uint DM_ORIENTATION = 0x1, DM_PAPERSIZE = 0x2;
    public const short DMORIENT_PORTRAIT = 1, DMORIENT_LANDSCAPE = 2;
    public const short DMPAPER_LETTER = 1, DMPAPER_A3 = 8, DMPAPER_A4 = 9, DMPAPER_A5 = 11;

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintDlgW(ref PRINTDLGW lppd);

    [LibraryImport("comdlg32.dll")]
    internal static partial uint CommDlgExtendedError();

    [LibraryImport("gdi32.dll")]
    internal static partial int StartDocW(nint hdc, ref DOCINFOW lpdi);

    [LibraryImport("gdi32.dll")]
    internal static partial int StartPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int EndPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int EndDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int AbortDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDeviceCaps(nint hdc, int index);

    [LibraryImport("gdi32.dll")]
    internal static partial int SetStretchBltMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetBrushOrgEx(nint hdc, int x, int y, nint lppt);

    [LibraryImport("gdi32.dll")]
    internal static partial int StretchDIBits(nint hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, void* lpBits, BITMAPINFOHEADER* lpbmi, uint iUsage, uint rop);

    [LibraryImport("gdi32.dll")]
    internal static partial nint ResetDCW(nint hdc, nint devmode);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    public const int SHCNE_ASSOCCHANGED = 0x08000000;

    [LibraryImport("shell32.dll")]
    internal static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
