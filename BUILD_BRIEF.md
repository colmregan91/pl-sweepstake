# Build Brief — PL Sweepstake Leaderboard

Hand this to Claude Code alongside `CLAUDE.md`. Read `CLAUDE.md` first — it contains the
architectural constraints and the ESPN endpoint contract. This file is the build plan.

**Before starting:** save the source spreadsheet screenshot to `docs/design-reference.jpg`
and look at it. It defines the visual target.

---

## Goal

A static Blazor WebAssembly site, hosted on GitHub Pages, showing two side-by-side
leaderboards for a 17-person Premier League sweepstake:

- **Left panel — Goals.** Each entrant's 3 goalscorer picks and their season goal totals.
- **Right panel — Assists.** Each entrant's 3 assister picks and their season assist totals.

Entrants are ranked independently on each panel, highest total first. Stats come from the
ESPN API, fetched at build time by a console tool and committed as static JSON.

---

## Phase 0 — Confirm the environment

```bash
dotnet --version   # expect 10.x
```

If the SDK is not .NET 10, stop and tell the user rather than silently targeting net8.0.

Create the repo structure from the layout in `CLAUDE.md`. Copy the provided `data/picks.json`
in as-is — do not regenerate or reformat it.

---

## Phase 1 — Solution scaffold

```bash
dotnet new sln -n Sweepstake
dotnet new classlib  -o src/Sweepstake.Core
dotnet new blazorwasm -o src/Sweepstake.Web        # standalone WASM. NOT --hosted, NOT Blazor Server.
dotnet new console   -o tools/Sweepstake.StatsFetcher
dotnet new xunit     -o tests/Sweepstake.Core.Tests
```

Add all four to the solution. `Web`, `StatsFetcher` and `Tests` all reference `Core`.

Set `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` everywhere.

---

## Phase 2 — Core models and leaderboard calculation

This is the only part with real logic. Build it first and test it before touching the UI or
the network, so that when something looks wrong later you already know the maths is right.

Models (records, in `Sweepstake.Core`):

- `Player(string Name, string Club, string? SheetName)` — registry entry, keyed by id
- `Pick(string EspnId, string Odds)` — note: **no player name here**, only an id
- `Entrant(string Name, IReadOnlyList<Pick> Goalscorers, IReadOnlyList<Pick> Assisters)`
- `PicksFile(string Season, string SeasonLabel, IReadOnlyDictionary<string, Player> Players, IReadOnlyList<Entrant> Entrants)`
- `PlayerStat(string Name, int Goals, int Assists)`
- `StatsFile(DateTimeOffset GeneratedUtc, string Season, IReadOnlyDictionary<string, PlayerStat> Players)`
- `ScoredPick(string EspnId, Player Player, string Odds, int Value)`
- `ScoredEntrant(string Name, IReadOnlyList<ScoredPick> Picks, int Total, int OddsTotal, int Rank)`

A `LeaderboardBuilder` with a method per board (or one method taking a board enum) that:

1. Joins each pick to its stats by `EspnId` — a plain dictionary lookup. **No name matching,
   no normalisation, no fuzzy comparison anywhere.** If you find yourself writing string
   comparison of player names, stop; you have gone wrong.
2. An id present in the `players` registry but absent from `stats.json` scores **0** — normal,
   not an error (nobody has scored yet, or the player is injured).
3. An id referenced by a pick but **missing from the registry** is a hard error — throw.
4. Sums the three picks into `Total`.
5. Sums the three odds numerators (parse `"8/1"` → `8`) into `OddsTotal`.
6. Sorts by `Total` descending, then entrant name ascending as a stable tiebreak.
7. Assigns **competition ranking**: equal totals share a rank and the next rank skips
   (1, 2, 2, 4). Do not give tied entrants different ranks.

### Tests to write (all in `Sweepstake.Core.Tests`, no network)

- Totals sum correctly across three picks.
- An id missing from `stats.json` contributes 0 rather than throwing.
- An id missing from the `players` registry throws.
- Ties share a rank and the following rank skips accordingly.
- Odds parsing: `"8/1"` → 8, `"150/1"` → 150. Malformed input throws a clear exception.
- Odds totals match the source spreadsheet on a sample: Eanan goals = 128, Ste D goals = 174,
  Raleigh assists = 174, Ciano assists = 145.
- Sorting puts the highest total first and is stable for equal totals.

Plus one integrity test that loads the real `data/picks.json` and asserts: 17 entrants, 38
registry entries, exactly 3 picks per list, every referenced id present in the registry, no
registry entry unused, no duplicate id within a single pick list, and all 34 odds totals
matching the spreadsheet. This is cheap and catches a bad hand-edit immediately.

Get `dotnet test` green before Phase 3.

---

## Phase 3 — StatsFetcher: `verify` (maintenance command)

`data/picks.json` already contains every ESPN athlete id, resolved and hand-checked on
2026-08-22. **There is no resolve step in the build.** Do not write one, and do not add any
name-matching code.

