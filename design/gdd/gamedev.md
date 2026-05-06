---
status: reverse-documented
source: Code/GameDev/
date: 2026-04-27
verified-by: Kaito Tagawa
---

# GameDev — Design Document

**Status**: Reverse-Documented
**Source**: `Code/GameDev/` (5 files, ~1,200 LOC)
**Date**: 2026-04-27
**Verified By**: Kaito Tagawa
**Implementation Status**: Partially implemented — the 3-page setup flow and core pillar production tick are wired and working. The 2026-04-29 production-loop rework swapped the variable-duration TimeCost curve for a fixed `ProjectDuration` + per-pillar effort cap (§2.1.4); Crunch penalty is retired (§2.1.15). Producer role, GameDevAbility multipliers, Programming stat routing, and the entire post-Finished payoff loop ("Step 5+") are still missing-but-required for MVP.

> **⚠️ Reverse-Documentation Notice**
>
> This GDD captures the GameDev system as it exists today and the design
> intent clarified during the 2026-04-27 reverse-documentation pass. Where
> sections describe mechanics not yet in code (Producer cleaning, ability
> wiring, Programming routing, Crunch, ship payoff), they are explicitly
> marked ❌ MVP-required.

---

## 1. Overview

**Purpose**: GameDev is the system that takes the studio's people and produces
shipped games. It owns the project lifecycle (setup → production → finished),
the role-and-pillar mapping that turns employee stats into game-quality
points, the genre system that flavors each project, and the time-allocation
curve that paces development.

If Employees is *who* is in the studio, GameDev is *what they do all day*.

**Pillar Alignment**:
- **Pillar 2 — Every Project Is an Authored Mix**: this is where Pillar 2
  lives. Genre tags + role assignments + time allocations = a unique cocktail
  per project. The combinatorial design space is the gameplay.
- **Pillar 4 — A Milestone, Not an Ending**: shipped games accumulate; their
  total drives the milestone-target endgame.

**Scope**:
- ✅ In scope: 3-page setup flow, role-based pillar production, time-allocation
  curve, genre catalogue + synergy, multi-role efficiency, PM and SuperHacker
  boosts, sacrificeable pillars, GameDevAbility multipliers, Producer role
  effect, Programming stat wiring, Crunch penalty
- ❌ Out of scope (Step 5+, separate GDD): ship → revenue → reviews → GOTY
  payoff, marketing campaigns, post-launch patches, sequels
