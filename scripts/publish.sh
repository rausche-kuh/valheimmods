#!/usr/bin/env bash
# Uploads each mod's current version to Thunderstore and Hexium - wherever that version is not up
# yet. Asks both sites first, so a version that is already published is never built or sent again;
# re-running after a half failed publish only sends what is still missing.
#
# A mod is published by its package/publish.json, which holds the categories per site:
#   { "thunderstore": ["client-side", "tweaks"], "hexium": ["Client-only"] }
# A site missing from it is not published to; a mod without one is skipped.
#
# The API tokens come from THUNDERSTORE_TOKEN and HEXIUM_TOKEN, or from a gitignored .publish.env
# at the repo root holding those two lines. Shows the plan and waits for a y before uploading.
# With no mod names, every mod and pack in the repo is checked; packs go last, since Thunderstore
# only takes a pack whose dependencies are already up.
#
# Usage: scripts/publish.sh [-n|--dry-run] [-y|--yes] [mod ...]
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

dry_run=0
assume_yes=0
mods=()
while [ $# -gt 0 ]; do
    case "$1" in
        -h|--help)     usage; exit 0 ;;
        -n|--dry-run)  dry_run=1; shift ;;
        -y|--yes)      assume_yes=1; shift ;;
        -*)            die "unknown argument: $1" ;;
        *)             mods+=("$1"); shift ;;
    esac
done
named=${#mods[@]}
mods=($(resolve_mods "${mods[@]+"${mods[@]}"}"))

command -v curl >/dev/null    || die 'curl is needed'
command -v python3 >/dev/null || die 'python3 is needed'

# key, site root, token variable. The key is the one publish.json uses.
sites=(
    'thunderstore https://thunderstore.io THUNDERSTORE_TOKEN'
    'hexium       https://hexium.gg       HEXIUM_TOKEN'
)
community=valheim

# The categories publish.json gives a site, as a JSON array - or nothing if the site is not in it.
categories() {
    python3 - "$1" "$2" <<'PY'
import json, sys
try:
    data = json.load(open(sys.argv[1], encoding='utf-8'))
except ValueError as e:
    sys.exit(f'{sys.argv[1]}: {e}')
if sys.argv[2] in data:
    print(json.dumps(data[sys.argv[2]]))
PY
}

# 200 when that version is up, 404 when it is not.
published() {
    curl -s -o /dev/null -w '%{http_code}' --max-time 30 \
        "$1/api/experimental/package/$author/$2/$3/" || printf '000'
}

# Thunderstore rejects an unknown category slug only after the upload, so check them up front.
# Hexium's category listing leaves out the side ones (Client-only, ...) it accepts, so only
# Thunderstore is checked; a bad Hexium category fails at submit instead.
check_categories() {
    python3 - "$1" "$2" "$3" <<'PY'
import json, sys, urllib.request
url, wanted, mod = sys.argv[1], json.loads(sys.argv[2]), sys.argv[3]
# Thunderstore turns away urllib's default User-Agent with a 403.
req = urllib.request.Request(url, headers={'User-Agent': 'rauschekuh-publish'})
with urllib.request.urlopen(req, timeout=30) as r:
    data = json.load(r)
known = {c['slug'] for c in data.get('results', data)}
bad = [c for c in wanted if c not in known]
if bad:
    sys.exit(f"{mod}: unknown Thunderstore categories {bad} - valid: {', '.join(sorted(known))}")
PY
}

# ---- plan: what is missing where ----------------------------------------------------------------

uploads=()   # "mod version key root tokenvar categories-json"
dirty=()
step 'Checking what is published...'
for mod in "${mods[@]}"; do
    dir=$(mod_dir "$mod")
    spec="$dir/package/publish.json"
    if [ ! -f "$spec" ]; then
        [ "$named" -gt 0 ] && die "${spec#$root/} missing - it names the categories per site."
        note "  $mod: no package/publish.json, not published"
        continue
    fi
    version=$(mod_version "$mod")
    line=$(printf '  %-20s %-8s' "$mod" "$version")
    wants=0
    for site in "${sites[@]}"; do
        read -r key url tokenvar <<< "$site"
        cats=$(categories "$spec" "$key")
        [ -n "$cats" ] || continue
        case "$(published "$url" "$mod" "$version")" in
            200) line+=" $key: up" ;;
            404) line+=" $key: $(printf '\033[32mnew\033[0m')"; wants=1
                 uploads+=("$mod $version $key $url $tokenvar $cats") ;;
            *)   die "could not ask $url whether $mod $version is published" ;;
        esac
    done
    printf '%s\n' "$line"

    [ "$wants" -eq 1 ] || continue
    # A version that bump never closed off would go out without its notes.
    grep -qE "^##[[:space:]]+$(printf '%s' "$version" | sed 's/\./\\./g')[[:space:]]*$" \
        "$dir/package/CHANGELOG.md" ||
        die "${dir#$root/}/package/CHANGELOG.md has no ## $version - run scripts/bump.sh $mod first."
    [ -z "$(git -C "$root" status --porcelain -- "$dir")" ] || dirty+=("$mod")
