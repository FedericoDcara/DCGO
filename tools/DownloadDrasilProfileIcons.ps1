#requires -Version 5.1
<#
.SYNOPSIS
  Download Project Drasil profile avatars into StreamingAssets ProfileIcons.

.DESCRIPTION
  Pulls PNGs from WE-Kaito/digimon-tcg-simulator (frontend/src/assets/profile_pictures)
  into Assets/StreamingAssets/Textures/ProfileIcons/ and rewrites catalog.json.

.PARAMETER Force
  Re-download files that already exist.

.EXAMPLE
  powershell -File tools/DownloadDrasilProfileIcons.ps1

.EXAMPLE
  powershell -File tools/DownloadDrasilProfileIcons.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $RepoRoot 'Assets\StreamingAssets\Textures\ProfileIcons'
$CatalogPath = Join-Path $OutDir 'catalog.json'

$ApiUrl = 'https://api.github.com/repos/WE-Kaito/digimon-tcg-simulator/contents/frontend/src/assets/profile_pictures?ref=main'
$RawBase = 'https://raw.githubusercontent.com/WE-Kaito/digimon-tcg-simulator/main/frontend/src/assets/profile_pictures'
$SkipNames = @('Placeholder.png')

function Write-Info([string]$Message) {
    Write-Host "[drasil-icons] $Message"
}

if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

Write-Info "Listing icons from GitHub Contents API..."
$headers = @{
    'User-Agent' = 'DCGO-DownloadDrasilProfileIcons'
    'Accept'     = 'application/vnd.github+json'
}

$entries = Invoke-RestMethod -Uri $ApiUrl -Headers $headers -Method Get
if ($null -eq $entries) {
    throw "GitHub API returned no entries for profile_pictures."
}

$pngs = @($entries | Where-Object {
    $_.type -eq 'file' -and $_.name -like '*.png' -and ($SkipNames -notcontains $_.name)
} | Sort-Object name)

if ($pngs.Count -eq 0) {
    throw "No PNG icons found in profile_pictures."
}

Write-Info "Found $($pngs.Count) icons (excluding Placeholder)."

$downloaded = 0
$skipped = 0
$failed = 0

foreach ($entry in $pngs) {
    $name = [string]$entry.name
    $dest = Join-Path $OutDir $name

    if ((Test-Path -LiteralPath $dest) -and -not $Force) {
        $skipped++
        continue
    }

    $url = "$RawBase/$name"

    try {
        Write-Info "Downloading $name..."
        Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -Headers @{ 'User-Agent' = 'DCGO-DownloadDrasilProfileIcons' }
        $downloaded++
    }
    catch {
        Write-Warning "[drasil-icons] Failed $name : $($_.Exception.Message)"
        $failed++
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
        }
    }
}

# Rebuild catalog from all PNGs actually present (stable for Android)
$present = @(Get-ChildItem -LiteralPath $OutDir -Filter '*.png' -File |
    Where-Object { $_.Name -notin $SkipNames } |
    Sort-Object Name |
    ForEach-Object { $_.BaseName })

if ($present.Count -eq 0) {
    $catalogJson = '{"files":[]}'
}
else {
    $escaped = ($present | ForEach-Object { '"' + ($_ -replace '\\', '\\' -replace '"', '\"') + '"' }) -join ','
    $catalogJson = '{"files":[' + $escaped + ']}'
}

[System.IO.File]::WriteAllText($CatalogPath, $catalogJson)

Write-Info "Done. downloaded=$downloaded skipped=$skipped failed=$failed catalog=$($present.Count)"
if ($failed -gt 0) {
    exit 1
}
