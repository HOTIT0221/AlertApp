<#
    Builds a portable, no-install version of Tempo: a self-contained folder that runs in place
    and keeps its data (reminders + settings) in a `Data` subfolder next to the executable,
    thanks to the `Tempo.portable` marker file. Nothing is written to Program Files, and the
    only per-user state is whatever Windows itself records for toast notifications.

    Usage:
        powershell -ExecutionPolicy Bypass -File .\build-portable.ps1

    Output:
        dist\Tempo-Portable-1.0.0.zip     (unzip anywhere writable and run Tempo.exe)
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [switch]$SkipPublish   # reuse an existing publish\Tempo (e.g. produced by build-installer.ps1)
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$publishDir = Join-Path $root "publish\Tempo"
$proj = Join-Path $root "src\Tempo\Tempo.csproj"

if ($SkipPublish -and (Test-Path (Join-Path $publishDir "Tempo.exe"))) {
    Write-Host "==> Reusing existing publish at $publishDir" -ForegroundColor Cyan
}
else {
    Write-Host "==> Publishing Tempo ($Configuration / $Runtime)..." -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    & $dotnet publish $proj -c $Configuration -r $Runtime -p:Platform=x64 `
        -p:WindowsAppSDKSelfContained=true -p:SelfContained=true --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

# Stage a copy so the portable marker never ends up in the installer's publish output.
$staging = Join-Path $root "dist\portable-staging"
$appFolder = Join-Path $staging "Tempo"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null

Write-Host "==> Staging portable copy..." -ForegroundColor Cyan
# robocopy exit codes 0-7 are success; swallow them.
robocopy $publishDir $appFolder /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)." } else { $global:LASTEXITCODE = 0 }

# The marker file that switches the app into portable mode.
@"
This file makes Tempo run in portable mode.

While it is present next to Tempo.exe, Tempo stores its reminders and settings in the
'Data' folder beside the app instead of under %LOCALAPPDATA%. Keep this folder together
and in a writable location (a USB stick, Downloads, etc.). Delete this file to make this
copy use per-user data instead.
"@ | Set-Content -Path (Join-Path $appFolder "Tempo.portable") -Encoding UTF8

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zip = Join-Path $dist "Tempo-Portable-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "==> Zipping portable package..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Zip the staging root so the archive contains a top-level 'Tempo\' folder.
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $staging, $zip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

Remove-Item $staging -Recurse -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "==> Portable package written: $zip ($mb MB)" -ForegroundColor Green
Write-Host "    Unzip anywhere writable and run Tempo\Tempo.exe." -ForegroundColor Green