done

if [ ${#uploads[@]} -eq 0 ]; then
    ok 'Everything is published.'
    exit 0
fi

for mod in "${dirty[@]+"${dirty[@]}"}"; do
    note "$mod has uncommitted changes - they would be in the upload."
done

for u in "${uploads[@]}"; do
    read -r mod version key url tokenvar cats <<< "$u"
    [ "$key" = thunderstore ] || continue
    check_categories "$url/api/experimental/community/$community/category/" "$cats" "$mod" ||
        exit 1
done

[ "$dry_run" -eq 0 ] || { info 'Dry run - nothing uploaded.'; exit 0; }

# Tokens only matter once something is to be sent.
if [ -f "$root/.publish.env" ]; then
    . "$root/.publish.env"
fi
for u in "${uploads[@]}"; do
    read -r mod version key url tokenvar cats <<< "$u"
    [ -n "${!tokenvar:-}" ] || die "$tokenvar is not set - export it or put it in .publish.env"
done

if [ "$assume_yes" -eq 0 ]; then
    printf '\nUpload %d package(s)? This cannot be taken back. [y/N] ' "${#uploads[@]}" >&2
    read -r reply < /dev/tty || reply=''
    [[ $reply =~ ^[Yy]$ ]] || { info 'Nothing uploaded.'; exit 0; }
fi

# ---- build and send ------------------------------------------------------------------------------

packaged=' '
for u in "${uploads[@]}"; do
    read -r mod version key url tokenvar cats <<< "$u"
    zip="$root/dist/$mod-$version.zip"
    # Built fresh once per run - a zip left in dist/ may be older than the source.
    if [[ $packaged != *" $mod "* ]]; then
        "$root/scripts/package.sh" "$mod"
        packaged+="$mod "
    fi
    [ -f "$zip" ] || die "package.sh did not produce ${zip#$root/}"

    step "Uploading $mod $version to $key..."
    TOKEN=${!tokenvar} python3 - "$url/api/experimental" "$zip" "$author" "$community" "$cats" <<'PY'
import json, os, sys, urllib.error, urllib.request

api, zip_path, author, community, categories = sys.argv[1:6]
token = os.environ['TOKEN']
archive = open(zip_path, 'rb').read()
# Thunderstore turns away urllib's default User-Agent with a 403.
agent = 'rauschekuh-publish'

def call(path, body):
    req = urllib.request.Request(
        f'{api}/{path}', data=json.dumps(body).encode(), method='POST',
        headers={'Authorization': f'Bearer {token}', 'Content-Type': 'application/json',
                 'User-Agent': agent})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            text = r.read().decode()
            return json.loads(text) if text else {}
    except urllib.error.HTTPError as e:
        sys.exit(f'{path} failed: {e.code} {e.read().decode(errors="replace")}')

# The upload is multipart: the site hands out one presigned URL per byte range, each PUT returns an
# ETag, and finishing the upload with every ETag turns it into media a submission can point at.
started = call('usermedia/initiate-upload/',
               {'filename': os.path.basename(zip_path), 'file_size_bytes': len(archive)})
uuid = started['user_media']['uuid']
parts = []
try:
    for p in started['upload_urls']:
        chunk = archive[p['offset']:p['offset'] + p['length']]
        req = urllib.request.Request(p['url'], data=chunk, method='PUT',
                                     headers={'Content-Type': 'application/octet-stream',
                                              'User-Agent': agent})
        with urllib.request.urlopen(req, timeout=300) as r:
            parts.append({'ETag': r.headers['ETag'], 'PartNumber': p['part_number']})
    call(f'usermedia/{uuid}/finish-upload/', {'parts': parts})
except BaseException:
    try:
        call(f'usermedia/{uuid}/abort-upload/', {'uuid': uuid})
    except BaseException:
        pass
    raise

result = call('submission/submit/', {
    'upload_uuid': uuid,
    'author_name': author,
    'communities': [community],
    'categories': [],
    'community_categories': {community: json.loads(categories)},
    'has_nsfw_content': False,
})
version = result.get('package_version', {})
print(version.get('full_name') or json.dumps(result))
if result.get('hidden'):
    print('\033[33mthe site accepted it as hidden - check the package page\033[0m')
PY
    ok "$mod $version is on $key"
done