- ❌ Out of scope: training stat-growth (lives in Employees system once it's built)
- ❌ Out of scope: applicant generation (Employees system)

**Current Implementation**: 5 files cleanly split — `GameProject` is the
mutable per-project DTO, `GameProjectManager` is the singleton orchestrator,
`GameDevRole` defines the 5 roles plus a 25-entry ability multiplier table,
`GameGenre` defines the 17-tag catalogue plus synergy table, `IDevWorker`
unifies hires and the founder under one assignment-grid contract.

The setup → production → finished loop runs end-to-end. Four flagship
mechanics promised by the data model are not yet wired into the runtime.

**Design Intent** (clarified 2026-04-27):
- **Producer = in-production cleaner**: not a deferred Step-5+ feature.
  Producers actively improve quality during production, not just at ship time.
- **GameDevAbilities = rare-and-earned**: most hires won't have one. Legendary
  is a true wow-moment, achievable only via extensive training + shipped
  proof. Natural-roll Legendary is impossible.
- **Programming stat is live now**: Programmer hires must have observable
  effect during production. Recommended path: route Programming through
  Producer (closes both gaps with one wire).
- **Fixed pacing, capped quality** (reworked 2026-04-29): every project ships
  in `ProjectDuration` (120s default) regardless of effort. The per-pillar
  effort slider is hard-capped by team strength ÷ genre complexity. Pacing is
  no longer about "how long does this take" — it's about "how good can we
  make it given our team and the genre's complexity." See §2.1.4.
- **Sacrificeable pillars are a feature, not a bug**: 0% time = pre-completed
  pillar. The player explicitly chooses to ship without sound, or without
  graphics, etc. Cheap shovelware is a viable strategy.

---

## 2. Detailed Design

### 2.1 Core Mechanics

#### 2.1.1 Project Lifecycle

**Phases** (`GameProjectPhase`):
- **Setup**: player is moving through the three setup pages. Production
  hasn't started; cancel is allowed.
- **Production**: per-frame tick advances pillar progress and points until
  every pillar hits 100%.
- **Finished**: phase flips, notification fires. ❌ The post-Finished payoff
  ("Step 5+") is missing entirely.

**Implementation**: `GameProjectManager.Current` holds the in-flight project.
`StartNewProject()` creates a fresh `GameProject` and opens the modal.
`BeginProduction()` flips to Production. `TickProduction()` runs every frame
while in Production. The Finished phase is a dead-end today.

**Player-Facing**: a "Create Game" tile in the menu opens the setup modal.
After the player commits, the project ticks down at game-speed × time
multiplier; the player can leave the modal and watch the office while the
project runs.

**Open question**: cancellation of a running project is a TODO. `CancelSetup()`
only handles Setup-phase abandonment. A confirmation flow for in-progress
cancellation is needed.

---

#### 2.1.2 Three-Page Setup Flow

**Page 1 — Title + Genre**:
- `Title` field (free text; required, non-whitespace)
- 1–N genre tag picker, capped by `GenreSlots`
- `GenreSlots = 2` early-game; lifts to `5` when `GenreExpansionAchievement` fires
- Tags below their unlock requirement are visible but un-clickable
- **CanAdvance**: title non-empty AND `Genres.Count > 0`

**Page 2 — Worker Allocation**:
- 5 role columns; same worker can be slotted into multiple
- Each extra role halves their effective contribution (see §2.1.6)
- Founder (`PlayerStats`) and hires (`EmployeeNPC`) appear together via `IDevWorker`
- **CanAdvance**: at least one role has at least one assignment (`HasAnyAssignment`)
- The system *allows* leaving roles unstaffed — pillars can still progress
  via secondary contributions (see §2.1.5)

**Page 3 — Effort Allocation** (reworked 2026-04-29 — shared budget):
- Three sliders: `DesignTime`, `SoundTime`, `GraphicsTime` ∈ [0, 1]
- 0 on a pillar = sacrifice it (pre-completes; ships empty; 0 points).
- The three sliders share a **single budget pool** (`TotalEffortBudget`):
  `DesignTime + SoundTime + GraphicsTime ≤ TotalEffortBudget`. Decreasing one
  slider frees that headroom for the other two. There is no separate
  per-pillar cap; the per-slider maximum is just `Budget − sum of the other
  two`.
- Effort is a *score multiplier*, not a time multiplier — pushing harder
  raises pillar points, not the dev schedule.
- **CanAdvance**: at least one pillar has nonzero effort (`HasAnyTimeAllocated`)

**Implementation**: `GameProjectManager.SetupPage` 0/1/2; `NextPage()` /
`PreviousPage()` gated by `CanAdvancePage`. Setters `SetTitle`, `TryAddGenre`,
`ToggleGenre`, `Assign`, `ToggleAssign`, `SetDesignTime`, etc.

---

#### 2.1.3 Roles and Pillars

**5 Roles** (`GameDevRole`):

| Role | Pillar / Effect | Stat Used | Status |
|---|---|---|---|
| ProjectManager | Throughput boost (PM Boost, +50% cap) | `Focus.Average` | ✅ Wired |
| Producer | **In-production cleaner** (Producer Boost, +30% cap) | **`Programming.Average` (proposed)** | ❌ MVP-required |
| GameDirector | Design pillar | `Design.Average` | ✅ Wired |
| SoundDirector | Sound pillar | `Sound.Average` | ✅ Wired |
| ArtDirector | Graphics pillar | `Artistry.Average` | ✅ Wired |

**Multi-role assignment**: a single worker can sit in multiple role columns
on a project. Each extra role halves their effective contribution
(see §2.1.6 for the formula).

**Primary vs secondary contribution** (`PillarContribution`):
- Workers in the pillar's primary role contribute at **1.0× weight**
- Workers assigned anywhere else on the project contribute at **0.3× weight**
- Each worker counted once even if covering multiple roles

This means a project missing its primary director still progresses — a
Producer or PM with the right stat can carry a pillar, just slower. **Pillars
never freeze** as long as anyone is on the project.

---

#### 2.1.4 Fixed Duration + Shared Effort Budget (reworked 2026-04-29; budget model 2026-04-29 v2)

**Duration**: every project ships in `ProjectDuration = 120s` real seconds at
1× game speed (scaled by `GameManager.TimeMultiplier`). All three pillars'
progress fills linearly to 1.0 over that window. The TimeCost curve is gone;
weak teams no longer take longer to ship.

**Total effort budget** (single shared pool):

```
TotalTeamPower      = PillarContribution(Design)
                    + PillarContribution(Sound)
                    + PillarContribution(Graphics)

TotalEffortBudget   = clamp( TotalTeamPower / (EffortBaseline × EffortMultiplier), 0..1 )

DesignTime + SoundTime + GraphicsTime ≤ TotalEffortBudget
```

- `EffortBaseline = 1000` — total team strength required to unlock the full
  100% budget on a 1.0× genre.
- `EffortMultiplier` — product of every selected genre's `EffortMultiplier`
  (heavy or stacked genres squeeze the budget downward).
- `PillarContribution` uses the existing 1.0× primary / 0.3× secondary
  weighting (§2.1.3). Workers in a pillar's primary role contribute their
  full stat; workers in any other role still contribute at 0.3× — they
  *help* every pillar but specifically *boost* the pillar of their primary
  role assignment.

**Per-pillar slider ceiling**: `Budget − sum of the OTHER two pillars`,
clamped to [0, 1]. Pulling Design down by 5% lifts both Sound's and
Graphics's max by 5% each. There are no separate per-pillar caps.

**Worked examples** (synergy = 0, no PM/SuperHacker boost):

| Team layout | TotalTeamPower | Genre Effort× | TotalEffortBudget |
|---|---|---|---|
| Founder solo (Flat-30, assigned to GameDirector) | ~30 | 1.0× | 3% |
| Founder + 1 hire (one in GameDirector, one in SoundDirector, both ~200 stat) | ~400 | 1.0× | 40% |
| 3-person team, every primary role staffed (~300 each) | ~900 | 1.0× | 90% |
| Same 3-person team, 3-genre stack | ~900 | ~2.0× | 45% |
| 5-person strong team (~1500 total) | ~1500 | 1.0× | 100% (capped) |
| Same 5-person team, 5-genre stack | ~1500 | ~3.0× | 50% |

**Score per pillar** (unchanged shape):

```
target = clamp( effort × PillarContribution(pillar) × quality, 0..1000 )
```

A Sound Director on the team raises Sound's `PillarContribution` significantly
(1.0× weight on their Sound stat), so each unit of budget spent on Sound
returns more points than the same budget spent on an under-staffed pillar.
Spending budget where the team is strong is the primary strategic lever.

**Design Rationale**: the old model paid for over-ambition with longer
real-time waits (the 0.6 → 1.0 premium tail). The first 2026-04-29 rework
swapped that for per-pillar caps, but those caps were so small for early
teams (1–3% each) that the +/− buttons couldn't visibly move the slider
inside the cap. Sharing one bigger budget across the three sliders means the
player always has a meaningful number to play with, and decreasing one
slider produces an immediate, legible response on the other two — the
"hand-on-the-wheel" feel the cap-per-pillar model was lacking.

---

#### 2.1.5 Production Tick — Pillar Advancement (reworked 2026-04-29)

**Per-frame, per-pillar** (`AdvancePillar`):

1. Skip if pillar effort = 0 (sacrificed) or already at 1.0 (finished)
2. `progressDelta = dt / ProjectDuration` — uniform across all pillars
3. `progress = min(1.0, current + progressDelta)`
4. Compute `teamStrength = PillarContribution(role, statSelector)` (see §2.1.7)
5. Compute score multipliers shared across pillars:
   - `pmBoost`     ∈ [0, 0.5]   (PM Focus team — see §2.1.7)
   - `hackerBoost` = 0.10 × count(unassigned w/ SuperHacker)
   - `synergy`    = 1 + Σ pair-synergy(genres) × 0.05
   - `quality`    = (1 + pmBoost) × (1 + hackerBoost) × synergy
6. `target = min(1000, effort × teamStrength × quality)`
7. `points = round(progress × target)`
8. After all 3 pillars tick, if `IsProductionComplete`: phase → Finished, notification

**dt scaling**: `dt = Time.Delta × TimeMultiplier` — speed tiers (1×/2×/4×/8×)
multiply through, so the wall-clock duration shortens with calendar speed.

**PM and SuperHacker repurposing**: prior to the 2026-04-29 rework these
boosted production *throughput* (less wall-clock time). With duration fixed,
they boost the score *target* instead — same gameplay levers, different
output channel. Producer Boost (§2.1.12, planned) follows the same pattern.

---

#### 2.1.6 Multi-Role Efficiency

**Rule**: an employee in N roles on the same project contributes at
`1 / 2^(N-1)` of their stat per role:

| Roles | Efficiency | Effective stat per role |
|---|---|---|
| 1 | 100% | full |
| 2 | 50% | half |
| 3 | 25% | quarter |
| 4 | 12.5% | eighth |
| 5 | 6.25% | tiny |

**Implementation**: `GameProject.EfficiencyForRoleCount(roleCount)` —
`1f / (1 << (roleCount - 1))`. Computed on-demand from `RoleCountFor(worker)`,
which scans the assignment dictionary.

**Design Rationale**: discourages "stack one Legendary on every role"
strategies. Specialization wins. The `Polymath` employee ability cancels
this penalty entirely — it's the explicit "everyone-wears-hats" hire.

---

#### 2.1.7 Pillar Contribution

For a given pillar's `primaryRole` and `pickStat` selector:

```
total = 0
seen = ∅
for each (role, workers) in project.Assignments:
  for each worker in workers:
    if worker in seen: continue   ← counted once even across multiple roles
    seen.add(worker)

    roleCount = project.RoleCountFor(worker)
    eff       = EfficiencyForRoleCount(roleCount)        ← see §2.1.6
    morale    = (worker is EmployeeNPC) ? worker.Morale : 1.0   ← founder always 1.0
    weight    = primaryList.Contains(worker) ? 1.0 : 0.3 ← primary vs secondary

    contribution = pickStat(worker) × eff × morale × weight
    contribution × = AbilityMultiplier(worker, primaryRole)   ← proposed §2.1.13

    total += contribution

return total
```

The `AbilityMultiplier` step is the proposed wiring of the 25-entry
`GameDevAbility` table — see §2.1.13.

---

#### 2.1.8 PM Boost

**Description**: a strong Project Manager lifts the team's throughput on
every pillar.

**Formula**: `pmBoost = clamp(PM_team_stat / 2000, 0, 0.5)`
- Where `PM_team_stat` is `TeamStat(ProjectManager, w => w.Stats.Focus.Average)`
- A solo founder PM at flat-30 Focus contributes ~30 → 0.015 boost (negligible)
- A senior PM with ~700 Focus contributes ~700 → 0.35 boost
- A team of 2× senior PMs caps out at 0.5 boost (+50% throughput)

**Implementation**: `ComputePmBoost()`. Applied as `× (1 + pmBoost)` to speed.

**Design Rationale**: PMs don't move pillars themselves — they multiply
everyone else's output. A great PM is the difference between a 4-min
project and a 6-min project on the same team.

---

#### 2.1.9 SuperHacker Boost

**Description**: each *unassigned* employee with the `SuperHacker` ability
adds a flat 10% to every pillar's progress speed.

**Formula**: `hackerBoost = 0.10 × count(unassigned staff w/ SuperHacker)`

**Implementation**: `ComputeHackerBoost()`. Iterates `Current.UnassignedStaff`
(staff and founder not on any role) for the SuperHacker ability.

**Design Rationale**: the lone-wolf "shipping at 3 AM" archetype.
SuperHackers are *worse* when assigned (they want to be left alone) and
*better* when benched. Tension: assign them for predictable output, or
bench them for hidden bonus?

---

#### 2.1.10 Synergy

**Description**: each unordered pair of genre tags contributes a small
production speed (and points) multiplier.

**Formula**: `synergy = 1 + TotalSynergy(genres) × 0.05`
- `TotalSynergy` sums `Synergy(a, b)` across all unordered pairs
- All listed pairs have positive synergy (no negative synergy / clashes)
- Top stacks (e.g., Multiplayer + MOBA + FPS): TotalSynergy ≈ 0.7 → +0.035
- The 5% scalar is the design constant — small but compounds with high-tag projects

**Synergy table highlights**:
- Mobile + Idle: +0.30
- Mobile + Gacha: +0.30
- Multiplayer + MOBA: +0.30
- Multiplayer + Soccer: +0.30
- Story + SinglePlayer: +0.20
- 2D + Roguelike: +0.20
- 3D + FPS: +0.20

**Design Rationale**: rewards thematically coherent tag stacks. A "2D Action
Roguelike Single-Player Story" project gets multiple natural-fit bonuses;
a "3D Mobile Gacha Story" gets none. The synergy scalar (5%) is small
enough that bad combinations are still viable.

---

#### 2.1.11 Sacrificeable Pillars

**Description**: setting any pillar's time allocation to 0 in setup-page-3
causes that pillar to **pre-complete** when production begins:
- `progress = 1.0` (so `IsProductionComplete` doesn't gate on it)
- `points = 0` (the pillar ships with quality 0)

**Implementation**: `BeginProduction()` checks each `*Time <= 0f` and pre-completes.

**Design Rationale**: cheap shovelware should be a viable strategy. If the
player wants to ship 10 silent games to chase Mobile + Idle revenue
synergies, they can. The points-0 on a sacrificed pillar will hurt at ship
time (when reviews exist), so this is a real tradeoff — but it's *the
player's tradeoff to make*.

---

#### 2.1.12 Producer — In-Production Cleaning (PROPOSED)

**Description**: Producers improve the **quality (points)** of every pillar
during production without affecting **progress (time)**. They're "cleaning
up rough edges" while the project runs.

**Per user clarification (2026-04-27)**: Producer is NOT a Step-5+ deferred
feature. Producer's stat must visibly affect production now.

**Proposed mechanic**:
```
producerBoost = clamp(Producer_team_stat / 2000, 0, 0.30)

# In TickProduction.AdvancePillar:
points = round(progress × min(1000, teamStat × synergy × (1 + producerBoost)))
```

**Recommended stat**: `Programming.Average` (closes both Programming and
Producer gaps with one wire — see §2.1.14 Option C).

**Cap**: 30% — meaningful but not OP. Stops the strategy of "1 Legendary
Producer = ship anything good." A real Producer Boost requires a strong
team in the role (ideally multiple senior programmers).

**Design Rationale**: a Producer with low stats does little. A team of
strong Producers raises the points ceiling on every pillar by up to +30%.
This makes Producer assignments meaningful without changing the project's
ship time. Players can prioritize *speed* (PM Boost) or *quality*
(Producer Boost) or both.

**Why points and not progress?** Progress is when the game ships; points
are how good it is. A Producer doesn't make a project ship faster —
they make it ship *cleaner*. Wiring Producer to progress would make them
a second PM; wiring them to points keeps the design distinct.

---

#### 2.1.13 GameDevAbility Multipliers (PROPOSED WIRING + ACQUISITION)

**The data**: `GameDevAbilities.All` is a 25-entry table — 5 tiers × 5 roles
— with multipliers from 1.05× (Novice) to 1.80× (Legendary), each with
themed flavor text ("Time Itself", "The Next Miyamoto", "Heir of Mozart",
"Descendant of Van Gogh").

**Today**: defined but unused. `TickProduction` and `PillarContribution`
never consult this table.

**Proposed wiring**: when a worker has a `GameDevAbility` whose `Role`
matches the role they're currently assigned to, the multiplier applies to
their pillar contribution:

```
contribution × = (worker has matching-role GameDevAbility)
                  ? GameDevAbilities.MultiplierFor(ability.Tier)
                  : 1.0
```

**Wrong-role abilities do nothing**: a Legendary `GameDirector` ability does
not boost a pillar when the holder is slotted as `SoundDirector`. This is
the explicit specialization tradeoff.

**Acquisition** (per user clarification 2026-04-27 — abilities should be rare,
especially Legendary):

**A) Natural roll on hire** — weighted by tier:

| Hire tier | Novice | Skilled | Renowned | Worldly | Legendary |
|---|---|---|---|---|---|
| Junior | 5% | 0 | 0 | 0 | 0 |
| Mid | 20% | 5% | 0 | 0 | 0 |
| Senior | 30% | 15% | 5% | 0 | 0 |
| Returning intern | (use Senior) | | | | |

Legendary is **never** rolled at hire time. Natural rolls cap at Renowned
even for top-tier hires. The role of the rolled ability is the worker's
primary `EmployeeRole` (mapped to its game-dev role: see role table below).

**Role mapping**:
| EmployeeRole | GameDevRole |
|---|---|
| Programmer | Producer (per Programming-as-Producer-stat — §2.1.14) |
| Designer | GameDirector |
| Creative | GameDirector |
| Artist | ArtDirector |
| SoundDesigner | SoundDirector |
| Researcher | ProjectManager |

**B) Earned via training** — once the training system exists (see Employees
GDD §2.1.6):

