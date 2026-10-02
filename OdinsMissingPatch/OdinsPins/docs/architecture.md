# Architecture

What each file under `src/` and `assets/` of Odin's Pins holds. `CLAUDE.md` has the one-line map;
[`map-pins.md`](map-pins.md) has the design and a fuller table per file. The family's shared code
in `Common/src/` (the tweak framework, `UiAssets`, `Translations`) is described in
[`../../docs/architecture.md`](../../docs/architecture.md).

| Path | What |
| --- | --- |
| `src/OdinsPins.cs` | BepInEx entry point: the `Tweaks` list, then `TweakHost.Start`. |
| `src/Tweaks/<Name>.cs` | One tweak, with its `[HarmonyPatch]` classes nested inside it. |
| `src/UniversalPins.cs` | Map pins that belong to nobody (a fixed owner, an `OdinsMissingPatch_<category>` author): the identity, adding, the removed-pin record, and the patches that keep them through a table read and turn a claim into a tick. Used by AutoPins, SharedMapTable and PinLooks. |
| `src/PinBroadcast.cs` | The routed RPCs that hand an auto pin to every player online the moment it is made, and that give a joining player everyone's pins once. Sends for AutoPins, receives into it. |
| `src/PinSweep.cs` | The two-sweep "is it still there?" check for pins near the player, shared by DeathPins and AutoPins' mined-out check. |
| `src/Dev/MapCommands.cs` | Debug only: `omp_locations [filter]`, `omp_pins`, `omp_pins_forget`, `omp_pins_clear` — see [`map-pins.md`](map-pins.md). |
| `assets/icons/` | The coloured `map_*` pin icons PinLooks draws, shipped beside the DLL; loaded by `UiAssets.Icon`, which also looks beside the DLL since Gale flattens the folder. |
| `assets/translations.csv` | The place names and map toggle labels, one row per `$omp_` token, one column per language — see [`../../docs/translations.md`](../../docs/translations.md). |
