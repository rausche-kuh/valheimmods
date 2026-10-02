# Shared helpers for the scripts in this folder. Sourced, never executed.
#
# A mod is any directory holding <Name>/<Name>.csproj, at the repo root or one level down (the
# members of a family such as OdinsMissingPatch/). A pack is a directory holding
# package/manifest.json and no csproj: a Thunderstore modpack, nothing to build, its version in
# the manifest. Names are unique across the repo, so a script only ever needs the name.
# Scripts that take mod names default to every mod and pack in the repo.

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
# Thunderstore team name - the install folder and zip are named <author>-<mod>.
author=rauschekuh

die()  { printf '\033[31merror:\033[0m %s\n' "$*" >&2; exit 1; }
info() { printf '%s\n' "$*"; }
note() { printf '\033[33m%s\033[0m\n' "$*"; }
step() { printf '\033[36m%s\033[0m\n' "$*"; }
ok()   { printf '\033[32m%s\033[0m\n' "$*"; }

# The leading comment block of the calling script, minus the shebang.
usage() { awk 'NR==1 {next} /^#/ {sub(/^# ?/, ""); print; next} {exit}' "$0"; }

# The directories a mod or pack may sit in: the root's and their children, minus what the scripts
# generate (decompiled/ holds a csproj per game assembly).
candidate_dirs() {
    local d
    for d in "$root"/*/ "$root"/*/*/; do
        case "$d" in "$root"/decompiled/*|"$root"/lib/*|"$root"/dist/*) continue ;; esac
        printf '%s\n' "${d%/}"
    done
}

# Every mod, then every pack: a pack depends on its members, so they go first wherever order matters.
all_mods() {
    local d n
    while read -r d; do
        n=${d##*/}
        [ -f "$d/$n.csproj" ] && printf '%s\n' "$n"
    done < <(candidate_dirs)
    while read -r d; do
        n=${d##*/}
        [ -f "$d/package/manifest.json" ] && ! ls "$d"/*.csproj >/dev/null 2>&1 && printf '%s\n' "$n"
    done < <(candidate_dirs)
    return 0
}

# The directory of a mod or pack, by name.
mod_dir() {
    local d
    for d in "$root/$1" "$root"/*/"$1"; do
        if [ -f "$d/$1.csproj" ] || { [ -f "$d/package/manifest.json" ] && ! ls "$d"/*.csproj >/dev/null 2>&1; }; then
            printf '%s\n' "$d"
            return 0
        fi
    done
    die "unknown mod '$1' (have: $(all_mods | tr '\n' ' '))"
}

# Whether the name is a pack: a manifest and nothing to build.
is_pack() {
    local d
    d=$(mod_dir "$1")
    [ ! -f "$d/$1.csproj" ]
}

# Mod names given as arguments, or every mod and pack if there were none.
resolve_mods() {
    local m
    if [ $# -eq 0 ]; then
        all_mods
        [ -n "$(all_mods)" ] || die "no mods found in $root"
        return 0
    fi
    for m in "$@"; do
        mod_dir "$m" >/dev/null
        printf '%s\n' "$m"
    done
}

# The version of a mod: the VERSION const in its plugin source, the single source of truth. A
# pack has no source, so its manifest's version_number is.
mod_version() {
    local d v
    d=$(mod_dir "$1")
    if is_pack "$1"; then
        v=$(sed -nE 's/.*"version_number"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "$d/package/manifest.json" | head -1)
    else
        v=$(grep -rhoE 'VERSION[[:space:]]*=[[:space:]]*"[^"]+"' "$d/src" 2>/dev/null |
            head -1 | sed -E 's/.*"([^"]+)"/\1/')
    fi
    [ -n "$v" ] || die "could not read the version of $1"
    printf '%s\n' "$v"
}

# The file a mod's version is written in: the plugin source holding VERSION, or a pack's manifest.
version_file() {
    local d f
    d=$(mod_dir "$1")
    if is_pack "$1"; then
        printf '%s\n' "$d/package/manifest.json"
        return 0
    fi
    f=$(grep -rlE 'VERSION[[:space:]]*=[[:space:]]*"[^"]+"' "$d/src" 2>/dev/null | head -1)
    [ -n "$f" ] || die "could not find VERSION in ${d#$root/}/src"
    printf '%s\n' "$f"
}

# The commit that set the mod's current version - its last release, as far as the repo can tell.
# No tags: bump writes the version, the release is committed with it, and every checkout can
# work this out on its own. Read from the manifest, which bump and package keep in step with the
# source for mods and which is the version for packs; followed through renames, so a mod moved
# into a family keeps its history. Empty when the version was never committed (a new mod).
release_commit() {
    local manifest v rel line c path text
    manifest="$(mod_dir "$1")/package/manifest.json"
    v=$(mod_version "$1")
    rel=${manifest#$root/}
    # A move not committed yet has no history at its new path: follow it back to the old one.
    if [ -z "$(git -C "$root" log -1 --format=%H -- "$rel" 2>/dev/null)" ]; then
        path=$(git -C "$root" diff -M --name-status HEAD 2>/dev/null |
            awk -F '\t' -v n="$rel" '$1 ~ /^R/ && $3 == n { print $2 }')
        [ -n "$path" ] && rel=$path
    fi
    local -a found=()
    # Newest first, each commit followed by the path the file had in it.
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        if [[ $line =~ ^[0-9a-f]{40}$ ]]; then c=$line; else found+=("$c $line"); fi
    done < <(git -C "$root" log --follow --format=%H --name-only -- "$rel" 2>/dev/null)
    # Oldest first: the first commit whose copy holds this version set it.
    local i
    for ((i = ${#found[@]} - 1; i >= 0; i--)); do
        read -r c path <<< "${found[$i]}"
        text=$(git -C "$root" show "$c:$path" 2>/dev/null || true)
        # No pipe into grep -q: its quitting early would SIGPIPE the writer and fail under pipefail.
        if grep -qE "\"version_number\"[[:space:]]*:[[:space:]]*\"$v\"" <<< "$text"; then
            printf '%s\n' "$c"
            return 0
        fi
    done
    return 0
}

# The shared source a family member compiles in: <family>/Common/, or nothing for a lone mod.
family_common() {
    local d
    d=$(dirname "$(mod_dir "$1")")
    [ "$d" != "$root" ] && [ -d "$d/Common" ] && printf '%s\n' "$d/Common"
    return 0
}

# What the mod's changelog holds under ## Unreleased, blank lines dropped.
unreleased_notes() {
    awk '
        /^##[[:space:]]+[Uu]nreleased[[:space:]]*$/ { inside = 1; next }
        /^##[[:space:]]/                            { inside = 0 }
        inside && NF                                { print }
    ' "$(mod_dir "$1")/package/CHANGELOG.md"
}

# The mod manager profile scripts/setup.sh picked, e.g. .../gale/valheim/profiles/Default.
profile_dir() {
    local props="$root/Valheim.props" p
    [ -f "$props" ] || die 'Valheim.props missing - run scripts/setup.sh first.'
    p=$(sed -nE 's;.*<ProfileDir>(.*)</ProfileDir>.*;\1;p' "$props")
    [ -n "$p" ] || die 'no <ProfileDir> in Valheim.props - re-run scripts/setup.sh.'
    printf '%s\n' "$p"
}

# The files under a directory that differ from a commit: committed since, uncommitted, untracked.
changed_since() {
    local base=$1 dir=${2#$root/}
    {
        git -C "$root" diff --name-only "$base" -- "$dir"
        git -C "$root" ls-files --others --exclude-standard -- "$dir"
    } | sort -u
}

# The languages whose words differ in a translations.csv between a commit and the working tree,
# comma separated in header order. A row the commit lacks counts for every language it fills.
changed_languages() {
    local base=$1 csv=$2 rel=${2#$root/}
    python3 - "$csv" <(git -C "$root" show "$base:$rel" 2>/dev/null || true) <<'PY'
import csv, io, sys

def table(path):
    try:
        text = open(path, encoding='utf-8').read()
    except OSError:
        return [], {}
    lines = [l for l in text.splitlines() if l.strip() and not l.lstrip().startswith('//')]
    rows = list(csv.reader(io.StringIO('\n'.join(lines))))
    if not rows:
        return [], {}
    return rows[0], {r[0]: r for r in rows[1:] if r}

head, now = table(sys.argv[1])
old_head, old = table(sys.argv[2])
changed = []
for i, language in enumerate(head[1:], 1):
    j = old_head.index(language) if language in old_head else None
    for key, row in now.items():
        cell = row[i] if i < len(row) else ''
        before = old.get(key)
        was = before[j] if before is not None and j is not None and j < len(before) else ''
        if cell != was:
            changed.append(language)
            break
print(', '.join(changed))
PY
}

# What a mod has waiting since its last release, as "state<TAB>detail":
#   new          never released (no commit holds its current version yet)
#   release      ## Unreleased has notes - something players see changed
#   translations only translations.csv changed; detail names the languages
#   unnoted      shipped files changed without a changelog note - release optional
#   shared       only the family's Common/ changed - ships with the next release, never forces one
#   clean        nothing
# A pack is "release" with notes, otherwise "clean"; its member versions are status.sh's business.
assess() {
    local mod=$1 dir base files shipped csv common notes langs
    dir=$(mod_dir "$mod")
    base=$(release_commit "$mod")
    notes=$(unreleased_notes "$mod")
    if [ -z "$base" ]; then
        printf 'new\t%s\n' "$([ -n "$notes" ] && echo 'first release, notes written' || echo 'first release, no notes yet')"
        return 0
    fi
    if [ -n "$notes" ]; then
        printf 'release\t%s note(s)\n' "$(printf '%s\n' "$notes" | grep -c '^[[:space:]]*-')"
        return 0
    fi
    is_pack "$mod" && { printf 'clean\t\n'; return 0; }
    files=$(changed_since "$base" "$dir")
    # What reaches a player: the source minus the Debug-only src/Dev/, the assets, the page.
    shipped=$(printf '%s\n' "$files" | grep -E "^${dir#$root/}/(src/|assets/|package/(README\.md|icon\.png))" |
        grep -vE "^${dir#$root/}/src/Dev/" || true)
    csv="${dir#$root/}/assets/translations.csv"
    if [ -n "$shipped" ] && [ -z "$(printf '%s\n' "$shipped" | grep -vxF "$csv")" ]; then
        langs=$(changed_languages "$base" "$root/$csv")
        printf 'translations\t%s\n' "${langs:-no words changed}"
        return 0
    fi
    if [ -n "$shipped" ]; then
        printf 'unnoted\t%s file(s), e.g. %s\n' "$(printf '%s\n' "$shipped" | wc -l)" \
            "$(printf '%s\n' "$shipped" | head -1 | sed "s;^${dir#$root/}/;;")"
        return 0
    fi
    common=$(family_common "$mod")
    if [ -n "$common" ] && [ -n "$(changed_since "$base" "$common")" ]; then
        printf 'shared\t%s changed\n' "${common#$root/}"
        return 0
    fi
    printf 'clean\t\n'
}

# The members a pack lists that live in this repo, as "name<TAB>listed version".
pack_members() {
    python3 - "$(mod_dir "$1")/package/manifest.json" "$author" <<'PY'
import json, sys
deps = json.load(open(sys.argv[1], encoding='utf-8')).get('dependencies', [])
for d in deps:
    team, _, rest = d.partition('-')
    name, _, version = rest.rpartition('-')
    if team == sys.argv[2]:
        print(f'{name}\t{version}')
PY
}
