# Map pins: how the game does it

The game facts behind [`map-pins.md`](map-pins.md) (`SharedMapTable`, `AutoPins`, `PinLooks`,
`DeathPins`, `UniversalPins`, `PinBroadcast`), read out of `decompiled/assembly_valheim/` for the
current build, with line numbers. Split out of `map-pins.md` on 2026-09-26.

All in `decompiled/assembly_valheim/Minimap.cs` unless said otherwise. The publicizer makes every
private member below reachable.

- **A pin** is `Minimap.PinData` (line 47): `m_name`, `m_type` (`PinType`, line 25: `Icon0`
  fire, `Icon1` house, `Icon2` hammer, `Icon3` orb, `Icon4` portal, then `Death`, `Bed`, `Shout`,
  `None`, `Boss`, `Player`, ..., `Memorial`), `m_pos`, `m_save`, `m_ownerID`, `m_author`
  (`PlatformUserID`), `m_checked`, plus UI state (`m_uiElement`, `m_iconElement`,
  `m_NamePinData`). All pins, of every kind, live in the one list `m_pins` (line 316).
- **Pin names are localized when drawn** (`PinNameData.SetTextAndGameObject`, line 97), so a
  `$token` name shows in each reader's own language. A pin with a valid author that is not the
  local user goes through `CensorShittyWords.FilterUGC`, which returns the text unchanged unless
  the platform requires text filtering (consoles).
- **`PinNameData.PinNameText` is a `TMP_Text`**, and TextMeshPro is not among the staged
  references: naming the property is CS0012. `PinLooks.NameText` finds the same child as the
  `Graphic` whose type name starts with `TextMeshPro`.
- **Profile save** (`GetMapData` line 2150 / `SetMapData` 2185) keeps every `m_save` pin with
  name, pos, type, checked, owner and author. Owner and author round-trip; nothing else about a pin
  survives a reload, so the icon is re-derived from the type on load. Filter state
  (`m_visibleIconTypes`) is not saved.
- **Adding** is `AddPin(pos, type, name, save, isChecked, ownerID, author)` (line 2352). A type
  past `m_visibleIconTypes.Length` is coerced to `Icon3` with a warning, which is why custom
  types are fragile. Adding a pin of a hidden type flips that type visible again
  (`ToggleIconFilter`, 2658, which also rumbles a gamepad); `UniversalPins.Add` puts the filter
  and the legend's grey back itself. `HaveSimilarPin` (2340) is name+type+save within 1m;
  `HavePinInRange` (2256) is any saved pin within a radius. `DiscoverLocation` (2309) is the
  game's own "auto pin" helper, and its message token **`$msg_pin_added`** ("Map location added")
  is what AutoPins shows too — there is no `$omp_` row for it.
- **Removing** is `RemovePin(PinData)` (2292). The player's removals — right click
  (`RemovePinUnderPointer`, 2528) and the gamepad's `JoyTabRight` (1009) — both go through
  **`RemovePin(Vector3, float)`** (2245), and nothing else does: the table read, the death pin and
  the event pins remove by `PinData`. So a prefix on the `Vector3` overload is exactly "the player
  took this off the map", which is where the removed-pin record is written.
- **Left click** (`OnMapLeftClick` 2476, and the gamepad's `JoyTabLeft` at 1015 inside
  `UpdateMap`): a pin with `m_ownerID != 0` is *claimed* (owner set to 0) instead of checked.
  Second click checks. The gamepad branch does **not** set `m_pinUpdateRequired`, so a fix in the
  `UpdatePins` postfix would lag behind it; `UniversalPins.UndoClaims` is a `Minimap.Update`
  postfix instead, while the large map is open.
- **Drawing** (`UpdatePins` 1627) runs only when `m_pinUpdateRequired` (Update, line 761), so on
  map movement, zoom, or a pin change, not every frame. For each pin: skipped when off screen,
  when its type is filtered, or when it has an owner and `m_sharedMapDataFade` is 0. A pin with an
  owner is drawn in `(0.7, 0.7, 0.7, 0.8 * fade)` and switches on `m_sharedMapHint`; others are
  white. Names show only in the large map below `m_showNamesZoom` (0.5; the large map zooms from
  `m_minZoom` 0.01 to `m_maxZoom` 1). `m_iconElement.color` and the name's colour are written on
  every run, so a tint has to be reapplied after it. **The game never activates a marker it
  finds inactive** — it only creates, moves and destroys them — so a patch that culls by
  `SetActive(false)` has to turn it back on itself.
