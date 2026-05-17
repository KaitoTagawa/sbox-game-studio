# ADR-0002: Leaderboards

## Status
Accepted (2026-05-09)

## Date
2026-05-09

## Last Verified
2026-05-09

## Decision Makers
Kaito Tagawa (creative + technical)

## Summary
Single-player tycoons have nothing to compete over once you've finished a
run. We add four "for-fun" leaderboards (best single game's revenue, best
single game's score, best lifetime earnings in any one run, peak balance
ever held) using s&box's first-party `Sandbox.Services.Stats` +
`Sandbox.Services.Leaderboards` services. Scores are submitted from the
client, with no server-side validation — accepted limitation in exchange
for shipping the feature in days, not weeks.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | s&box 1.0 (rolling release, build pinned 2026-04-27) |
| **Domain** | Live Services / UI |
| **Knowledge Risk** | HIGH — engine launched after LLM training cutoff (May 2025). Leaderboard API verified 2026-05-09 against `github.com/Facepunch/sbox-docs/docs/services/`. |
| **References Consulted** | `docs/engine-reference/sbox/VERSION.md`; `https://sbox.game/dev/doc/services/stats`; `https://sbox.game/dev/doc/services/leaderboards/`; `https://github.com/Facepunch/sbox-docs/blob/master/docs/services/leaderboards/index.md` |
| **Post-Cutoff APIs Used** | `Sandbox.Services.Stats.SetValue(name, value)`, `Sandbox.Services.Stats.Increment(name, n)`, `Sandbox.Services.Leaderboards.GetFromStat(ident, name)`, `Leaderboard.Refresh()`, `Leaderboard.SetAggregationMax/Min/Sum/Avg/Last()`, `Leaderboard.SetSortAscending()`, `Leaderboard.CenterOnMe()`, `Leaderboard.MaxEntries`, `Leaderboard.Entries[].{Rank, DisplayName, Value, CountryCode, Timestamp}`. None are exercised by this codebase yet — only `Sandbox.Services.Achievements.Unlock(string)` has been used so far (in `Code/Achievements/Achievements.cs`). |
| **Verification Required** | Round-trip a `SetValue` call from a published `dellort.game2` build and confirm the entry appears at `sbox.game/dellort.game2/leaderboards`. Confirm the exact game-ident format expected by `GetFromStat` (likely `org.ident` from `.sbproj`, but the docs use `facepunch.ss1` as the only example). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Save System) — `AchievementsSave` is extended to persist `PeakBalance`, and the schema bump goes through the ADR-0001 migrator chain. |
| **Enables** | None yet. Future "seasonal events" or "weekly challenges" features could reuse this stats plumbing. |
| **Blocks** | None — this is an additive feature. |
| **Ordering Note** | Implementation can land at any time after ADR-0001's save system is in place (it is, as of 2026-05-08). |

## Context

### Problem Statement
Players asked for a way to compete with each other on shipped-game
performance. The base game has no online component; a leaderboard is the
cheapest possible "social" hook, and s&box ships first-party plumbing for
exactly this case. The decision now is **what we expose as competitive
metrics** and **how to handle the cheating problem** that any
client-authoritative scoreboard inherits.

### Current State
Studio performance is tracked locally in `Achievements.cs`
(`LifetimeEarned`, `GamesShipped`, `PeakMonthlyPlayers`, `TotalHires`)
and in `Gallery.cs` (`ShippedGame.TotalSales`, `RevenueEarned`,
`DesignPoints`/`SoundPoints`/`GraphicsPoints`). Nothing leaves the local
save. Nothing tracks the player's all-time peak wallet balance.

### Constraints
- **Engine**: s&box 1.0 — `Sandbox.Services.Stats` + `Leaderboards` are
  the platform's first-party path. Anything else (custom backend, Steam
  leaderboards) is out of scope at this time.
- **Single-player**: there is no authoritative server. Whatever score the
  client submits is what gets ranked.
