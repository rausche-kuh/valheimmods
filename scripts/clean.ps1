#Requires -Version 5.1
<#
.SYNOPSIS
    Removes generated files. By default the per-mod bin/ and obj/ folders and dist/.
    With no mod names, every mod in the repo is cleaned.
.EXAMPLE
    .\scripts\clean.ps1 -All
#>
[CmdletBinding()]
param(
    # Mods to clean. Empty means all of them.
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Mods,
    # Also remove the mods from the mod manager profile.
    [switch]$Deployed,
    # Also remove the shared lib/, decompiled/ and Valheim.props - everything
    # scripts\setup.ps1 and scripts\decompile.ps1 produced.
    [switch]$All
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

$Mods = Resolve-Mods $Mods

function Remove-Generated([string]$Path) {
    if (-not (Test-Path $Path)) { return }
    Remove-Item $Path -Recurse -Force
    Write-Host "  removed $($Path.Replace("$Root\", ''))"
}

Write-Host "Cleaning $($Mods -join ', ')..." -ForegroundColor Cyan
foreach ($mod in $Mods) {
    if (Test-Pack $mod) { continue }
    $dir = Get-ModDir $mod
    Remove-Generated (Join-Path $dir 'bin')
    Remove-Generated (Join-Path $dir 'obj')
}
Remove-Generated (Join-Path $Root 'dist')

if ($Deployed) {
    # Missing Valheim.props means nothing was ever deployed from this checkout.
    $profileDir = $null
    try { $profileDir = Get-ProfileDir } catch { Write-Host '  no Valheim.props - nothing deployed to clean' -ForegroundColor Yellow }
    if ($profileDir) {
        foreach ($mod in $Mods) {
            Remove-Generated (Join-Path $profileDir "BepInEx\plugins\$Author-$mod")
        }
    }
}

if ($All) {
    Remove-Generated (Join-Path $Root 'lib')
    Remove-Generated (Join-Path $Root 'decompiled')
    Remove-Generated (Join-Path $Root 'Valheim.props')
    Write-Host 'Removed the shared setup - run scripts\setup.ps1 before building again.' -ForegroundColor Yellow
}

Write-Host 'Clean.' -ForegroundColor Green
