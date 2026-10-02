#!/usr/bin/env bash
# Bumps a mod's version for a Thunderstore release. Pick the mod and the kind of bump - major,
# minor or patch - from a menu, or pass them as arguments. Updates all three places a version
# lives: the VERSION const in the mod's plugin source (the source of truth), version_number in
# its package/manifest.json, and the ## Unreleased heading in its package/CHANGELOG.md, which
# becomes the heading for the new version. A pack has no source: its manifest is the version.
#
# Nothing is written until you confirm, and nothing is built, committed or published - run
# scripts/package.sh afterwards for the zip.
#
# Usage: scripts/bump.sh [mod] [major|minor|patch] [-y]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

mod=''
kind=''
assume_yes=0

while [ $# -gt 0 ]; do
    case "$1" in
        -h|--help)          usage; exit 0 ;;
        -y|--yes)           assume_yes=1; shift ;;
        major|minor|patch)  kind=$1; shift ;;
        -*)                 die "unknown argument: $1" ;;
        *)                  [ -z "$mod" ] || die "only one mod at a time"; mod=$1; shift ;;
    esac
done

# major.minor.patch, bumped by kind - a minor bump zeroes the patch, a major one both.
bumped() {
    local v=$1 kind=$2 major minor patch
    [[ $v =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || die "version '$v' is not major.minor.patch"
    major=${BASH_REMATCH[1]}; minor=${BASH_REMATCH[2]}; patch=${BASH_REMATCH[3]}
    case "$kind" in
        major) major=$((major + 1)); minor=0; patch=0 ;;
        minor) minor=$((minor + 1)); patch=0 ;;
        patch) patch=$((patch + 1)) ;;
    esac
    printf '%s.%s.%s\n' "$major" "$minor" "$patch"
}

# One numbered choice out of the lines given on stdin; the picked line goes to stdout.
choose() {
    local prompt=$1 i=1 reply
    shift
    local -a items=("$@")
    printf '\n\033[36m%s\033[0m\n' "$prompt" >&2
    for i in "${!items[@]}"; do printf '  %d) %s\n' "$((i + 1))" "${items[$i]}" >&2; done
    while true; do
        printf '> ' >&2
        read -r reply < /dev/tty || die 'nothing to read from - pass the arguments instead'
        if [[ $reply =~ ^[0-9]+$ ]] && [ "$reply" -ge 1 ] && [ "$reply" -le "${#items[@]}" ]; then
            printf '%s\n' "${items[$((reply - 1))]}"
            return 0
        fi
        printf '\033[33mpick 1-%d\033[0m\n' "${#items[@]}" >&2
    done
}

confirm() {
    local reply
    [ "$assume_yes" -eq 1 ] && return 0
    printf '\n%s [y/N] ' "$1" >&2
    read -r reply < /dev/tty || return 1
    [[ $reply =~ ^[Yy]$ ]]
}

mods=($(all_mods))
[ ${#mods[@]} -gt 0 ] || die "no mods found in $root"

if [ -n "$mod" ]; then
    printf '%s\n' "${mods[@]}" | grep -qxF "$mod" ||
        die "unknown mod '$mod' (have: $(all_mods | tr '\n' ' '))"
else
    labels=()
    for m in "${mods[@]}"; do labels+=("$(printf '%-24s %s' "$m" "$(mod_version "$m")")"); done
    mod=$(choose 'Which mod?' "${labels[@]}")
    mod=${mod%% *}
fi

current=$(mod_version "$mod")
file=$(version_file "$mod")
dir=$(mod_dir "$mod")
manifest="$dir/package/manifest.json"
changelog="$dir/package/CHANGELOG.md"
[ -f "$manifest" ]  || die "${manifest#$root/} missing"
[ -f "$changelog" ] || die "${changelog#$root/} missing"

if [ -z "$kind" ]; then
    labels=()
    for k in patch minor major; do
        labels+=("$(printf '%-6s %s' "$k" "$(bumped "$current" "$k")")")
    done
    kind=$(choose "Bump $mod from $current to?" "${labels[@]}")
    kind=${kind%% *}
fi

next=$(bumped "$current" "$kind")

# The ## Unreleased section is what this release ships; it becomes the new version's heading.
unreleased=$(awk '
    /^##[[:space:]]+[Uu]nreleased[[:space:]]*$/ { inside = 1; next }
    /^##[[:space:]]/                            { inside = 0 }
    inside                                      { print }
' "$changelog")

printf '\n\033[36m%s\033[0m %s -> \033[32m%s\033[0m (%s)\n' "$mod" "$current" "$next" "$kind"
[ "$file" = "$manifest" ] || info "  ${file#$root/}"
info "  ${manifest#$root/}"
info "  ${changelog#$root/}   ## Unreleased -> ## $next"

if [ -n "$(printf '%s' "$unreleased" | tr -d '[:space:]')" ]; then
    printf '\n%s\n' "$unreleased" | sed -E '/^[[:space:]]*$/d;s/^/  /'
else
    printf '\n' >&2
    note "${changelog#$root/} has no ## Unreleased section with anything under it -"
    note 'the release would go out with no notes, and the heading would stay as it is.'
fi

confirm "Write $next?" || { info 'Nothing written.'; exit 0; }

[ "$file" = "$manifest" ] || sed -i -E "s;(VERSION[[:space:]]*=[[:space:]]*\")[^\"]+;\1$next;" "$file"
sed -i -E "s;(\"version_number\"[[:space:]]*:[[:space:]]*\")[^\"]+;\1$next;" "$manifest"
# Only the first Unreleased heading - an older one further down would be a mistake, not a target.
awk -v v="$next" '
    !done && /^##[[:space:]]+[Uu]nreleased[[:space:]]*$/ { print "## " v; done = 1; next }
    { print }
' "$changelog" > "$changelog.tmp" && mv "$changelog.tmp" "$changelog"

ok "$mod is now $next"
info 'Next: commit, then ./scripts/publish.sh '"$mod"
