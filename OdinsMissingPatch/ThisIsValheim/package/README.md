# This Is Valheim!

No one tells me how to open a door.

Valheim, a little more brutal: you kick doors in like a battering ram hit them.

> ⚠️ **Alpha.** Built heavily with AI. Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## Door kick

Your secondary attack, aimed at a shut door, kicks it open with a bang and splinters, whatever you
hold. Double doors go together. A warded or locked door throws you back and says why.

## Configuration

The config file is `BepInEx/config/rauschekuh.thisisvalheim.cfg`. You can also change settings in
game with a config manager, and they apply right away.

- `Kick` → `Enabled`, how fast the door swings, whether a locked door gives way without its key,
  and the hover hint. `Effects` → the game's own effects that go off at the door.

> 💡 Switched **off at game start, the kick doesn't touch the game at all**. If it clashes with
> another mod, switch it off and restart.

## Multiplayer

Install it on every client that should get the show. Nothing goes on the server, and players
without it just see a normal door open.

## Install

Use a mod manager (Gale or r2modman), or unzip into `BepInEx/plugins/rauschekuh-ThisIsValheim/`.
Requires the BepInEx pack for Valheim.

Want trolls and bosses to smash the creatures in their way?
[Collateral Damage](https://thunderstore.io/c/valheim/p/rauschekuh/CollateralDamage/) is its
companion.
