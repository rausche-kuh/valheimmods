# Odin's Paths

Many have tried to achieve Odins quest and layed paths from their advanture. Are you the one
fulfilling Odins request?

> **In development, not released:** the roads are built and being tested; expect rough edges.
> The mod is developed heavily with the use of AI.

## What it does

- **Paths to the altars** — when the world loads and every time it sleeps, a trail is laid to
  the next boss altar in the order the bosses are fought, and to the traders, whose camps then
  show on your map (or none, if you would rather search for them). It starts from the
  nearest point of the path network: the sacrificial stones where every Viking wakes, a base you have marked, or a path that is already there, so
  the trails grow into a network rather than a bundle of lines.
- **Like a real path** — the trail follows the easiest ground and avoids climbing, so it winds
  through the land the way old footpaths do. Where the sea is in the way it ends at the shore and
  carries on from the far one, and a vegvisir with blue runes at the shore shows you where.
- **Clears the way** — grass gives way to packed dirt, and in the Deep North the path is dug out
  of the snow.
- **Old harbours** — where a road meets the sea it runs on out onto an old dock, with a few
  weathered huts and sheds beside it. The docks and buildings are JSON files: add your own, or
  replace the mod's by name, in `BepInEx/config/OdinsPaths/harbours/`.

## Without the mod

The paths are ordinary terrain, stored the way the game stores anything you dig or hoe: they stay
when the mod is removed, and players without the mod see them too.

## Multiplayer

Install it on the dedicated server, or on the machine that hosts the world: the paths are laid
there. Players who want to see the harbour stones - the vegvisirs with blue runes where a path
meets the sea, which show the harbour across - need it too, as do those who want to see the furniture of the docks and huts; without it the
paths are all the same, but the stones and the furniture are missing. Custom harbour files belong
on every player's machine as well, or their furniture stays invisible.

## Install

A mod manager (Gale or r2modman), or drop the zip's contents into
`BepInEx/plugins/rauschekuh-OdinsPaths/`. Requires the BepInEx pack for Valheim.
