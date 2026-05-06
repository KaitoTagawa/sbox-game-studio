# Step 5 Payoff — Ship, Revenue, Reviews & Awards

> **Status**: In Design
> **Author**: Kaito Tagawa + Claude (forward design)
> **Last Updated**: 2026-04-27
> **Implements Pillar**: Pillar 4 (A milestone, not an ending) + Pillar 2 (Every project is an authored mix — choices must pay off)
> **Type**: Forward design — no implementation yet (off-index, to add when /map-systems runs)

## Overview

**Step 5 Payoff** is the system that turns a finished `GameProject` into a shipped game with measurable impact. When a project's three pillars all hit 100% and the player ships it, this system computes the **review score** (a 0–100 critical reception number derived from pillar points, sacrificed pillars, crunch state, and Producer bug-cleaning), the **sales curve** (revenue accruing over a release window, modulated by genre revenue multipliers and any active marketing), and the **peak players** number. Each shipped game becomes a permanent `ShippedGame` record in the `Gallery`, and at the end of every in-game year a year-end ceremony selects winners across **seven trophy divisions** (Game of the Year, Best Design, Best Sound, Best Visual Design, Best Indie, Best Newcomer, Critics' Choice) — each award becoming a `Trophy` record.

For the player, this is **the moment everything in the studio matters**. The setup choices (which genres, which roles, how much time per pillar), the team they assembled, the morale they preserved or sacrificed during crunch — all of it materializes here as scrolling reviews, a sales ticker climbing in real-time, and ultimately the year-end ceremony. The endgame **milestone target** (specific milestone TBD per the game-concept doc — e.g., "Win Game of the Year" or "Reach $10M lifetime revenue") is achieved through this system; it is the only place such a target can be detected.

## Player Fantasy

The player fantasy of Step 5 is **"the moment your studio gets reviewed"** — the universal anxiety-and-thrill of every game launch. You've spent four to ten in-game minutes assembling a team, picking genre tags, allocating time, watching three pillar bars tick toward completion. Now the bars are full. You hit ship. **The world responds.**

Reviews scroll in over a few seconds — short, themed snippets that reflect the actual numbers your project produced. A high-Design game gets *"a masterclass in mechanical design"*; a high-Sound, low-Graphics game gets *"sounds incredible, looks rough"*; a sacrificed-Sound game gets *"silence is not a stylistic choice"*. The aggregate score lands as a single number, 0–100, painted on the Gallery card forever.

Sales tick up over the release window — first few days are explosive (early-adopter spike), then a long tail. Marketing hires bend the curve upward; bad reviews flatten it. The player watches the totalizer climb like a slot machine.

And once a year, the **year-end ceremony**. The studio's best game of the year is named in front of a curtain-pull animation. If it wins Game of the Year, the trophy goes on the permanent shrine in the Gallery. The player sees their studio's history — *"2027 Best Indie: Fishing Roguelike — A landmark release in the genre"* — accumulate over a 5–10 year run.

**The fantasy ladder**:
1. **First ship** (~5 min in): *I made a thing. People are reviewing it. The numbers are real.*
2. **First positive review** (~hour 1): *They liked it. They actually liked it.*
3. **First trophy** (~end of in-game year 1): *We won something. It says my studio's name on it.*
4. **First Game of the Year** (~year 3+): *That's a real legacy. That's the hall of fame.*
5. **Endgame milestone hit**: *We made it. We got there.*

This system is where **Pillar 4 ("A milestone, not an ending")** lives. There's no failure state — even bad games ship and add to the Gallery. But the *good* games are the ones the player will remember.

## Detailed Design

### Core Rules

#### 2.1.1 Ship Lifecycle

```
Production → Finished → ShipReady → Released → ReleaseEnded
                            │            │            │
                            │            │            └── 60 in-game days after ship
                            │            └── Daily revenue accrues; reviews finalize
                            └── Player opens ShipModal, picks Marketing campaign, clicks "Ship It"
```

1. Project hits 100% on all three pillars → Phase = `Finished`
2. A `ShipReady` notification fires; the project is queued for the Ship Modal
3. Player opens the Ship Modal (auto-prompt or via a desk interaction):
   - Reviews the project summary (title, genres, team, pillar points)
   - Selects a **marketing campaign** tier (None / Small / Medium / Large)
   - Pays the campaign cost up front
   - Clicks **Ship It**
4. System computes `ReviewScore`, `BaselineSales`, `PeakPlayers`; opens the **review-reveal animation**
5. `ShippedGame` record written to `Gallery`
6. Project enters `Released` state — a 60 in-game-day release window begins; daily revenue accrues
7. After 60 days → `ReleaseEnded`; the game stops generating revenue and is now a permanent Gallery record only

#### 2.1.2 Review Score Computation

Computed once at ship time. Scale: 0–100. Formalized in §Formulas as **F-RS**.

Inputs (final at ship):
- Pillar points (Design, Sound, Graphics) — already 1–1000 each
- Whether each pillar was sacrificed (0% time)
- Total `ProducerBoost` accrued during production (cleans up bugs)
- Whether the project was in Crunch
- Genre revenue/effort multipliers (some genres are inherently riskier)

Conceptual breakdown (full formula in §Formulas):
- **Base**: average of the three pillar points, normalized to 0–100
- **Sacrificed pillar penalty**: each sacrificed pillar drops the base by a flat amount (~15 points)
- **Crunch penalty**: −5 to −10 if the project shipped while in crunch
- **Producer cleaning bonus**: small uplift (up to +5) for high Producer Boost
- **Synergy bonus**: small uplift (~+3) for top-synergy genre combos

Output: clamped 0–100, written to `ShippedGame.ReviewScore`.

#### 2.1.3 Baseline Sales Computation

Computed at ship. The `TotalSales` figure is the *target* for the 60-day release window. Formalized as **F-BS**.

Conceptual breakdown:
- **Quality factor**: `ReviewScore²/100` — quadratic on review (a 90/100 game outsells a 60/100 game by ~2.25×)
- **Genre revenue multiplier**: product of per-tag `RevenueMultiplier` from `GameGenres` (range 0.8 → ~8.7× for max stacks)
- **Marketing multiplier**: from the campaign tier (see 2.1.4)
- **Year scaling**: late-game sales are larger (audience grows with studio reputation) — `1 + (year - 2026) × 0.20`
- **Base unit**: a tunable constant (suggested $50,000) representing what a 50-review baseline-genre solo-developer game earns