- **Save-data is plaintext JSON** — modifying it is trivial (Notepad).
  Any leaderboard backed by saved counters is therefore trivially gamed.
- **Stats only flow from published `dellort.game2` builds** signed in to
  sbox.game. Editor sessions and unpublished builds silently skip
  submission.
- **No backend budget** — we are not standing up a service for this.

### Requirements
- Four leaderboards: best single game's revenue, best single game's
  score, best lifetime earnings in any one run, peak balance ever held.
- Player can view all four from inside the running game.
- Stats are submitted only at meaningful checkpoints (game ship, manual
  save) — not every frame.
- Submission failures must not crash, hang, or produce user-facing
  errors. The platform service is best-effort.
- The implementation must be removable in one commit if Facepunch ever
  takes the Stats service down or changes its shape.

## Decision

Use s&box's `Sandbox.Services.Stats.SetValue` for submission and
`Sandbox.Services.Leaderboards.GetFromStat` for display, with a thin
static `Leaderboards` wrapper that hides the platform calls behind
project-level naming and graceful failure handling. Cheating is accepted
as a known limitation, surfaced in the in-game UI as "for fun — not
moderated."

### Architecture

```
                        ┌─ on ship ────────────────┐
                        │                          ▼
   Gallery ────────────►│            ┌──► Sandbox.Services.Stats
   .RecordShippedGame() │            │    .SetValue(name, value)
                        │            │
                        ▼            │
              Leaderboards ──────────┤
              (static wrapper)       │
                        ▲            │
                        │            │
   GameSaveManager ─────┤            │
   .TrySaveRun()        │            │
                  on save            │
                                     │
   LeaderboardsPanel ◄─── Sandbox.Services.Leaderboards
   (UI, on open)         .GetFromStat(ident, name)
                          .Refresh()
                          .Entries → Rank/DisplayName/Value
```

### Key Interfaces

```csharp
// New: Code/Leaderboards.cs
public static class Leaderboards
{
    /// Project ident — must match game2.sbproj's "Org.Ident".
    public const string GameIdent = "dellort.game2";

    /// Master kill switch. Set false to disable all submissions and
    /// queries (debug builds, A/B tests, kill-platform-call rollback).
    public static bool Enabled { get; set; } = true;

    public const string StatBestGameRevenue = "best_game_revenue";
    public const string StatBestGameScore   = "best_game_score";
    public const string StatLifetimeEarn    = "lifetime_earnings";
    public const string StatPeakBalance     = "peak_balance";

    /// Called from Gallery.RecordShippedGame after Achievements.RecordShippedGame.
    public static void SubmitOnShip( ShippedGame game ) { ... }

    /// Called from GameSaveManager.TrySaveRun after a successful write.
    public static void SubmitOnSave() { ... }

    /// Async helper used by LeaderboardsPanel. Returns null on failure.
    public static async Task<List<LeaderboardRow>> FetchAsync( string statName, int max = 50 ) { ... }
}

public sealed record LeaderboardRow( int Rank, string DisplayName, double Value, string CountryCode );
```

```csharp
// Extension to Code/Achievements/Achievements.cs
public static long PeakBalance { get; private set; }   // updated from GameManager.OnUpdate

public static void RecordBalance( long money )
{
    if ( money > PeakBalance ) PeakBalance = money;
}
```

```csharp
// Extension to Code/Save/GameSave.cs (AchievementsSave)
public sealed class AchievementsSave
{
    // ... existing fields ...
    public long PeakBalance { get; set; }   // NEW
}
```

### Implementation Guidelines

**Stats schema (the source of truth for what's submitted and how it's
aggregated):**

| Stat name (constant) | Submitted as | When | Aggregation | Sort |
|----------------------|-------------|------|-------------|------|
| `best_game_revenue` | `(double)game.TotalSales` | On each `Gallery.RecordShippedGame` | Max | Desc |
| `best_game_score` | `(double)(game.DesignPoints + game.SoundPoints + game.GraphicsPoints)` | On each `Gallery.RecordShippedGame` | Max | Desc |
| `lifetime_earnings` | `(double)Achievements.LifetimeEarned` | On each ship, and on each manual save | Max | Desc |
| `peak_balance` | `(double)Achievements.PeakBalance` | On each ship, and on each manual save | Max | Desc |

