#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the solution. Uses a local .NET SDK when the machine-wide one is absent.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish
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

    throw 'Не найден .NET SDK 8+. Установите его: https://dotnet.microsoft.com/download'
}

$dotnet = Find-Dotnet
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray

if ($Publish) {
    $out = Join-Path $root 'artifacts\PrivateProxy'
    & $dotnet publish (Join-Path $root 'src\PrivateProxy.App\PrivateProxy.App.csproj') `
        -c $Configuration -r win-x64 --self-contained false -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Готово: $out" -ForegroundColor Green
    exit 0
}

& $dotnet build (Join-Path $root 'PrivateProxy.sln') -c $Configuration
exit $LASTEXITCODE