| Tier | Training hours required | Additional gate |
|---|---|---|
| Novice | 50 | none |
| Skilled | 150 | shipped at least 1 game |
| Renowned | 400 | shipped 5 games where they held the role |
| Worldly | 1,000 | shipped a game with all-pillar quality ≥ 700 |
| Legendary | 2,500 | shipped a Legendary-tier game (review/quality ≥ 900) where they held the role |

(Numbers are placeholders — calibrate during the training-system balance pass.)

**Design Rationale**: Legendary abilities should be the moments players
remember. "I hired a Senior with Renowned" is *exciting*; "I trained Alex
to Legendary over 30 in-game years and he carried our last 4 GOTY winners"
is the long-form payoff. The two-step gate (hours + shipped proof) ensures
Legendary tier requires real investment.

---

#### 2.1.14 Programming Stat Wiring (PROPOSED — 3 OPTIONS)

**Today**: Programming has three sub-stats (CodeQuality, BugResistance,
ImplementationSpeed), Programmer hires pay a 1.10× salary premium for them,
and **no pillar uses Programming**.

**Per user clarification (2026-04-27)**: Programming should affect production
now, not be deferred to Step 5+.

**Option A — 4th Pillar "Code"**:
- Add `GameDevRole.TechnicalDirector` (or LeadEngineer)
- Add `CodeTime`, `CodeProgress`, `CodePoints` to `GameProject`
- Add 4th slider on Page 3
- Pillar uses `Programming.Average`
- **Cost**: significant UI rework, balance redesign of TimeCost curve, genre
  effort multipliers may need rebalancing
