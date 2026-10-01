# The trader and the inventory rows

PocketUpgrades.

## Conventions

- A gate on something the trader sells is moved by writing the item's own
  `m_requiredGlobalKey` before the trader filters its shelf, never by replacing the filter: the
  game goes on asking the question, so the shop list, the price, the purchase and any condition
  the game grows later stay entirely vanilla, and the patch is a prefix on the one method the
  filter lives in. The vanilla gate of every item touched is kept aside in a
  `ConditionalWeakTable` and the config is applied to *that* (`Ranges`' idempotent
  rescale, one bullet of `conventions.md`), so the write can simply be repeated every time the
  shelf is read - no bookkeeping, and switching the tweak off puts the vanilla gate back on the
  next read rather than needing a restore.
- An item on the shelf is identified by its `m_buyKey`, not by its name, its index in `m_items` or
  the trader holding it. The name is a localization token, the list shifts whenever content is
  added, and the buy key is the character's record of the purchase - so it is both stable and the
  reason the item exists. Matching on it also means any trader who ever sells the same upgrade is
  covered, with no check for Haldor by name.

## Game facts

- Haldor is a `Trader` (`Assets/Characters/TraderHaldor/Haldor.prefab`, bundle `c4210710`) and his
  shelf is `m_items`, a list of the serializable `Trader.TradeItem`: prefab, stack, price,
  `m_requiredGlobalKey`, and - under a `Player Key Item` header - `m_icon`, `m_name`, `m_tooltip`,
  `m_buyKey`, `m_incrementKey`, `m_incrementAmount` for the things he sells that are not items.
  `Trader.GetAvailableItems()` is the whole gate, and everything the player sees goes through it:
  an item is offered while its `m_requiredGlobalKey` is empty or set in the world
  (`ZoneSystem.GetGlobalKey`, the world's own progress - a kill the *character* carries from
  another world is not asked about) and its `m_buyKey` is empty or not yet among the character's
  unique keys. Its only callers are `StoreGui.FillList` and `Trader.DiscoverItems`, both local, so
  nothing about the shelf crosses the network.
- Buying a key item (`StoreGui.OnBuyItem`) pays the coins, `AddUniqueKey(m_buyKey)` - which is what
  makes it once per character, forever - and then adds `m_incrementAmount` to the running value of
  `m_incrementKey`. That last step carries one hardcoded special case: `m_incrementKey == "invrows"`
  calls `Player.SetInventorySize(total)` instead of storing the value, so inventory rows are the
  only key item the game does anything with.
- The two upgrades, off Haldor's prefab: `$hud_extrainvslot1` = **Wider Pockets**, 1000 coins,
  gated on `defeated_dragon` (Moder), buy key `invslot1`; `$hud_extrainvslot2` = **Deeper Pockets**,
  2000 coins, gated on `defeated_queen` (the Queen), buy key `invslot2`. Both are
  `m_incrementKey = "invrows"`, `m_incrementAmount = 1`, i.e. one row each, so a character who
  bought both carries 6 rows. The English names are in `valheim_Data/resources.assets`
  (`hud_extrainvslot1,Wider Pockets` ... `hud_extrainvslot2,Deeper Pockets`), the icons are
  `PocketIcon1.png` / `PocketIcon2.png`; which boss sets which key is in
  [radial-menu](radial-menu.md), read off the boss prefabs for PowerPicker.
- A player's inventory is built 8x4 (`Humanoid.DefaultInventoryHeight`, `Player.InventoryRowsKey`).
  `Player.SetInventorySize(rows)` clamps to 0-9, sets the inventory's height, stores
  `AddUniqueKeyValue("invrows", rows)`, grows the inventory panel by
  `(rows - 4) * m_invGridHeight` (`InventoryGui.SetInventorySize`) and drops what no longer fits
  (`DropInvalidItems`). `Player.OnSpawned` reads `invrows` back and applies it, writing `invrows=4`
  when the character has none - so the row count is saved with the *character* while the gate is
  the *world's* progress, and a character takes its bought rows into a fresh world.
