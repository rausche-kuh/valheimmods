# Roadmap

What comes next, roughly in the order it is worth doing: small fixes first, the features that
need a server or change balance last. Each entry says which tweak it belongs to (new or existing)
and which doc it has to be checked against before any code is written — see the table in
`CLAUDE.md`.

Game facts marked **verify** were written from memory of the game code, without `decompiled/` at
hand. Check each one against `decompiled/assembly_valheim/` first; what the check teaches goes
into the doc for the area, not here.

## Enshrouded style sleeping

_New tweak, SharedSleep — needs a server install_

Going to bed no longer waits for everybody:

- Getting into bed broadcasts `{in bed}/{online} waiting for sleep` to everyone.
- Every player in bed makes time run faster (for example `+1×` per sleeper, capped), so one
  sleeper speeds the night up a little and everyone asleep still skips it the vanilla way.
- Everybody in bed is the normal Valheim sleep: `Game.UpdateSleeping` / `EnvMan.SkipToMorning`
  untouched.

Research: the day is the server's clock (`ZNet.m_netTime` on the server, sent to clients, verify),
so speeding it up is server side — this is the first tweak that needs the mod on the server, and
it breaks the mod page's "nothing needs a server install" line unless it is kept optional and
says so. Things that run on time (plant growth, smelters, fermenters, respawn timers) speed up
with it, which is the point of a shorter night but should be stated.

**Credits:** the idea is copied from https://thunderstore.io/c/valheim/p/Vapok/BetterSleepBruh/

# Bugs

None known.
