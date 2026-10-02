# Shared helpers for the scripts in this folder. Dot-sourced, never run directly.
#
# A mod is any directory holding <Name>\<Name>.csproj, at the repo root or one level down (the
# members of a family such as OdinsMissingPatch\). A pack is a directory holding
# package\manifest.json and no csproj: a Thunderstore modpack, nothing to build, its version in
# the manifest. Names are unique across the repo. Scripts that take mod names default to every
# mod and pack in the repo.

$Root = Split-Path $PSScriptRoot -Parent
# Thunderstore team name - the install folder and zip are named <author>-<mod>.
$Author = 'rauschekuh'

# The directories a mod or pack may sit in: the root's and their children, minus what the scripts
# generate (decompiled\ holds a csproj per game assembly).
function Get-CandidateDirs {
    foreach ($top in Get-ChildItem $Root -Directory) {
        if ('decompiled', 'lib', 'dist' -contains $top.Name) { continue }
        $top
        Get-ChildItem $top.FullName -Directory
    }
}

function Test-PackDir($Dir) {
    (Test-Path (Join-Path $Dir.FullName 'package\manifest.json')) -and
        -not (Get-ChildItem $Dir.FullName -Filter *.csproj -File)
}

# Every mod, then every pack: a pack depends on its members, so they go first.
function Get-Mods {
    $dirs = @(Get-CandidateDirs)
    $dirs | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } | ForEach-Object Name
    $dirs | Where-Object { Test-PackDir $_ } | ForEach-Object Name
}

# The directory of a mod or pack, by name.
function Get-ModDir([string]$Mod) {
    foreach ($dir in Get-CandidateDirs) {
        if ($dir.Name -ne $Mod) { continue }
        if ((Test-Path (Join-Path $dir.FullName "$Mod.csproj")) -or (Test-PackDir $dir)) { return $dir.FullName }
    }
    throw "unknown mod '$Mod' (have: $((Get-Mods) -join ', '))"
}

# Whether the name is a pack: a manifest and nothing to build.
function Test-Pack([string]$Mod) {
    -not (Test-Path (Join-Path (Get-ModDir $Mod) "$Mod.csproj"))
}

# Mod names as passed on the command line, or every mod if there were none.
function Resolve-Mods([string[]]$Names) {
    $all = @(Get-Mods)
    if (-not $Names -or $Names.Count -eq 0) {
        if (-not $all) { throw "no mods found in $Root" }
        return $all
    }
    foreach ($n in $Names) {
        if ($all -notcontains $n) { throw "unknown mod '$n' (have: $($all -join ', '))" }
    }
    # Packs last, whatever order they were named in.
    $Names = @($Names | Where-Object { -not (Test-Pack $_) }) + @($Names | Where-Object { Test-Pack $_ })
    return $Names
}

# The file a mod's version is written in: the plugin source holding VERSION, or a pack's manifest.
function Get-VersionFile([string]$Mod) {
    $dir = Get-ModDir $Mod
    if (Test-Pack $Mod) { return (Join-Path $dir 'package\manifest.json') }
    foreach ($file in Get-ChildItem (Join-Path $dir 'src') -Filter *.cs -Recurse) {
        if ((Get-Content $file.FullName -Raw) -match 'VERSION\s*=\s*"[^"]+"') { return $file.FullName }
    }
    throw "could not find VERSION in $Mod\src"
}

# A mod's version: the VERSION const in its plugin source, or a pack's manifest version_number.
function Get-ModVersion([string]$Mod) {
    $text = Get-Content (Get-VersionFile $Mod) -Raw
    if ($text -match '(?:VERSION\s*=|"version_number"\s*:)\s*"([^"]+)"') { return $Matches[1] }
    throw "could not read the version of $Mod"
}

# The mod manager profile scripts\setup.ps1 picked, e.g. ...\gale\valheim\profiles\Default.
function Get-ProfileDir {
    $props = Join-Path $Root 'Valheim.props'
    if (-not (Test-Path $props)) { throw 'Valheim.props missing - run scripts\setup.ps1 first.' }
    $dir = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.ProfileDir
    if (-not $dir) { throw 'no <ProfileDir> in Valheim.props - re-run scripts\setup.ps1.' }
    return $dir
}
