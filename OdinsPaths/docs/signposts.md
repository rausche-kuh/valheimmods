# Signposts

Planned and built 2026-10-01 (`src/Signposts.cs`), **not run in game yet**. The user's brief: a
signpost at every fork of the main network, at most two or three signs per direction; **hints
rather than names** (a giant tree, not the Elder; a deep mine, not the Queen); far places
**grouped by region** ("Deep North"), so a sign leads to the Deep North harbour and the harbour's
own posts then name what lies beyond it. Later (2026-10-01): bosses by their names after all.

## Game facts (read 2026-10-01, `Sign.cs`, `PrivateArea.cs`)

- A vanilla `sign` keeps its text on its ZDO (`ZDOVars.s_text`) and shows it in the world as
  is - **not localized**. A sign the server writes is in the server's words for every client.
- No author (`s_author` empty) counts as allowed to view, so a server-written sign shows on
  every client, vanilla ones too: signposts stay server side only.
- `m_characterLimit` bounds only what a player types; the server may set more.
- A ward keeps its placer's name on its ZDO (`ZDOVars.s_creatorName`).

## Where a post stands

- **Forks:** a road point on dry ground with three ways or more - where a main road set out from another (its
  first point joined to the nearest point of another road). **Main roads only** (the user,
  2026-10-01): spurs and side roads are not part of the graph, so they make no fork. None right at the sacrificial
  stones, where the roads leaving them join.
- **Harbours:** at every harbour stone beside a main road, found from the stones themselves
  (seen 2026-10-01: posts planned from the road's sea runs stood in front of the harbour, in the
  shallows), at the road's dry point nearest the stone, showing every way from there; inland is
  the way whose ground stays highest. **Beside the stone** (the user, 2026-10-01: out of the way
  but impossible to miss): on its side of the road, a little up the road from it. Where the
  stone stands on a dock, beside the road at that point like a fork's.
- Sites close together share one post: a harbour's first, then the one with more ways.
- Planning chooses the spot too, so `paths signs` pins what `place` builds; a site with no dry,
  free ground beside it gets no post (logged).
- **The post:** a `wood_pole_log_4`, its foot in the ground, in the widest gap between the ways,
  far enough out that both roads beside the gap pass clear; moved further out where something is
  built, into the next gap where the ground will not do, and no post where none will.
- **The ground** (seen 2026-10-01: posts sunk, one in the shallows): read as it stands - the
  generator's height plus the zone's terrain edits off its compiler ZDO, so the road's levelling
  counts. The foot goes in at the lowest ground around the pole; not in water or the shallows,
  not where the ground under a board's far end rises over its foot. One `sign` board **per line**, stacked from the top, each pointing its way like a
  fingerpost (its middle out along the way), its text toward the road. More lines than the pole
  has room for: a line off the way with the most, until they fit.
- **Weathered but whole** (the user, 2026-10-01): health lowered so the pieces look worn, but
  every piece stands.
- **Players may edit or take them down** (the user: keep it simple; no `Relics` marking). An
  edited text stays until the post's lines change; a post missing a piece is rebuilt at the next
  refresh.

## What a sign says

1. **The ways of a post** and, per way, the destinations reached through it: each road's end
   (its place, or a base), and the sacrificial stones at the start of each road leaving them,
   goes to the way its shortest walk from the post leaves by.
2. **Each destination has a hint and a biome.**
3. **No board for a place in sight** (the user, 2026-10-01): a fork beside a point of interest
   or road end does not name it.
4. **Lines, nearest first, at most `[Signposts] Lines` per way:**
   - a place in a **region** biome (`[Signposts] Regions`, default the Mistlands, Ashlands and
     Deep North) other than the post's own is the region's name; the post at the harbour or
     inside the region names its places;
   - every other place is its hint - the bosses of the Meadows and Black Forest by name (the
     user, 2026-10-01: a biome alone was too vague there); the same words twice are one line;
   - a post in a **remote** biome (`[Signposts] Remote`, default the Ashlands and Deep North)
     names only its own biome's places and the sacrificial stones (the user, 2026-10-01: the
     Elder on a Deep North post did not fit; out there the way back is all that counts);
   - the nearest lines stay, the rest are left to the next post.
5. Words: the hints below and *Sacrificial stones*, a base *{name}'s hut* by its ward's placer
   (*Hut* without one), a biome by the game's `$biome_*` token in the server's language, another
   location (a custom one) by `Progress.NameOf`. English in the code for now.

Hints (settled by the user, 2026-10-01): an infected mine _Dvergr mine_, a charred fortress
_Fortress_, a Deep North village _Village_, the winding tunnels _Tunnels_, Mörkhalla _Dark hall_
(mine, not settled), the traders _Trader_ / _Merchant_ / _Witch's hut_, the forge _Old forge_.
**A boss by its name in the game** (`Progress.NameOf`, the user, 2026-10-01), **Eikthyr on no
sign** - it matters only for the first hours; its road still counts as a way.

## When

Once at the end of a growth that laid a road (`Grower.Run`, `paths grow`), and with `paths signs place`: every post planned
afresh from the network, a post standing where it should with the same lines and every piece is
kept, others are rebuilt, and posts the network no longer has are taken down. Posts are found
by a marker on each piece (`OdinsPaths_Signpost`, the post's position); the pole holds the lines
and the piece count. `paths signs` plans them and pins each one with its boards, placing nothing.
With the whole network laid from the start (the user, 2026-10-01; `ROADMAP.md`), every
destination is on the signs from the first day - which is why they give hints.

## To check in game

- Which side of a board the text reads from (taken from the sign's text widget) and that the
  boards point the right way; how the stack looks on the pole, the boards' spacing.
- That `wood_pole_log_4` and `sign` stand as placed in a zone generated after them (support).
- Posts clear of the road, the harbour stone and the dock; at a narrow fork, clear of both roads.
- Whether the lines read well: too many regions, too few, the own-biome rule.
- How long a refresh takes on a whole network (one Dijkstra per post, on the main thread).