#### 2.1.4 Marketing Campaigns

The Ship Modal offers four campaign tiers. The chosen tier is a one-time cost paid up front, and its effect amplifies `TotalSales`.

| Tier | Cost | Max Effect | With No MarketingAgent |
|---|---|---|---|
| None | $0 | × 1.00 | × 1.00 |
| Small | $5,000 | × 1.25 | × 1.125 |
| Medium | $20,000 | × 1.50 | × 1.25 |
| Large | $80,000 | × 2.00 | × 1.50 |

The "Max Effect" applies when the studio has at least one **MarketingAgent** with high Creativity stat (campaign quality scales with `Creativity.Average` of all assigned MarketingAgents). Without any MarketingAgent, campaigns are half as effective. Formalized as **F-MK**.

This is how MarketingAgent earns their 1.5× salary premium — without them, the player still gets a partial campaign effect, but the full uplift requires the hire.

#### 2.1.5 Daily Revenue Accrual (Release Window)

Each in-game day during the 60-day release window, a fraction of `TotalSales` is added to `GameManager.Money`:

```
DailyRevenue(t) = TotalSales × (1/τ) × e^(-t/τ)
   where τ = 14 in-game days; t = 1 .. 60
```

Front-loaded curve — the launch spike is in the first 2 weeks, decaying exponentially. Formalized as **F-DR** in §Formulas with worked examples.

Player-facing: a Gallery card shows the running total + a small "active sales" indicator while in the release window.

#### 2.1.6 Year-End Award Ceremony

Fires on **Day 1 of every in-game year** (i.e., when `Month` rolls back to 1 with a year increment). Scope: every game shipped in the **previous in-game year** (Year - 1). Skipped silently if zero games shipped that year.

**Ceremony flow**:
1. Calendar tick advances: Year increments
2. `GameManager.OnYearEnd` event (new) fires (subscribers: Gallery, Achievements)
3. Step 5 system gathers all `ShippedGame` records where `ShipYear == prevYear`
4. For each `TrophyDivision`, picks a winner per the division's rule
5. Ceremony modal opens — curtain pull, trophy reveal, citation flavour text
6. Each award writes a `Trophy` record via `Gallery.AwardTrophy`
7. Achievements fires for relevant unlocks (first GotY, etc.)

**Per-division winner-pick rules** (formalized as **F-AW** in §Formulas):

| Division | Rule |
|---|---|
| Game of the Year | Highest `ReviewScore`; tie → highest `TotalSales` |
| Best Design | Highest `DesignPoints` |
| Best Sound | Highest `SoundPoints` |
| Best Visual Design | Highest `GraphicsPoints` |
| Best Indie | Highest `ReviewScore` among games shipped by a studio of ≤ 5 staff at ship time |
| Best Newcomer | Highest `ReviewScore` from a studio's **first** game in that division (one-time eligibility per studio) |
| Critics' Choice | Highest score on a "depth" metric: `ReviewScore × (Genres.Count / 5)` — rewards ambitious tag stacks |

Skipped divisions (no eligible game) emit no Trophy. Citations are templated per (division, score-band) — see §Visual/Audio Requirements for the flavour-text bank.

#### 2.1.7 Year-3 Run Score & Soft Endgame

There is **no clear endgame**. Instead, every 3 in-game years (year 4, 7, 10, ...) the game offers the player a **Run Score**: a composite number summarizing how the run went so far. The player can either **Continue** (keep playing the current run; the next Run Score event fires at Year+3) or **Restart** (reset the per-run state to a fresh 2026, but carry over genre and speed-tier unlocks). Both options are first-class — a player can play one infinite freeform run, or churn fast 3-year runs chasing a higher score.

**Trigger**: on Day 1 of Year 4 (calendar `Year` rolls from 2028 to 2029, etc.), the `RunScoreManager` checks if a score event is due. Same logic at Year 7, 10, 13, etc. Cleanly mod-3 from the start year.

**Score components** (formula F-RUN in §Formulas):
- **Trophies** — division-weighted count across all year-end ceremonies this run
- **Revenue** — log-scaled lifetime revenue (rewards orders-of-magnitude jumps)
- **Game count** — number of titles shipped (capped at 30, anti-shovelware)
- **Quality** — avg ReviewScore × game count (rewards quality + volume jointly)

**Run Score Modal flow**:
1. Calendar advances to Day 1 Year+3
2. `RunScoreManager.OnYearEnd` (chained off existing year-end event) detects the boundary
3. Modal opens showing score breakdown, comparison to player's best previous run, and the studio's **Hall of Fame** snapshot
4. Player chooses Continue or Restart
5. If **Continue**: modal closes, game proceeds, `_nextScoreYear` advances by +3
6. If **Restart**: confirmation prompt → meta-progression carryover persists → per-run state reset to fresh studio (Year 2026, $1,000, 1 desk, founder only); previous run's score added to Hall of Fame

**Pillar 4 alignment** (revised): "A run with no fixed end" — the player sets when to cash out. Pillar 4 in `game-concept.md` was originally "A milestone, not an ending"; with this redesign it becomes "A scored run with optional restart and meta-progression". *(Flagged for game-concept.md update — see Open Questions.)*

#### 2.1.8 Meta-Progression Carryover

A **separate persistence layer** from the per-run save. Survives Restart; does not affect Continue.

**Persistent fields** (`meta-progression.json`):
- `unlocked_genres : Set<GameGenre>` — genres the player has unlocked across any run
- `unlocked_speed_tiers : Set<GameSpeedTier>` — Fast / VeryFast / Insane unlocks
- `best_run_score : int` — highest single Run Score event observed
- `lifetime_score_total : int` — sum of all Run Score events ever (Hall of Fame metric)
- `total_runs : int` — count of completed runs (incremented on Restart)
- `hall_of_fame : List<HallOfFameRun>` — score, date, headline trophies, best game title; capped at 20 entries

**What does NOT carry over** (deliberately):
- Money, employees, projects, current achievements, founder XP/level/abilities, current trophies
- Job posting tiers, special hire kinds (achievement-gated; achievements re-earn each run)
- Desk unlocks (paid for with money — runs need their own grind)
- Relationships, alumni queue, gift cooldowns

