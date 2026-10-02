# The radial menu and guardian powers

PowerPicker. The guardian power pose and its `GPower` animation event are in
[item-magnet](item-magnet.md).

- The radial menu (`Valheim.UI`, `Hud.m_radialMenu`, opened on `OpenRadial` = G) is built from
  configs rather than from a prefab, which is what makes it extensible without an asset. An
  `IRadialConfig` is `LocalizedName` + `Sprite` + `InitRadialConfig(RadialBase)`, and the last
  builds a `List<RadialMenuElement>` and ends in `radial.ConstructRadial(list)`. The game's own
  configs are ScriptableObjects only because they are authored in the editor - the interface asks
  for nothing of the sort, so a plain class is a page of the menu. `RadialData.SO` holds the
  element prefabs (`GroupElement` opens a config, `EmoteElement` is a leaf with an icon,
  `EmptyElement`, `BackElement`, `ItemElement`) and `MainGroupConfig`, the `ValheimRadialConfig`
  that is the top level ring. So a category of one's own is: a prefix on `ConstructRadial` that
  adds an instantiated `GroupElement` to `elements` while `radial.CurrentConfig is
  ValheimRadialConfig` (the list is still the caller's there; by the postfix it has been laid
  out), `GroupElement.Init(config, radial.CurrentConfig, radial)` to point it at the page and
  back at the ring, and the page's own `InitRadialConfig` for the leaves. Everything is rebuilt
  on every open, so nothing has to be cached or invalidated.
- A `RadialMenuElement` is three things: `Name` and `SubTitle` (what the middle of the ring reads
  while it is hovered - `protected set`, reachable thanks to the publicizer), `Icon` (the `Image`
  to give a sprite and a colour) and `Interact` / `CloseOnInteract` (what it does, and whether
  doing it shuts the menu). `Init` on each prefab only fills those in for its own purpose, so an
  element of another kind sets them itself instead of calling it. `ConstructRadial` destroys the
  previous elements by walking `m_elementContainer`'s children, and new ones are still unparented
  at that point, so building them in a prefix is safe. The radial keeps the last element
  interacted with as `LastUsed` and `ValheimRadialConfig` puts it back in the top level ring - it
  survives the menu it was built in (`GroupElement`s are exempt), so a leaf that draws its own
  state has to keep it right after its `Interact`.
- `RadialBase.SetElementsPerLayer` rounds the count up to the next value of
  `RadialData.SO.MaxElementsRange` (`{ 8, 12 }`), so the vanilla eight top level elements fill a
  ring of 8 exactly and a ninth turns it into nine of twelve: an arc, with `CenterBackButton`
  moving the first element to the middle to centre it. That is the game's own look for any page
  with fewer elements than its ring, so it costs nothing but a wider top level.
- A guardian power is a `StatusEffect` in `ObjectDB.m_StatusEffects` named `GP_<Boss>`, told
  apart by `m_cooldown`, the one field under its `__Guardian power__` header. The player holds
  one: `m_guardianPower` (the name), `m_guardianPowerHash` and `m_guardianSE`, all set by
  `SetGuardianPower(string)` and saved with the character. `ItemStand.DelayedPowerActivation` is
  the only vanilla caller, i.e. pressing Use on a sacrificial stone, and `SetGuardianPower` also
  calls `AddUniqueKey(name)` - so the character's unique keys are the record of every power ever
  taken. `m_guardianPowerCooldown` is a timer on the *player*,
  counted down by `UpdateGuardianPower` and only ever set by `ActivateGuardianPower`, so
  switching powers neither resets nor dodges it. Nothing about any of this crosses the network.
- Which powers a character may take is boss progress, and nothing in `assembly_valheim` pairs a
  power with its boss: the stone at the temple names the power, the boss names the key, and only
  the trophy in between ties them together. The pairing was read out of the game's own data - the
  `BossStone_*` prefabs under `Assets/world/Props/StartTemple/` resolved through
  `ItemStand.m_guardianPower`, and every `Character.m_defeatSetGlobalKey` resolved to its owning
  GameObject, both in bundle `c4210710` (UnityPy, as in the root `CLAUDE.md`). It is
  `GP_Eikthyr`/`defeated_eikthyr`, `GP_TheElder`/`defeated_gdking`,
  `GP_Bonemass`/`defeated_bonemass`, `GP_Moder`/`defeated_dragon`,
  `GP_Yagluth`/`defeated_goblinking`, `GP_Queen`/`defeated_queen`, `GP_Fader`/`defeated_fader`.
  There are exactly seven: `FrozenKing` sets `defeated_frozenking` but has neither stone nor
  power, so `GP_Ashlands` and `GP_DeepNorth` in `Player.StartGuardianPower`'s stat switch are
  names that never come up. `Character.OnDeath` writes the key twice - into
  `Player.m_addUniqueKeyQueue` on every client near the kill, and into
  `ZoneSystem.SetGlobalKey` - so both the world and the character remember it, and reading both
  is what lets a character carry a kill from one world into another.
