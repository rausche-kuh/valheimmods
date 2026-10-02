#!/usr/bin/env bash
# Builds Thunderstore-ready zips in dist/, one per mod. A mod's version comes from the VERSION
# const in its plugin source and is stamped into its package/manifest.json. The zip ships the mod's
# package/ folder - manifest.json, icon.png, README.md (the Thunderstore page) and CHANGELOG.md
# (its Changelog tab). A pack has no DLL: its zip is the package/ files alone, its manifest's
# dependencies naming the members.
# With no mod names, every mod and pack in the repo is packaged.
#
# Usage: scripts/package.sh [mod ...]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

mods=()
while [ $# -gt 0 ]; do
    case "$1" in
        -h|--help) usage; exit 0 ;;
        -*)        die "unknown argument: $1" ;;
        *)         mods+=("$1"); shift ;;
    esac
done

mods=($(resolve_mods "${mods[@]+"${mods[@]}"}"))

for mod in "${mods[@]}"; do
    dir=$(mod_dir "$mod")
    version=$(mod_version "$mod")
    step "Packaging $mod $version..."

    manifest="$dir/package/manifest.json"
    # The Thunderstore page: package/README.md, not the mod's dev facing README.md.
    readme="$dir/package/README.md"
    [ -f "$readme" ] || die "$mod/package/README.md missing - it is the Thunderstore description."
    # Thunderstore renders a CHANGELOG.md at the zip root as the Changelog tab.
    changelog="$dir/package/CHANGELOG.md"
    [ -f "$changelog" ] || die "$mod/package/CHANGELOG.md missing - it is the Thunderstore changelog."
    # Thunderstore dislikes a BOM in manifest.json; sed in place keeps the file plain UTF-8.
    sed -i -E "s;(\"version_number\"[[:space:]]*:[[:space:]]*\")[^\"]+;\1$version;" "$manifest"

    stage="$root/dist/stage"
    rm -rf "$stage"
    mkdir -p "$stage"

    if ! is_pack "$mod"; then
        dotnet build "$dir/$mod.csproj" -c Release
        cp "$dir/bin/Release/$mod.dll" "$stage"
        [ -d "$dir/assets" ] && cp -r "$dir/assets/." "$stage" || true
    fi
    cp "$manifest" "$stage"
    cp "$dir/package/icon.png" "$stage"
    cp "$readme" "$stage"
    cp "$changelog" "$stage"

    zip="$root/dist/$mod-$version.zip"
    rm -f "$zip"
    # Thunderstore wants the files at the root of the zip, no stage/ prefix.
    if command -v zip >/dev/null 2>&1; then
        (cd "$stage" && zip -q -r "$zip" .)
    else
        python3 - "$zip" "$stage" <<'PY'
import os, sys, zipfile
zip_path, stage = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
    for dirpath, _, names in os.walk(stage):
        for n in sorted(names):
            full = os.path.join(dirpath, n)
            z.write(full, os.path.relpath(full, stage))
PY
    fi
    rm -rf "$stage"

    ok "Packaged $zip"
done
