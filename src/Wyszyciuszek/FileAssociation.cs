using Microsoft.Win32;
using Wyszyciuszek.Core.Project;

namespace Wyszyciuszek;

/// <summary>
/// Skojarzenie plików .wyszycie z aplikacją — dwuklik w Eksploratorze otwiera projekt.
/// Wpisy idą do HKCU\Software\Classes (bez uprawnień administratora); skrypt odinstalowania je usuwa.
/// </summary>
internal static class FileAssociation
{
    public const string ProgId = "Wyszyciuszek.Projekt";

    /// <summary>Jednoplikowy starter podaje tu swoją ścieżkę — to on ma otwierać pliki, nie rozpakowana kopia.</summary>
    public const string LauncherVariable = "WYSZYCIUSZEK_LAUNCHER";

    /// <summary>Ustawiona (dowolna wartość) — program nie zapisuje niczego w rejestrze (testy, wersja przenośna).</summary>
    public const string NoRegistryVariable = "WYSZYCIUSZEK_NO_REGISTRY";

    public static void EnsureRegistered()
    {
#if !DEBUG
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(NoRegistryVariable))) return;
        try
        {
            var exe = Environment.GetEnvironmentVariable(LauncherVariable);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            bool changed = false;
            changed |= Set(classes, ProgId, null, "Wzór do haftu (Wyszyciuszek)");
            changed |= Set(classes, $@"{ProgId}\DefaultIcon", null, $"\"{exe}\",0");
            changed |= Set(classes, $@"{ProgId}\shell\open\command", null, $"\"{exe}\" \"%1\"");
            changed |= Set(classes, ProjectFile.Extension, null, ProgId);
            changed |= Set(classes, $@"{ProjectFile.Extension}\OpenWithProgids", ProgId, "");
            if (changed) Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, 0, 0, 0);
        }
        catch
        {
            // brak dostępu do rejestru — aplikacja działa normalnie, tylko bez dwukliku
        }
#endif
    }

    private static bool Set(RegistryKey root, string path, string? name, string value)
    {
        using var key = root.CreateSubKey(path);
        if (key.GetValue(name) is string current && current == value) return false;
        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }
}
