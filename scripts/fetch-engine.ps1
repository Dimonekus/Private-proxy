#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads the pinned zapret engine binaries and verifies their SHA-256 hashes.

.DESCRIPTION
    Reads engine/manifest.json, downloads every file from its pinned URL and
    refuses to install anything whose hash does not match. Nothing is taken from
    a floating branch: every URL points at an immutable commit.

.PARAMETER VerifyOnly
    Do not download; only check the files that are already present.

.PARAMETER Force
    Re-download files even if their hash is already correct.

.PARAMETER UpdateHashes
    After downloading, rewrite sha256/size in the manifest from the local files.
    Only use this when you intentionally bumped an upstream commit.
#>
[CmdletBinding()]
param(
    [switch]$VerifyOnly,
    [switch]$Force,
    [switch]$UpdateHashes
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'engine\manifest.json'
$binDir = Join-Path $root 'engine\bin'

if (-not (Test-Path $manifestPath)) {
    throw "Не найден манифест: $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
New-Item -ItemType Directory -Force $binDir | Out-Null

function Get-Sha256([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$downloaded = 0
$verified = 0
$failed = 0

foreach ($file in $manifest.files) {
    $dest = Join-Path $binDir $file.path
    $sizeOk = $true
    $hashOk = $false

    if (Test-Path $dest) {
        $actualSize = (Get-Item $dest).Length
        $sizeOk = (-not $file.size) -or ($actualSize -eq [int64]$file.size)
        if ($sizeOk) {
            $hashOk = (Get-Sha256 $dest) -eq $file.sha256
        }
    }

    if ($hashOk -and -not $Force) {
        Write-Host ("  ok      {0}" -f $file.path) -ForegroundColor DarkGray
        $verified++
        continue
    }

    if ($VerifyOnly) {
        Write-Host ("  BAD     {0}" -f $file.path) -ForegroundColor Red
        $failed++
        continue
    }

    Write-Host ("  download {0}" -f $file.path) -ForegroundColor Cyan
    $temp = "$dest.part"
    try {
        if (Test-Path $temp) { Remove-Item $temp -Force }
        Invoke-WebRequest -UseBasicParsing -Uri $file.url -OutFile $temp -TimeoutSec 120

        $actualSize = (Get-Item $temp).Length
        if ($file.size -and $actualSize -ne [int64]$file.size) {
            throw "размер $actualSize не совпадает с ожидаемым $($file.size)"
        }

        $actualHash = Get-Sha256 $temp
        if ($actualHash -ne $file.sha256) {
            throw "SHA-256 $actualHash не совпадает с манифестом $($file.sha256)"
        }

        Move-Item -Path $temp -Destination $dest -Force
        Write-Host ("  ok      {0}" -f $file.path) -ForegroundColor Green
        $downloaded++
    }
    catch {
        if (Test-Path $temp) { Remove-Item $temp -Force -ErrorAction SilentlyContinue }
        Write-Host ("  FAIL    {0}: {1}" -f $file.path, $_.Exception.Message) -ForegroundColor Red
        $failed++
    }
}

if ($UpdateHashes -and -not $VerifyOnly) {
    foreach ($file in $manifest.files) {
        $dest = Join-Path $binDir $file.path
        if (Test-Path $dest) {
            $file.sha256 = Get-Sha256 $dest
            $file.size = (Get-Item $dest).Length
        }
    }

    $json = $manifest | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($manifestPath, $json, (New-Object Text.UTF8Encoding($false)))
    Write-Host 'Манифест обновлён.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host ("Итог: проверено {0}, скачано {1}, ошибок {2}" -f $verified, $downloaded, $failed)

if ($failed -gt 0) {
    exit 1
}