What this command does instead is confirm the committed ids are still correct:

1. For each of the 38 ids in the `players` registry:
   `GET .../seasons/2026/athletes/{id}` → the athlete's name and current team `$ref`.
2. Compare the returned name against the registry `name` and the returned club against
   `club`.
3. Print a report of every disagreement. **Do not auto-correct `picks.json`** — print what
   changed and let a human decide.
4. Exit non-zero if any id 404s, since that means a player has left the league or ESPN has
   retired the id.

Run this after a transfer window, or if a player's numbers look implausibly frozen. It is not
part of the deploy path and must not run on every build — it is 38 calls to confirm something
that changes twice a year.

A note on why the design is this way, so nobody "improves" it later: matching football players
by name is a trap. Accents vary by provider, providers disagree on name forms (ESPN calls
`301894` "Igor Thiago" while the spreadsheet says "Thiago"), several active players share a
name (ESPN has two Igor Jesuses and ten Bruno Fernandeses), and transfers move players between
clubs mid-season. A matcher loose enough to survive all that is loose enough to eventually pick
the wrong player, and the wrong player yields plausible-looking numbers that nobody catches.
The string match was done once, by hand, under review. It does not need doing again at runtime.

---

## Phase 4 — StatsFetcher: `fetch`

1. `GET .../seasons/2026/types/0/leaders?limit=1000`.
2. Find the categories whose `name` is exactly `goals` and exactly `assists`. There are 12
   categories and two pairs share a `displayName` — `goals`/`goalsLeaders` both display as
   "Goals", `assists`/`assistsLeaders` both as "Assists". Matching on `displayName` will
   sometimes select the wrong one depending on array order. Match on `name`, and throw if
   either is missing rather than defaulting to empty.
   Each entry has a value and an athlete `$ref` containing the athlete id — parse the id out
   of the URL. Values arrive as doubles (`1.0`); round to int.
3. Cross-check against `goalsLeaders` / `assistsLeaders` for the same athletes and warn on any
   disagreement — a mismatch means the payload shape has changed upstream.
4. For every athlete id in the `players` registry of `data/picks.json`, take its goals and
   assists from the leaders payload, defaulting to 0 when absent. Only those 38 ids are
   written out — the leaders payload carries hundreds of players we do not care about.
5. Write `src/Sweepstake.Web/wwwroot/data/stats.json` per the shape in `CLAUDE.md`, stamped
   with `generatedUtc`.
6. If the HTTP call fails, or the response has no `goals` category at all, **exit non-zero
   without writing the file.** A partial or empty stats file must never overwrite a good one.

Set a `User-Agent` header. Add a short retry with backoff on 5xx and 429. Be a polite client:
this API is undocumented and we are guests on it.

Optional hardening, worth doing: after writing, spot-check 2–3 of the highest-scoring players
against the per-athlete `statistics/0` endpoint and warn on any mismatch.

---

## Phase 5 — Blazor UI

Single page. On startup, fetch `data/picks.json` and `data/stats.json` from the app's own origin (relative to `<base href>`), pass them to
`LeaderboardBuilder`, render.

### Layout

Match `docs/design-reference.jpg`:

- Two panels side by side, **goals left, assists right**, separated by a narrow cream gutter.
- Within each panel, entrants are listed in **rank order, best first** — unlike the source
  spreadsheet, which is unsorted.
- Each entrant block is 3 player rows plus a `Totals` row.
- Player row columns: entrant name (first row only) · player · club · odds · value.
- The `Totals` row shows summed odds and the summed goals/assists.

### Colours (sample the reference image to confirm)

- Column header band: dark green, white text
- Goals `Totals` row: blue, white text
- Assists `Totals` row: teal, white text
- Entrant name: green, bold
- Player rows: white, thin light grey rules
- Gutter: cream

### Auto-refresh while the page is open

The page must update itself without the user pressing refresh.

- A `PeriodicTimer` on a 10-minute interval re-fetches `data/stats.json`.
- Append a cache-busting query (`?t={unixSeconds}`) — GitHub Pages caches aggressively and
  without this the browser will serve the same file back for an hour.
- Compare `generatedUtc` against the loaded copy. If unchanged, do nothing and do not
  re-render. If changed, recompute the leaderboards and update.
- When a row's value changes, briefly highlight it and animate any change in rank order, so a
  viewer with the tab open notices that something happened.
- Pause the timer when `document.hidden` is true, resume and fetch immediately on becoming
  visible. No point polling a background tab.
- A failed refresh must be silent and non-destructive: keep showing the last good data, log to
  console, try again next tick. Never blank the board because one fetch failed.

This is a same-origin request for our own static file, so it is cheap, cannot be blocked by
CORS, and does not touch ESPN.

### Additions beyond the spreadsheet