`Max` aggregation across multiple `SetValue` calls means each new
submission only displaces the player's leaderboard rank if it beats
their previous best. Multiple playthroughs do not accumulate.

**Submission rules:**
- Every platform call wrapped in `try/catch` with a `Log.Warning` on
  failure — never user-facing. Mirrors the achievement-bridge pattern in
  `Achievements.cs:269` (`BridgeUnlockToPlatform`).
- `if ( !Enabled ) return;` at the top of every `Submit*` entry point.
- No platform calls fire from `Reset()` / `RestartRun()` — leaderboard
  state is per-account at the platform, not per-save.

**Peak-balance tracking:**
- `Achievements.PeakBalance: long` initialised at 0.
- `GameManager.OnUpdate()` calls `Achievements.RecordBalance( Money )`
  once per frame. Cost: one comparison + one assignment when the wallet
  hits a new high. Negligible.
- Persisted via `AchievementsSave.PeakBalance`. Bumped through schema
  migrator (see Migration Plan).
- Reset to 0 in `Achievements.Reset()` (which is already called from
  `GameSaveManager.RestartRun`).

**UI (`LeaderboardsPanel`):**
- New fullscreen modal opened from a "Leaderboards" 🏆 tile in
  `GameMenu.Tiles`.
- Activate() route mirrors HR/Shop/Inventory pattern: close menu, open
  modal.
- Four tabs at the top (one per stat). Tab labels and descriptions:
  - "Top Game Sales" — best single game's lifetime sales
  - "Top Game Score" — best single game's pillar score
  - "Top Run Earnings" — best lifetime earnings in any one run
  - "Top Wallet" — peak balance ever held
- Body: scrolling list of `LeaderboardRow { Rank, DisplayName, Value }`.
- Refresh fires automatically on open and on tab switch.
  `Leaderboard.Refresh()` is `async`; while in flight, body shows a
  "Loading…" empty state. On exception or `null` result, body shows
  "Leaderboards unavailable in editor / unpublished build."
- Subtle footer: "For-fun board. Scores submitted from the client and
  are not moderated."
