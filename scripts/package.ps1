#Requires -Version 5.1
<#
.SYNOPSIS
    Builds Thunderstore-ready zips in dist/, one per mod. A mod's version comes from the VERSION
    const in its plugin source and is stamped into its package/manifest.json. The zip ships the
    mod's package/ folder - manifest.json, icon.png, README.md (the Thunderstore page) and
    CHANGELOG.md (its Changelog tab).
    With no mod names, every mod in the repo is packaged.
.EXAMPLE
    .\scripts\package.ps1 GraveOfTruth
#>
[CmdletBinding()]
param(
    # Mods to package. Empty means all of them.
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Mods
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

$Mods = Resolve-Mods $Mods

foreach ($mod in $Mods) {
    $dir = Get-ModDir $mod
    $version = Get-ModVersion $mod
    Write-Host "Packaging $mod $version..." -ForegroundColor Cyan

    $manifestPath = Join-Path $dir 'package\manifest.json'
    # The Thunderstore page: package\README.md, not the mod's dev facing README.md.
    $readmePath = Join-Path $dir 'package\README.md'
    if (-not (Test-Path $readmePath)) {
        throw "$mod\package\README.md missing - it is the Thunderstore description."
    }
    # Thunderstore renders a CHANGELOG.md at the zip root as the Changelog tab.
    $changelogPath = Join-Path $dir 'package\CHANGELOG.md'
    if (-not (Test-Path $changelogPath)) {
        throw "$mod\package\CHANGELOG.md missing - it is the Thunderstore changelog."
    }
    $manifest = (Get-Content $manifestPath -Raw) -replace '("version_number"\s*:\s*")[^"]+', "`${1}$version"
    # Thunderstore dislikes a BOM in manifest.json, so bypass Set-Content's encoding defaults.
    [IO.File]::WriteAllText($manifestPath, $manifest.TrimEnd() + "`n", (New-Object Text.UTF8Encoding $false))

    $stage = Join-Path $Root 'dist\stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory $stage -Force | Out-Null

    # A pack has no DLL: its zip is the package files alone, its manifest naming the members.
    if (-not (Test-Pack $mod)) {
        dotnet build (Join-Path $dir "$mod.csproj") -c Release
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $mod." }
        Copy-Item (Join-Path $dir "bin\Release\$mod.dll") $stage
        $assets = Join-Path $dir 'assets'
        if (Test-Path $assets) { Copy-Item "$assets\*" $stage -Recurse -Force }
    }
    Copy-Item $manifestPath $stage
    Copy-Item (Join-Path $dir 'package\icon.png') $stage
    Copy-Item $readmePath $stage
    Copy-Item $changelogPath $stage

    $zip = Join-Path $Root "dist\$mod-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # Thunderstore wants the files at the root of the zip, no stage\ prefix.
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    Remove-Item $stage -Recurse -Force

    Write-Host "Packaged $zip" -ForegroundColor Green
}
