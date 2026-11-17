using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wyszyciuszek.Core.Updates;

namespace Wyszyciuszek;

/// <summary>Sprawdzanie i instalacja nowej wersji (tylko gdy program uruchomiono z jednoplikowego exe).</summary>
public sealed partial class MainWindow
{
    private ReleaseInfo? _release;

    private void SetupUpdates()
    {
        if (!AppInfo.Settings.CheckUpdates || Updater.LauncherPath is null) return;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.Tick += async (t, _) =>
        {
            t.Stop();
            try
            {
                _release = await Updater.CheckAsync();
                if (_release is not null && _release.DisplayVersion != AppInfo.Settings.SkippedVersion) ShowUpdateBar(_release);
            }
            catch
            {
                // brak internetu — sprawdzimy przy następnym uruchomieniu
            }
        };
        timer.Start();
    }

    private void ShowUpdateBar(ReleaseInfo release)
    {
        UpdateBar.Title = $"Nowa wersja {release.DisplayVersion}";
        var firstLine = release.Notes.Split('\n').Select(l => l.Trim().TrimStart('-', '*', '#', ' ')).FirstOrDefault(l => l.Length > 0) ?? "";
        UpdateBar.Message = firstLine.Length > 120 ? firstLine[..120] + "…" : firstLine;
        bool canInstall = Updater.CanSelfUpdate && release.CanInstall;
        var action = new Button { Content = canInstall ? "Zainstaluj" : "Pobierz ze strony" };
        action.Click += async (_, _) =>
        {
            if (!canInstall)
            {
                Updater.OpenReleasePage(release);
                return;
            }
            await InstallUpdateAsync(release, action);
        };
        UpdateBar.ActionButton = action;
        UpdateBar.CloseButtonClick += (_, _) =>
        {
            AppInfo.Settings.SkippedVersion = release.DisplayVersion;
            AppInfo.SaveSettings();
        };
        UpdateBar.IsOpen = true;
    }

    private async Task InstallUpdateAsync(ReleaseInfo release, Button action)
    {
        action.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => UpdateBar.Message = $"Pobieram… {p * 100:0}%");
            await Updater.InstallAsync(release, progress);
            UpdateBar.Message = "Gotowe — nowa wersja uruchomi się po ponownym otwarciu programu.";
            var restart = new Button { Content = "Uruchom ponownie" };
            restart.Click += async (_, _) =>
            {
                if (!await ConfirmDiscardAsync()) return;
                Updater.Restart(_session?.FilePath);
                _closeConfirmed = true;
                Close();
            };
            UpdateBar.ActionButton = restart;
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Message = $"Nie udało się zainstalować: {ex.Message}";
            action.IsEnabled = true;
        }
    }
}