- **Pro**: most thematically honest — Programmers ship code, Code is a pillar
- **Con**: scope blast — touches every level of the system

**Option B — Hidden Global Multiplier**:
- `programmingBoost = clamp(programming_team_stat / 4000, 0, 0.25)`
- Applied to every pillar's progress: `× (1 + programmingBoost)`
- Programmers become an unseen force — "the team needs them but their work
  doesn't have a name on the credits"
- **Pro**: no UI change, minimal risk
- **Con**: dilutes the role-pillar mapping; programmers are a "stat tax" rather than a creative discipline

**Option C — Producer's Stat (RECOMMENDED)**:
- Producer uses `Programming.Average` instead of `Focus.Average`
- Programmer hires become natural Producer candidates (the "QA lead" archetype)
- Producer Boost (§2.1.12) is the visible effect
- **Pro**: closes both gaps with one wire; minimal new code; thematically coherent
  ("Producers fix bugs" + "Programmers know code" → "Programmer-Producers fix bugs")
- **Con**: GameDirector-flavored Programmers (someone with high Programming
  but high Design) lose their alternative role. Counter: that's what
  multi-role + Polymath is for.

**Recommendation**: Option C for MVP. Re-evaluate post-launch if there's
demand for a 4th pillar.

---

#### 2.1.15 Crunch Penalty (RETIRED 2026-04-29)

**Status**: Retired by the fixed-duration / effort-cap rework (§2.1.4).

