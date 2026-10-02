# Item order

`src/MaterialOrder.cs` — what an item *is*, derived from the game every sort asks about.

## Conventions

- Anything that wants to know what an item *is* beyond its `ItemType` derives it from the game's
  data rather than from a list of item names (`MaterialOrder`): a name list goes stale with every
  update, says nothing about a modded item and has to be maintained, where the recipes, the
  buildable pieces and the station conversions already hold the whole progression. What the data
  cannot tell apart it cannot tell apart - every ore is "once you have a smelter" as far as the
  recipes go - so such an order ends in the display name, which is stable and is what the player
  reads. Derive it once per `ObjectDB` (keyed on the instance plus its recipe and item counts, so

## Game facts

- The whole crafting progression is reachable from `ObjectDB` alone: `m_recipes` (each with
  `m_item`, `m_resources`, `m_craftingStation` and `m_minStationLevel`), `GetAllBuildPieces(true)`
  for every buildable `Piece` (its `m_resources` and `m_craftingStation`), and the conversions
  those pieces run - `Smelter.m_conversion` (the smelter, the kiln, the windmill, the spinning
  wheel and the eitr refinery all use `Smelter`), `CookingStation.m_conversion`,
  `Fermenter.m_conversion`, each a plain from/to pair. A station is told from its piece by both
  components sitting on the same prefab. `GetAllBuildPieces` caches its list on first call and the
  game's own single caller (`Achievements`) passes `includeHidden: true`, so asking for the hidden
  ones changes nothing for the game. Fuel is *not* a conversion (`Smelter.m_fuelItem` stands
  apart), so coal's place comes from what it builds, not from what it burns in.
- `ItemDrop.ItemData.SharedData` is one object per item, shared by every stack of it -
  `ObjectDB.m_itemByData` is keyed on it and `ItemData.Clone()` is a `MemberwiseClone`, so an
  item in an inventory carries the very `m_shared` its prefab has. That makes it the key for a
  per-item table with no name lookup and no hashing of strings.
- `m_shared.m_teleportable` is the game's own "a portal refuses this" flag, and in vanilla it is
  false for exactly the ores and bars. It is the one field that groups the metals without naming
  one.
