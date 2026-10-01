# The words on screen

Everything the mod puts in front of a player goes through the game's own localization, so the mod
speaks whatever language Valheim is set to. `assets/translations.csv` holds the words,
`src/Translations.cs` hands the file to the game, and a call site holds nothing but a token.

## Conventions

- **A call site passes a `$omp_` token, never a sentence.** Tokens are namespaced with the mod's
  initials so nothing can collide with the game's own words or another mod's.
- **Every token lives in `assets/translations.csv`**, which the build copies next to the DLL. The
  first column is the key (without the `$`), the second is English, and each further column is a
  language named exactly as Valheim names it (`German`, `Portuguese_Brazilian`, ...). Adding a
  language is adding a column and nothing else — no code change, no new file. A row whose key
  starts with `//` is a comment; a value holding a comma goes in `"quotes"`, and a quote inside a
  value is doubled (`""`).
- **Adding a string means adding a row.** A literal that reaches the screen is a bug; the check is
  `grep -rn '"[A-Z][a-z]* ' src/` turning up nothing but config text.
- **Config descriptions stay English.** They are bound at `Awake`, long before a language is
  known, and BepInEx writes them into the `.cfg` file as comments — a translated description would
  rewrite the player's config file on every language change. The config manager is English too.
- **Where a token is translated depends on where it lands:**
  - `UITooltip.m_topic` / `m_text` and `Player.Message` translate what they are given when they
    show it, so those take a bare token and follow a language change by themselves.
  - A label written straight onto a component does not (`PanelButtons.TextButton.SetLabel`
    localizes on the way in and compares translated, so the caller may hand it the same token
    every frame).
  - A radial menu's `IRadialConfig.LocalizedName` is written onto a `TMP_Text` unchanged, despite
    the name — `PowerPicker` translates it itself.
- **A number or an item name goes in as `$1`, `$2`**, so word order stays the translator's to
  choose: `Localization.Localize("$omp_took", moved.ToString())`. The substitution happens *after*
  the lookup, so anything that is itself a token (`$item_wood`) has to be translated before it is
  passed in — see `StationRefill`'s hover line and `QuickStack`'s favourite message.
- **Plurals are separate tokens** (`$omp_clear_favourite` / `$omp_clear_favourites`,
  `$omp_stacked_one_chest` / `$omp_stacked_chests`). The game has no plural rules, and a language
  that needs a third form can give both rows the same words.
- **A translation borrows the game's own words.** A player reads the mod's buttons next to
  Valheim's, so a term the game already has is taken from it rather than invented: German says
  *Truhe*, *Inventar*, *Gegenstand*, *Stapel*, *Schnellleiste*, *Herstellung*, *Brennmaterial*,
  *angelegte Ausrüstung*, *verstauen* and *Verlassenen-Kraft* because `localization.csv` does, and
  `Take all` is *Alles nehmen* because `inventory_takeall` is — as is `Prendre tout`, `Взять все`,
  `全部拿取`, `Coger todo`, `Pegar tudo`, `Weź wszystko`, `Prendi tutto` and `全部取る`. The chest
  is whatever `piece_chestwood` calls it, the stack whatever `inventory_splitstack` does, and the
  on/off switch whatever `hud_on` / `hud_off` do — which is why Spanish reads *Acceso: sí* and not
  *activado*. How formally the player is addressed follows the game too: German and Polish say
  *du* / *ty*, Russian and French say *вы* / *vous*. See below for how to read the game's own file.
- **Eleven languages ship**: English, German, Russian, Chinese, Spanish, French,
  Portuguese_Brazilian, Polish, Italian, Japanese and Ukrainian. The other fourteen Valheim knows
  are a column away and fall back to English until someone adds one. Check a candidate's coverage
  in the game's own file first — Ukrainian is translated to 5598 of 5612 rows, so there is real
  vocabulary to borrow, while a column the game itself left half empty gives nothing to match.
- **A button's own label stays within about the English row's display width.** The layout is
  only ever checked in English, so a translation is the one thing that can break it unseen: a
  `PanelButtons.TextButton` sizes itself to its label, and the two of them sit in the vanilla
  Take all and Stack all slots growing towards each other. Only labels are bound this way — the
  rows are marked `BUTTON` in the CSV's section comments. A tooltip wraps and a centre message
  has the screen's width, so both may run as long as they need. Where the literal phrase will
  not fit, a shorter term that names the same thing is the right answer: *Nearby use: on* is
  German *Fernzugriff: Ein*, not *Zugriff in der Nähe: Ein*. **Width, not characters** — a CJK
  glyph is two columns wide, so Japanese gets about half the count. Measure it rather than
  eyeball it: `unicodedata.east_asian_width(c) in "WF"` is the test, and every shipped label is
  within +2 columns of its English row.
- **A translated string that is cached rather than rebuilt every frame must be rebuilt when the
  language changes.** `Translations.Revision` counts the loads; `ChestFavorites` compares it
  alongside the marks it described.

## Game facts

- `Localization` lives in **`assembly_guiutils.dll`**, not `assembly_valheim` — it is not in
  `decompiled/`, so read it with
  `ilspycmd -r lib -t Localization lib/assembly_guiutils.dll`.
- `Localization.SetupLanguage(string)` is the hook: the constructor calls it for English, startup
  calls it again for the player's language, and `SetLanguage` calls `Clear()` (wiping every
  translation and the lookup cache) and then it. A postfix on it therefore has to re-add the mod's
  words every time, which is exactly what `Translations.Load` does.
- `Localization.LoadCSV(TextAsset, language)` is public, and `AddWord` is private — the CSV is the
  way in. **A language the file has no column for loads nothing at all**, tokens included, so
  `Translations` checks the header and asks for English instead when a column is missing. Within a
  column that does exist, an empty cell falls back to column index 1, which is why English is
  second.
- `Localize` resolves `$word` up to the first of `` (){}[]+-!?/\&%,.:-=<>\n `` or the end of the
  string, so `_` is safe in a token and a value may hold anything. An unknown token shows as
  `[omp_...]` — that, in game, means the CSV did not load.
- `TextAsset` cannot be named from a `net472` build: its module is built against netstandard 2.1
  and the reference fails with CS1705, the same wall `PanelButtons.LoadPng` hits. `Translations`
  builds one and calls `LoadCSV` by reflection instead.
- Valheim ships 24 languages; the names are in `Localization.LocalizationConstants`, and
  `BCP47ToLanguage` in the same class maps a locale to one.
- **The game's own words are `TextAsset`s in `valheim_Data/resources.assets`**, not in a bundle —
  `localization` is the big one (~5 MB, ~5700 rows), beside `localization_deepnorth`,
  `localization_ashlands` and a handful of platform files. `SetupLanguage` loads every one of them
  in turn. To read them, `uv venv` + `uv pip install UnityPy` in a scratch directory, then
  `UnityPy.load(".../valheim_Data/resources.assets")` and write out every object whose
  `type.name == "TextAsset"`. Decode with `errors="replace"`: a few rows hold bytes that are not
  valid UTF-8. The columns are the same shape as this mod's file — key, `English`, then one per
  language (`German` is index 5) — so grepping the English column for a term gives the game's
  wording for it in every language at once.
- `LoadCSV` splits the **header** with a plain `Split(',')` while the body goes through
  `DoQuoteLineSplit`, so a language name may never be quoted or hold a comma. A cell that is empty
  *or starts with `\r`* falls back to column 1, which is how a CRLF file's last column survives.
