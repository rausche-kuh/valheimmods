# Odin's Beacon

Never lose your friends again. A glowing mark floats over every other player: seen through walls,
trees and mountains, shrinking with distance, and held at the edge of the screen as an arrow when
they are behind you or off screen. **Client side, no server install.**

![Odin's Beacon: a friend's mark seen through the forest](https://raw.githubusercontent.com/rausche-kuh/valheimmods/main/images/friend_marker.webp)

Part of [Odin's Missing Patch](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsMissingPatch/),
the pack. It works just as well on its own.

> ⚠️ **Alpha.** Built heavily with AI. Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## What it does

- A small glowing mark over the head of every other player, drawn on your HUD, with little
  sparks drifting off it.
- Up close it only shows for a player hidden behind terrain or a building; further off, every
  player gets one. Right next to you, nobody does.
- It shrinks and changes colour with distance, so a glance tells near from far, all the way
  across the map.
- A player off screen or behind you gets a mark pinned to the edge of the screen, pointing their
  way.

Good for co-op and multiplayer servers: finding each other after a portal trip, regrouping in a
dark swamp or the mist, keeping track of the party in a fight, or meeting up across the map.

## Configuration

The config file is `BepInEx/config/rauschekuh.odinsbeacon.cfg`, section `Player Marks`. You can
also change settings in game with a config manager, and they apply right away: the distances at
which marks show and hide, the size, the colours, the edge arrows and the sparks. Coming from
Odin's Missing Patch? On the first start, every setting it had under the same name is carried
over.

## Multiplayer

Only you need the mod to see the marks, and nothing goes on the server. Players near you are
marked where they stand; players further away are marked where the map shows them, so anyone
who hid their position from the map stays hidden here too.

## Install

Use a mod manager (Gale or r2modman), or unzip into `BepInEx/plugins/rauschekuh-OdinsBeacon/`.
Requires the BepInEx pack for Valheim.

## Also known as

If you came looking for any of these, this is the mod: player markers, player marks, friend
markers, friend finder, teammate markers, teammate locator, party markers, co-op markers, ally
indicator, player indicator, player locator, player tracker, player icons, player waypoints,
player nameplate marker, see players through walls, show players on screen, where are my
friends, off-screen player arrow, HUD player markers, multiplayer HUD.
