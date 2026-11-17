using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Vector;

namespace Wyszyciuszek.Core.Output;

/// <summary>
/// Prosty zapis PDF (wektorowy): ścieżki, kolory i tekst czcionką Helvetica z polskimi znakami.
/// Wymiary stron w mm, oś Y w dół — jak w <see cref="IDrawSurface"/>.
/// </summary>
public sealed class PdfWriter
{
    private const double PtPerMm = 72 / 25.4;
    private readonly string _title;
    private readonly List<(double W, double H, byte[] Content)> _pages = [];
    private PdfSurface? _current;
    private (double W, double H) _currentSize;

    public PdfWriter(string title)
    {
        _title = title;
    }

    public int PageCount => _pages.Count + (_current is null ? 0 : 1);

    /// <summary>Nowa strona; rysowanie przez zwrócone płótno (jednostki: mm).</summary>
    public IDrawSurface BeginPage(double widthMm, double heightMm)
    {
        EndPage();
        _current = new PdfSurface(heightMm);
        _currentSize = (widthMm, heightMm);
        return _current;
    }

    public void EndPage()
    {
        if (_current is null) return;
        _pages.Add((_currentSize.W, _currentSize.H, _current.Finish()));
        _current = null;
    }

    public void Save(Stream output)
    {
        EndPage();
        if (_pages.Count == 0) throw new InvalidOperationException("PDF bez stron.");
        var inv = CultureInfo.InvariantCulture;
        var offsets = new List<long>();
        var buf = new MemoryStream();

        void Raw(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            buf.Write(b);
        }
        void BeginObj(int n)
        {
            while (offsets.Count < n) offsets.Add(0);
            offsets[n - 1] = buf.Position;
            Raw($"{n} 0 obj\n");
        }

        // 1 katalog, 2 drzewo stron, 3 kodowanie, 4–5 czcionki, 6 informacje, potem (strona, treść) × n
        const int Catalog = 1, PagesObj = 2, EncodingObj = 3, FontRegular = 4, FontBold = 5, Info = 6, FirstPage = 7;
        buf.Write("%PDF-1.4\n%âãÏÓ\n"u8);

        BeginObj(Catalog);
        Raw($"<< /Type /Catalog /Pages {PagesObj} 0 R >>\nendobj\n");

        BeginObj(PagesObj);
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{FirstPage + i * 2} 0 R"));
        Raw($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\nendobj\n");

        BeginObj(EncodingObj);
        Raw("<< /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [192 " + string.Join(" ", PdfText.PolishGlyphs.Select(g => "/" + g)) + "] >>\nendobj\n");

        BeginObj(FontRegular);
        Raw($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding {EncodingObj} 0 R >>\nendobj\n");
        BeginObj(FontBold);
        Raw($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding {EncodingObj} 0 R >>\nendobj\n");

        BeginObj(Info);
        var date = DateTime.Now.ToString("yyyyMMddHHmmss", inv);
        buf.Write(Encoding.ASCII.GetBytes("<< /Title "));
        buf.Write(PdfText.Utf16String(_title));
        buf.Write(Encoding.ASCII.GetBytes($" /Producer (Wyszyciuszek) /CreationDate (D:{date}) >>\nendobj\n"));

        for (int i = 0; i < _pages.Count; i++)
        {
            var (w, h, content) = _pages[i];
            int pageObj = FirstPage + i * 2, contentObj = pageObj + 1;
            BeginObj(pageObj);
            Raw($"<< /Type /Page /Parent {PagesObj} 0 R /MediaBox [0 0 {F(w * PtPerMm)} {F(h * PtPerMm)}] " +
                $"/Resources << /Font << /F1 {FontRegular} 0 R /F2 {FontBold} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");
            var packed = Deflate(content);
            BeginObj(contentObj);
            Raw($"<< /Length {packed.Length} /Filter /FlateDecode >>\nstream\n");
            buf.Write(packed);
            Raw("\nendstream\nendobj\n");
        }

        long xref = buf.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Raw($"{o:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root {Catalog} 0 R /Info {Info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        buf.Position = 0;
        buf.CopyTo(output);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    internal static string F(double v)
    {
        if (Math.Abs(v) < 0.00005) return "0";
        return v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>Strumień treści jednej strony.</summary>
    private sealed class PdfSurface : IDrawSurface
    {
        private readonly MemoryStream _out = new();

        public PdfSurface(double heightMm)
        {
            // mm → pt i odwrócenie osi Y (PDF liczy od dołu)
            W($"{F(PtPerMm)} 0 0 {F(-PtPerMm)} 0 {F(heightMm * PtPerMm)} cm\n1 J 1 j\n");
        }

        private void W(string s) => _out.Write(Encoding.ASCII.GetBytes(s));

        public byte[] Finish() => _out.ToArray();

        public void PushTransform(Matrix3x2 m) => W($"q {F(m.M11)} {F(m.M12)} {F(m.M21)} {F(m.M22)} {F(m.M31)} {F(m.M32)} cm\n");

        public void PushClip(RectF r) => W($"q {F(r.X)} {F(r.Y)} {F(r.W)} {F(r.H)} re W n\n");

        public void Pop() => W("Q\n");

        private static string Color(Rgb c) => $"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)}";

        private void Path(PathData path)
        {
            var sb = new StringBuilder(path.Commands.Count * 20);
            foreach (var c in path.Commands)
            {
                switch (c.Op)
                {
                    case PathOp.Move: sb.Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)).Append(" m\n"); break;
                    case PathOp.Line: sb.Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)).Append(" l\n"); break;
                    case PathOp.Cubic:
                        sb.Append(F(c.P1.X)).Append(' ').Append(F(c.P1.Y)).Append(' ')
                          .Append(F(c.P2.X)).Append(' ').Append(F(c.P2.Y)).Append(' ')
                          .Append(F(c.P3.X)).Append(' ').Append(F(c.P3.Y)).Append(" c\n");
                        break;
                    case PathOp.Close: sb.Append("h\n"); break;
                }
            }
            W(sb.ToString());
        }

        public void Fill(PathData path, Rgb color)
        {
            if (path.IsEmpty) return;
            W($"{Color(color)} rg\n");
            Path(path);
            W("f*\n");
        }

        public void Stroke(PathData path, Rgb color, float width, float[]? dash = null)
        {
            if (path.IsEmpty) return;
            var d = dash is { Length: > 0 } ? string.Join(" ", dash.Select(v => F(v))) : "";
            W($"{Color(color)} RG {F(width)} w [{d}] 0 d\n");
            Path(path);
            W("S\n");
        }

        public void Text(string text, Vector2 baseline, float size, Rgb color, TextAnchor anchor = TextAnchor.Start, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            float x = baseline.X;
            if (anchor != TextAnchor.Start)
            {
                float w = FontMetrics.Measure(text, size, bold);
                x -= anchor == TextAnchor.Middle ? w / 2 : w;
            }
            W($"BT {Color(color)} rg /{(bold ? "F2" : "F1")} 1 Tf {F(size)} 0 0 {F(-size)} {F(x)} {F(baseline.Y)} Tm ");
            _out.Write(PdfText.LiteralString(text));
            W(" Tj ET\n");
        }
    }
}

/// <summary>Kodowanie tekstu PDF: WinAnsi + polskie litery na kodach 192–207 (przez /Differences).</summary>
public static class PdfText
{
    /// <summary>Nazwy glifów dla kodów 192…207 (ą ć ę ł ń ś ź ż Ą Ć Ę Ł Ń Ś Ź Ż).</summary>
    public static readonly string[] PolishGlyphs =
    [
        "aogonek", "cacute", "eogonek", "lslash", "nacute", "sacute", "zacute", "zdotaccent",
        "Aogonek", "Cacute", "Eogonek", "Lslash", "Nacute", "Sacute", "Zacute", "Zdotaccent",
    ];

    private const string PolishChars = "ąćęłńśźżĄĆĘŁŃŚŹŻ";

    public static byte Encode(char c)
    {
        if (c >= 32 && c <= 126) return (byte)c;
        int p = PolishChars.IndexOf(c);
        if (p >= 0) return (byte)(192 + p);
        return c switch
        {
            'ó' => 0xF3, 'Ó' => 0xD3, '×' => 0xD7, '·' => 0xB7, '°' => 0xB0,
            '–' => 0x96, '—' => 0x97, '„' => 0x84, '”' => 0x94, '“' => 0x93, '…' => 0x85,
            '’' => 0x92, '‘' => 0x91, '•' => 0x95, 'Ø' => 0xD8, 'ø' => 0xF8, '²' => 0xB2, ' ' => 32,
            'é' => 0xE9, 'ü' => 0xFC, 'ö' => 0xF6, 'ä' => 0xE4,
            '←' => (byte)'<', '→' => (byte)'>', '↑' => (byte)'^', '↓' => (byte)'v',
            _ => (byte)'?',
        };
    }

    /// <summary>Napis w nawiasach z ucieczkami: ( ) \ i bajty spoza ASCII jako \ooo.</summary>
    public static byte[] LiteralString(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        sb.Append('(');
        foreach (var ch in text)
        {
            byte b = Encode(ch);
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        sb.Append(')');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Napis UTF-16BE (dla metadanych, np. tytułu) jako ciąg szesnastkowy z BOM.</summary>
    public static byte[] Utf16String(string text)
    {
        var bytes = Encoding.BigEndianUnicode.GetBytes(text);
        return Encoding.ASCII.GetBytes("<FEFF" + Convert.ToHexString(bytes) + ">");
    }
}
