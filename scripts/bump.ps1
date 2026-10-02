#Requires -Version 5.1
<#
.SYNOPSIS
    Bumps a mod's version for a Thunderstore release. Pick the mod and the kind of bump - major,
    minor or patch - from a menu, or pass them as arguments. Updates all three places a version
    lives: the VERSION const in the mod's plugin source (the source of truth), version_number in
    its package\manifest.json, and the ## Unreleased heading in its package\CHANGELOG.md, which
    becomes the heading for the new version. A pack has no source: its manifest is the version.

    Nothing is written until you confirm, and nothing is built, committed or published - run
    scripts\package.ps1 afterwards for the zip.
.EXAMPLE
    .\scripts\bump.ps1
.EXAMPLE
    .\scripts\bump.ps1 OdinsEssentials patch -Yes
#>
[CmdletBinding()]
param(
    # The mod to bump. Empty means pick from a menu.
    [Parameter(Position = 0)]
    [string]$Mod,
    # Which part of major.minor.patch to raise. Empty means pick from a menu.
    [Parameter(Position = 1)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Kind,
    # Skip the confirmation prompt.
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

# major.minor.patch, bumped by kind - a minor bump zeroes the patch, a major one both.
function Step-Version([string]$Version, [string]$Kind) {
    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "version '$Version' is not major.minor.patch" }
    $major, $minor, $patch = [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
    switch ($Kind) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
    }
    return "$major.$minor.$patch"
}

# One numbered choice out of the labels given; the index picked is returned.
function Select-Item([string]$Prompt, [string[]]$Labels) {
    Write-Host ''
    Write-Host $Prompt -ForegroundColor Cyan
    for ($i = 0; $i -lt $Labels.Count; $i++) { Write-Host ("  {0}) {1}" -f ($i + 1), $Labels[$i]) }
    while ($true) {
        $reply = Read-Host '>'
        if ($reply -match '^\d+$' -and [int]$reply -ge 1 -and [int]$reply -le $Labels.Count) {
            return [int]$reply - 1
        }
        Write-Host "pick 1-$($Labels.Count)" -ForegroundColor Yellow
    }
}

$all = @(Get-Mods)
if (-not $all) { throw "no mods found in $Root" }

if ($Mod) {
    if ($all -notcontains $Mod) { throw "unknown mod '$Mod' (have: $($all -join ', '))" }
} else {
    $labels = $all | ForEach-Object { '{0,-24} {1}' -f $_, (Get-ModVersion $_) }
    $Mod = $all[(Select-Item 'Which mod?' $labels)]
}

$current = Get-ModVersion $Mod
$versionFile = Get-VersionFile $Mod
$modDir = Get-ModDir $Mod
$manifestPath = Join-Path $modDir 'package\manifest.json'
$changelogPath = Join-Path $modDir 'package\CHANGELOG.md'
if (-not (Test-Path $manifestPath))  { throw "$Mod\package\manifest.json missing" }
if (-not (Test-Path $changelogPath)) { throw "$Mod\package\CHANGELOG.md missing" }

$kinds = @('patch', 'minor', 'major')
if (-not $Kind) {
    $labels = $kinds | ForEach-Object { '{0,-6} {1}' -f $_, (Step-Version $current $_) }
    $Kind = $kinds[(Select-Item "Bump $Mod from $current to?" $labels)]
}

$next = Step-Version $current $Kind

# The ## Unreleased section is what this release ships; it becomes the new version's heading.
$lines = [IO.File]::ReadAllLines($changelogPath)
$unreleased = @()
$inside = $false
foreach ($line in $lines) {
    if ($line -match '^##\s+Unreleased\s*$') { $inside = $true; continue }
    if ($line -match '^##\s')                { $inside = $false }
    if ($inside)                             { $unreleased += $line }
}

Write-Host ''
Write-Host "$Mod $current -> $next ($Kind)" -ForegroundColor Cyan
if ($versionFile -ne $manifestPath) { Write-Host "  $($versionFile.Substring($Root.Length + 1))" }
Write-Host "  $Mod\package\manifest.json"
Write-Host "  $Mod\package\CHANGELOG.md   ## Unreleased -> ## $next"

if (($unreleased -join '').Trim()) {
    Write-Host ''
    $unreleased | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host ''
    Write-Host "$Mod\package\CHANGELOG.md has no ## Unreleased section with anything under it -" -ForegroundColor Yellow
    Write-Host 'the release would go out with no notes, and the heading would stay as it is.' -ForegroundColor Yellow
}

if (-not $Yes) {
    Write-Host ''
    if ((Read-Host "Write $next? [y/N]") -notmatch '^[Yy]$') {
        Write-Host 'Nothing written.'
        return
    }
}

$utf8 = New-Object Text.UTF8Encoding $false

# A pack's version lives in its manifest alone, written just below.
if ($versionFile -ne $manifestPath) {
    $source = (Get-Content $versionFile -Raw) -replace '(VERSION\s*=\s*")[^"]+', "`${1}$next"
    [IO.File]::WriteAllText($versionFile, $source, $utf8)
}

$manifest = (Get-Content $manifestPath -Raw) -replace '("version_number"\s*:\s*")[^"]+', "`${1}$next"
# Thunderstore dislikes a BOM in manifest.json, so bypass Set-Content's encoding defaults.
[IO.File]::WriteAllText($manifestPath, $manifest.TrimEnd() + "`n", $utf8)

# Only the first Unreleased heading - an older one further down would be a mistake, not a target.
$done = $false
$rewritten = foreach ($line in $lines) {
    if (-not $done -and $line -match '^##\s+Unreleased\s*$') { $done = $true; "## $next" } else { $line }
}
[IO.File]::WriteAllText($changelogPath, ($rewritten -join "`n").TrimEnd() + "`n", $utf8)

Write-Host "$Mod is now $next" -ForegroundColor Green
Write-Host "Next: .\scripts\package.ps1 $Mod  then upload dist\$Mod-$next.zip"
