#!/usr/bin/env bash
# Releases every mod that has something waiting (see scripts/status.sh): writes what can be
# written for you, bumps, commits the release and hands over to publish.sh.
#
#   release        bumped as it is - minor when a note starts "Added", patch otherwise
#   translations   gets "- Translations updated: <languages>." under ## Unreleased, then a patch
#   new            keeps the version it was made with; its ## Unreleased becomes that version
#   pack           its member versions are brought up to the newest before it goes out
#
# Mods that are "unnoted" or "shared" are never picked on their own: name one to release it,
# after writing its changelog note. Each step asks first unless -y is given; the commit holds
# only the version files, manifests and changelogs. publish.sh still asks before uploading.
#
# Usage: scripts/release.sh [-y] [mod ...]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

assume_yes=0
named=()
while [ $# -gt 0 ]; do
    case "$1" in
        -h|--help) usage; exit 0 ;;
        -y|--yes)  assume_yes=1; shift ;;
        -*)        die "unknown argument: $1" ;;
        *)         named+=("$1"); shift ;;
    esac
done

confirm() {
    local reply
    [ "$assume_yes" -eq 1 ] && return 0
    printf '\n%s [y/N] ' "$1" >&2
    read -r reply < /dev/tty || return 1
    [[ $reply =~ ^[Yy]$ ]]
}

# Puts a line at the top of ## Unreleased, making the heading under the title if there is none.
add_note() {
    python3 - "$1" "$2" <<'PY'
import re, sys
path, line = sys.argv[1], sys.argv[2]
text = open(path, encoding='utf-8').read()
m = re.search(r'^##[ \t]+[Uu]nreleased[ \t]*\n', text, re.M)
if m:
    rest = text[m.end():].lstrip('\n')
    text = text[:m.end()] + '\n' + line + '\n' + ('\n' if rest.startswith('#') else '') + rest
else:
    first = re.search(r'^##[ \t]', text, re.M)
    at = first.start() if first else len(text)
    text = text[:at] + '## Unreleased\n\n' + line + '\n\n' + text[at:]
open(path, 'w', encoding='utf-8').write(text)
PY
}

# Brings every member a pack names in this repo up to the member's current version.
sync_pack() {
    local manifest member listed current
    manifest="$(mod_dir "$1")/package/manifest.json"
    while IFS=$'\t' read -r member listed; do
        [ -n "$member" ] || continue
        current=$(mod_version "$member" 2>/dev/null) || continue
        [ "$current" = "$listed" ] && continue
        sed -i "s;\"$author-$member-$listed\";\"$author-$member-$current\";" "$manifest"
        info "  $1 now lists $member $current (was $listed)"
    done < <(pack_members "$1")
}

# ---- what is due --------------------------------------------------------------------------------

if [ ${#named[@]} -gt 0 ]; then
    mods=($(resolve_mods "${named[@]}"))
else
    mods=($(all_mods))
fi

due=()      # "mod state"
step 'Looking for what is due...'
for mod in "${mods[@]}"; do
    IFS=$'\t' read -r state detail <<< "$(assess "$mod")"
    case "$state" in
        release|translations) ;;
        new)
            [ -n "$(unreleased_notes "$mod")" ] || {
                [ ${#named[@]} -gt 0 ] && die "$mod has never been released and has no ## Unreleased notes to go out with"
                continue
            } ;;
        unnoted|shared)
            [ ${#named[@]} -gt 0 ] || continue
            die "$mod: $state ($detail) - write its ## Unreleased note first, then release it" ;;
        *)
            [ ${#named[@]} -gt 0 ] && note "  $mod: nothing to release"
            continue ;;
    esac
    printf '  %-20s %-8s %-13s %s\n' "$mod" "$(mod_version "$mod")" "$state" "$detail"
    due+=("$mod $state")
done
[ ${#due[@]} -gt 0 ] || { ok 'Nothing is due.'; exit 0; }
confirm "Release ${#due[@]} package(s)?" || { info 'Nothing written.'; exit 0; }

# ---- write, bump --------------------------------------------------------------------------------

released=()
files=()
for entry in "${due[@]}"; do
    read -r mod state <<< "$entry"
    dir=$(mod_dir "$mod")
    changelog="$dir/package/CHANGELOG.md"
    if [ "$state" = translations ]; then
        IFS=$'\t' read -r _ langs <<< "$(assess "$mod")"
        add_note "$changelog" "- Translations updated: $langs."
    fi
    is_pack "$mod" && sync_pack "$mod"

    if [ "$state" = new ]; then
        version=$(mod_version "$mod")
        awk -v v="$version" '
            !done && /^##[[:space:]]+[Uu]nreleased[[:space:]]*$/ { print "## " v; done = 1; next }
            { print }
        ' "$changelog" > "$changelog.tmp" && mv "$changelog.tmp" "$changelog"
        sed -i -E "s;(\"version_number\"[[:space:]]*:[[:space:]]*\")[^\"]+;\1$version;" "$dir/package/manifest.json"
        ok "$mod goes out as $version"
    else
        kind=patch
        unreleased_notes "$mod" | grep -qiE '^[[:space:]]*-[[:space:]]*(\*\*)?added' && kind=minor
        if [ "$assume_yes" -eq 1 ]; then
            "$root/scripts/bump.sh" "$mod" "$kind" -y
        else
            info "Suggested: $kind"
            "$root/scripts/bump.sh" "$mod"
        fi
    fi
    released+=("$mod")
    files+=("$(version_file "$mod")" "$dir/package/manifest.json" "$changelog")
done

# ---- commit, publish ----------------------------------------------------------------------------

summary=""
for mod in "${released[@]}"; do summary+="${summary:+, }$mod $(mod_version "$mod")"; done
if confirm "Commit \"Release $summary\"?"; then
    git -C "$root" commit -q -m "Release $summary" -- "${files[@]}"
    ok "Committed: Release $summary"
else
    note 'Not committed - status.sh only sees a release once its version is committed.'
fi

yes_flag=()
[ "$assume_yes" -eq 1 ] && yes_flag=(-y)
"$root/scripts/publish.sh" "${yes_flag[@]+"${yes_flag[@]}"}" "${released[@]}"
