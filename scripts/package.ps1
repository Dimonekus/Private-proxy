#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a single self-contained installer (Inno Setup) that can be copied to
    another Windows 10/11 x64 machine and installed without a .NET runtime or
    internet access. The engine binaries are bundled and their hashes are the
    pinned, verified ones from engine/manifest.json.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Find-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    $candidates += 'C:\Program Files\dotnet\dotnet.exe'

    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) { continue }
        try {
            $sdks = & $candidate --list-sdks 2>$null
            if ($LASTEXITCODE -eq 0 -and $sdks) { return $candidate }
        }
        catch { }
    }

    throw 'Не найден .NET SDK 8+. Установите: https://dotnet.microsoft.com/download'
}

$dotnet = Find-Dotnet
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray

$publishDir = Join-Path $root 'artifacts\publish'
$app = Join-Path $root 'src\PrivateProxy.App\PrivateProxy.App.csproj'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

if (-not (Test-Path $iscc)) {
    throw "Inno Setup не найден: $iscc"
}

# 1. Engine binaries must exist locally (they are not committed to git).
if (-not (Test-Path (Join-Path $root 'engine\bin\winws.exe'))) {
    Write-Host 'Движок не найден, загружаю с проверкой хэшей…' -ForegroundColor Yellow
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\fetch-engine.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 2. Self-contained publish: bundles the .NET runtime so the target needs nothing.
Write-Host 'Публикую self-contained (win-x64)…' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $dotnet publish $app -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 3. Compile the installer.
Write-Host 'Собираю установщик…' -ForegroundColor Cyan
& $iscc (Join-Path $root 'installer\PrivateProxy.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Готово: $root\artifacts\PrivateProxy-Setup-*.exe" -ForegroundColor Green