**What it used to do**: when a project's predicted dev time crossed
`MaxProjectSeconds = 600s`, the project was flagged `InCrunch` and assigned
EmployeeNPCs lost morale per in-game day until ship.

**Why it's gone**: with `ProjectDuration` fixed at 120s, there is no
"over-cap" condition to detect. The pacing pressure that crunch enforced
now lives directly in the effort cap (§2.1.4 G1) — under-staffed or
over-ambitious projects can't push past their cap, so they ship a worse
game rather than a slower one. Morale still matters because it feeds
`PillarContribution` (and therefore the cap), but it is no longer drained
*by* the production loop itself.

**If a separate "studio overwork" morale system is still wanted** (e.g.,
running multiple back-to-back projects without breathing room), it should
be defined independently of the per-project loop — see Open Questions.

---

### 2.2 Rules and Formulas

| # | Formula | Expression | Source |
|---|---|---|---|
| G1 | Total effort budget | `clamp((PillarContribution(D)+PillarContribution(S)+PillarContribution(G)) / (EffortBaseline × EffortMultiplier), 0, 1)` | ✅ Implemented (2026-04-29 v2) |
| G1b | Per-slider max | `clamp(Budget − sum of the OTHER two pillars, 0, 1)` | ✅ Implemented (2026-04-29 v2) |
| G2 | Pillar progress delta | `dt / ProjectDuration` (uniform across all 3 pillars) | ✅ Implemented (2026-04-29) |
| G3 | Pillar progress | `min(1, current + delta)` | ✅ Implemented |
| G4 | Multi-role efficiency | `1 / 2^(N-1)` | ✅ Implemented |
| G5 | Pillar contribution | `Σ pickStat(w) × eff × morale × weight` (1.0 primary / 0.3 secondary) | ✅ Implemented |
| G6 | Quality multiplier | `(1 + pmBoost) × (1 + hackerBoost) × synergy × (1 + producerBoost)` | ⚠️ producerBoost missing |
| G7 | PM Boost | `clamp(PM_focus_team / 2000, 0, 0.5)` | ✅ Implemented |
| G8 | Hacker Boost | `0.10 × count(unassigned w/ SuperHacker)` | ✅ Implemented |
| G9 | Synergy | `1 + TotalSynergy(genres) × 0.05` | ✅ Implemented |
| G10 | Pillar score target | `min(1000, effort × teamStrength × quality)` | ✅ Implemented (2026-04-29) |
| G11 | Pillar points (live) | `round(progress × target)` | ✅ Implemented |
| G12 | **Ability multiplier** (NEW) | `MultiplierFor(tier)` if matching-role ability, else 1.0 — folds into `quality` | ❌ Not implemented |
| G13 | **Producer Boost** (NEW) | `clamp(Producer_programming_team / 2000, 0, 0.30)` — folds into `quality` | ❌ Not implemented |
| G14 | **Programming routing** (NEW) | Producer's `pickStat = w.Stats.Programming.Average` | ❌ Not implemented |
| G15 | **Natural ability roll** (NEW) | tier-weighted (see §2.1.13 acquisition table); Legendary impossible | ❌ Not implemented |
| G16 | **Training ability earn** (NEW) | hours threshold + shipped-proof gate (see §2.1.13) | ❌ Not implemented |

> Crunch (G16/G17 in the pre-2026-04-29 model) was retired with the fixed
> duration. The pacing constraint that crunch policed now lives in the
> effort-cap formula G1: complex / under-staffed projects can't push past
> their cap, so they ship a worse game rather than a slower one.

---

### 2.3 State and Data

**Data Structures**:

```text
GameProjectPhase (enum)
├── Setup
├── Production
└── Finished

GameProject (DTO — single mutable instance per studio)
├── Title             : string
├── Genres            : List<GameGenre>
├── Assignments       : Dictionary<GameDevRole, List<IDevWorker>>
│                       (5 keys, populated; same worker may appear in multiple lists)
├── DesignTime        : float (0..1)
├── SoundTime         : float
├── GraphicsTime      : float
├── DesignProgress    : float (0..1, monotonic)
├── SoundProgress     : float
├── GraphicsProgress  : float
├── DesignPoints      : int (1..1000)
├── SoundPoints       : int
├── GraphicsPoints    : int
├── Phase             : GameProjectPhase
└── (proposed)
    ├── InCrunch      : bool (recomputed per tick)
    └── CodeTime/Progress/Points (only if Option A from §2.1.14)

GameProjectManager (singleton Component)
├── Current           : GameProject? (null between projects)
├── SetupPage         : int (0..2, only meaningful in Setup)
├── IsOpen            : bool
├── MaxGenresEarly    : 2
├── MaxGenresLate     : 5
├── GenreExpansionAchievement : AchievementId? (currently unset)
├── ProjectDuration           : 120  (real seconds at 1× game speed)
└── EffortBaseline            : 1000 (team strength to unlock 100% effort on a 1.0× genre)

GameDevRole (enum, 5 values)
├── ProjectManager
├── Producer
├── GameDirector
├── SoundDirector
└── ArtDirector

GameDevAbility (class — 25-entry static table)
├── Role        : GameDevRole
├── Tier        : Novice/Skilled/Renowned/Worldly/Legendary
├── Name        : flavor name
├── Description : flavor text
└── Multiplier  : 1.05 / 1.15 / 1.30 / 1.50 / 1.80

GameGenre (enum, 17 values, 5 categories)
GameGenreInfo (per-genre metadata — name, category, effort×, revenue×, achievement gate)
```

**Project lifecycle state diagram**:
```
   (none)
     │
     │ StartNewProject()
     ▼
   Setup ─┬── CancelSetup() ──→ (none)
          │
          │ BeginProduction() (CanAdvancePage at page 2)
          ▼
   Production ──── all 3 pillars hit 1.0 ───→ Finished
          │                                       │
          │                                       │ ❌ Step 5+ missing:
          │                                       │ - Ship action
          │                                       │ - Revenue
          │                                       │ - Reviews
          │                                       │ - GOTY consideration
          │                                       │ - Gallery write
          │                                       │ - Project archived
          │                                       ▼
          │                              (project just sits in Finished)
          │
          ⚠️ Cancel-during-production not implemented
```

