using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek;

/// <summary>Informacje o wersji (numer z src/Directory.Build.props + numer budowania z GitHub Actions) i ścieżki.</summary>
public static class AppInfo
{
    private static readonly Version AssemblyVersion = typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Numer, np. „1.0.17”.</summary>
    public static string Version => $"{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{Math.Max(0, AssemblyVersion.Build)}";

    public static string Name => "Wyszyciuszek";

    public static string FullName => $"{Name} {Version}";

    /// <summary>%LOCALAPPDATA%\Wyszyciuszek — ustawienia (i rozpakowana aplikacja, gdy uruchamia ją starter).</summary>
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wyszyciuszek");

    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    private static AppSettings? _settings;

    public static AppSettings Settings => _settings ??= AppSettings.Load(SettingsPath);

    public static void SaveSettings()
    {
        try { Settings.Save(SettingsPath); } catch { /* brak zapisu ustawień nie może psuć pracy */ }
    }
}
