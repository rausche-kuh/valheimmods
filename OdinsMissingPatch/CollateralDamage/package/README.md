# Collateral Damage

No troll cares who stands in the way of its club.

Trolls and bosses smash whatever creature stands in the way of their attacks: a troll's swing and
ground slam hit the greydwarfs at its feet, Eikthyr's lightning hits the dwarfs in his arena,
Yagluth's meteors hit fulings and lox, and so on for every boss but the Queen.

![Collateral Damage: a troll's swing hitting the creatures in its way](https://raw.githubusercontent.com/rausche-kuh/valheimmods/main/images/collateral_damage.webp)

> ⚠️ **Alpha.** Built heavily with AI. Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## What changes ⚖️

It changes troll and boss fights. Creatures that wander into an arena no longer need
spawn-proofing to keep out, since the boss hits them too, and a troll with a crowd of greydwarfs
at its feet is no longer a death sentence.

- Nothing hit this way fights back or picks a new target.
- Bosses never hit what they spawned.
- A creature killed this way drops nothing unless you did at least half of the work.
- Players and tamed creatures are never hit.

## Configuration

The config file is `BepInEx/config/rauschekuh.collateraldamage.cfg`. You can also change settings
in game with a config manager, and they apply right away.

- `Collateral Damage` → `Enabled`, which creatures and bosses hit their peers, how hard, and how
  much of the work you have to do for the loot.

## Multiplayer

Nothing goes on the server, but it only fully works when everyone has the mod, because each
creature is run by the game of whoever is nearest.

## Install

Use a mod manager (Gale or r2modman), or unzip into
`BepInEx/plugins/rauschekuh-CollateralDamage/`. Requires the BepInEx pack for Valheim.

Want more of this? [This Is Valheim!](https://thunderstore.io/c/valheim/p/rauschekuh/ThisIsValheim/)
lets you kick doors open like a battering ram hit them.
