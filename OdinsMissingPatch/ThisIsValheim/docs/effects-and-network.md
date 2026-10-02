# The bang, the network and the fast swing

What a kick that opens a door sets off, and how every client sees it. Whether the kick may open
the door is in [kick.md](kick.md).

## The bang

- The bang is a list of the game's own effect prefabs, named in the config (`[Effects] Prefabs`)
  and resolved in a `ZNetScene.Awake` postfix (behind the loading screen) and again on
  `SettingChanged`: `sfx_battering_ram_impact` (the Ashlands siege ram's piston landing on a
  gate), `sfx_wood_break`, `vfx_SawDust` and `fx_hit_camshake`. `fx_GP_Activation`, the Forsaken
  power activation, is still a valid name for anyone who wants it — its audio is
  `DarkMagic_HauntedMask2` and `DarkMagic_DeathWhisper`, which is why it sounds like a séance
  rather than a boot.
- Resolution (`ResolveEffects`) tries `ZNetScene.GetPrefab` first and falls back to a single
  `Resources.FindObjectsOfTypeAll<GameObject>()` sweep for *all* remaining names at once, because
  only effects carrying a `ZNetView` are in `ZNetScene` — `sfx_battering_ram_impact` and
  `fx_hit_camshake` are not. The sweep prefers root objects, because a child inside some other
  prefab can share an effect's name, and only falls back to a child (of a prefab asset,
  `!scene.IsValid()`, never of a world instance) when no root exists. That fallback is what finds
  `sfx_battering_ram_impact`: its standalone prefab sits in bundle `c4210710` but nothing
  hard-references it, so it is never loaded; the ram's `m_punchEffect` is `fx_batteringram_fire`
  (smoke, spikes, shockwave, flame spikes) with the sound as a child, and instantiating that
  child gives the sound alone.
- Effects without a `TimedDestruction` get `Destroy(go, EffectLifetime)` put on them (`Sweep`).
  Most of the game's effects clean themselves up; a bare `ZSFX` that normally lives as a child of
  some machine does not, and spawned loose it would sit at the door forever.
- Nothing is shipped in `assets/` and nothing is spawned with a ZDO: the effects go up under
  `ZNetView.m_forceDisableInit` (the game's own idiom — the `ZNetView`, if there is one, destroys
  itself in `Awake`), because every client spawns its own copy off the one RPC.

## The network

Everything is networked with one routed RPC (`ThisIsValheim_Kick`, the door's `ZDOID`) sent to
`ZRoutedRpc.Everybody`, which includes the sender — `InvokeRoutedRPC` handles it locally and the
server does not echo it back, so every client runs the handler exactly once and the bang lands on
the kicker's own screen in the same frame. `ZNet` builds a fresh `ZRoutedRpc` per session, so
registration hangs off a `Game.Start` postfix (`RegisterRpc`) and is guarded against
re-registering on the same instance (`m_functions.Add` throws on a duplicate). The handler bails
out on a dedicated server. A receiver whose `ZNetScene.FindInstance` comes up empty simply has the
door out of its loaded zones and does nothing. `RPC_Kick` starts the swing before the bang, so an
effect that throws cannot leave the door at normal speed.

## The fast swing

The fast swing is `Door.m_animator.speed`, wound up and put back. It cannot be tied to the
animation's length: the state change arrives through `UseDoor` whenever the door's owner gets to
it, so the animator is sped up *before* the animation starts and held through the round trip.
`SwingRoutine` polls the animator's `state` parameter each frame and restores the speed once it
has gone non-zero and the `open`-tagged state has finished (`normalizedTime >= 1`, not in
transition), or as soon as it drops back to 0 — so a door shut right after the kick closes at
normal speed. `SwingWindow` (1.5 s) is only the cap. One coroutine per door in `swinging`, so a
second kick on the same door does not restore the speed out from under the first. The tweak is
no MonoBehaviour, so the coroutines run on the plugin, `TweakHost.Plugin`.
