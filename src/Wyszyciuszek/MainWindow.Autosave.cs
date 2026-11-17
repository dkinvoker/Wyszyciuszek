using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wyszyciuszek.Canvas;
using Wyszyciuszek.Core.Pattern;
using Wyszyciuszek.Core.Project;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wyszyciuszek;

/// <summary>
/// Autozapis co minutę (tylko gdy są niezapisane zmiany) i przywracanie po nieoczekiwanym zamknięciu programu.
/// Kopia znika po zapisaniu projektu, odrzuceniu zmian i normalnym zamknięciu.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AutosaveMaxAge = TimeSpan.FromDays(30);

    private DispatcherQueueTimer? _autosaveTimer;
    private string? _autosavePath;
    private int _changeCount, _autosavedCount;
    private bool _autosaving;
    private readonly List<AutosaveEntry> _recoverable = [];

    private static string AutosaveFolder => Path.Combine(AppInfo.DataFolder, "autozapis");

    private static long ProcessStartTicks(Process p) => p.StartTime.ToUniversalTime().Ticks;

    private void SetupAutosave()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            _autosavePath = Autosave.FileFor(AutosaveFolder, me.Id, ProcessStartTicks(me));
        }
        catch
        {
            return; // bez autozapisu — program działa normalnie
        }
        _autosaveTimer = DispatcherQueue.CreateTimer();
        _autosaveTimer.Interval = AutosaveInterval;
        _autosaveTimer.Tick += (_, _) => _ = AutosaveNowAsync();
        _autosaveTimer.Start();
        Closed += (_, _) =>
        {
            _autosaveTimer.Stop();
            // tu dochodzimy tylko po zapisaniu albo świadomym odrzuceniu zmian
            DiscardAutosave();
        };
        DispatcherQueue.TryEnqueue(FindRecoverable);
    }

    /// <summary>Zapisuje kopię, jeśli od ostatniej coś się zmieniło (w tle — praca w oknie trwa dalej).</summary>
    private async Task AutosaveNowAsync()
    {
        if (_autosaving || _autosavePath is not { } path || _session is not { IsDirty: true } session || !session.Document.HasPattern) return;
        if (_changeCount == _autosavedCount) return;
        int count = _changeCount;
        var d = session.Document;
        // migawka: mapa i paleta kopiowane, żeby edycja w trakcie zapisu niczego nie pomieszała
        var copy = d.Snapshot();
        var original = session.FilePath;
        _autosaving = true;
        try
        {
            await Task.Run(() => Autosave.Save(path, copy, original));
            _autosavedCount = count;
        }
        catch
        {
            // pełny dysk, brak dostępu… — spróbujemy za minutę
        }
        finally
        {
            _autosaving = false;
        }
    }

    /// <summary>Usuwa kopię tego okna (po zapisie, odrzuceniu zmian albo zamknięciu).</summary>
    private void DiscardAutosave()
    {
        _autosavedCount = _changeCount;
        if (_autosavePath is { } path) Autosave.Delete(path);
    }

    // ================================================================== przywracanie

    /// <summary>Kopie po programach, które już nie działają (nieoczekiwane zamknięcie) — do przywrócenia.</summary>
    private async void FindRecoverable()
    {
        List<AutosaveEntry> entries;
        try
        {
            entries = await Task.Run(() => Autosave.List(AutosaveFolder));
        }
        catch
        {
            return;
        }
        _recoverable.Clear();
        foreach (var e in entries)
        {
            if (e.Path.Equals(_autosavePath, StringComparison.OrdinalIgnoreCase) || IsRunning(e.ProcessId, e.ProcessStartTicks)) continue;
            if (DateTime.UtcNow - e.SavedUtc > AutosaveMaxAge)
            {
                Autosave.Delete(e.Path);
                continue;
            }
            _recoverable.Add(e);
        }
        ShowRecoverBar();
    }

    private static bool IsRunning(int pid, long startTicks)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return ProcessStartTicks(p) == startTicks;
        }
        catch
        {
            return false;
        }
    }

    private void ShowRecoverBar()
    {
        if (_recoverable.Count == 0)
        {
            RecoverBar.IsOpen = false;
            return;
        }
        var e = _recoverable[0];
        var when = e.SavedUtc.ToLocalTime();
        var more = _recoverable.Count > 1 ? $" Starszych kopii: {_recoverable.Count - 1}." : "";
        RecoverBar.Message = $"„{e.Title}” — kopia z {when:d.MM, HH:mm}. Program zamknął się bez zapisania zmian; możesz je odzyskać.{more}";
        RecoverBar.IsOpen = true;
    }

    private async void Recover_Click(object sender, RoutedEventArgs e)
    {
        if (_recoverable.Count == 0) return;
        var entry = _recoverable[0];
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            var doc = await Task.Run(() => ProjectFile.Load(entry.Path));
            LoadedImage? image = null;
            if (doc.SourceImage is { Length: > 0 } src)
            {
                try { image = await ImageLoader.DecodeAsync(src); } catch { }
            }
            var original = entry.OriginalPath is { } p && File.Exists(p) ? p : null;
            StartSession(doc, image, original);
            _session!.MarkDirty();
            _changeCount++; // od razu nowa kopia — już pod nazwą tego okna
            UpdateTitle();
            Autosave.Delete(entry.Path);
            _recoverable.RemoveAt(0);
            ShowRecoverBar();
            ShowToast("Przywrócono projekt", original is null
                ? "Zapisz go (Ctrl+S), żeby nie zginął."
                : $"Zmiany nie są jeszcze zapisane w {Path.GetFileName(original)} — zapisz je (Ctrl+S).", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Nie udało się przywrócić kopii", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RecoverDiscard_Click(object sender, RoutedEventArgs e)
    {
        if (_recoverable.Count == 0) return;
        Autosave.Delete(_recoverable[0].Path);
        _recoverable.RemoveAt(0);
        ShowRecoverBar();
    }
}