**Persistence (when save system exists)**:
- ✅ `Current` (full project state including all pillar progress/points, assignments, page index, phase)
- ✅ `IsOpen` (so the UI re-opens correctly)
- Per-employee ability slot (separate from per-NPC `Ability1/Ability2`) — needs a new data field
- Player-progression unlocks (genre achievements, GenreExpansionAchievement state) — already in Achievements

**What can be recomputed**:
- Time/cost estimates (derived from genres + allocations)
- Synergy total (derived from genres)
- `InCrunch` flag (recomputed per tick)
- `IsProductionComplete` (derived)

---

### 2.4 Integration Points

**This system reads from:**
- `Employees` (`HRManager.Staff`, `EmployeeNPC.Stats / Morale / Abilities`) — workforce
- `PlayerStats` — founder as `IDevWorker`; `IsPlayer` flag for UI
- `GameManager` — `TimeMultiplier`, calendar (for crunch tick), money (no current spend, but Step 5+ revenue lands here), notifications
- `Achievements` — `GenreExpansionAchievement` gate, per-genre unlocks
- `GameGenres.All` — static catalogue
- `GameDevAbilities.All` — static catalogue (currently unused)

**Systems that depend on this:**
- `Gallery` (planned `RecordShippedGame`) — when Step 5+ ships, the project archives here
- `Achievements` — game count, GOTY count, milestone-target progress
- `Employees` (Crunch path) — needs to drain morale on assigned workers in crunch
- `Relationships` (planned, design-only) — `RecordSameProject` and `RecordSamePillar` per in-game day; `RecordShippedTogether` on Finished
- Step-5+ payoff system (TBD GDD): consumes points + InCrunch flag + genre revenue multipliers

**Public API surface** (`GameProjectManager`):
- `StartNewProject() / CancelSetup() / SetOpen(bool)`
- `NextPage() / PreviousPage()`
- `SetTitle / TryAddGenre / RemoveGenre / ToggleGenre`
- `Assign(worker, role) / Unassign / ToggleAssign`
- `SetDesignTime / SetSoundTime / SetGraphicsTime`
- `BeginProduction()`
- Read-only: `Current`, `SetupPage`, `CanAdvancePage`, `GenreSlots`,
  `MaxDesignEffort` / `MaxSoundEffort` / `MaxGraphicsEffort` (per-slider headroom),
  `DesignTeamStrength` / `SoundTeamStrength` / `GraphicsTeamStrength`,
  `TotalTeamPower`, `TotalEffortBudget`, `TotalEffortSpent`,
  `ProjectDuration`, `EffortBaseline`

---

## 3. Edge Cases

### Handled in Code

- ✅ **Empty assignments grid**: `CanAdvancePage` requires ≥ 1 assignment before page 2 → 3
- ✅ **All-zero time allocation**: `CanAdvancePage` requires at least one nonzero pillar before commit
- ✅ **Same worker in multiple roles**: tracked via dictionary; counted once in PillarContribution; halves contribution per extra role
- ✅ **Locked genre selection**: `TryAddGenre` checks `IsUnlocked`; un-clickable in UI
- ✅ **Slot cap enforcement**: `TryAddGenre` rejects past `GenreSlots`
- ✅ **Restart-during-production**: `StartNewProject` on a Production phase project returns silently (just opens the modal on the running project)
- ✅ **Sacrificed pillars**: `BeginProduction` pre-completes 0% pillars
- ✅ **Stronger team mid-production**: `points = progress × target` formula lifts the ceiling smoothly when a worker is reassigned in
- ✅ **Negative-or-empty assignment list**: `RoleCountFor(null)` returns 0; no-op assigns
- ✅ **No PM assigned**: PM Boost = 0; pillars still progress at base speed
- ✅ **No SuperHacker on bench**: Hacker Boost = 0
- ✅ **Empty genre set**: TotalEffort = 1, TotalRevenue = 1, synergy = 1

### ⚠️ Not Handled — MVP Work

- ❌ **Producer's contribution** (§2.1.12): TickProduction does not multiply points by Producer Boost
- ❌ **GameDevAbility multipliers** (§2.1.13): contribution is not multiplied by tier multiplier
- ❌ **Programming stat wiring** (§2.1.14): Programming has no path into production
- ❌ **Crunch penalty** (§2.1.15): time cap exists but no morale drain
- ❌ **Step 5+ ship phase**: project sits in Finished forever; no revenue, no review, no archive
- ❌ **Cancel running project**: `CancelSetup` only handles Setup phase
- ❌ **Marketing system**: `MarketingAgent` employee kind exists but does nothing
- ❌ **GenreExpansionAchievement target unset**: late-game genre slot expansion (5 slots) has no defined trigger

### ❓ Ambiguous — Need Decision

