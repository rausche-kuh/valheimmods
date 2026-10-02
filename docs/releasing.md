# Releasing

`bump`, `package` and `publish`: from a finished change to a version on Thunderstore and Hexium.
The usual release is `bump`, commit, `publish`. The overview is [scripts/README.md](../scripts/README.md).

## `bump` — raise a version for a release

```powershell
.\scripts\bump.ps1 [mod] [major|minor|patch] [-Yes]
```

```bash
./scripts/bump.sh [mod] [major|minor|patch] [-y]
```

Asks which mod and which part of `major.minor.patch` to raise — both menus are skipped when given
as arguments — then shows the new version, what the release would ship and waits for a `y`. On
confirmation it writes all three places a version lives:

- the `VERSION` const in the mod's plugin source, which is the source of truth,
- `version_number` in its `package/manifest.json`,
- the `## Unreleased` heading in its `package/CHANGELOG.md`, which becomes `## <version>`.

A minor bump zeroes the patch, a major one zeroes both. If there is nothing under `## Unreleased`
it says so before asking, since that release would show up on Thunderstore with no notes. Nothing
is built, committed or published — commit, then run `publish`.

## `package` — Thunderstore zips

```powershell
.\scripts\package.ps1 [mod ...]
```

```bash
./scripts/package.sh [mod ...]
```

Reads each mod's `VERSION` const from its `src/`, stamps it into its `package/manifest.json`,
builds Release, and writes a flat `dist/<Mod>-<version>.zip` containing the DLL, the assets and the
mod's `package/` folder — `manifest.json`, `icon.png`, `README.md` and `CHANGELOG.md`. On Linux it
uses `zip` if present, otherwise `python3`.

The shipped README is `<Mod>/package/README.md`, and it is the Thunderstore page: what the mod does,
how to install it, multiplayer and compatibility notes. Nothing about building from source and no
links relative to the repo — Thunderstore renders it standalone, so a `../` link is a dead link.
`<Mod>/package/CHANGELOG.md` sits next to it and becomes the Changelog tab on the mod page: a
`## <version>` section per release, newest first, with `## Unreleased` on top for what has not
shipped yet. `<Mod>/README.md` is the dev facing one and is not shipped.

Only bump `VERSION` for an actual Thunderstore release, and use `bump` above to do it.
Check `dependencies` in that mod's `package/manifest.json` against the current BepInEx pack first
(`denikson-BepInExPack_Valheim-5.4.2350` as of 2026-09-21).

### Preview images on the mod page

Thunderstore does not serve anything out of the zip, so an image on the page needs an absolute
URL. Put screenshots and clips in the repo's top level `images/` (it is not zipped) and link them
by their raw GitHub URL:

```markdown
![A friend's marker on the map](https://raw.githubusercontent.com/rausche-kuh/valheimmods/main/images/friend_marker.webp)
```

Clips must be animated WebP (or GIF): a markdown image is an `<img>`, which cannot play webm or
mp4, and Thunderstore strips `<video>`. `ffmpeg -i clip.webm -vf fps=20,scale=720:-1 -c:v libwebp
-q:v 50 -loop 0 clip.webp` keeps a short clip small.

The image only shows once the commit is pushed. Links to `main` always show the current file, so
rename rather than overwrite when an old image should stay as it was, or use a tag in place of
`main` to pin a release to its images. `<img src="…" width="600">` scales one down.

## `publish` — upload to Thunderstore and Hexium (Linux only)

```bash
./scripts/publish.sh [-n|--dry-run] [-y|--yes] [mod ...]
```

Asks both sites whether each mod's current `VERSION` is already up
(`/api/experimental/package/rauschekuh/<Mod>/<version>/`), then packages and uploads only what is
missing — a version that is out is never built or sent again, and after a half failed run the next
one sends just the rest. The usual release is `bump`, commit, `publish`; with no mod names it
checks every mod, so a bare `./scripts/publish.sh` is also "is everything out?".

Before anything is sent it refuses a version whose `## <version>` heading is missing from the
changelog (`bump` was not run), checks the Thunderstore categories against the site's list, warns
about uncommitted changes in the mod, prints the plan and waits for a `y`. `--dry-run` stops after
the plan, `--yes` skips the question.

Hexium is a Thunderstore fork with the same API and zip layout, so the same zip goes to both.
What differs per site is the categories, which each mod keeps in `package/publish.json` (not
shipped):

```json
{
  "thunderstore": ["client-side", "tweaks"],
  "hexium": ["Client-only"]
}
```

Thunderstore takes slugs (`client-side`, the list is at
`https://thunderstore.io/api/experimental/community/valheim/category/`), Hexium takes names, plus
the side categories its listing leaves out: `Client-only`, `Client & Server`, `Client (& Server)`,
`Server-only`. A site missing from the file is not published to, and a mod without the file is
never published — add one to publish a new mod.

The tokens come from `THUNDERSTORE_TOKEN` and `HEXIUM_TOKEN`, or from a gitignored `.publish.env`
at the repo root with those two `NAME=value` lines. Thunderstore's is a service account token
(team settings, _Service Accounts_), Hexium's an API token from the team page on valheim.hexium.gg.

