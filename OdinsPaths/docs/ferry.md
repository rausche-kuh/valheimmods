# The ferryman: research and plan

Research of 2026-10-01 into a ghost at each harbour that appears only at night and, for a fee,
takes the player across to a harbour linked to its own. Nothing here is built yet. It depends
on the harbour stones (`Harbours`), which have not run in game - verify them first
(`ROADMAP.md`, "To check in game").

## What is already there

- **A spot beside the road** at every landing: `Harbours.Place` / `StoneBesideRoad` stand the
  stone there. The ghost stands near its stone and is placed in the same step; `Harbours.Relink`
  already walks every older harbour and can give those a ghost too.
- **A prefab of the mod's own**, registered on the server and on every client with the mod: the
  stone. The ghost follows the same pattern, so the multiplayer requirement does not change
  (clients already need the mod to see the stones).
- **The destinations**: a stone's links are on its own ZDO, which a client near it has.
  `Harbours.Group` gives every harbour reachable through others - the list of places the ferry
  can go, the farther the dearer.

## The parts

### The ghost

Copy only the `Visual` child (animator and meshes) of the game's `Ghost`, which is a network
prefab and so comes from `ZNetScene.GetPrefab` (no `SoftReference` load as for the stone). Put
it on a plain object with a `ZNetView` and a component of our own that is `Hoverable` and
`Interactable`. That is how Haldor is built: `Trader` is no `Character`, only a component beside
an animator. The whole `Ghost` is a hostile `Character` with `MonsterAI` (targeting, damage,
being attacked), none of which the ferryman needs. Like the furniture (`Relics`), it refuses the
hammer.

**Verify:** whether the `Visual` child's animator idles without the creature's components; what
the Deep North's `Ghost_sleeping` / `Spawner_Ghost_sleeping` are (the bundles hold them; their
scripts sit in a bundle not tracked down yet - probably the hostile ghost asleep, through
`MonsterAI.m_sleeping`).

### Night only

The ghost's ZDO always exists; the component hides its renderers and collider while
`EnvMan.IsNight()` is false, fading in and out (`vfx_ghost_spawn`), and refuses to interact by day.
Nothing is spawned or despawned. A setting for "always" costs almost nothing and helps anyone
who finds a night-only ferry too rare (sleeping skips the night, and the trip back lands the
player in the dark among night spawns).

### Choosing the harbour

1. **First: cycle on the ghost.** Alt-interact steps through the destinations, the hover text
   names the harbour and its price, interact pays and goes. Enough for the usual few links.
2. **Later: pick on the map.** Interact opens the large map, where the stone has already pinned
   the linked harbours in their group's colour; a click on one of them ferries there (a patch on
   the map's click). Worth it once groups grow large.
3. Not planned: a list panel of its own - most work, and its labels have to fit in every
   language.

### Price and payment

- **Coins:** `Inventory.CountItems("$item_coins")` and `RemoveItem(name, amount)`, on the client,
  which owns its inventory.
- **The price:** a base fee plus a rate per kilometre (straight line, or the sea leg the network
  knows), times a factor for the destination's biome (`WorldGenerator.GetBiome`). All config.
- **Items instead:** for far trips or the Ashlands / Deep North, an item from a config list per
  biome (a late meal or similar), handed over the vanilla way - `Interactable.UseItem`, the
  hotbar key, as with Haldor's and Hildir's quest items. **Verify** the item prefab names.
- **Optional gate:** no ferry into a biome before its boss key (e.g. `defeated_queen` for the
  Ashlands).

### The crossing

`Player.TeleportTo(pos, rot, distantTeleport: true)`: the game waits for the area and then
finds the floor. Two things to settle:

- **The destination's height.** Links keep only x and z, and a client seldom has the far
  stone's ZDO. Either store the height in the links (old links fall back to the generated
  ground) or ask the server for the far stone's position by RPC. The player arrives beside the
  far stone, facing the road.
- **Ore and the like.** `TeleportTo` does not check what may pass a portal, only the portal
  does, so the ferry carries ore by default, as a boat would. A setting can apply portal rules
  (`Humanoid.IsTeleportable`). The player's own ship stays where it is.

### Words

Greetings, the price and refusals ("only after dark", "not enough coins") through
`Chat.instance.SetNpcText`, as `Trader.Say` does, translated, about as long as the English.

## Order of work

1. The harbour stones verified in game.
2. The ghost prefab, placed with the stone and by `Relink`; night visibility.
3. Cycling destinations, coin price, the crossing (with the height settled).
4. Item payment and the biome gate.
5. The map pick, if groups turn out large.
