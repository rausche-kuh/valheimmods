# Conventions

How a tweak is written, beyond the four rules in the mod's `CLAUDE.md` that always apply. The
subsystem docs add their own rules on top: see `chests.md`, `inventory-ui.md`, `item-order.md`.

## Patching

- `Patcher` applies only the patch classes of tweaks that are on at launch, each in its own
  try/catch. A class that fails (a game update renamed its target) switches off every tweak it
  serves for the session, takes out what was patched for them alone, and logs one line per tweak.
  A tweak switched off mid game keeps its patches, inert behind `On`, until the next launch.
- A class nested in a tweak belongs to it. A shared one (`NearbyChests`, `UniversalPins`, ...)
  carries `[Serves(typeof(A), typeof(B))]` naming every tweak that needs it: it is applied when
  any of them is on and its failure switches all of them off. `Optional = true` for a nicety whose
  failure should only be logged (a hover line, a tint). `[Always]` is for the mod's words and dev
  commands. A class with neither is applied always and warned about in the log - add the
  `Serves` when a tweak starts to use a shared patch.
- `[LoadHook]` marks a class whose target runs once per object as it loads (`Container.Awake`,
  `MapTable.Start`, `Game.Start`): patched mid game it would miss everything already loaded. A
  tweak switched on mid game waits for the next launch while one of its load hooks is not in;
  otherwise it is patched on the spot. A load patch whose tweak has a rescale that catches up on
  what is loaded (`Ranges`) needs no `LoadHook`.
- Every `OnSettingChanged` handler also runs when the tweak's patches go in or come out, since `On`
  changes then without a setting changing. So it has to bring what is loaded up to date from any
  state - the `vanilla * scale` rescale below does that by construction.
- Name a patch target with `nameof`, never a string: the publicizer makes private members
  reachable, and a rename then breaks the build after `setup` instead of a tweak at runtime.

## Writing a tweak

- A multiplier is `1` when its tweak is off, so callers multiply either way instead of branching.
  `BindMultiplier` clamps to 0.1–20: the value goes straight into a game field.
- A tweak that only reads its setting where it is used (`Ranges`' comfort radius) needs nothing
  else. One that *writes* game state on load (`Ranges` scales `m_rangeBuild` in
  `CraftingStation.Start`) keeps each object's vanilla values in a `ConditionalWeakTable` the first
  time it sees it and sets `vanilla * scale`. That is idempotent, so the load patch and an
  `OnSettingChanged` rescale over the game's registry can both apply it at any time without
  bookkeeping (`Ranges`, `PocketUpgrades`).
- `CraftingStation.m_allStations` / `StationExtension.m_allExtensions` (private statics, reachable
  thanks to the publicizer) are the registries of what is actually in the world; use them for a
  rescale rather than `Resources.FindObjectsOfTypeAll`, which also returns the prefabs — scaling a
  prefab would compound with the `Start` patch on every station spawned afterwards.
- Scale exactly what a rescale can reach again. A patch that writes to an object the game never
  registered (a placement ghost) leaves it stranded at whatever the multiplier was when it spawned,
  so `Ranges`' `StationExtension` postfix repeats the game's own `GetZDO() != null` condition and skips it.
- A cost that is computed and spent inside one method (`CombatStamina`'s sprint, jump, swim and
  sneak) is waived by letting the method run untouched and dropping the spend at
  `Player.UseStamina`, under a static flag the method's prefix sets and its postfix clears. That
  keeps the skill XP, the empty-bar flash and the drown timer as vanilla, does not care what the
  drain formula is, and needs no game field to be zeroed and restored. It only works because each
  of those methods makes exactly one `UseStamina` call and none nest; check that before adding a
  fifth. A cost that is fetched from a getter and spent elsewhere (`GetBuildStamina`,
  `Attack.GetAttackStamina`) is zeroed at the getter instead, which also clears the
  `HaveStamina` gate that reads the same value.
- Anything decided per character (`Attack` runs for every character in the world) is waived only
  for `Player.m_localPlayer`; a check that is only ever clear when nothing hostile is near is
  exactly what a wandering boar would otherwise collect on.
- A list setting is one comma separated `ConfigEntry<string>` bound with `Tweak.BindList`
  (`KeepGearOnDeath.KeepTypes`), parsed into a set once at bind and again on every change, never
  per use.
  Unknown names are logged and skipped rather than failing the whole list; the description lists
  every valid name so a player never has to look them up.
- A tweak that repeats a game action on many objects (`AreaRepair`) pays the game's own price for
  each one and asks the game's own questions about each one, rather than making the first action
  cheaper or wider. The piece the game already handled is simply asked again and refuses by itself,
  which keeps the patch a postfix with no special case for it and no way to pay twice.
- A game method that is a filter over a private list (`Inventory.MoveInventoryToGrave`,
  `RemoveUnequipped`) is replaced by a prefix that returns false and runs the same loop with the
  tweak's predicate folded in, rather than pulling items out of the list around the original: a
  throw inside the original would leave the pulled items nowhere, and this is the death path.
- A tweak that needs a Debug-only hook (`PlayerMarks` marking wards so it can be tried alone) is
  declared `partial` and calls a `static partial void` method; the body lives in `src/Dev/` in a
  second `partial` part of the same class. A Release build has no body, so the compiler drops the
  call along with it — no flag, no `#if`, nothing of the dev code ships.
- A colour the mod draws itself comes from `Palette` (`src/Palette.cs`), one colour per meaning,
  and those follow the game's UI palette so they fit in. Settings more than one tweak reads live in
  `SharedSettings` (the `General` section), not in one of the tweaks. The game has no palette in
  code (the text markup uses the named `orange` and `yellow`); these were read from the
  `Image` and text colours in `_GameMain`'s bundle on 2026-09-24. Accents: ornament and
  separator orange #FF8E00, selection amber #FFA300, bar and selection gold #FFD800, the map's
  player marker #FFE200, soft amber #FFB75B (food icons, ship marker, highlighter), equipped blue
  #60A8E5, the Bonus damage text #FFA03D. Darks: scroll panel brown #302114, braid line brown
  #261E11, plus plain black backgrounds. Light: plain white; there is no cream text colour.
