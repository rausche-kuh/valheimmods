#!/usr/bin/env bash
# Shows, per mod and pack, what is waiting since its last release - the commit that set its
# current version - and whether that needs one:
#
#   release        ## Unreleased has notes: players see something new
#   translations   only translations.csv changed; release.sh writes the note and bumps a patch
#   unnoted        shipped code changed without a changelog note; release only if it matters
#   shared         only the family's Common/ changed; it ships with the next real release
#   new            never released yet
#
# For a pack it also lists members whose newest version is past the one the pack names. That
# needs no release: a mod manager updates the members on its own; the pack is brought up to date
# whenever it is released for another reason. Reads git only, sends nothing anywhere.
#
# Usage: scripts/status.sh [mod ...]
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
    IFS=$'\t' read -r state detail <<< "$(assess "$mod")"
    case "$state" in
        release|new)  colour='\033[32m' ;;
        translations) colour='\033[36m' ;;
        unnoted)      colour='\033[33m' ;;
        *)            colour='\033[2m' ;;
    esac
    printf "  %-20s %-8s ${colour}%-13s\033[0m %s\n" "$mod" "$(mod_version "$mod")" "$state" "$detail"
    if is_pack "$mod"; then
        while IFS=$'\t' read -r member listed; do
            [ -n "$member" ] || continue
            current=$(mod_version "$member" 2>/dev/null) || { note "      lists $member, which is not in the repo"; continue; }
            [ "$current" = "$listed" ] || printf '\033[2m      lists %s %s, newest is %s\033[0m\n' "$member" "$listed" "$current"
        done < <(pack_members "$mod")
    fi
done
