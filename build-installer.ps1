<#
    Publishes Tempo (self-contained, unpackaged WinUI 3) and builds a Windows installer with Inno Setup.

    Usage:
        pwsh ./build-installer.ps1              # publish + build installer
        pwsh ./build-installer.ps1 -NoInstaller # publish only

    Output:
        publish/Tempo/           self-contained app folder
        dist/Tempo-Setup-1.0.0.exe   installer (if Inno Setup is available)
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$NoInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$publishDir = Join-Path $root "publish\Tempo"
$proj = Join-Path $root "src\Tempo\Tempo.csproj"

Write-Host "==> Publishing Tempo ($Configuration / $Runtime)..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& $dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    --self-contained true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "==> Published to $publishDir" -ForegroundColor Green

if ($NoInstaller) { return }

# Locate Inno Setup compiler (ISCC).
$iscc = $null
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)
foreach ($c in $candidates) { if (Test-Path $c) { $iscc = $c; break } }
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup"
    Write-Warning "The published app is ready in $publishDir; skipping installer build."
    return
}

Write-Host "==> Building installer with $iscc ..." -ForegroundColor Cyan
$iss = Join-Path $root "installer\Tempo.iss"
& $iscc "/DSourceDir=$publishDir" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

Write-Host "==> Installer written to $(Join-Path $root 'dist')" -ForegroundColor Green
