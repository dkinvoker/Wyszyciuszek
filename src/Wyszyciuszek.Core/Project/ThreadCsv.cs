using System.Globalization;
using System.Text;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Core.Project;

/// <summary>
/// „Moje nici” w pliku CSV: <c>Nazwa;Kolor;Metry na szpulce</c>. Średnik (jak w polskim Excelu), ale przy odczycie
/// działa też przecinek i tabulator. Kolor jako #RRGGBB (albo bez #).
/// </summary>
public static class ThreadCsv
{
    public const string Header = "Nazwa;Kolor;Metry na szpulce";

    public static void Write(TextWriter writer, IEnumerable<SavedThread> threads)
    {
        writer.WriteLine(Header);
        foreach (var t in threads)
        {
            var color = Rgb.TryParse(t.Color, out var c) ? c.Hex : "#000000";
            var meters = t.SpoolMeters is { } m ? m.ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) : "";
            writer.WriteLine($"{Quote(t.Name)};{color};{meters}");
        }
    }

    /// <summary>Plik z polskimi znakami otwierany dwuklikiem w Excelu — UTF-8 ze znacznikiem BOM.</summary>
    public static void Write(string path, IEnumerable<SavedThread> threads)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Write(w, threads);
    }

    /// <summary>Nici z CSV; wiersze bez nazwy albo bez poprawnego koloru są pomijane (w tym nagłówek).</summary>
    public static List<SavedThread> Read(TextReader reader)
    {
        var result = new List<SavedThread>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            var cells = Split(line);
            if (cells.Count < 2) continue;
            var name = cells[0].Trim();
            var colorText = cells[1].Trim();
            if (!colorText.StartsWith('#')) colorText = "#" + colorText;
            if (name.Length == 0 || !Rgb.TryParse(colorText, out var color)) continue;
            double? meters = null;
            if (cells.Count > 2 && cells[2].Trim() is { Length: > 0 } m
                && double.TryParse(m.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0)
                meters = v;
            result.Add(new SavedThread { Name = name, Color = color.Hex, SpoolMeters = meters });
        }
        return result;
    }

    public static List<SavedThread> Read(string path)
    {
        using var r = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(r);
    }

    /// <summary>Dołącza nici z pliku do listy; ta sama nazwa i kolor — aktualizuje tylko metry. Zwraca liczbę nowych.</summary>
    public static int Merge(List<SavedThread> into, IEnumerable<SavedThread> imported)
    {
        int added = 0;
        foreach (var t in imported)
        {
            var same = into.FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.CurrentCultureIgnoreCase)
                                                && string.Equals(x.Color, t.Color, StringComparison.OrdinalIgnoreCase));
            if (same is not null)
            {
                if (t.SpoolMeters is not null) same.SpoolMeters = t.SpoolMeters;
                continue;
            }
            into.Add(t);
            added++;
        }
        return added;
    }

    private static string Quote(string s) =>
        s.IndexOfAny([';', ',', '"', '\t']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>Podział wiersza: separator to pierwszy z ; , tab występujący poza cudzysłowem.</summary>
    private static List<string> Split(string line)
    {
        char sep = ';';
        bool quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (!quoted && ch is ';' or ',' or '\t') { sep = ch; break; }
        }
        var cells = new List<string>();
        var cell = new StringBuilder();
        quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == sep && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(ch);
            }
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