- Rank number on each entrant block; subtle highlight or medal tint for the top 3.
- `generatedUtc` rendered as "Last updated ..." in the user's local time.
- On viewports under ~900px, stack the two panels vertically (goals first). Each panel scrolls
  horizontally inside its own container rather than the page scrolling sideways.
- Respect `prefers-color-scheme`. Define the light palette on `:root` and override only the
  tokens in a dark block — do not let any colour exist solely inside the dark block.
- Duplicate picks are common (Isak appears in six goal teams). Consider a light visual cue for
  the most-picked players, but do not let it clutter the table.

Keep it plain CSS. No Bootstrap, no external fonts, no CDN.

---

## Phase 6 — GitHub Actions

**`update-stats.yml`** — scheduled refresh:

- `schedule:` cron every 15 minutes, plus `workflow_dispatch` for manual runs.
  Note that GitHub's scheduler is best-effort, not exact: runs are frequently delayed by
  10–30 minutes under load and occasionally skipped entirely. That is acceptable here — the
  page shows `generatedUtc` so staleness is always visible — but do not design anything that
  assumes the cron fired on time.
- Runs `dotnet run --project tools/Sweepstake.StatsFetcher -- fetch`.
- Commits `wwwroot/data/stats.json` only if it changed. Skip the commit when the diff is
  limited to `generatedUtc`, otherwise every run creates a noise commit.
- If the fetcher exits non-zero, the job fails and commits nothing.

**`deploy.yml`** — build and publish:

- Triggers on push to `main` and on completion of `update-stats.yml`.
- `dotnet publish src/Sweepstake.Web -c Release -o publish`
- Then, against `publish/wwwroot`:
  - `touch .nojekyll`
  - rewrite `<base href="/" />` to `<base href="/<repo-name>/" />`
  - `cp index.html 404.html`
- Upload `publish/wwwroot` with `actions/upload-pages-artifact`, deploy with
  `actions/deploy-pages`. Set `permissions: contents: write, pages: write, id-token: write`.

Re-read the Pages gotchas in `CLAUDE.md` before writing this. All four bite silently.

---

## Phase 7 — Verification (required, do not skip)

1. `dotnet test` — green.
2. Run `resolve`. Every one of the picked players resolves, or is explicitly listed in
   `manualOverrides`. Zero unresolved.
3. Run `fetch`. Open `stats.json` and sanity-check it against the live Premier League table —
   the numbers should be real and current, not all zeros and not obviously stale.
4. Run the app locally. Check by hand that one entrant's total equals the sum of their three
   players' values as shown on their own rows. Do this for a second entrant on the other panel.
5. Confirm sorting: the top entrant genuinely has the highest total on each panel.
6. Confirm tie handling with a temporary fixture if no real tie exists yet.
7. Screenshot the rendered page and compare against `docs/design-reference.jpg`.
8. Test the deployed Pages URL — not just localhost. The `.nojekyll` and `<base href>` bugs
   only appear once deployed, and both produce a blank white page rather than an error.

---

## Appendix — alternative: fetch ESPN directly from the browser

The default design above is **baked**: a scheduled Action calls ESPN and commits
`stats.json`; the browser only ever reads that file. Build this unless the user says otherwise.

There is a simpler variant: the browser calls the ESPN `leaders` endpoint itself on each
10-minute tick, dropping the `update-stats.yml` workflow and the fetcher's `fetch` command.

**CORS was tested on 2026-08-22 from a browser on an unrelated origin and the request
succeeded**, so this variant is technically available. Two conditions still apply:

1. **`picks.json` still carries the committed athlete ids.** They are already resolved, so
   nothing changes there — only the single `leaders` call moves client-side, and the browser
   filters it down to the 38 ids in the registry.
2. **The CORS result is not a guarantee.** This is an undocumented API; the header could
   disappear without notice, at which point the site breaks for everyone with no warning and
   no fix available from our side.

Tradeoffs, stated plainly:

- *For:* fewer moving parts — no stats workflow, no bot commits, no committed data file, no
  staleness window. Note this does **not** remove GitHub Actions from the project: a deploy
  workflow is required regardless to build and publish the Blazor app to Pages. The saving is
  one extra workflow file, not the whole pipeline.
- *Against:* every visitor's every poll hits ESPN, so load scales with the audience rather
  than being constant. If ESPN changes shape or goes down the board renders empty instead of
  stale-but-correct. And ESPN is an undocumented API we have no agreement with — pushing that
  traffic to end users' browsers is harder to justify than one server-side call per quarter hour.

If this variant is chosen, keep `IStatsSource` as an interface in `Sweepstake.Core` with a
`StaticJsonStatsSource` and an `EspnLiveStatsSource`, so switching back is a DI registration
change rather than a rewrite.

## Out of scope

No auth, no editing picks in the UI, no live in-match updates, no per-goal event history, no
database, no server. If a request seems to need any of those, re-read the constraints section
of `CLAUDE.md` before building it.