**Unlock check integration** — modify `IsUnlocked` for genres and speed tiers:
```
GenreInfo.IsUnlocked(genre) =
  (genre.RequiredAchievement is null)
  || Achievements.IsUnlocked(genre.RequiredAchievement)         ← per-run achievement
  || MetaProgression.HasUnlockedGenre(genre)                    ← carryover

(same pattern for SpeedTier)
```

**MetaProgression API** (proposed singleton):
- `HasUnlockedGenre(genre) : bool`
- `HasUnlockedSpeedTier(tier) : bool`
- `RecordGenreUnlock(genre)` — called whenever a genre's achievement gates fire
- `RecordSpeedTierUnlock(tier)` — same for speed tiers
- `RecordRunScore(score, breakdown)` — called by RunScoreManager on every score event
- `RecordRestart()` — called when player picks Restart; increments total_runs, archives current run to hall_of_fame
- `Reset()` — debug-only; wipes the meta-progression file

**Save semantics**: `meta-progression.json` is written on every restart and on every Run Score event. Per-run save (`save.json` or equivalent) is written separately and is wiped on Restart.

**Design Rationale**: minimal carryover preserves the "fresh studio" feel of every restart while removing the most punishing chunk of the early-game grind (genre unlocks happen only at specific achievement gates and would otherwise need re-earning). Speed tier carryover specifically addresses the pacing pain — without it, every restart forces a return to 1× speed for the early game, which would feel terrible after spending hours at 4× or 8×.

### States and Transitions

| State | Owned by | Transitions |
|---|---|---|
| `Setup` | GameProject | → `Production` (BeginProduction) |
| `Production` | GameProject | → `Finished` (all pillars at 100%) |
| `Finished` | GameProject | → `ShipReady` (notification + modal eligible) |
| `ShipReady` | (new) | → `Released` (player clicks Ship It) · → `Cancelled` (player abandons — currently undesigned) |
| `Released` | (new) | → `ReleaseEnded` (60 in-game days after ship) |
| `ReleaseEnded` | (new) | terminal |

| Ceremony state | Transitions |
|---|---|
| `Idle` | → `Pending` (Year increment with eligible games) |
| `Pending` | → `Showing` (player opens ceremony modal) |
| `Showing` | → `Idle` (player closes modal — all Trophies awarded) |

| Run-score state | Transitions |
|---|---|
| `Idle` | → `Pending` (Day 1 of Year n where (n - start_year) mod 3 == 0 and n > start_year) |
| `Pending` | → `Showing` (player opens Run Score modal) |
| `Showing` | → `Idle` (player picks Continue) · → `Restarting` (player picks Restart) |
| `Restarting` | → `Idle` (after meta-progression write + per-run state wipe; new run begins) |

### Interactions with Other Systems

