using System.Globalization;
using Wyszyciuszek.Core.Pattern;

namespace Wyszyciuszek.Core.Project;

/// <summary>Kopia z autozapisu: projekt, skąd pochodził i który proces ją zapisał.</summary>
/// <param name="OriginalPath">Plik projektu, z którego pracowano (null — jeszcze niezapisany).</param>
public sealed record AutosaveEntry(string Path, string Title, string? OriginalPath, DateTime SavedUtc, int ProcessId, long ProcessStartTicks);

/// <summary>
/// Autozapis: co minutę kopia otwartego, niezapisanego projektu w folderze ustawień — awaria, wyłączenie prądu albo
/// zamknięcie komputera nie zabiorą pracy. Plik nazywa się „{proces}-{czas startu}.wyszycie”, więc przy starcie łatwo
/// odróżnić kopie po zamkniętych programach od kopii programu, który właśnie działa obok.
/// </summary>
public static class Autosave
{
    private const string MetaExtension = ".txt";

    public static string FileFor(string folder, int processId, long processStartTicks) =>
        Path.Combine(folder, $"{processId}-{processStartTicks.ToString(CultureInfo.InvariantCulture)}{ProjectFile.Extension}");

    /// <summary>Zapisuje kopię projektu (atomowo) i obok — ścieżkę oryginału.</summary>
    public static void Save(string path, PatternDocument doc, string? originalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ProjectFile.Save(doc, path);
        File.WriteAllText(Path.ChangeExtension(path, MetaExtension), originalPath ?? "");
    }

    /// <summary>Wszystkie kopie w folderze, od najnowszej. Uszkodzone pliki są pomijane.</summary>
    public static List<AutosaveEntry> List(string folder)
    {
        var result = new List<AutosaveEntry>();
        if (!Directory.Exists(folder)) return result;
        foreach (var file in Directory.EnumerateFiles(folder, "*" + ProjectFile.Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file).Split('-');
            if (name.Length != 2 || !int.TryParse(name[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                || !long.TryParse(name[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) continue;
            string? original = null;
            try
            {
                var meta = Path.ChangeExtension(file, MetaExtension);
                if (File.Exists(meta) && File.ReadAllText(meta).Trim() is { Length: > 0 } p) original = p;
            }
            catch
            {
            }
            var title = ProjectFile.ReadTitle(file);
            if (title is null) continue;
            result.Add(new AutosaveEntry(file, title, original, File.GetLastWriteTimeUtc(file), pid, ticks));
        }
        return result.OrderByDescending(e => e.SavedUtc).ToList();
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(Path.ChangeExtension(path, MetaExtension)); } catch { }
    }
}
