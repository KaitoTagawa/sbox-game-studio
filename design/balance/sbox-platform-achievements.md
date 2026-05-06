# sbox.game platform achievements — registration sheet

This is the canonical list of achievements to register on the project's
**sbox.game dashboard** so that the in-game `Achievements.Unlock()` calls
flow through to platform unlocks. The mapping lives in
`Code/Achievements/Achievements.cs::PlatformIdMap` — keep this doc in
sync if you add or rename achievements.

The bridge is gated by the constant `BridgeToPlatform` (default `true`)
in the same file. Flip it to `false` to disable platform pings without
removing the map.

## Per-achievement entries to create on the dashboard

Use the `Platform ID` column verbatim as the achievement's string ID on
sbox.game. Name / Description are pre-written copy from the in-game
catalogue — feel free to tighten for the dashboard if dashboard UX
prefers shorter strings.

| Platform ID              | Name              | Description                                      | Money Bonus |
|--------------------------|-------------------|--------------------------------------------------|-------------|
| `earn_1k`                | First Thousand    | Earn $1,000 in lifetime sales.                   | (set on Achievements.cs) |
| `earn_10k`               | Five-Figure Studio| Earn $10,000 in lifetime sales.                  | "" |
| `earn_100k`              | Six-Figure Studio | Earn $100,000 in lifetime sales.                 | "" |
| `earn_1m`                | Million-Dollar Studio | Earn $1,000,000 in lifetime sales.           | "" |
| `first_hire`             | First Recruit     | Hire your first employee.                        | $200 |
| `hire_5`                 | Studio Built      | Hire 5 employees over your career.               | $1,000 |
| `hire_8`                 | Growing Pains     | Hire 8 employees over your career.               | $2,500 |
| `hire_10`                | Studio Veteran    | Hire 10 employees over your career.              | $5,000 |
| `players_100`            | First Hundred Fans| Reach 100 monthly players in any released game.  | $500 |
| `players_10k`            | Cult Following    | Reach 10,000 monthly players in any released game.| $5,000 |
| `players_1m`             | Mainstream Hit    | Reach 1,000,000 monthly players in any released game.| $50,000 |
| `discover_first_ability` | Talent Scout      | First time you see a hire with a special ability.| $200 |
| `discover_all_abilities` | Master Recruiter  | Encounter every special ability across hires.    | $5,000 |
| `ship_first_game`        | First Ship        | Ship your first game.                            | $300 |
| `ship_3_games`           | Repeat Performer  | Ship 3 games.                                    | $2,000 |
| `ship_10_games`          | Veteran Developer | Ship 10 games.                                   | $10,000 |

> Verify the in-game `Name` / `Description` against
> `Code/Achievements/Achievements.cs` if anything looks stale — the
> in-game catalogue is the source of truth.

## Registration workflow on sbox.game

1. Open the project page on sbox.game and find the **Achievements** tab.
2. For each row above, click **Add Achievement**.
3. Fill in:
   - **ID**: the `Platform ID` column verbatim (lowercase snake_case).
   - **Name**: from the table.
   - **Description**: from the table.
   - **Score**: 5–25 depending on rarity (suggest: tier-1 = 5, tier-2 = 10, tier-3 = 25 — see "Scoring" below).
   - **Unlock mode**: `Manual` for all of these — the in-game catalogue's
     `Evaluate` predicates do the gating, then `BridgeUnlockToPlatform`
     calls `Sandbox.Services.Achievements.Unlock(id)`.
   - **Icon**: optional — upload one per achievement, or leave the
     default until visual pass.
4. Save. Test in a published build (the platform service may not fire
   from a local editor session — verify before assuming silence is a
   bug).

## Suggested scoring (5/10/25 tier model)

| Tier | Score | Achievements |
|------|-------|--------------|
| Bronze (easy / early-game) | 5 | `first_hire`, `ship_first_game`, `earn_1k`, `players_100`, `discover_first_ability` |
| Silver (mid-game milestones) | 10 | `hire_5`, `hire_8`, `ship_3_games`, `earn_10k`, `players_10k`, `discover_all_abilities` |
| Gold (late-game / mastery) | 25 | `hire_10`, `ship_10_games`, `earn_100k`, `earn_1m`, `players_1m` |

Total points across all 16 = 5×5 + 10×6 + 25×5 = **210**.

## What happens if you forget to register one

`BridgeUnlockToPlatform` swallows exceptions and logs a warning — the
in-game unlock still fires (toast, money bonus, gallery entry). The
worst case is the player's sbox.game profile is missing one achievement
that they actually earned in-game. Add the dashboard entry, and the
NEXT unlock of that same achievement (e.g. on a fresh save / new run)
will sync.

If you want already-earned-but-not-platform-synced achievements to
backfill, add a one-shot bootstrap call in `Achievements.Load()` that
walks `_unlocked` after a save load and calls
`Sandbox.Services.Achievements.Unlock(platformId)` for each. Not
implemented today — the platform expects fire-once semantics and
re-firing is harmless but slightly wasteful.
