using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Wyszyciuszek.Launcher;

/// <summary>
/// Jednoplikowy starter: rozpakowuje osadzoną aplikację (raz na wersję) do %LOCALAPPDATA%\Wyszyciuszek\app\&lt;id&gt;,
/// uruchamia ją z przekazaniem argumentów (np. projekt .wyszycie) i kładzie obok siebie skrypt odinstalowania.
/// </summary>
internal static partial class Program
{
    private const string AppFolderName = "Wyszyciuszek";
    private const string AppExe = "Wyszyciuszek.exe";
    private const string CompleteMarker = ".complete";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // po aktualizacji z aplikacji zostaje poprzedni plik startera — już niepotrzebny
            if (Environment.ProcessPath is { } me) TryDeleteFile(me + ".old");
            var appDir = EnsureExtracted();
            var psi = new ProcessStartInfo(Path.Combine(appDir, AppExe))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            // aplikacja kojarzy pliki .wyszycie z TYM exe (rozpakowana kopia zmienia folder przy każdej wersji)
            if (Environment.ProcessPath is { } self) psi.Environment["WYSZYCIUSZEK_LAUNCHER"] = self;

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Nie udało się uruchomić procesu aplikacji.");
            TryWriteUninstallScript();

            // tryb linii poleceń: czekamy na wynik i zwracamy kod wyjścia; okno — od razu kończymy
            if (!args.Any(a => a.StartsWith("--export-", StringComparison.OrdinalIgnoreCase))) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBoxW(0, $"Nie udało się uruchomić Wyszyciuszka.\n\n{ex.Message}", "Wyszyciuszek", 0x10 /* MB_ICONERROR */);
            return 1;
        }
    }

    private static Stream OpenResource(string name) =>
        typeof(Program).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Ten plik nie zawiera aplikacji (zbuduj go przez publish.ps1).");

    /// <summary>Zwraca folder z rozpakowaną aplikacją; rozpakowuje tylko przy pierwszym starcie danej wersji.</summary>
    private static string EnsureExtracted()
    {
        string id;
        using (var reader = new StreamReader(OpenResource("payload.id")))
            id = reader.ReadToEnd().Trim().ToLowerInvariant();
        if (id.Length < 12) throw new InvalidDataException("Uszkodzony identyfikator wersji.");
        id = id[..12];

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "app");
        var target = Path.Combine(root, id);
        if (IsComplete(target, id)) return target;

        Directory.CreateDirectory(root);
        var temp = $"{target}.tmp{Environment.ProcessId}";
        TryDeleteDirectory(temp);
        using (var zip = OpenResource("payload.zip"))
            ZipFile.ExtractToDirectory(zip, temp, overwriteFiles: true);
        File.WriteAllText(Path.Combine(temp, CompleteMarker), id);

        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // niedokończona wcześniejsza próba
            Directory.Move(temp, target);
        }
        catch (IOException) when (IsComplete(target, id))
        {
            // równolegle uruchomiona druga kopia zdążyła pierwsza — użyj jej wyniku
            TryDeleteDirectory(temp);
        }

        CleanupOldVersions(root, id);
        return target;
    }

    private static bool IsComplete(string dir, string id) =>
        File.Exists(Path.Combine(dir, AppExe)) &&
        File.Exists(Path.Combine(dir, CompleteMarker)) &&
        File.ReadAllText(Path.Combine(dir, CompleteMarker)).Trim() == id;

    /// <summary>Usuwa foldery starszych wersji (jeśli któraś jeszcze działa, zostanie usunięta następnym razem).</summary>
    private static void CleanupOldVersions(string root, string currentId)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (Path.GetFileName(dir).Equals(currentId, StringComparison.OrdinalIgnoreCase)) continue;
                TryDeleteDirectory(dir);
            }
        }
        catch
        {
            // sprzątanie jest opcjonalne
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // jeszcze zajęty — spróbujemy następnym razem
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // plik w użyciu — spróbujemy przy kolejnym starcie
        }
    }

    // ------------------------------------------------------------------ odinstalowanie

    private const string UninstallScriptName = "Wyszyciuszek - odinstaluj.cmd";

    /// <summary>
    /// Kładzie obok tego exe skrypt, który usuwa rozpakowaną aplikację z %LOCALAPPDATA%.
    /// .cmd zamiast .ps1, bo uruchamia się dwuklikiem (PowerShell otworzyłby się w Notatniku).
    /// </summary>
    private static void TryWriteUninstallScript()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir)) return;
            var path = Path.Combine(dir, UninstallScriptName);
            var content = UninstallScript.ReplaceLineEndings("\r\n"); // cmd.exe wymaga CRLF
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // folder tylko do odczytu itp. — aplikacja i tak działa
        }
    }

    private const string UninstallScript = """
        @echo off
        setlocal
        chcp 65001 <nul >nul
        title Wyszyciuszek - odinstalowanie
        set "DATADIR=%LOCALAPPDATA%\Wyszyciuszek"
        set "APPDIR=%DATADIR%\app"

        echo.
        echo   ===  WYSZYCIUSZEK - odinstalowanie  ===
        echo.
        if not exist "%DATADIR%" goto :nothing

        tasklist /fi "imagename eq Wyszyciuszek.exe" /nh <nul 2>nul | find /i "Wyszyciuszek.exe" >nul
        if not errorlevel 1 goto :running

        echo   Zostanie usunięta rozpakowana aplikacja:
        echo     %APPDIR%
        echo   oraz skojarzenie plików .wyszycie (przestaną się otwierać dwuklikiem).
        echo.
        echo   Twoje projekty .wyszycie, obrazki, pliki PDF, PNG i SVG zostaną nietknięte.
        echo.
        set "ANSWER="
        set /p "ANSWER=  Odinstalować? [T/N] "
        if /i "%ANSWER%"=="t" goto :remove
        if /i "%ANSWER%"=="tak" goto :remove
        goto :cancel

        :remove
        call :unassociate
        rmdir /s /q "%APPDIR%" 2>nul
        if exist "%APPDIR%" goto :failed

        echo.
        echo   Usunąć także ustawienia programu (w tym listę „Moje nici”)?
        set "ANSWER="
        set /p "ANSWER=  Usunąć ustawienia? [T/N] "
        if /i "%ANSWER%"=="t" rmdir /s /q "%DATADIR%" 2>nul
        if /i "%ANSWER%"=="tak" rmdir /s /q "%DATADIR%" 2>nul

        echo.
        echo   Gotowe - Wyszyciuszek został odinstalowany.
        echo   Plik Wyszyciuszek.exe możesz teraz po prostu usunąć.
        echo   Jeśli uruchomisz go ponownie, aplikacja rozpakuje się od nowa.
        echo.
        pause
        (goto) 2>nul & del "%~f0"

        :nothing
        call :unassociate
        echo   Nie ma czego usuwać - aplikacja nie jest rozpakowana na tym komputerze.
        echo.
        pause
        (goto) 2>nul & del "%~f0"

        :unassociate
        rem skojarzenie .wyszycie (HKCU) - rozszerzenie usuwamy tylko, jeśli wskazuje na Wyszyciuszka
        reg delete "HKCU\Software\Classes\Wyszyciuszek.Projekt" /f >nul 2>nul
        reg query "HKCU\Software\Classes\.wyszycie" /ve 2>nul | find /i "Wyszyciuszek.Projekt" >nul
        if not errorlevel 1 reg delete "HKCU\Software\Classes\.wyszycie" /f >nul 2>nul
        ie4uinit.exe -show >nul 2>nul
        exit /b 0

        :running
        echo   Wyszyciuszek jest teraz uruchomiony.
        echo   Zamknij go - najpierw zapisz projekt! - i uruchom ten plik jeszcze raz.
        echo.
        pause
        exit /b 1

        :cancel
        echo.
        echo   Anulowano - nic nie zostało usunięte.
        echo.
        pause
        exit /b 0

        :failed
        echo.
        echo   Nie udało się usunąć wszystkiego - może któryś plik jest otwarty.
        echo   Zamknij Wyszyciuszka i spróbuj ponownie albo usuń ręcznie folder:
        echo     %APPDIR%
        echo.
        pause
        exit /b 1

        """;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
