#!/usr/bin/env bash
# One-time dev setup (Linux): locates Valheim + BepInEx, stages the reference assemblies every mod
# in this repo builds against into lib/, and writes Valheim.props (both gitignored).
# Safe to re-run after a game update.
#
# Usage: scripts/setup.sh [--valheim-dir DIR] [--profile NAME]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

valheim_dir=""
profile=""

while [ $# -gt 0 ]; do
    case "$1" in
        --valheim-dir) valheim_dir=${2:-}; [ -n "$valheim_dir" ] || die "$1 needs a value"; shift 2 ;;
        --profile)     profile=${2:-}; [ -n "$profile" ] || die "$1 needs a value"; shift 2 ;;
        -h|--help)     usage; exit 0 ;;
        *)             die "unknown argument: $1" ;;
    esac
done

# Steam library roots, including the Flatpak install.
steam_roots() {
    local r vdf
    for r in "$HOME/.steam/steam" "$HOME/.steam/root" "$HOME/.local/share/Steam" \
             "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
        [ -d "$r" ] || continue
        printf '%s\n' "$r"
        vdf="$r/steamapps/libraryfolders.vdf"
        # Extra libraries (second drives) are listed as "path" entries in libraryfolders.vdf.
        [ -f "$vdf" ] && sed -nE 's/.*"path"[[:space:]]+"([^"]+)".*/\1/p' "$vdf" || true
    done
    return 0
}

find_valheim() {
    local l p
    while read -r l; do
        [ -n "$l" ] || continue
        p="$l/steamapps/common/Valheim"
        if [ -f "$p/valheim_Data/Managed/assembly_valheim.dll" ]; then
            printf '%s\n' "$p"
            return 0
        fi
    done < <(steam_roots | awk '!seen[$0]++')
    return 1
}

# Profile roots of the mod managers, Flatpak variants included.
profile_roots() {
    local b
    for b in "$HOME/.local/share/com.kesomannen.gale/valheim/profiles" \
             "$HOME/.config/com.kesomannen.gale/valheim/profiles" \
             "$HOME/.var/app/com.kesomannen.gale/data/com.kesomannen.gale/valheim/profiles" \
             "$HOME/.config/r2modmanPlus-local/Valheim/profiles" \
             "$HOME/.var/app/game.r2modman.R2ModMan/config/r2modmanPlus-local/Valheim/profiles"; do
        [ -d "$b" ] && printf '%s\n' "$b" || true
    done
    return 0
}

# BepInEx.dll carries its version as a plain string; used only to prefer the newest install.
bepinex_version() {
    local v=""
    command -v strings >/dev/null 2>&1 &&
        v=$(strings -a "$1" 2>/dev/null | grep -oE '^5\.[0-9]+\.[0-9]+(\.[0-9]+)?$' | sort -V | tail -1)
    printf '%s\n' "${v:-0}"
}

# Newest BepInEx we can find: game dir first, then every mod manager profile.
find_bepinex_core() {
    local c dll
    {
        [ -f "$valheim_dir/BepInEx/core/BepInEx.dll" ] && printf '%s\n' "$valheim_dir/BepInEx/core" || true
        while read -r b; do
            for c in "$b"/*/BepInEx/core; do
                [ -f "$c/BepInEx.dll" ] && printf '%s\n' "$c" || true
            done
        done < <(profile_roots)
    } 2>/dev/null | awk '!seen[$0]++' | while read -r c; do
        dll="$c/BepInEx.dll"
        printf '%s\t%s\t%s\n' "$(bepinex_version "$dll")" "$(stat -c %Y "$dll")" "$c"
    done | sort -k1,1V -k2,2n | tail -1 | cut -f3
}

# --- .NET SDK -----------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks >/dev/null 2>&1; then
    note 'No .NET SDK found. Install it, then re-run this script:'
    if command -v pacman >/dev/null 2>&1;   then info '  sudo pacman -S dotnet-sdk'
    elif command -v apt-get >/dev/null 2>&1; then info '  sudo apt install dotnet-sdk-8.0'
    elif command -v dnf >/dev/null 2>&1;     then info '  sudo dnf install dotnet-sdk-8.0'
    else info '  https://dotnet.microsoft.com/download'; fi
    exit 1
fi

# --- game ---------------------------------------------------------------------
if [ -z "$valheim_dir" ]; then
    valheim_dir=$(find_valheim || true)
fi
[ -n "$valheim_dir" ] && [ -d "$valheim_dir" ] ||
    die 'Valheim not found. Re-run with --valheim-dir "<path to Valheim>".'
valheim_dir=$(cd "$valheim_dir" && pwd)
managed="$valheim_dir/valheim_Data/Managed"
[ -f "$managed/assembly_valheim.dll" ] || die "no valheim_Data/Managed in $valheim_dir"

unity="unknown"
if command -v strings >/dev/null 2>&1; then
    unity=$(strings -a "$valheim_dir/valheim_Data/globalgamemanagers" 2>/dev/null |
        grep -oE '^[0-9]{4,}\.[0-9]+\.[0-9]+[a-z][0-9]+$' | head -1 || true)
fi
info "Valheim : $valheim_dir (Unity ${unity:-unknown})"

core=$(find_bepinex_core)
[ -n "$core" ] || die 'BepInEx not found. Install the BepInEx pack in your mod manager first.'
info "BepInEx : $core ($(bepinex_version "$core/BepInEx.dll"))"

# Deploy into the profile BepInEx was found in, unless --profile names another one.
# Each mod lands in <profile>/BepInEx/plugins/<author>-<mod>/.
dest_profile=""
if [ -n "$profile" ]; then
    while read -r b; do
        [ -d "$b/$profile" ] && { dest_profile="$b/$profile"; break; }
    done < <(profile_roots)
    [ -n "$dest_profile" ] || die "profile '$profile' not found under any mod manager directory."
else
    dest_profile=${core%/BepInEx/core}
fi
info "Profile : $dest_profile"
info "Mods    : $(all_mods | tr '\n' ' ')"

# --- stage lib/ ---------------------------------------------------------------
lib="$root/lib"
rm -rf "$lib"
mkdir -p "$lib"

cp "$managed"/assembly_*.dll "$lib"
cp "$managed/Assembly-CSharp.dll" "$lib"
cp "$managed"/UnityEngine*.dll "$lib"
# Splatform holds PlatformUserID, which Minimap.PinData and AddPin take.
cp "$managed"/Splatform*.dll "$lib"
for dll in BepInEx.dll 0Harmony.dll BepInEx.Harmony.dll; do
    [ -f "$core/$dll" ] && cp "$core/$dll" "$lib" || true
done
info "lib/    : $(find "$lib" -name '*.dll' | wc -l) assemblies"

# --- Valheim.props ------------------------------------------------------------
cat > "$root/Valheim.props" <<PROPS
<Project>
  <!-- Generated by scripts/setup.sh - do not commit. -->
  <PropertyGroup>
    <ValheimDir>$valheim_dir</ValheimDir>
    <ProfileDir>$dest_profile</ProfileDir>
  </PropertyGroup>
</Project>
PROPS

for mod in $(all_mods); do is_pack "$mod" || dotnet restore "$(mod_dir "$mod")/$mod.csproj"; done

echo
ok 'Setup done. Next: scripts/deploy.sh (build + install), scripts/decompile.sh (game source).'