- ❓ **Producer Boost cap (30%)**: confirm or override
- ❓ **Programming wiring approach**: A (4th pillar) / B (hidden mult) / C (Producer's stat — recommended)
- ❓ **Natural ability roll percentages**: 5%/20%/30% per tier feels right but is unvalidated
- ❓ **Training thresholds**: 50/150/400/1000/2500 hours — placeholder
- ❓ **Crunch morale drain rate**: −0.05/day proposed; tune via playtest
- ❓ **GenreExpansionAchievement target**: which achievement unlocks the 5-slot expansion?
- ❓ **Pillar weight (0.3) for secondary contributors**: confirm or tune
- ❓ **EffortBaseline (1000)**: confirms how steep the founder-progression curve has to be before the founder solo can ship a non-trivial game. Tune against the founder upgrade roadmap.
- ❓ **Sound and Graphics 1.0/1.0 prevents Code wiring**: if Option A (4th pillar) is later picked, splitting effort across 4 pillars instead of 3 is a major rebalance — the cap formula is structurally OK but every "good team" benchmark would shift.

---

## 4. Dependencies

**Technical Dependencies**:
- s&box `Component` lifecycle (`OnAwake`, `OnUpdate`)
- `IDevWorker` interface — must remain stable; both `EmployeeNPC` and `PlayerStats` implement it
- `Time.Delta` (s&box) for production tick scaling
- `MathF` / `MathX.Lerp` (s&box stdlib)

**Design Dependencies (other GDDs)**:
- `employees.md` — supplies `IDevWorker` (hires + founder), morale, abilities
- `relationships.md` — TickProduction must consult RelationshipManager for the Helping path; same-project / same-pillar growth fires here
- `economy.md` (TBD) — Step 5+ revenue; project costs (none currently — no per-day burn beyond salary)
- `step-5-payoff.md` (TBD) — ship → revenue → review → GOTY → Gallery
- `training.md` (TBD) — ability acquisition path B
- `marketing.md` (TBD) — MarketingAgent's effect
- `save-system.md` (TBD ADR) — project state persistence

**Content Dependencies**:
- 17 genre tag descriptions + (future) genre icons
- 25 GameDevAbility flavor entries (already authored — keep)
- Production-tick UI (pillar bars, points, ETA) — not yet detailed; HUD design needed

---

## 5. Balance and Tuning

### Tuning Knobs

| Parameter | Current | Rationale | Tune? |
|---|---|---|---|
| `MaxGenresEarly` | 2 | Forces focus early; player can't tag-stack from day 1 | ✅ ok |
| `MaxGenresLate` | 5 | Late-game chasing big synergy stacks | ✅ ok |
| `GenreExpansionAchievement` | (unset) | Needs to be picked — see Open Q | ❌ **must set** |
| `ProjectDuration` | 120s | Every project ships in 2 real-time minutes at 1× game speed (2 in-game days). Authoritative pacing knob. | ⚠️ confirm via playtest |
| `EffortBaseline` | 1000 | Total team strength required to unlock the full 100% shared budget on a 1.0× genre. Lower = easier to max; higher = team must scale up first. | ⚠️ tune against founder-progression curve |
| Multi-role base | `2^(n-1)` (×0.5/×0.25/…) | Steep — makes specialization the dominant strategy | ⚠️ may want softer (e.g., ×0.7/×0.5/×0.3) |
| Secondary-role weight | 0.3 | Workers contribute usefully even when not in pillar primary | ✅ ok |
| PM Boost cap | 0.5 | +50% throughput from a strong PM team | ⚠️ confirm vs Producer Boost (30%) |
| PM Boost denom | 2000 | 700 Focus PM with 100% efficiency = 0.35 boost | ⚠️ confirm |
| Hacker flat per | 0.10 | Each unassigned SuperHacker = +10% all pillars | ⚠️ may stack OP — confirm cap |
| Synergy scalar | 0.05 | TotalSynergy of 1.0 = +5% production | ⚠️ confirm |
| Producer Boost cap | 0.30 | Proposed (§2.1.12) | ❓ confirm |
| Producer Boost denom | 2000 | Same shape as PM Boost | ❓ confirm |
| Ability multiplier range | 1.05 – 1.80 | Already-authored table | ✅ ok |
| Natural ability roll % | 5/20/30 (jr/mid/sr) | Proposed (§2.1.13) | ❓ confirm in simulation |
| Training hours / tier | 50/150/400/1000/2500 | Placeholder | ❓ depends on training system tuning |
| Crunch morale drain | −0.05/in-game day | Proposed (§2.1.15) | ❓ confirm |
| Pillar pre-complete on 0 time | yes | Sacrificeable pillars are intentional | ✅ ok |

### Balance Concerns Identified

- ⚠️ **PM + Hacker stacking**: a team with 2 senior PMs (cap 0.5) and 3 unassigned SuperHackers (3 × 0.10 = 0.3) gets `(1 + 0.5) × (1 + 0.3) = 1.95×` speed. With ability multipliers (Legendary GameDirector 1.80×) compounding, the upper bound becomes wild. Simulation-test the maximum stack.
- ⚠️ **Producer Boost (30%) compounding with Synergy / PM Boost**: pillar points = `progress × teamStat × synergy × (1 + producerBoost)`. With a 0.3 synergy stack and 0.3 producer boost, points are 1.69× the base teamStat. Plus ability multipliers: easy 3× pillar points. Confirm this is the intended ceiling.
- ⚠️ **Multi-role efficiency curve**: `2^(n-1)` is brutal — anyone in 4 roles works at 12.5% per role, totalling 50% of single-role output. Polymath ability flatlines this. Without Polymath, multi-role stuffing is never optimal. Confirm intent.
- ⚠️ **Sacrificed pillar revenue at Step 5+**: 0 points on a pillar should hurt at ship time but doesn't hurt during production. Validate the post-Finished payoff penalizes sacrificed pillars enough to make the choice meaningful.
- ⚠️ **Default 0.6 sweet spot** with TotalEffort = 1 = 270s. With a TotalEffort of 1.4 (e.g., 3D + FPS + Multiplayer = 1.4×1.3×1.5 = 2.73), 270 × 2.73 = 737s — already over crunch cap. Player hitting "default" sliders on a 3-tag project hits crunch by accident. Confirm intent or rebalance the sweet spot.
- ⚠️ **Genre revenue stacking**: TotalRevenue is product, so 5 high-revenue tags can push to 2.0×1.6×1.5×1.3×1.4 = ~8.7×. May produce a degenerate revenue strategy at Step 5+. Validate.

### Recommended Balance Pass (post-implementation)

- Run `/balance-check` after Producer Boost + Programming routing land
- Simulate "year 5 senior team with 3 GameDevAbility holders" and confirm pillar speeds + points are within design intent
- Run a "first project" simulation with solo founder at flat-30 stats; confirm completion time is < 8 min real
- Run a "shovelware" simulation: solo founder, 2 sacrificed pillars, low-effort genre stack; confirm < 4 min completion
- Confirm crunch penalty actually deters 1.0/1.0/1.0 stacking without making the option non-viable

---

## 6. Acceptance Criteria

### What Exists (implemented)
- ✅ 3-page setup flow with full validation
- ✅ Title + genre tag selection (with achievement gates + slot cap)
- ✅ Role assignment grid (5 roles, multi-role allowed)
- ✅ Time-allocation sliders + cost-curve preview
- ✅ Sacrificeable pillars (0% time → pre-complete)
- ✅ Per-pillar production tick (Design / Sound / Graphics)
- ✅ Multi-role efficiency formula (2^-(n-1))
- ✅ Primary-vs-secondary contribution weights (1.0 / 0.3)
- ✅ PM Boost (Focus-driven, +50% cap)
- ✅ SuperHacker bench bonus (+10% per unassigned hacker)
- ✅ Synergy bonus (5% scalar)
- ✅ Genre catalogue + synergy table + effort/revenue multipliers
- ✅ TimeMultiplier integration (game speed scales production)
- ✅ Founder + hires unified via `IDevWorker`
- ✅ Phase transitions (Setup → Production → Finished)
- ✅ 25-entry GameDevAbility data table (data only — not consulted)

### ⚠️ Partially Implemented
- ⚠️ Producer role: assignable, named, has flavor text; **does nothing in code**
- ⚠️ GameDevAbility table: defined; **never multiplies anything**
- ⚠️ Programming stat: stored, salary-premiumed; **never affects production**
- ⚠️ Crunch warning: comments + UI hint planned; **no penalty**
- ⚠️ GenreExpansionAchievement field: present in inspector; **target unset**
- ⚠️ Marketing system: MarketingAgent kind exists; no marketing mechanic

### ❌ Missing — MVP Required
- ❌ **Producer Boost** (Programming-stat-driven, +30% to pillar points)
- ❌ **GameDevAbility wiring** (apply matching-role multiplier to contribution)
- ❌ **Programming routing** (per Option C: route through Producer's stat)
- ❌ **Crunch morale drain** (−0.05/in-game day per assigned worker over cap)
- ❌ **Natural ability roll** (tier-weighted percentages on hire)
- ❌ **Training-earned abilities** (depends on training system in Employees)
- ❌ **Step 5+ ship payoff phase** (revenue / reviews / GOTY / Gallery)
- ❌ **Cancel-running-project flow**
- ❌ **GenreExpansionAchievement target**: pick which achievement unlocks 5 slots

### Definition of Done

- [ ] Producer Boost wired; verified via test (assigning a strong Producer raises pillar points without changing progress speed)
- [ ] GameDevAbility multipliers applied; verified via test (matching-role ability boosts contribution; wrong-role doesn't)
- [ ] Programming stat routes through Producer (Option C); verified via test
- [ ] Crunch morale drain implemented; verified via test (project over 600s in production drops staff morale)
- [ ] Natural ability roll on hire; verified via simulation (10,000 hires produce expected distribution)
- [ ] Training-earned ability path implemented (depends on training system)
- [ ] Step 5+ payoff phase designed and implemented (separate GDD)
- [ ] Cancel-running-project flow implemented
- [ ] GenreExpansionAchievement decision made and applied
- [ ] All G1–G19 formulas have at least one balance-check test
- [ ] Save/load roundtrip preserves project state through every phase
- [ ] Playtest: a fresh player ships their first project in 4–6 min real time
- [ ] Playtest: chasing 1.0/1.0/1.0 visibly costs morale, but is doable

---

## 7. Open Questions and Follow-Up Work

### Questions Needing User Decision

1. **Programming wiring approach** (§2.1.14): pick A (4th pillar) / B (hidden mult) / **C (Producer's stat — recommended)**
2. **Producer Boost cap**: 30% suggested. Confirm or override.
3. **GameDevAbility natural-roll percentages**: 5%/20%/30% per tier. Confirm or simulate.
4. **Training thresholds for ability earn**: 50/150/400/1000/2500 hours suggested. Confirm or defer to training-system balance pass.
5. **GenreExpansionAchievement target**: which achievement unlocks 5-slot expansion? Suggested: a milestone-tier achievement like "Earn $1M" or "Ship 10 Games" — needs to fit late-game pacing.
6. **Crunch morale drain rate**: −0.05/in-game day proposed. Confirm.
7. **Cancel-running-project**: just refund time? Penalize the player (morale hit, partial salary lost)? Or let it ship as-is at current points?
8. **Step 5+ payoff scope**: ship-to-revenue is obvious; what's in MVP and what's deferred? Reviews? GOTY? Trophies? Marketing modifier? Suggest separate `/design-system step-5-payoff` once GameDev MVP wires settle.

### Flagged Follow-Up Work

- [ ] **Implement Producer Boost** wired to Programming.Average
- [ ] **Implement GameDevAbility multiplier wiring** in PillarContribution
- [ ] **Implement Crunch InCrunch flag + daily morale tick**
- [ ] **Implement natural ability roll** in `Employee.GenerateApplicant` and `GenerateSpecialApplicant`
- [ ] **Pick GenreExpansionAchievement target** (e.g., `Earn1M` or `Hire10`); set in inspector
- [ ] **Design Step 5+ payoff GDD** as the next major reverse-doc / forward-design pass
- [ ] **Add balance-check tests**: one per formula in §2.2
- [ ] **Cancel-running-project flow design + implementation**
- [ ] **Marketing GDD** when MarketingAgent's effect is designed
- [ ] **Update employees.md §2.1.10**: alumni "max abilities (2)" should be revisited — do they also get a GameDevAbility on return?

---

## 8. Version History

| Date | Author | Changes |
|---|---|---|
| 2026-04-27 | Claude (reverse-doc) | Initial reverse-documentation from `Code/GameDev/` (5 files, ~1,200 LOC) |
| 2026-04-27 | Kaito Tagawa | Clarified: Producer is in-production cleaner; GameDevAbilities are rare-and-earned (especially Legendary); Programming should affect production now (route via Producer recommended); Crunch should drop morale to enforce pacing |

---

**Next Steps**:
1. Pick Programming wiring approach (Open Q1) — blocks implementation
2. Confirm Producer Boost / Crunch numerical knobs (Open Q2, Q6)
3. Pick GenreExpansionAchievement target (Open Q5)
4. Design Step 5+ payoff phase next — that's the missing other half
5. `/balance-check` after the four MVP wires land

**Related Skills**:
- `/balance-check` — validate G1–G19 formulas once Producer/Ability/Programming/Crunch land
- `/architecture-decision` — for save-system implications and Producer routing choice
- `/design-system step-5-payoff` — author the missing payoff GDD
- `/design-system marketing` — author MarketingAgent's effect

---

*This document was generated by `/reverse-document design Code/GameDev/` on 2026-04-27 and updated with user clarifications the same day.*