- **Clicking needs a visible marker**: `GetClosestPin` (2273) skips a pin whose `m_uiElement` is
  not `activeInHierarchy`, so a culled pin cannot be ticked or removed by accident.
- **The shared-map toggle** is vanilla: `OnToggleSharedMapData` (2642) flips
  `m_showSharedMapData`, Update (738-757) fades `m_sharedMapDataFade` and the map shader's
  `_SharedFade`. It hides every pin with an owner and the "explored by others" fog. It is a button
  on the large map that appears once any owned pin exists (`m_sharedMapHint`).
- **The legend filter** is vanilla too: double tap an icon (`IconPressed` 2622,
  `ToggleIconFilter` 2658) hides that whole type; `JoyDPadRight` on gamepad.
- **The table blob.** `MapTable.cs`: the ZDO holds `ZDOVars.s_data`, a compressed
  `ZPackage`: version int (`Version.SharedMap.PinsAuthor` = 3), the explored bitmap as one bool
  per cell (`m_textureSize²`, the prefab sets 2048 so ~4M bools), pin count, then per pin owner
  long, name, pos, type int, checked, author string. `GetSharedMapData(old)` (2707) merges the
  old bitmap with `m_explored | m_exploredOthers` and writes every `m_save` pin **of the local
  map** except deaths — the old blob's pins are not carried over, which is why every write reads
  first — owner = `m_ownerID` or, when 0, the local player id; author = `m_author` when valid.
  `AddSharedMapData(data)` (2781) explores the others-bitmap, then **marks every pin with a
  foreign owner for deletion**, walks the blob, keeps a marked pin when any saved pin lies within
  1m of the blob pin (`HavePinInRange`), adds a blob pin whose owner is not the local player when
  nothing is within 1m, and finally removes what is still marked. So: foreign pins are replaced
  by what the table holds, own pins are never touched, and duplicates are impossible as long as
  the position is the same. A blob pin never carries owner 0 (the writer substitutes its id).
- **Read and write** (`MapTable.OnRead` line 53, `OnWrite` 84): read decompresses `s_data` and
  calls `AddSharedMapData`; write calls read first (no message), checks
  `PrivateArea.CheckAccess(pos)` (the flashing variant), builds `GetMapData(current)` and sends it
  with `m_nview.InvokeRPC("MapData", pkg)`, which the owner answers by setting `s_data`
  (`RPC_MapData` 111) to `pkg.GetArray()` — byte for byte what the writer sent. Any client may
  write; the hover texts use `PrivateArea.CheckAccess(pos, 0f, flash: false)` for the
  non-flashing check. `MapTable` has no instance registry and no `OnDestroy`.
- **Cost of a write:** `ReadExploredArray` builds a `List<bool>` of 4M entries from the old blob,
  `GetSharedMapData` writes 4M bools, then `Utils.Compress`. Main thread, roughly a tenth of a
  second; it is the hitch the game already has on a manual write.
