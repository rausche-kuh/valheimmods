#Requires -Version 5.1
<#
.SYNOPSIS
    Builds mods and installs them into the profile configured by scripts/setup.ps1.
    With no mod names, every mod in the repo is deployed.
.EXAMPLE
    .\scripts\deploy.ps1 GraveOfTruth -Configuration Debug
#>
[CmdletBinding()]
param(
    # Mods to deploy. Empty means all of them. Position 0 keeps bare arguments here - without it
    # PowerShell would hand the first one to -Configuration.
    [Parameter(Position = 0, ValueFromRemainingArguments)]
    [string[]]$Mods,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # Install into another profile than the ProfileDir in Valheim.props.
    [string]$ProfileDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

$Mods = Resolve-Mods $Mods
if (-not $ProfileDir) { $ProfileDir = Get-ProfileDir }

# Mono maps a plugin DLL into memory and reads method bodies from it lazily, so overwriting one
# under a running game corrupts what it has not compiled yet ("BadImageFormatException: Method
# has zero rva", garbled method names in stack traces). The copy still happens - the game has
# to be restarted for the new build anyway - but say so.
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) {
    Write-Host 'Valheim is running: the game keeps the old build and may throw BadImageFormatException until restarted.' -ForegroundColor Yellow
}

foreach ($mod in $Mods) {
    $dir = Get-ModDir $mod
    $pluginDir = Join-Path $ProfileDir "BepInEx\plugins\$Author-$mod"
    if (Test-Pack $mod) {
        # A pack is installed through its members. Its folder only ever held a DLL from before it
        # became a pack, which would now patch everything a second time beside the members.
        $stale = Join-Path $pluginDir "$mod.dll"
        if (Test-Path $stale) {
            Remove-Item $stale -Force
            Write-Host "Removed the old $mod.dll from $pluginDir - $mod is a pack now" -ForegroundColor Yellow
        }
        continue
    }
    Write-Host "Deploying $mod..." -ForegroundColor Cyan
    dotnet build (Join-Path $dir "$mod.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $mod." }

    New-Item -ItemType Directory $pluginDir -Force | Out-Null
    Copy-Item (Join-Path $dir "bin\$Configuration\$mod.dll") $pluginDir -Force
    $assets = Join-Path $dir 'assets'
    if (Test-Path $assets) { Copy-Item "$assets\*" $pluginDir -Recurse -Force }
    Copy-Item (Join-Path $dir 'package\manifest.json') $pluginDir -Force
    Copy-Item (Join-Path $dir 'package\icon.png') $pluginDir -Force

    Write-Host "Installed to $pluginDir" -ForegroundColor Green
}