**Reads from**:
- `GameDev` (`GameProject.DesignPoints` / `SoundPoints` / `GraphicsPoints`, `Phase`, `Genres`, `Title`, `InCrunch` flag, total Producer Boost across the project's lifetime)
- `Employees` (`HRManager.Staff` for MarketingAgent count + Creativity stats; `Staff.Count` for Best Indie eligibility)
- `GameManager` (`Year`, `Month`, calendar event for ceremony trigger)
- `Achievements` (read milestone target setting)
- `GameGenres` (per-tag `RevenueMultiplier`, `EffortMultiplier`)
- `Relationships` (read who was assigned to the project — for `RecordShippedTogether`)

**Writes to**:
- `GameManager.AddMoney(dailyRevenue)` per release-window tick
- `Gallery.RecordShippedGame(record)` once at ship time
- `Gallery.AwardTrophy(trophy)` per division at ceremony time
- `Achievements.RecordShippedGame()`, `RecordTrophy(division)`, `RecordRevenue(total)`, `RecordMilestone(target)`
- `Relationships.RecordShippedTogether(idA, idB)` for every assigned pair
- `Notifications.Push(...)` for ship, release-ended, ceremony, milestone

**Public API surface (proposed `ShipManager` singleton component)**:
- `ShipReadyProjects : IReadOnlyList<GameProject>` — projects in Finished/ShipReady waiting on player
- `OpenShipModal(project)` — UI hook
- `ConfirmShip(project, marketingTier)` — performs the ship (compute review, baseline sales, write Gallery record, start release window)
- `CancelShipReady(project)` — abandon a Finished project (undesigned — see Open Questions)
- `OnYearEnd()` — event handler; runs ceremony

**Public API surface (proposed `RunScoreManager` singleton component)**:
- `OnYearEnd()` — chained off the same calendar event; checks if score event due
- `OpenScoreModal()` — UI hook
- `ConfirmContinue()` — close modal, advance `_nextScoreYear`
- `ConfirmRestart()` — write meta-progression, wipe per-run state, reset to fresh studio

**Public API surface (proposed `MetaProgression` singleton component)**:
- `HasUnlockedGenre(genre) : bool` — meta-aware unlock check
- `HasUnlockedSpeedTier(tier) : bool`
- `RecordGenreUnlock(genre)` — called when achievement gate fires this run
- `RecordSpeedTierUnlock(tier)` — same for speed tiers
- `RecordRunScore(score, breakdown)` — called by RunScoreManager on every score event
- `RecordRestart()` — called on Restart; archives current run to hall of fame
- `Reset()` — debug-only; wipes meta-progression file

## Formulas

### F-RS — Review Score

```
ReviewScore =
  base
  - sacrificed_penalty
  - crunch_penalty
  + producer_bonus
  + synergy_bonus

where:
  base                = (DesignPoints + SoundPoints + GraphicsPoints) / 30   ← 0–100 (avg of 1–1000 / 10)
  sacrificed_penalty  = 15 × count(pillars where time = 0)                    ← 0, 15, 30, or 45
  crunch_penalty      = InCrunch ? 10 : 0
  producer_bonus      = clamp(producer_boost_avg × 16, 0, 5)                   ← max +5 at 0.30 boost
  synergy_bonus       = clamp(TotalSynergy × 4, 0, 3)                          ← max +3 at synergy 0.75+

ReviewScore = clamp(round(...), 0, 100)
```

**Variables**:

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| `DesignPoints` | DP | int | 1–1000 | Final Design pillar points (from GameDev) |
| `SoundPoints` | SP | int | 1–1000 | Final Sound pillar points |
| `GraphicsPoints` | GP | int | 1–1000 | Final Graphics pillar points |
| `producer_boost_avg` | PB | float | 0–0.30 | Average Producer Boost during production |
| `InCrunch` | C | bool | — | Was the project in crunch at ship time? |
| `TotalSynergy` | S | float | 0–~1.5 | Sum of pair synergies across genre tags |

**Output Range**: 0–100 under normal play.
**Examples**:
- "Solid mid-tier game" — pillars 500/500/500, no sacrifice, no crunch, normal producer 0.10, no synergy: `50 - 0 - 0 + 1.6 + 0 ≈ 52`
- "Polished indie" — pillars 800/800/600, no sacrifice, no crunch, 0.20 producer, +0.5 synergy: `73 + 3.2 + 2 ≈ 78`
- "Sacrificed sound shovelware" — pillars 600/0/600, sound sacrificed, no crunch, 0.05 producer, no synergy: `40 - 15 + 0.8 = 25.8`
- "Crunched legendary" — pillars 950/950/950, no sacrifice, crunch, 0.25 producer, +0.7 synergy: `95 - 10 + 4 + 2.8 ≈ 92`

### F-BS — Baseline Sales

```
TotalSales = round(
  BASE_SALE_UNIT × (ReviewScore² / 100) × TotalRevenueMult × MarketingMult × YearScale
)

where:
  BASE_SALE_UNIT      = 50_000           ← tunable; what a "ReviewScore=50, baseline genre, year 0" earns ≈ $12,500
  TotalRevenueMult    = product of per-tag RevenueMultiplier across project.Genres
  MarketingMult       = see F-MK
  YearScale           = 1 + (gm.Year − 2026) × 0.20
```

**Output Range**: ~$0 to $150M+ at extreme stacks (top reviews × max-revenue tag stack × Large marketing × late-game year scale). Tune `BASE_SALE_UNIT` down if max stacks land too high.

**Examples**:
- "Solid mid game, review 52, single tag (Fishing 1.0×), no marketing, year 2026": 50,000 × 27.04 × 1.0 × 1.0 × 1.0 = **$1.35M**
- "Polished indie, review 78, 3 tags (3D × FPS × Multiplayer = 2.73), Medium marketing w/ MarketingAgent (1.50×), year 2028": 50,000 × 60.84 × 2.73 × 1.50 × 1.40 = **$17.4M**
- "Crunched legendary, review 92, 5 tags max revenue stack (~8.7×), Large marketing (×2.00), year 2031": 50,000 × 84.6 × 8.7 × 2.00 × 2.00 = **$147M**

### F-MK — Marketing Multiplier

```
MarketingMult = 1 + (max_effect × campaign_quality)

where:
  max_effect       = 0.00 / 0.25 / 0.50 / 1.00   (None / Small / Medium / Large)
  campaign_quality = 0.5 + 0.5 × clamp(MA_creativity_avg / 1000, 0, 1)
                     ↑ baseline 0.5 (no MarketingAgent), up to 1.0 (Legendary MarketingAgent)

  MA_creativity_avg = average Creativity.Average across all MarketingAgent staff (or 0 if none)
```

**Output Range**: 1.0 (None) to 2.0 (Large + full MarketingAgent quality).

**Examples**:
- No MarketingAgent, Large: `1 + 1.00 × 0.5 = 1.50` (half effect)
- Senior MarketingAgent (Creativity ~600), Large: `1 + 1.00 × (0.5 + 0.5 × 0.6) = 1.80`
- Two senior MarketingAgents (Creativity avg ~700), Large: `1 + 1.00 × (0.5 + 0.5 × 0.7) = 1.85`
- Legendary MarketingAgent (Creativity 1000), Large: `1 + 1.00 × 1.0 = 2.00`

### F-DR — Daily Revenue Accrual

```
DailyRevenue(t) = TotalSales × (1/τ) × e^(-t/τ)
   where τ = 14 in-game days; t = 1 .. 60

cumulative_share(t) = 1 − e^(-t/τ)
```

**Output Range**: per-day revenue over a 60-day window, summing to ~98.6% of `TotalSales` (the remaining 1.4% is the "tail" dropped at ReleaseEnded — acceptable).

**Cumulative share table** (τ = 14):

| Day | Cumulative % | Day | Cumulative % |
|---|---|---|---|
| 1 | 6.9% | 30 | 88.6% |
| 7 | 39.4% | 45 | 96.0% |
| 14 | 63.2% | 60 | 98.6% |
| 21 | 77.7% | (cap) | 100% |

So roughly **40% by week 1**, **63% by week 2**, **78% by week 3**, **89% by month 1**, **96% by month 1.5**, **99% by month 2**. Front-loaded launch spike, fast tail.

**Per-tick implementation**: in `OnUpdate`, daily tick checks each `Released` game's day-since-ship. Computes that day's slice and calls `GameManager.AddMoney(slice)`.

### F-PP — Peak Players

```
PeakPlayers = round(TotalSales / AVG_PURCHASE_PRICE)
   where AVG_PURCHASE_PRICE = 25  ← tunable; suggests a $25 game
```

Simple lazy formula — peak players is a function of copies sold. Refine in a future pass with retention modeling. Stored on `ShippedGame.PeakPlayers` for Gallery display.

### F-AW — Year-End Award Selection

For each `TrophyDivision`, scan `ShippedGame[]` shipped in the previous year. Apply the division rule (see §Detailed Design 2.1.6) to pick a winner.

**Tie-breaking is uniform**: same primary metric → highest `ReviewScore` → highest `TotalSales` → earliest `ShipMonth`.

**Best Newcomer special rule**: a studio is "first-time-eligible" the first year it has any shipped games at all. After that year, no more Best Newcomer eligibility. (Tracked via a `_studiosFirstYearAwarded` set on the ShipManager; persists per-run only — restart re-enables Best Newcomer.)

**Best Indie eligibility**: `studio_size_at_ship ≤ 5`. Stored on `ShippedGame` at ship time as `StaffCountAtShip` so the year-end check doesn't have to recompute.

### F-RUN — Run Score (Year-3 composite)

```
RunScore = trophy_score + revenue_score + games_score + quality_score

where:
  trophy_score  = Σ trophy_weight[t.Division]      ← over every Trophy this run
                  GotY = 10
                  BestDesign / BestSound / BestVisual = 5 each
                  BestIndie = 4
                  CriticsChoice = 4
                  BestNewcomer = 3

  revenue_score = floor(log10(max(1, lifetime_revenue / 1000)) × 20)
                  $1k → 0
                  $10k → 20
                  $100k → 40
                  $1M → 60
                  $10M → 80
                  $100M → 100

  games_score   = min(games_shipped × 2, 60)        ← cap at 30 games

  quality_score = floor(avg_review_score × games_shipped / 10)
                  ↑ rewards quality AND volume jointly
```

**Variables**:

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| `trophy_score` | TS | int | 0–~80 | Weighted sum of all Trophy records this run |
| `lifetime_revenue` | LR | long | 0–unbounded | Σ TotalSales of all ShippedGame this run |
| `revenue_score` | RS | int | 0–~120 | Log-scaled revenue; OOM jumps matter, marginal cents don't |
| `games_shipped` | GS | int | 0–unbounded | Count of ShippedGame this run |
| `games_score` | GSC | int | 0–60 | Capped count; anti-shovelware |
| `avg_review_score` | AR | float | 0–100 | Mean of ReviewScore across this run's ShippedGame; 0 if none shipped |
| `quality_score` | QS | int | 0–~3000 | Quality × volume; rewards consistency over many games |

**Output Range**: typical year-3 first run ~100–250; strong year-3 run ~300–500; multi-cycle runs (Year 6/9/12...) accumulate to 600–2000+. No upper bound.

**Examples**:
- *Slow first run* — 1 GotY, $5M lifetime, 4 games at avg review 65: `10 + 60 + 8 + 26 = 104`
- *Strong year-3 run* — 1 GotY + 2 BestIndie + Critics' Choice, $30M, 8 games at avg 78: `(10 + 4 + 4 + 4) + 80 + 16 + 62 = 184`
- *Marathon to year 9* — 3 GotY + 5 division wins + 2 Critics' Choice, $150M, 25 games at avg 80: `(30 + 25 + 8) + 100 + 50 + 200 = 413`
- *Shovelware* — 0 trophies, $200k lifetime, 30 games at avg review 25: `0 + 28 + 60 + 75 = 163`
  ↑ note: shovelware caps but doesn't bottom out; quality matters more for top scores

## Edge Cases

### Handled by Design

- ✅ **Zero games shipped in a year**: Year-End Ceremony silently no-ops; no modal opens
- ✅ **Project canceled in Setup**: never reaches ShipReady — no edge case
- ✅ **Marketing campaign unaffordable**: ship-it button is gated if `Money < cost`; "None" tier always free
- ✅ **ReviewScore clamping**: explicit clamp 0–100 in F-RS prevents over/underflow
- ✅ **All pillars sacrificed**: impossible — `CanAdvancePage` requires at least one pillar with nonzero time
- ✅ **Two pillars sacrificed**: 30-point penalty applied; legal but harsh
- ✅ **Zero MarketingAgents**: campaign quality drops to 0.5 baseline; campaign still works at half effect
- ✅ **Tied awards (multiple metrics tie)**: deterministic tiebreak chain (primary metric → ReviewScore → TotalSales → earliest ShipMonth)
- ✅ **Best Newcomer one-time-per-studio**: persistent `_studiosFirstYearAwarded` set; multiple shipping years don't re-qualify
- ✅ **Best Indie boundary check**: `StaffCountAtShip` captured at ship time, not recomputed at ceremony
- ✅ **Studio with one shipped game in the year**: GotY / Best Design / etc. still award (any non-empty cohort gets a winner); only Best Newcomer has eligibility gates
- ✅ **TotalSales overflow**: `long` ranges to ~9 quintillion — plenty of headroom

### Not Yet Handled — Need Design

- ⚠️ **Player cancels ShipReady (chooses not to ship a Finished project)**: undesigned. Does the project sit in limbo? Auto-ship after N in-game days? Get scrapped? See Open Questions.
- ⚠️ **Multiple ShipReady projects queued simultaneously**: undesigned (can the player run multiple projects in parallel?). Currently `GameProjectManager.Current` only holds ONE project — this whole flow assumes serial development. If parallel projects are ever added, ShipReady queue needs sorting.
- ⚠️ **Save/load mid-release-window**: must persist per-Released-game `day_since_ship`. **Required for save system.**
- ⚠️ **Save/load mid-ceremony**: snap back to `Pending` state on load; player re-opens the ceremony modal.
- ⚠️ **Save/load mid-Ship-Modal**: project stays in ShipReady; modal can re-open.
- ⚠️ **Save/load with milestone already triggered**: `MilestoneAchieved` flag must persist; don't re-fire celebration.
- ⚠️ **Day 1 ceremony with active release window**: a game that shipped in late December is still in its release window when January arrives — eligible for Year N awards (it shipped in Year N − 1). Confirm this is the desired behavior.

### Ambiguous — Need Decision (see Open Questions)

- ❓ **Legendary milestone threshold**: `≥ 95` for clean boundary (recommended) or `> 95`?
- ❓ **Studio name rename**: if mutable, do past trophies show old or new name? Recommend: always current.
- ❓ **End-of-year ship attribution**: a game shipped on Dec 30 Year N → eligible for Year N+1 ceremony (judging Year N). Recommend: count by `ShipYear`.
- ❓ **`ReviewScore = 0` game**: ships anyway (character — even bad games on the trophy wall). Recommend: keep.

## Dependencies

**Hard dependencies** (this system cannot function without):

| System | Interface | Notes |
|---|---|---|
| `GameDev` | Reads finished `GameProject` (pillar points, genres, in-crunch flag, producer-boost-avg) | Producer Boost is per-tick; this system needs the *time-averaged* value. Requires GameDev to track running average. |
| `GameManager` | Calendar (Year, Month, Day), `OnYearEnd` event (new), `AddMoney()` | New event; emit on Day 1 with year increment. Both ShipManager.ceremony + RunScoreManager subscribe. |
| `Gallery` | `RecordShippedGame()`, `AwardTrophy()`, `ResetProgress()` | Already exists; record shapes already match. `ResetProgress()` will be called on Restart. |
| `Achievements` | `RecordShippedGame()`, `RecordTrophy(division)`, `RecordRevenue(total)` | Per-run achievements; reset on Restart. |
| `MetaProgression` (new) | `HasUnlockedGenre()`, `HasUnlockedSpeedTier()`, `RecordRunScore()`, `RecordRestart()` | New singleton; persisted separately from per-run save. |
| `Notifications` | `Push()` | Already exists. |

**Soft dependencies** (enhance behavior; system functions without):

| System | Interface | Notes |
|---|---|---|
| `Employees` | MarketingAgent count + Creativity stats | Without any MarketingAgent, marketing campaigns work at 0.5× quality. |
| `Relationships` | `RecordShippedTogether(idA, idB)` | Without it, alumni-cementing-by-shared-projects mechanic doesn't fire. |
| `Save System` | Per-run save: ShipManager state (release windows, ceremony state). Meta save: MetaProgression. Two distinct files. | Without it, mid-window crashes lose progress. **MVP-blocker for Step 5+ to ship.** |

**ShippedGame record extensions needed** (new fields on the existing record):
- `StaffCountAtShip : int` — for Best Indie eligibility check at ceremony
- `MarketingTier : MarketingTier` — for Gallery display ("Marketed: Large")
- `MarketingMult : float` — for analytics later
- `DayShippedInYear : int` — for tiebreak by earliest ShipMonth (currently only `ShipMonth` precision)

**ShipManager state needed** (new singleton, persisted in per-run save):
- `_releaseWindows : List<ReleaseWindow>` (each with `ShippedGame` ref + `daySinceShip`)
- `_studiosFirstYearAwarded : HashSet<StudioId>` (Best Newcomer tracking — single-studio for now, but data-shaped for multi-studio later)
- `_pendingCeremonyYear : int?` (set on year boundary; cleared when player views ceremony)

**RunScoreManager state needed** (new singleton, persisted in per-run save):
- `_runStartYear : int` (which year did this run start? defaults 2026; restart-aware)
- `_nextScoreYear : int` (when does the next score event fire? defaults `_runStartYear + 3`)
- `_pendingScoreEvent : bool` (waiting for player to open the modal)

**MetaProgression state needed** (new singleton, persisted in `meta-progression.json` — separate from per-run save):
- `unlocked_genres : Set<GameGenre>`
- `unlocked_speed_tiers : Set<GameSpeedTier>`
- `best_run_score : int`
- `lifetime_score_total : int`
- `total_runs : int`
- `hall_of_fame : List<HallOfFameRun>` (capped at 20 entries)

## Tuning Knobs

| Knob | Default | Safe range | Breaks if too high | Breaks if too low |
|---|---|---|---|---|
| `BASE_SALE_UNIT` | $50,000 | $20K – $200K | Late-game revenues overflow long; balance trivial | Early games unprofitable; player can't afford anything |
| `MARKETING_COST.Small` | $5,000 | $2K – $20K | Marketing inaccessible early | Trivial cost, no gating |
| `MARKETING_COST.Medium` | $20,000 | $10K – $50K | Accessible mid-game | Trivial |
| `MARKETING_COST.Large` | $80,000 | $40K – $200K | Late-game-only | Easy to spam |
| `MARKETING_EFFECT.Small` | +0.25 | +0.10 – +0.40 | Dominates strategy | Negligible |
| `MARKETING_EFFECT.Medium` | +0.50 | +0.30 – +0.80 | Same | Same |
| `MARKETING_EFFECT.Large` | +1.00 | +0.70 – +1.50 | Same | Same |
| `MARKETING_BASELINE` | 0.5 | 0.3 – 0.7 | MarketingAgent useless | MarketingAgent over-required |
| `RELEASE_WINDOW_DAYS` | 60 | 30 – 90 | Sales linger; player attention drifts | Sales finish too fast; missed |
| `RELEASE_DECAY_TAU` | 14 | 7 – 21 | Long tail; sales never feel "done" | Sales spike-and-die immediately |
| `BASE_AVG_PURCHASE_PRICE` | $25 | $15 – $50 | Low player counts | Inflated player counts |
| `YEAR_SCALE_PER_YEAR` | 0.20 | 0.10 – 0.30 | Late-game runaway revenue | Late-game stagnation |
| `SACRIFICED_PILLAR_PENALTY` | −15 | −10 to −25 | Sacrifice never viable | Sacrifice always viable (degenerate) |
| `CRUNCH_REVIEW_PENALTY` | −10 | −5 to −15 | Crunch never worth it | Crunch always worth it |
| `PRODUCER_REVIEW_BONUS_CAP` | +5 | +3 to +10 | Producer dominates | Producer trivial |
| `SYNERGY_REVIEW_BONUS_CAP` | +3 | +1 to +5 | Synergy dominates review | Synergy doesn't matter |
| `BEST_INDIE_STAFF_CAP` | 5 | 3 – 8 | "Indie" loses meaning | Few games qualify |
| `RUN_SCORE_CYCLE_YEARS` | 3 | 2 – 5 | Restarts feel forced | Score events feel rare; loop loose |
| `TROPHY_WEIGHT.GotY` | 10 | 8 – 15 | Single GotY dominates score | Trivial GotY value |
| `TROPHY_WEIGHT.BestDesign / Sound / Visual` | 5 | 4 – 7 | Per-pillar sweep dominates | Trivial |
| `TROPHY_WEIGHT.BestIndie` | 4 | 3 – 6 | Indie sweep dominates | Trivial |
| `TROPHY_WEIGHT.CriticsChoice` | 4 | 3 – 6 | Same | Same |
| `TROPHY_WEIGHT.BestNewcomer` | 3 | 2 – 5 | One-time too valuable | Trivial |
| `REVENUE_LOG_SCALAR` | 20 | 12 – 30 | Revenue dominates score | Revenue ignored |
| `GAMES_SHIPPED_CAP` | 30 | 20 – 50 | Shovelware floor too easy | Anti-shovelware too aggressive |
| `QUALITY_DIVISOR` | 10 | 5 – 20 | Quality dominates score | Quality ignored |
| `HALL_OF_FAME_CAP` | 20 | 10 – 50 | UI cluttered | Recent runs lost |

## Visual/Audio Requirements

This system carries the most cinematic moments in the game.

**Ship Modal**:
- Project summary card (title, genres, team, pillar bars at 100%)
- Marketing tier picker (4 options with cost/effect summary, locked state if unaffordable)
- "Ship It" CTA — large, prominent
- Background: subtle starfield / shipping-rocket motif (TBD Art Direction)

**Review-Reveal Animation** (5–10 seconds):
- After Ship It clicked, modal transitions into review reveal
- Reviews scroll in 3-second intervals — ~3 short snippets
- Snippets templated by (pillar emphasis, score band, genre flavour)
- Score number counts up from 0 to final ReviewScore (1–2s ease-out)
- Sales ticker starts immediately (visible on Gallery card)

**Year-End Ceremony Modal** (15–30 seconds total):
- Curtain-pull intro animation
- Per-division reveal (curtain-up, trophy icon zoom-in, division name + winner card)
- Citation flavour text appears below
- Optional: applause / fanfare audio sting per division
- "Continue" button after all divisions revealed

**Run Score Modal** (NEW — heart of the new endgame):
- Header: "Year [N+3]: Time to Take Stock"
- Composite score number — large, prominent, count-up animation
- Score breakdown card (trophy points, revenue points, games points, quality points)
- Comparison to player's best previous run + best lifetime score
- Hall of Fame strip (top 5 entries)
- Two CTAs: **Continue this run** (subtle) vs **Restart for a new run** (prominent, distinct color)
- Restart confirmation: "Reset to fresh studio? You'll keep unlocked genres and speed tiers."

**Hall of Fame view** (in Gallery, new tab):
- List of all past Run Score events
- Per-row: score, year ended, total trophies, top game's title
- Lifetime score totalizer at top
- Total runs counter

**Citation flavour text bank** (per division × score band):
- Per `TrophyDivision` × score band (90+ / 80–89 / 70–79 / <70):
  - GotY 90+: "A landmark of the medium." / "Will be studied for years to come."
  - GotY 80–89: "The strongest game of the year by a clear margin." / "Years from now, players will remember this."
  - BestSound 90+: "An aural masterclass." / "Reset the bar for what game audio can do."
  - BestIndie: "Punching far above its weight class." / "Proof that scope and talent are independent variables."
  - …(approx 30–50 lines total — Open Question to delegate to `writer` agent)

**Audio**:
- Ship It button click sound
- Review-reveal "ticker" sound per review snippet
- Score-reveal swell
- Trophy reveal sting (per-division pitch variation)
- Run Score reveal: longer, more triumphant musical phrase
- Restart confirmation: thoughtful, ceremonial

> 📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:step-5-payoff` to produce per-asset visual descriptions, dimensions, and generation prompts from this section.

## UI Requirements

**Ship Modal** (new): full-screen modal at Finished → ShipReady transition; mutex with other modals.

**Review-Reveal Modal**: chained from Ship Modal click; auto-advances; player can click to skip.

**Ceremony Modal** (new): full-screen modal triggered by `OnYearEnd`; mutex with other modals; player can dismiss.

**Run Score Modal** (new): full-screen modal triggered by `RunScoreManager` on Year+3 boundary; mutex with other modals; cannot be dismissed without choosing Continue or Restart.

**Restart Confirmation**: small modal layered on top of Run Score Modal when Restart is clicked. Shows what will be lost (current studio state) vs kept (unlocked genres + speed tiers). "Confirm Restart" / "Cancel".

**Gallery Card Updates**: existing Past Games view extended to show:
- Active release-window indicator + running revenue ticker
- ReviewScore
- Marketing tier used
- Genre tags
- Trophy badges (when won)

**Hall of Fame tab** (new in Gallery): scrollable list of past run scores, lifetime totalizer, total run counter.

**HUD**: small "active release window" indicator near the money display (e.g., "💰 + $X today from N games selling"). Possibly a subtle Year+3 countdown for upcoming Run Score events.

> 📌 **UX Flag — Step 5 Payoff**: This system has multiple complex modals (Ship, Review-Reveal, Ceremony, Run Score, Restart Confirmation, Hall of Fame tab). In Phase 4 (Pre-Production), run `/ux-design` to create UX specs for each modal **before** writing epics. Stories that reference these modals should cite `design/ux/[modal-name].md`, not this GDD directly.

## Acceptance Criteria

Given-When-Then format. One per core rule and per formula.

**Ship Lifecycle**:
- **GIVEN** a project in `Production` state with all 3 pillars at 100%, **WHEN** the next tick runs, **THEN** project transitions to `ShipReady` and a notification fires
- **GIVEN** a project in `ShipReady`, **WHEN** the player clicks "Ship It" from the Ship Modal with marketing tier `Medium`, **THEN** $20,000 is deducted, ReviewScore + TotalSales + PeakPlayers are computed, a `ShippedGame` record is written to `Gallery`, and the project enters `Released` state
- **GIVEN** a `Released` project at day 60, **WHEN** the next daily tick runs, **THEN** the project transitions to `ReleaseEnded` and stops generating revenue

**Review Score (F-RS)**:
- **GIVEN** a project with pillars 800/800/600, no sacrifice, no crunch, producer_boost_avg 0.20, TotalSynergy 0.5, **WHEN** ship is triggered, **THEN** ReviewScore = clamp(73 + 3.2 + 2, 0, 100) ≈ 78
- **GIVEN** a project with pillars 950/950/950, no sacrifice, in crunch, producer_boost_avg 0.25, TotalSynergy 0.7, **WHEN** ship is triggered, **THEN** ReviewScore = clamp(95 − 10 + 4 + 2.8, 0, 100) ≈ 92
- **GIVEN** a project with pillars 100/100/100, two sacrificed, in crunch, no producer, **WHEN** ship is triggered, **THEN** ReviewScore is clamped to 0

**Baseline Sales (F-BS)**:
- **GIVEN** ReviewScore 50, single tag (Fishing 1.0×), no marketing, year 2026, **WHEN** ship is triggered, **THEN** TotalSales = $1.25M
- **GIVEN** ReviewScore 90, max tag stack, Large marketing, year 2030, **WHEN** ship is triggered, **THEN** TotalSales matches F-BS calculation within ±$10k

**Marketing Multiplier (F-MK)**:
- **GIVEN** no MarketingAgent on staff, Large campaign chosen, **WHEN** ship is triggered, **THEN** MarketingMult = 1.50
- **GIVEN** Senior MarketingAgent (Creativity 800), Medium campaign, **WHEN** ship is triggered, **THEN** MarketingMult ≈ 1.45

**Daily Revenue (F-DR)**:
- **GIVEN** a `Released` game with TotalSales = $1.0M and 7 days since ship, **WHEN** the daily tick runs, **THEN** DailyRevenue ≈ $35,000
- **GIVEN** a `Released` game at day 60, **WHEN** daily tick runs, **THEN** cumulative revenue paid is between 98% and 100% of TotalSales

**Year-End Ceremony**:
- **GIVEN** zero games shipped in Year N, **WHEN** Year increments to N+1, **THEN** no ceremony modal opens
- **GIVEN** 1+ games shipped in Year N, **WHEN** Year increments to N+1, **THEN** ceremony modal opens with one Trophy per applicable division
- **GIVEN** a studio of size 6 ships a high-review game in Year N (no other games), **WHEN** ceremony fires, **THEN** Best Indie has no winner (skipped)
- **GIVEN** a studio's first year of any shipped games, **WHEN** ceremony fires, **THEN** the studio is recorded in `_studiosFirstYearAwarded` and Best Newcomer is awarded
- **GIVEN** a studio with prior Best Newcomer trophy, **WHEN** ceremony fires for a later year, **THEN** Best Newcomer is skipped

**Run Score Event (F-RUN)**:
- **GIVEN** a fresh run started in 2026, **WHEN** Year increments to 2029 (Day 1 Year 4), **THEN** Run Score Modal opens with composite score from F-RUN and `_nextScoreYear` is set to 2032
- **GIVEN** a Run Score Modal open, **WHEN** player clicks Continue, **THEN** modal closes; per-run state preserved; next score event scheduled for Year+3
- **GIVEN** Run Score Modal open, **WHEN** player clicks Restart and confirms, **THEN** meta-progression is updated, current run added to hall_of_fame, per-run state wiped, fresh studio created at year 2026, unlocked genres and speed tiers retained

**Run Score Computation**:
- **GIVEN** a run with 1 GotY, $5M revenue, 4 games shipped at avg review 65, **WHEN** F-RUN computes, **THEN** RunScore = 10 + 60 + 8 + 26 = 104
- **GIVEN** a run with 0 trophies, $200k revenue, 30 games at avg review 25, **WHEN** F-RUN computes, **THEN** RunScore = 0 + 28 + 60 + 75 = 163

**Meta-Progression Carryover**:
- **GIVEN** a run with `Soccer` genre unlocked via the FirstHire achievement, **WHEN** the player Restarts, **THEN** `Soccer` is in `MetaProgression.unlocked_genres` and is selectable from the genre picker on the new run's first project (without re-earning FirstHire)
- **GIVEN** a run with `Fast` and `VeryFast` speed tiers unlocked, **WHEN** the player Restarts, **THEN** both tiers are accessible from the speed selector immediately, while `Insane` remains locked
- **GIVEN** a Restart event, **WHEN** the new run begins, **THEN** Money = $1,000, Staff = empty, Achievements = empty, Money/desk/posting state are all fresh

**Save/Load**:
- **GIVEN** mid-release-window state (Released game at day 30), **WHEN** save and reload, **THEN** day count, accumulated revenue, ShippedGame record all preserved
- **GIVEN** Pending ceremony state, **WHEN** save and reload, **THEN** ceremony modal can re-open with same content
- **GIVEN** Pending Run Score state, **WHEN** save and reload, **THEN** Run Score Modal re-opens
- **GIVEN** meta-progression state (genres unlocked, hall of fame), **WHEN** the player closes and reopens the game, **THEN** all meta-progression survives

## Open Questions

1. **`game-concept.md` updates needed** (separate task):
   - Pillar 4 reframe from "A milestone, not an ending" → "A scored run with optional restart and meta-progression"
   - Anti-pillar "NOT a roguelike" softened or removed (this IS soft-roguelike)
   - MVP Definition: replace "endgame milestone target" → "Year-3 Run Score event + meta-progression carryover working"

2. **Cancel ShipReady flow**: what if the player chooses NOT to ship a Finished project? Auto-ship after N days? Get scrapped? Stays in limbo? **Recommend**: scrap after 30 in-game days with a notification ("[Title] is no longer relevant — the moment passed").

3. **Multiple ShipReady projects**: design assumes serial development. If parallel projects are added, ShipReady queue needs sorting.

4. **Citation flavour text bank**: ~30–50 lines needed for trophies. Delegate to `writer` agent in a future session via `/writer` or `/team-narrative`.

5. **Marketing campaign without ShipReady project**: should there be a "Marketing-only" tier to build hype between projects? **Recommend**: campaigns only at ship time (simpler).

6. **Studio size at-ship lock**: confirm `StaffCountAtShip` captures founder + employees (founder + 4 employees = 5 → qualifies for Best Indie).

7. **Year scaling beyond Year 5**: at year 2031, YearScale = 2.0×; at year 2036, 3.0×; etc. Late-game inflation. Confirm or cap at year 5.

8. **Run Score event during a multi-year continued run**: a player who Continues at Year 4 hits another at Year 7, then 10... should the score reset between events (just-this-cycle) or accumulate (lifetime)? **Recommend**: each event reports composite score from the *entire run so far*, but only the highest single score is tracked in `best_run_score`. Open to alternatives.

9. **Score weights**: trophy weights, revenue scalar, games cap, quality divisor — all numerical knobs in §Tuning Knobs. Run a simulation pass at MVP to confirm score ranges feel right (typical year-3 first run ≈ 100–200; great runs ≈ 300–500).

10. **Future carryover expansion**: only genres + speed tiers carry today. Future questions:
    - Should special-hire kinds (Mentor, Marketing, Remote, Intern) carry?
    - Should the Founder's level/abilities partially carry (e.g., starting level 2)?
    - Should the GenreExpansionAchievement unlock state carry?
    - Should at-ship staff count thresholds for past Best Indie wins matter?

11. **Restart at Year 3 with no shipped games**: if a player reaches Year 3 having shipped zero games, F-RUN gives ~0 score. Should the modal still fire? **Recommend**: yes (the Restart option is still useful). Show "Score: 0 — try again with a different approach".

12. **Achievements per-run vs persistent**: confirm that the achievement system stays per-run (achievements wipe on Restart). Persistent achievement state would interact strangely with carryover unlocks. **Recommend**: per-run achievements; persistent unlocks live in `MetaProgression`.

13. **Hall of Fame run identity**: should hall-of-fame entries include details like which specific games shipped, or just the score + year? **Recommend**: top game's title + score + total trophies. Compact, evocative.

14. **Cross-run founder identity**: is the founder the same character across runs (renaming permitted), or a fresh founder each run? Recommend: fresh per run, but allow the player to set a name once (carries across runs as a "studio personality" flag).
