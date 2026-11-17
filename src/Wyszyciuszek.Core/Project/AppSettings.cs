using System.Text.Json;
using System.Text.Json.Serialization;
using Wyszyciuszek.Core.Imaging;
using Wyszyciuszek.Core.Pattern;

namespace Wyszyciuszek.Core.Project;

/// <summary>Nić użytkownika (w pliku ustawień).</summary>
public sealed class SavedThread
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#000000";

    public ThreadColor ToThread() => new(Name, Rgb.TryParse(Color, out var c) ? c : Rgb.Black);
}

/// <summary>Ustawienia programu: „Moje nici”, ostatnie pliki, domyślne opcje. Plik JSON w %LOCALAPPDATA%\Wyszyciuszek.</summary>
public sealed class AppSettings
{
    public List<SavedThread> MyThreads { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
    public SimplifyOptions LastOptions { get; set; } = new();
    public PrintSettings LastPrint { get; set; } = new();
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Skalibrowana skala projektora/ekranu: fizyczne piksele na milimetr, osobno dla każdego ekranu.</summary>
    public Dictionary<string, double> ScreenScale { get; set; } = [];
    /// <summary>Projektor: jasne linie na czarnym tle (true) albo ciemne na białym — ekran jako podświetlana szyba.</summary>
    public bool ProjectorDark { get; set; } = true;
    /// <summary>Wersja, o której użytkownik powiedział „nie teraz”.</summary>
    public string? SkippedVersion { get; set; }

    [JsonIgnore]
    public IReadOnlyList<ThreadColor> Threads => MyThreads.Select(t => t.ToThread()).ToList();

    public void AddRecent(string path, int max = 8)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > max) RecentFiles.RemoveRange(max, RecentFiles.Count - max);
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize(fs, SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings(); // uszkodzony plik ustawień nie może blokować startu
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var fs = File.Create(temp)) JsonSerializer.Serialize(fs, this, SettingsJson.Default.AppSettings);
        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext
{
}
