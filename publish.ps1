# Buduje Wyszyciuszka (Native AOT):
#   publish\win-<arch>\        - folder z aplikacją (exe + biblioteki Windows App SDK i Win2D)
#   dist\Wyszyciuszek.exe      - JEDEN plik do rozdania (aplikacja spakowana w środku)
#
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Arch arm64
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -BuildNumber 17     (tak robi GitHub Actions)
param(
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [int]$BuildNumber = 0,
    [switch]$NoSingleFile
)
$ErrorActionPreference = 'Stop'

$app = Join-Path $PSScriptRoot 'src\Wyszyciuszek\Wyszyciuszek.csproj'
$launcher = Join-Path $PSScriptRoot 'src\Wyszyciuszek.Launcher\Wyszyciuszek.Launcher.csproj'
$out = Join-Path $PSScriptRoot "publish\win-$Arch"
$version = "-p:BuildNumber=$BuildNumber"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish $app -c Release -r "win-$Arch" -o $out $version
if ($LASTEXITCODE -ne 0) { throw "Publikacja aplikacji nie powiodła się (kod $LASTEXITCODE)." }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Folder:      {0}  ({1:N0} MB)" -f $out, $size) -ForegroundColor Green

if (-not $NoSingleFile) {
    $dist = Join-Path $PSScriptRoot 'dist'
    $name = if ($Arch -eq 'x64') { 'Wyszyciuszek.exe' } else { "Wyszyciuszek-$Arch.exe" }
    $tmp = Join-Path $PSScriptRoot "artifacts\launcher-$Arch"
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }

    # AppPublishDir bez końcowego "\" (inaczej \" psuje cudzysłów w linii poleceń)
    dotnet publish $launcher -c Release -r "win-$Arch" -o $tmp "-p:AppPublishDir=$out" $version
    if ($LASTEXITCODE -ne 0) { throw "Budowanie pliku jednoplikowego nie powiodło się (kod $LASTEXITCODE)." }

    New-Item -ItemType Directory -Force $dist | Out-Null
    Copy-Item (Join-Path $tmp 'Wyszyciuszek.exe') (Join-Path $dist $name) -Force
    $one = Get-Item (Join-Path $dist $name)
    Write-Host ("Jeden plik:  {0}  ({1:N0} MB)" -f $one.FullName, ($one.Length / 1MB)) -ForegroundColor Green
}