- Scope: **Global / All Time only** for v1. (Country / monthly /
  centered are deferred — see "Alternatives Considered" #4.)

**Scene wiring:**
- `LeaderboardsPanel` PanelComponent placed on the existing `MenuUI`
  GameObject (ZIndex 100). Same ScreenPanel as `GameMenuPanel` so
  internal SCSS `z-index` works as expected when both are open. (See
  `memory/sbox_screenpanel_zindex_layering.md`.)
- A new `Leaderboards` Component goes on the same GameObject as a
  scene-bound singleton, mirroring how `HR.cs` lives next to `HRPanel`.

**Player-lock integration:**
- `GameManager.ApplyPlayerLock` gets a new clause:
  `|| (Leaderboards.Instance?.IsOpen ?? false)`. Same pattern as every
  other modal.

## Alternatives Considered

### Alternative 1: No leaderboard
- **Description**: Ship without competitive features.
- **Pros**: Zero code, zero cheating risk, zero platform dependency.
- **Cons**: No social hook. The player's runs end and there's nowhere
  to look at how others did.
- **Estimated Effort**: 0.
- **Rejection Reason**: User explicitly asked for the feature. The
  cost/benefit at "for-fun" tier is favourable.

### Alternative 2: Server-authoritative scoring
- **Description**: Run a backend that re-derives scores from a sealed
  game-state replay or from milestone events the server can sanity-check.
- **Pros**: Real defence against cheaters. The leaderboard reflects
  actual play.
- **Cons**: Requires a backend service, hosting cost, ops on-call,
  authentication beyond what sbox.game gives, a wire format for the
  game state, and an integrity bar we cannot meet without dedicating
  weeks to anti-cheat. Out of proportion to a tycoon side-project.
- **Estimated Effort**: 2-4 weeks.
- **Rejection Reason**: Out of budget. Bookmarked for if the game ever
  grows a paid online component.

### Alternative 3: Cap + sanity-check scoring
- **Description**: Submit only milestones (`SetValue("ship_count", N)`
  capped at 10000). Ignore obviously-impossible values client-side
  before submission.
- **Pros**: Filters casual cheaters. Some teeth without a backend.
- **Cons**: Caps reduce information density of the boards (everyone
  with >10000 ships ties at 10000). Determined cheaters edit the cap
  out of the binary. Doesn't actually solve the integrity problem,
  just blunts it.
- **Estimated Effort**: ~1 day on top of v1.
- **Rejection Reason**: Half-measure. If we don't promise integrity in
  the UI ("for fun — not moderated") then capping does nothing the
  disclaimer doesn't already do.

### Alternative 4: Multiple scopes (global + monthly + country + centered)
- **Description**: Ship every scope filter the platform offers.
- **Pros**: More to look at; monthly leaderboards stay fresh after the
  global board calcifies.
- **Cons**: 4× the UI surface area at v1 with no demand evidence yet.
  Each filter is a separate `Leaderboard.Refresh()` round-trip.
- **Estimated Effort**: ~1 day on top of v1.
- **Rejection Reason**: Deferred. Ship Global / All Time first, see if
  anyone notices the leaderboard exists, then add scopes if engagement
  warrants. The platform API supports this trivially — adding a scope
  later is a 30-line UI change.

## Consequences

### Positive
- Players have something to chase after the run-loop's natural ending.
- Foundation for any future season / event / challenge feature is in
  place — the stats plumbing is reusable.
- Validates the `Sandbox.Services.Stats` integration end-to-end before
  we need it for anything load-bearing.

### Negative
- The boards will be cheated. The disclaimer in the UI is the only
  defence. New players who don't see the disclaimer may be confused or
  dismissive when the top score is impossibly high.
- Editor sessions silently skip submission. New developers/testers may
  ship test games and wonder why their stats don't show up — this is
  documented in `Leaderboards.cs` but easy to miss.
- One more on-update tick consumer (peak-balance tracker). Cost is a
  long compare; budget impact is zero, but it joins the list of
  "things that run every frame and need to stay fast."

### Neutral
- The save-schema bumps from v1 to v2. Any save written by today's
  build will load fine in tomorrow's, but a save written by tomorrow's
  build will refuse to load in today's. Standard ADR-0001 migrator
  protocol.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Platform Stats service signature differs from docs we read | LOW | HIGH (won't compile) | First implementation step is a one-line `SetValue` smoke test from a published build before we wire submission anywhere. |
| Game ident `"dellort.game2"` is the wrong format for `GetFromStat` | MEDIUM | MEDIUM (queries return empty) | Smoke test confirms by submitting then querying with the same string. Constant lives in one place; one-line fix. |
| Cheaters dominate boards within days of launch | HIGH | LOW | Disclaimer in UI sets expectations. No claims of integrity made anywhere in copy. |
| Schema migration corrupts existing saves | LOW | HIGH | Reuses ADR-0001's tested migrator chain. New field defaults safely (`PeakBalance = 0`); migrator coverage adds a `MigrateFromV1` test alongside existing migration tests. |
| Adding `RecordBalance` to `OnUpdate` regresses frame time | LOW | LOW | Single long compare per frame. If profiling ever shows it, batch to once per second. |

## Performance Implications

| Metric | Before | Expected After | Budget |
|--------|--------|---------------|--------|
| CPU (frame time) | baseline | +<0.001ms (one long compare/frame) | 16.6ms total |
| Memory | baseline | +negligible (4 string consts, 1 long, 1 list of ~50 rows when panel open) | 4GB managed heap |
| Network | 0 | Burst on each ship/save (~100 bytes per `SetValue`); on-demand `Refresh` (~5KB per board) | unbounded — out of player's control |
| Load Time | baseline | unchanged | <5s |

## Migration Plan

1. **Bump `GameSaveManager.SchemaVersion` from 1 to 2.**
   `AchievementsSave.PeakBalance` is the only new persisted field. No
   removed or renamed fields.
2. **Add `GameSaveMigrations.MigrateFromV1`** — sets
   `dto.Achievements.PeakBalance = 0` on legacy saves. Existing
   `MigrateFromV0` chain pattern already handles defaulting via DTO
   property defaults; this migrator is explicit about the field for
   readability.
3. **Add `Achievements.PeakBalance` + `RecordBalance(long)` + `Save/Load`
   wiring** for the new DTO field.
4. **Add `Code/Leaderboards.cs`** with the static API + the constants.
   Leave all `Submit*` methods as no-ops in the first commit so the
   platform call doesn't fire until we've verified the API.
5. **Smoke-test step.** In a published build, manually call
   `Sandbox.Services.Stats.SetValue("smoke_test", 42)` once and confirm
   it appears in the project's leaderboard dashboard. Document result.
6. **Wire submissions:** call `Leaderboards.SubmitOnShip` from
   `Gallery.RecordShippedGame`; call `Leaderboards.SubmitOnSave` from
   `GameSaveManager.TrySaveRun` after the successful write.
7. **Add `LeaderboardsPanel` UI + scene wiring.** New PanelComponent on
   `MenuUI` GameObject. New `Leaderboards` Component (singleton) for
   open/close state. New tile in `GameMenu.Tiles`. Activate() routing.
   `ApplyPlayerLock` clause.
8. **End-to-end QA.** Ship a test game, save, open the panel, confirm
   the player's own row shows. Repeat from a second account if
   feasible to confirm rank ordering.

**Rollback plan**: Set `Leaderboards.Enabled = false` to silence
submissions immediately. Removing the feature entirely is a single
revert of the implementation commits — `AchievementsSave.PeakBalance`
becomes a vestigial property but reads default to 0 on legacy saves so
no manual migration is needed for the rollback.

## Validation Criteria

- [ ] Submitting a value via `Sandbox.Services.Stats.SetValue` from a
      published `dellort.game2` build appears at the project's
      leaderboard dashboard (smoke test before wiring submissions).
- [ ] Shipping a game in-engine triggers exactly four `SetValue` calls
      with the documented stat names and aggregation modes.
- [ ] Manual save triggers exactly two `SetValue` calls
      (`lifetime_earnings`, `peak_balance`).
- [ ] `LeaderboardsPanel` opens from the GameMenu tile, shows entries
      after `Refresh()`, falls back gracefully in editor.
- [ ] Save written under SchemaVersion 2 loads cleanly. Save written
      under SchemaVersion 1 migrates to 2 with `PeakBalance = 0`.
- [ ] No exception thrown during `Submit*` paths under any normal flow
      (editor session, missing platform login, network down).
- [ ] Frame-time profile in production loop unchanged (±noise) with the
      `RecordBalance` per-frame call.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|--------------|--------|-------------|--------------------------|
| _none_       | _n/a_  | Foundational — no GDD requirement. | This decision adds a new optional system. Future GDDs (seasonal events, weekly challenges) can build on the stats plumbing. |

## Related

- ADR-0001 (Save System) — depends on, extends `AchievementsSave`.
- `memory/sbox_screenpanel_zindex_layering.md` — informs scene wiring
  decision (place panel on `MenuUI` GameObject, not a new ScreenPanel).
- `Code/Achievements/Achievements.cs:269` — `BridgeUnlockToPlatform`
  pattern reused for graceful platform-call failure.
- `Code/Gallery.cs:480` — `RecordShippedGame` is the submission trigger
  for the three ship-time stats.
