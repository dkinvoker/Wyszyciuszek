using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Wyszyciuszek.Core.Updates;

namespace Wyszyciuszek;

/// <summary>
/// Aktualizacje (jak w Kasetowniku): kilka sekund po starcie — najwyżej raz na ~20 godzin — pytanie GitHuba o nowe
/// wydanie, pasek „Nowa wersja” z przyciskami Zaktualizuj / Co nowego? / Pomiń tę wersję, pobranie i podmiana exe,
/// ponowne uruchomienie. Ustawienia i „Sprawdź teraz” — pod numerem wersji w pasku tytułu.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(20);

    private ReleaseInfo? _update;
    private bool _updating, _updateInstalled, _updateFailed;

    private void SetupUpdates()
    {
        var s = AppInfo.Settings;
        AutoUpdateCheck.IsChecked = s.CheckUpdates;
        if (!s.CheckUpdates) return;
        // testowy adres wydań (WYSZYCIUSZEK_UPDATE_FEED) — bez przerwy między sprawdzeniami
        if (!Updater.HasFeedOverride && s.LastUpdateCheck is { } last && DateTime.UtcNow - last < UpdateCheckInterval) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            await CheckForUpdatesAsync(manual: false);
        });
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        var s = AppInfo.Settings;
        if (manual)
        {
            UpdateStatusText.Text = "Sprawdzam…";
            CheckUpdateButton.IsEnabled = false;
        }
        try
        {
            var release = await Updater.CheckAsync();
            s.LastUpdateCheck = DateTime.UtcNow;
            AppInfo.SaveSettings();
            if (release is null)
            {
                if (manual) UpdateStatusText.Text = $"Masz najnowszą wersję ({AppInfo.Version}).";
                return;
            }
            if (manual) UpdateStatusText.Text = $"Jest nowa wersja {release.DisplayVersion}.";
            else if (s.SkippedVersion == release.Tag) return; // „pomiń tę wersję” — nie przypominamy
            ShowUpdateBar(release);
        }
        catch
        {
            // bez internetu, GitHub niedostępny… — przy automatycznym sprawdzaniu po cichu
            if (manual) UpdateStatusText.Text = "Nie udało się sprawdzić (brak połączenia z GitHubem?).";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private bool CanInstall(ReleaseInfo r) => !_updateFailed && r.CanInstall && Updater.CanSelfUpdate;

    private void ShowUpdateBar(ReleaseInfo r)
    {
        _update = r;
        _updateInstalled = _updateFailed = false;
        UpdateDot.Visibility = Visibility.Visible;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = $"Nowa wersja {r.DisplayVersion}";
        UpdateBar.Message = CanInstall(r)
            ? "Pobiorę ją z GitHuba, sprawdzę sumę kontrolną i podmienię program — potem wystarczy uruchomić go ponownie."
            : "Pobierzesz ją ze strony wydania na GitHubie.";
        UpdateActionButton.Content = CanInstall(r) ? "Zaktualizuj" : "Otwórz stronę";
        UpdateActionButton.IsEnabled = true;
        UpdateSkipLink.Visibility = Visibility.Visible;
        UpdateNotesLink.Visibility = string.IsNullOrWhiteSpace(r.Notes) ? Visibility.Collapsed : Visibility.Visible;
        UpdateBar.IsOpen = true;
    }

    private async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r || _updating) return;
        if (_updateInstalled)
        {
            await RestartAfterUpdateAsync();
            return;
        }
        if (!CanInstall(r))
        {
            try { Updater.OpenReleasePage(r); } catch { }
            UpdateBar.IsOpen = false;
            return;
        }

        _updating = true;
        UpdateActionButton.IsEnabled = false;
        UpdateSkipLink.Visibility = Visibility.Collapsed;
        try
        {
            UpdateBar.Message = "Pobieram…";
            // postęp dociera do okna z opóźnieniem — po zakończeniu nie nadpisuje już końcowego komunikatu
            var progress = new Progress<double>(p => { if (_updating) UpdateBar.Message = $"Pobieram… {p * 100:0}%"; });
            await Updater.InstallAsync(r, progress);
            _updating = false;
            _updateInstalled = true;
            UpdateBar.Severity = InfoBarSeverity.Success;
            UpdateBar.Title = $"Wersja {r.DisplayVersion} jest gotowa";
            UpdateBar.Message = "Uruchom program ponownie, żeby z niej korzystać (albo po prostu przy następnym starcie).";
            UpdateActionButton.Content = "Uruchom ponownie";
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateFailed = true;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "Aktualizacja nie powiodła się";
            UpdateBar.Message = $"{ex.Message} Program został bez zmian — nową wersję pobierzesz ze strony wydania.";
            UpdateActionButton.Content = "Otwórz stronę";
        }
        finally
        {
            _updating = false;
            UpdateActionButton.IsEnabled = true;
        }
    }

    /// <summary>Ponowne uruchomienie po aktualizacji: najpierw zapis (albo odrzucenie) zmian, potem nowy starter.</summary>
    private async Task RestartAfterUpdateAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            Updater.Restart(_session?.FilePath);
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się uruchomić programu ponownie", ex.Message, InfoBarSeverity.Error);
            return;
        }
        DiscardAutosave();
        _closeConfirmed = true;
        Close();
    }

    private async void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r) return;
        var dlg = new ContentDialog
        {
            Title = $"Co nowego w wersji {r.DisplayVersion}",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = r.Notes.Trim(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 520 },
            },
            CloseButtonText = "Zamknij",
        };
        await ShowDialogAsync(dlg);
    }

    private void UpdateSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r) return;
        AppInfo.Settings.SkippedVersion = r.Tag;
        AppInfo.SaveSettings();
        UpdateBar.IsOpen = false;
    }

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        AppInfo.Settings.CheckUpdates = AutoUpdateCheck.IsChecked == true;
        AppInfo.SaveSettings();
    }

    private async void CheckUpdateNow_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private void ReleasesPage_Click(object sender, RoutedEventArgs e)
    {
        try { Updater.OpenReleasePage(null); } catch { }
    }

    private void AboutFlyout_Opening(object? sender, object e)
    {
        AboutTitle.Text = AppInfo.FullName;
        if (_update is { } r && UpdateStatusText.Text.Length == 0) UpdateStatusText.Text = $"Jest nowa wersja {r.DisplayVersion}.";
    }

    /// <summary>
    /// Przycisk wersji leży w pasku tytułu, który w całości służy do przeciągania okna — jego prostokąt trzeba
    /// z tego wyłączyć, żeby dało się go kliknąć.
    /// </summary>
    private void UpdateTitleBarPassthrough()
    {
        var xr = RootHost.XamlRoot;
        if (xr is null || VersionButton.ActualWidth <= 0) return;
        try
        {
            double s = xr.RasterizationScale;
            var b = VersionButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, VersionButton.ActualWidth, VersionButton.ActualHeight));
            var rect = new RectInt32((int)Math.Floor(b.X * s), (int)Math.Floor(b.Y * s), (int)Math.Ceiling(b.Width * s), (int)Math.Ceiling(b.Height * s));
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
        }
        catch
        {
            // starszy system bez tej funkcji — wersję widać, tylko nie da się jej kliknąć
        }
    }
}
