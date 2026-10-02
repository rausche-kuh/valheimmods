#!/usr/bin/env bash
# Removes generated files. By default the per-mod bin/ and obj/ folders and dist/.
# With no mod names, every mod in the repo is cleaned.
#
#   --deployed   also remove the mods from the mod manager profile
#   --all        also remove the shared lib/, decompiled/ and Valheim.props,
#                i.e. everything scripts/setup.sh and scripts/decompile.sh produced
#
# Usage: scripts/clean.sh [--deployed] [--all] [mod ...]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

deployed=0
all=0
mods=()

while [ $# -gt 0 ]; do
    case "$1" in
        --deployed) deployed=1; shift ;;
        --all)      all=1; shift ;;
        -h|--help)  usage; exit 0 ;;
        -*)         die "unknown argument: $1" ;;
        *)          mods+=("$1"); shift ;;
    esac
done

mods=($(resolve_mods "${mods[@]+"${mods[@]}"}"))

# Paths outside the repo (a deployed plugin) are printed in full.
gone() { case "$1" in "$root"/*) info "  removed ${1#"$root"/}" ;; *) info "  removed $1" ;; esac; }
scrub() { [ -e "$1" ] || return 0; rm -rf "$1"; gone "$1"; }

step "Cleaning ${mods[*]}..."
for mod in "${mods[@]}"; do
    is_pack "$mod" && continue
    dir=$(mod_dir "$mod")
    scrub "$dir/bin"
    scrub "$dir/obj"
done
scrub "$root/dist"

if [ "$deployed" -eq 1 ]; then
    # Missing Valheim.props means nothing was ever deployed from this checkout.
    profile=$(profile_dir 2>/dev/null) || profile=""
    if [ -n "$profile" ]; then
        for mod in "${mods[@]}"; do
            scrub "$profile/BepInEx/plugins/$author-$mod"
        done
    else
        note '  no Valheim.props - nothing deployed to clean'
    fi
fi

if [ "$all" -eq 1 ]; then
    scrub "$root/lib"
    scrub "$root/decompiled"
    scrub "$root/Valheim.props"
    note 'Removed the shared setup - run scripts/setup.sh before building again.'
fi

ok 'Clean.'
