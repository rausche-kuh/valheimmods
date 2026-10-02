# Player marks

`PlayerMarks` (`src/Tweaks/PlayerMarks.cs`, section "Player Marks") puts a small round mark over
the head of every other player. The settings and their defaults are in the source; this is how
it is drawn and why.

## Where it is drawn

- An `EnemyHud.LateUpdate` postfix draws the marks as `Image`s under `m_hudRoot`. That canvas is a
  screen-space overlay, so a `transform.position` there is a screen pixel, and the marks hide
  with the HUD for free. They are also hidden while the large map or the inventory is open.
- Loaded players come from `Player.GetAllPlayers()`. Past the loaded area they come from
  `ZNet.GetOtherPublicPlayers` - the server's player list, which carries a position only for a
  player who shares it on the map, so a player hidden from the map gets no mark out there.

## When it shows

- No mark within `HideWithin`. Up to `ShowFrom` only when the player is hidden: a raycast from
  the camera hits terrain or a piece first (BaseAI's solid mask, a hit right at the target
  ignored). Always beyond that.
- It shrinks towards `FarFrom`; `HideBeyond` (off by default) drops it altogether.
- Off screen or behind the camera it slides to the screen's edge along the line from the centre
  (`ShowOffScreen`).
- The mark is always opaque: it is subtle only by its size.

## How it looks

- Each mark is three runtime-generated, mipmapped sprites, stacked as a parent and two stretched
  children: the near disc (`CentreColor` out to `EdgeColor`), the far disc (`EdgeColor` out to
  `FarEdgeColor`) faded in over it by distance - so the blend stays at full brightness - and the
  rings: a thin light one inside a dark brown outline, so one of the two contrasts with any
  background.
- The colours were tuned in game from the UI palette (see [`conventions.md`](../../docs/conventions.md)): a muted gold centre
  out to the ornament orange, far out to a pale blue; a gold ring, since the UI's plain white
  stood out of the scene; and the scroll panels' brown for the outline.
- All radii are fixed parts of the sprite, so `MarkSize` scales the whole mark.
- The discs and rings are drawn with every radius divided by an outline function of the angle -
  a few sine waves of different bump counts laid over each other - so the mark is a rugged, hewn
  shape rather than a perfect circle, and the rings follow it.
- Sparks (`Sparks`) drift out from under the outline: a few soft-dot children per mark, drawn
  beneath the far disc and the rings, in the edge colour of the moment, each fading out over its
  flight and restarting at a new angle. The angle is a hash of the mark, the spark and the round,
  so they keep no state.

## Tuning and the sprites

- The ring widths, the gradient's curve, the far size, the ring colours, the outline's
  ruggedness and bumps and the sparks' count, rate, travel and size are static fields rather than
  config. In a Debug build `omp_mark <name> <value>` tunes them and the config values live
  (`omp_mark` alone prints them all as one pasteable line, `omp_mark reset` restores the
  defaults), so a look is found in game and then written back as the defaults.
- The gradients are baked into the textures, since an `Image` tint only multiplies and cannot
  make the centre brighter than the edge; they are redrawn when a colour setting changes.
- The sprite maths uses its own smooth step: `Mathf.SmoothStep(from, to, t)` interpolates between
  `from` and `to`, and used as a step it gave a translucent square.
- The colour keys were `NearColor` / `FarColor`, then `MarkColor` / `FarMarkColor`, renamed each
  time so existing configs pick up the new look; `CentreColor` / `EdgeColor` / `FarEdgeColor`
  kept their names when they moved to the palette.
- In a Debug build `src/Dev/MarkWards.cs` fills the partial `AddDevTargets` with every loaded
  ward, so the marks can be tried alone (`omp_marks_wards` switches it); in Release the partial
  has no body and the call is gone.