- **Locations on a client.** `ZoneSystem.m_locations` (ZoneSystem.cs 511) is the list of
  `ZoneLocation` (148): `m_prefabName`, `m_biome`, `m_group`, `m_unique`, `m_iconAlways`,
  `m_iconPlaced`, and `m_prefab`, a `SoftReference<GameObject>` from `SoftReferenceableAssets.dll`
  — which `setup` does not stage, so the dev dump reaches `Load` / `Asset` / `Release` by
  reflection. `ZoneSystem.GetLocation(int hash)` finds one by prefab name hash. The list is
  present on every client. `m_locationInstances` (where each one is in this world) is **server
  only**; clients only get the icon-flagged ones through the `LocationIcons` RPC, which
  `UpdateLocationPins` (Minimap 1454) draws as unsaved `PinType.None` pins with a location
  sprite. A placed location in a loaded zone is a `LocationProxy` (LocationProxy.cs) whose ZDO
  holds `s_location` (the prefab name hash) and which parents the spawned prefab under itself
  (`m_instance`, private); `GetComponentInParent<LocationProxy>()` from the `Location` leads back
  to the name. The spawned prefab carries a `Location` component (Location.cs): `m_hasInterior`,
  `m_exteriorRadius`, `m_interiorTransform`, `m_discoverLabel` (the name `Player` records as a
  known location, line 2037; a fallback pin name), `m_biome`, and the static registry
  `s_allLocations` (line 50), filled in `Awake`, emptied in `OnDestroy`. A dungeon entrance is a
  `Teleport` child of that prefab (Teleport.cs): `m_enterText` is the place name the game shows
  on entry (`$location_forestcrypt` "Burial Chambers", `$location_mountaincave` "Frost Caves",
  `$location_morkhalla` "Mörkhalla", ... — the game's own tokens), `Interact` moves the player.
  The interior sits ~5000m above the entrance, so `Character.InInterior(pos)` (Character.cs 4371,
  `y > 3000`) tells the two apart.
- **The location table can be read without running the game.** `SoftRef/manifest` (not
  `_extended`) lists every location prefab as `Assets/world/Locations/<Biome>/<Name>.prefab` with
  its asset ID and bundle. The `ZoneLocation` lists are MonoBehaviours in `_GameMain`'s bundle:
  `_ZoneSystem` (130 entries; a second, stale `_ZoneSystem` holds 124) plus `_LocationList_Mistlands`,
  `_Ashlands`, `_DeepNorth`, `_MountainCaves`, `_Hildir` and `_cp1` (the tar pits, easy to miss:
  under 1 KB). **The serialized `m_prefabName` is stale** (the game sets it from `m_prefab.Name`
  at load, ZoneSystem.cs 935): match `m_prefab.m_assetID` instead, whose `v3 v2 v1 v0` as 8-digit
  hex each is the manifest's asset ID. As of 2026-09-23: 170 enabled locations. `omp_locations`
  is still the in-game check.
- **A location's networked pieces are spawned apart from it, but its copy keeps them.**
  `ZoneSystem.SpawnLocation` (2426) in `Full`/`Ghost` mode instantiates every enabled `ZNetView`
  child as an object of its own (pickables, the tar, a `DungeonGenerator`, which then generates),
  then makes the `LocationProxy`; the proxy spawns the prefab in `Client` mode, which deactivates
  the `ZNetView` children, instantiates, and reactivates them on the prefab only. So the `Location`
  under the proxy has them as **inactive children** on every machine, and
  `GetComponentsInChildren<T>(true)` sees what the location is made of. `Location.m_generator`
  points at the copy's generator; `DungeonGenerator.m_themes` is a `Room.Theme` flag set
  (`GoblinCamp` 0x10, `MeadowsVillage` 0x20, `MeadowsFarm` 0x40, `AshlandRuins` 0x1000,
  `FortressRuins` 0x2000, `NorthVillage` 0x10000, ...). `LiquidVolume.m_liquidType` is
  `LiquidType.Tar` in a tar pit. `Vegvisir.m_name` is `$piece_vegvisir`.
- **Items that cannot go through a portal** (`m_shared.m_teleportable == false`): the ores and
  metals, `DvergrNeedle`, `MechanicalSpring`, `CharredCogwheel`, Hildir's three chests and the
  `DragonEgg` (type `Misc`) — and of those, only the dragon egg is a `Pickable` in any location.
- **Ore.** The game has three kinds of rock, and every deposit is one of them:
  - `MineRock5` (MineRock5.cs): `m_name`, `m_dropItems` (a `DropTable`, `m_drops` of
    `DropData.m_item`). The copper deposit, silver vein, muddy scrap pile, the Mistlands giant
    armour (`giant_helmet*`, `giant_sword*`: iron and copper scrap) and the Deep North's
    petrified gammeltroll (`GoldOre`) break apart as one — the `_frac` prefab.
  - `MineRock` (MineRock.cs): the same `m_dropItems`. The Ashlands' lava leviathan
    (`LeviathanLava`, flametal) and the ocean's `Leviathan` (chitin) are this.
  - `Destructible` (Destructible.cs) with a `DropOnDestroyed` (`m_dropWhenDestroyed`): tin and
    obsidian. The copper deposit and silver vein are a `Destructible` too, whose
    `m_spawnWhenDestroyed` is the `_frac` `MineRock5`; the first hit swaps it.
  All three have `Damage(HitData)`, the entry every hit goes through on the client that swung,
  before the RPC to the owner; `HitData.GetAttacker()` names who. A deposit `Destructible` is
  **immune to everything but the pickaxe** (`m_damages`: `m_blunt`/`m_chop`/... `Immune`,
  `m_pickaxe` `Normal`); a Dvergr barrel or crate that drops copper scrap or flametal takes blunt
  normally. Which drop is ore is not a name list: it is whatever some `Smelter.m_conversion` on
  `ZNetScene.m_prefabs` accepts as `m_from` (the derivation ServersideQoL uses) — **restricted
  to smelters with an `m_fuelItem`**, since the kiln, the windmill and the spinning wheel are
  `Smelter`s too and would make wood, barley and flax "ore". `m_to` is what it becomes. Soft
  tissue (`Softtissue`, the Mistlands giant remains) is the eitr refinery's *fuel*, not its
  input, so the rule misses it; `ExtraOre` adds it back. Tin (`TinOre`) passes the rule but
  lines every Black Forest shore; `SkipOre` drops it.
- **Death pin.** `Player.OnDeath` (Player.cs 3317, owner only) sets the profile's death point
  (read by nothing: `Minimap.UpdateProfilePins` calls `HaveDeathPoint()` and drops the result),
  calls `CreateTombStone` (3283), then adds a saved `PinType.Death` pin `$hud_mapday N` at the
  player's feet (3448). The game never removes it. `CreateTombStone` makes a grave only when the
  inventory is not empty and `DeathKeepInventory` is off, and then calls
  `TombStone.Setup(name, playerID)` on it — the one point that says a grave was made (the
  inventory count does not: `KeepGearOnDeath` and the world's death keys decide too). An empty
  grave destroys itself in `UpdateDespawn` on whichever machine owns its ZDO (`GiveBoost` runs
  there, so a patch on it misses a grave a friend emptied); `PositionCheck` puts a grave that
  drifted more than 4m (XZ) back to its spawn point. `TombStone.GetOwner()` is the dead player's
  id. No registry of graves exists.
- **Portals.** `TeleportWorld` (TeleportWorld.cs): the tag is ZDO `s_tag`, `GetText()` returns
  it (UGC-filtered), `SetText` sends `RPC_SetTag`, which the owner applies (187-212) — so a
  `SetText` postfix would see the old tag on every client but the owner. A placement ghost has no
  ZDO and disables itself in `Awake`. No instance registry.
- **Biome at a point**: `Heightmap.FindBiome(Vector3)` (Heightmap.cs 1147) needs a **loaded**
  heightmap and returns `None` otherwise, which a pin far across the map never has.
  `WorldGenerator.instance.GetBiome(Vector3)` (WorldGenerator.cs 746) is the noise itself,
  microseconds, anywhere. `Heightmap.GetBiomeColor(Biome)` is the terrain splat mask
  (`(255,0,0,0)` for Swamp), not a colour anyone should look at.
- **Per character storage** is `Player.m_customData` (Player.cs 581), a string dictionary saved
  and loaded with the character. BetterMap keeps its pin record there, BetterCartographyTable its
  shared pins.
- **Routed RPCs** (`ZRoutedRpc.cs`): `InvokeRoutedRPC(target, name, params)` (line 120)
  serialises the parameters, handles the call **locally too** when the target is `m_id` or
  `Everybody` (0), and sends it on. A client sends everything to the server; the server
  (`RouteRPC`, 140) forwards a targeted call to that one peer and an `Everybody` call to every
  ready peer but the sender - whether or not the server has the mod, since it never looks at the
  method. A receiver (`HandleRoutedRPC`, 189) looks the method hash up in `m_functions` and
  **silently drops one it does not know**, so a client without the mod is unaffected. `Register`
  (210) takes an `Action<long, ...>` whose first argument is the sender's id; `ZPackage` is a
  legal parameter (the table's own `MapData` RPC uses one). `ZNet.Awake` (ZNet.cs 349) builds a
  fresh `ZRoutedRpc` per session, so a registration has to be repeated per session (`Game.Start`
  postfix, keyed on the instance, as GraveOfTruth and ThisIsValheim do). `m_id` is the local
  peer id; `Game.SpawnPlayer` sets the local player (`SetLocalPlayer`, Game.cs 498) before
  `OnSpawned`, so by the first `Player.Update` the connection is long ready.
- **`PlatformUserID`** is not in `assembly_valheim` or `assembly_utils` but in
  `valheim_Data/Managed/Splatform.dll` (struct `Splatform.PlatformUserID`), which `setup.sh` /
  `setup.ps1` now stage into `lib/` (`Splatform*.dll`). `new PlatformUserID(platform, userID)`
  builds one with `m_platform` a `Platform` (`Equals(string)` compares the name) and `m_userID`
  the rest; `TryParse` maps only `S`/`X`/`N`/`A`/`V` prefixes to real platforms and accepts any
  other `prefix_value`, `IsValid` is true for it, and `ToString()` gives `prefix_value` back. So
  `OdinsMissingPatch_dungeon` is a legal author that survives the profile and the table
  unchanged and carries the category. Nothing in `Minimap` displays the author.
