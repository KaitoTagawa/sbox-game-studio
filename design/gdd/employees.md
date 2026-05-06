---
status: reverse-documented
source: Code/Employees/
date: 2026-04-27
verified-by: Kaito Tagawa
---

# Employees — Design Document

**Status**: Reverse-Documented
**Source**: `Code/Employees/` (11 files, ~1,700 LOC)
**Date**: 2026-04-27
**Verified By**: Kaito Tagawa
**Implementation Status**: Partially implemented — core flow works; training stat-growth, severance, and special-rate rebalance are missing-but-required for MVP.

> **⚠️ Reverse-Documentation Notice**
>
> This design document was created **after** the implementation already existed.
> It captures current behavior and clarified design intent based on code analysis
> and user consultation on 2026-04-27. Sections explicitly marked with ⚠️ or ❌
> describe gaps the player will notice — they are not optional polish.

---

## 1. Overview

**Purpose**: The Employees system is the studio's HR pipeline and workforce simulation. It produces the staff that the GameDev system consumes. Without employees there is no team to assign to projects; without the HR pipeline there is no way to acquire them.

**Scope**:
- ✅ In scope: posting management, applicant generation, interview gate, hiring, desk allocation, monthly payroll, morale system, intern contracts + alumni return, NPC behavior on the office floor, special-hire kinds (Mentor / MarketingAgent / RemoteWorker / Intern)
- ❌ Out of scope: project-role assignment (lives in `GameDev/` — this system supplies `IDevWorker` instances; assignment lives there)
- ❌ Out of scope: founder mechanics (`PlayerStats` — separate system, even though founder also implements `IDevWorker`)
- ❌ Out of scope: shop furniture (`Shop.cs`) — cosmetic office population only

**Current Implementation**: 11 files cleanly split across four layers — data DTOs (`Employee`, `EmployeeStats`, `EmployeeAbility`, `EmployeeKind`, `EmployeeState`), static catalogues (`JobPosting`, `SpecialHires`), live actors (`EmployeeNPC`, `EmployeeDesk`, `WorkplaceAnchor`), and a single `HRManager` orchestrator. The hire-to-fire loop runs end-to-end. Three mechanics promised by the data model are not implemented: training stat-growth, severance/fire-morale, and a balanced special-applicant rate.

**Design Intent** (clarified):
- **Pillar 3 alignment** — "Hiring is meaningful, not noise." Every applicant has a real cost (energy + money + desk slot), every hire is a deliberate act, and the inbox cannot be batch-processed.
- **Investment-shaped specials** — Interns cost less and produce less than a regular junior, but their *future return as a senior at a discount* is a multi-month hook. The first intern is guaranteed to return so the player learns the mechanic.
- **The talent pool grows with the studio** — `progressionFactor` lifts the stat ceiling over the first 5 in-game years (2026 → 2031). The studio's reputation is mechanically modeled.
- **Morale is the only soft-fail loop** — there is no bankruptcy, no game-over. Missing payroll drops morale; sustained morale collapse triggers quitting. The player can always recover.

---

## 2. Detailed Design

### 2.1 Core Mechanics

#### 2.1.1 Job Posting

**Description**: The studio chooses one posting tier at a time. The tier sets the monthly fee charged on Day 1 and the *quality bracket* of the applicants that arrive.

**Tiers**:
| Tier | Monthly | Job Quality | Unlock |
|---|---|---|---|
| None (Off) | $0 | — (no applicants) | Always |
| Bulletin Board | $200 | 0.20 | Always |
| Career Site | $1,500 | 0.55 | Achievement: Hire 5 |
| Headhunter Agency | $6,000 | 0.90 | Achievement: Earn $100k |

**Implementation**: `HRManager.PostingTier` is a `[Property]` enum. `JobPosting.Get(tier)` returns the static metadata. `OnMonthStart` charges `PostingInfo.MonthlyCost`; if unaffordable, the posting is forcibly switched to `None` and the player is notified.

**Design Rationale**: Posting tier is the player's primary lever for *who* applies. Higher tiers are the only way to recruit late-game-quality talent at a reasonable rate, but their fees create a sustained money pressure that prevents stockpiling.

**Player-Facing**: The HR posting picker shows each tier's cost, description, and unlock state. Locked tiers are visible (so the player can see what's coming) but un-clickable.

---

#### 2.1.2 Applicant Generation

**Description**: While a posting is active, new applicants arrive every `SecondsBetweenApplicants` seconds (default 180s = 3 real minutes). Each applicant rolls a kind (regular vs special), a tier (jr/mid/sr), a role, a stat block, 1–2 abilities, a salary, and an appearance seed.

**Cap**: 8 applicants in the inbox simultaneously. Excess rolls are dropped silently.

**Implementation**: `HRManager.TickApplicantTimer()`. Calls `RollApplicant()` → `Employee.GenerateApplicant()` or `Employee.GenerateSpecialApplicant()` based on the kind roll.

**Player-Facing**: A notification fires for every new applicant. Special applicants get a `[KIND]` pill prefix (e.g., `[INTERN] Alex Kim`).

---

#### 2.1.3 Interview Gate

**Description**: An applicant's stats are NOT visible in the inbox — only name, age, role, and salary. To see the full stat sheet (and decide hire/pass), the player must spend energy on an interview.

**Energy cost**: `5 + tier × 4 + years_played` (so 5/9/13 + 1/year — junior at year 0 costs 5⚡, senior at year 5 costs 18⚡).

**Constraints**:
- Only one interview at a time. Starting a new one while another is open prompts a notification.
- Energy spent on an interview is **not refunded** if the player passes. The cost IS the interviewing.

**Implementation**: `HRManager.TryStartInterview()` removes the applicant from the inbox into `InterviewSubject`, calls `GameManager.TrySpendEnergy()`. `HireInterviewSubject()` and `PassOnInterviewSubject()` end the interview.

**Design Rationale**: This is the heart of Pillar 3. Without the interview gate, the player would just sort the inbox by stats and spam-click hire. With it, every interview is a real cost — the player must pre-commit before knowing if the hire is worth it.

---

#### 2.1.4 Hiring & Desk Assignment

**Description**: Hiring an interview subject:
1. Charges the first month's salary up front
2. For desk-taking kinds (Regular, Intern): allocates the lowest free desk index, fails if no free desk
3. For non-desk kinds (Mentor, MarketingAgent, RemoteWorker): spawns at HRManager's transform (off-camera)
4. Clones `EmployeeNpcPrefab` at the target pose
5. Calls `EmployeeNPC.Assign(employee, deskPos, deskRot)` to copy data and start the state machine
6. Records the hire in Achievements

**Desk discovery**: HRManager iterates `DesksRoot.Children` at startup (includes disabled children — that's the point). `UnlockedDesks` toggles `GameObject.Enabled` per desk index.

**Desk pricing**: `DeskBaseCost × DeskCostMultiplier^(unlockedDesks-1)` → defaults: $2,500 → $5,000 → $10,000 → $20,000 → $40,000…

**Implementation**: `HRManager.HireInterviewSubject()` — handles every failure case (no prefab, no desk, can't afford, intern slot collision) with a notification + early return. `TryBuyNextDesk()` runs the desk unlock purchase.

**Player-Facing**: Successful hire notification names the employee and their desk number ("starts at desk 4"). Remote hires get "starts (remote)".

---

#### 2.1.5 Monthly Cycle (`OnMonthStart`)

Fires on Day 1 of each in-game month. Order is fixed:

1. **Pay posting fee** (`PayPostingFee`) — if unaffordable, posting auto-switches to None
2. **Pay salaries** (`PaySalaries`) — per-employee:
   - Paid → Morale `+5%` (capped at 100%)
   - Missed → Morale `−25%`; if Morale ≤ 15%, employee quits
3. **Advance internships** (`AdvanceInternships`) — interns' contracts decrement; expired interns are auto-fired and queued for alumni return (60% chance, first guaranteed)
4. **Advance alumni** (`AdvanceAlumni`) — alumni cooldowns decrement; due alumni inject themselves into the inbox as returning interns

**Implementation**: Subscribed via `GameManager.OnMonthStart += PayMonthly`.

**Design Rationale**: A single per-month tick prevents micro-decision spam (no daily wage drips). It also concentrates financial pressure into one moment per month, creating a natural "did I make payroll?" beat.

---

#### 2.1.6 NPC Behavior on the Office Floor

**Description**: Hired desk-taking employees walk between three states on the office floor: **Working** (at their desk), **Training** (at a `WorkplaceAnchor` of kind Training), **Distracted** (at a `WorkplaceAnchor` of kind Break, or random offset if none). State duration: random between `MinStateDuration` (12s) and `MaxStateDuration` (30s).

**State diagram**:
```
   Idle (initial, pre-Assign)
     │
     ▼
   Working  ⇆  Training (10% chance per cycle)
     │
     ▼
   Distracted (5–30% chance per cycle, scales with Focus)
     │
     ▼
   Working (always returns to Working before re-rolling)
```

**State transition rule**: Anything other than Working returns to Working first. From Working, roll for distraction (5–30%, lower with higher Focus stat) then training (flat 10%). Otherwise re-affirm Working.

**Movement**: Linear interpolation toward `_targetPos` at `WalkSpeed = 80` units/sec. Snap to desk rotation when arriving at Working state. Face planar motion direction otherwise.

**Implementation**: `EmployeeNPC.TickStateMachine()` + `TickMovement()`. Non-desk kinds skip both — they live in the data layer only.

**⚠️ Currently incomplete — Training stat-growth is MISSING**:
- The Training state walks the NPC to a bookshelf, but no stat gain occurs
- `EmployeeStats.GrowSub(subIndex, amount)` exists — the API is ready
- `EmployeeAbility.QuickLearner` (40% faster training), `Leader` (nearby +25%), and the `Mentor` kind ("speeds up your team's training") all reference a system that does not yet do anything
- **MVP-required, missing — must be implemented before ship**

---

#### 2.1.7 Morale System

**Description**: Each NPC has a 0–1 `Morale` float. It multiplies all stat contributions: `EffectiveStat(block) = clamp(block.Average × Morale, 1, 1000)`.

**Modifiers**:
- Salary paid on time: `+5%` (cap 100%)
- Salary missed: `−25%`
- Morale ≤ 15%: employee quits (auto-Fire)
- ❌ Firing → morale ripple to remaining staff (planned, see 2.1.8)
- ❌ Severance bonus paid → suppress firing's morale ripple (planned, see 2.1.8)

**Founder (`PlayerStats`)** is fixed at Morale = 1.0 — the founder doesn't pay themselves a salary, so the salary-driven morale loop doesn't apply.

**Design Rationale**: Morale is the soft-fail loop. The player cannot lose money outright; they lose *people* via morale collapse. This keeps failure recoverable while still creating real consequences for poor management.

---

#### 2.1.8 Firing & Severance (DESIGN — partly implemented)

**Description**: The player can dismiss any employee from the HR Stats panel. Two paths:

**A) Standard Firing** (free):
- Employee is removed from `_staff`, GameObject destroyed
- ❌ **All remaining staff lose morale by a flat amount** — current TODO in `HRManager.Fire()`. **MVP-required.**
- Suggested penalty: `−10%` morale to every remaining employee. Tunable. Severe enough to make firing painful, recoverable enough to not cascade into a quit-spiral.

**B) Redundancy / Severance** (paid):
- Player pays a one-off bonus equal to N months of the fired employee's salary (suggested N = 2 months)
- The morale ripple in (A) is fully suppressed
- The fired employee is still removed
- This is the "do it the right way" path — costs money but preserves team morale

**Implementation**: Currently only path (A) is implemented and the morale ripple is missing. **Both halves of this mechanic are MVP-required.**

**Design Rationale (per user clarification 2026-04-27)**: Firing is supposed to hurt — players should think twice before dismissing staff. But there must be a money-for-morale escape valve so a wealthy late-game studio can refresh its team without spiraling. The redundancy bonus is that escape valve.

**Open questions** (need design decisions before implementation):
- Q1: What's the morale penalty per remaining staff? Suggested −10%. Confirm or override.
- Q2: How many months of salary in a severance? Suggested 2 months. Confirm or override.
- Q3: Is severance a per-employee toggle ("fire with severance" / "fire") or a global setting? Suggested per-employee.
- Q4: Does quitting (Morale ≤ 15%) trigger the same ripple as firing? **Suggested: NO** — when an employee quits, the ripple is implicit in the morale collapse that drove them out; another ripple would double-punish.

---

#### 2.1.9 Special-Hire Kinds

**Description**: Beyond regular hires, four special kinds offer mechanical variety. Each is achievement-gated, has a salary multiplier, and may or may not require a desk.

| Kind | Unlock | Desk? | Salary × | Guaranteed Abilities | Effect |
|---|---|---|---|---|---|
| Regular | Always | ✅ | 1.0 | 0 (1–2 random) | — |
| Mentor | Hire 5 | ❌ | 1.4 | 1 | Speeds team training |
| MarketingAgent | Hire 10 | ❌ | 1.5 | 1 | Boosts marketing on ship |
| RemoteWorker | Earn $100k | ❌ | 1.3 | 0 | Standard role contribution, no desk |
| Intern | Earn $1M | ✅ | 0.4 | 0 | 2-month contract, may return as star |

**Important**: Mentor and MarketingAgent reference downstream systems (training + marketing) that are not yet implemented. The kinds exist but their *effect* doesn't fire yet for those two.

**Spawn rule**: When a hire's `Kind.TakesDesk == false`, the spawned NPC is positioned at HRManager's transform (off-floor) and skips the state machine + movement. They contribute through the data layer only.

**⚠️ Special applicant rate — needs rebalancing (per user 2026-04-27)**:
- Current rule: 20% chance per applicant roll, uniform random among unlocked specials
- Late-game effect: with all 4 unlocked, 20% × 4 = ~80% of those rolls are non-Regular, so the inbox skews heavily toward specials
- **Marked as a balance concern, not a balance constant.** Likely fixes:
  - Scale the chance down with unlock count (`0.20 / max(1, unlocked.count)`)
  - Tier-gate specials by posting (Mentors only at CareerSite+; top kinds only at Headhunter)
  - Per-kind probability weights (Interns common, Mentors rare)
- Decision deferred to a balance-pass story

---

#### 2.1.10 Intern Contracts & Alumni Return

**Description**: Interns are explicitly **an investment, not a productivity hire** (clarified 2026-04-27). Their value is the alumni return.

**Fresh intern**:
- Forced Junior tier, zero abilities, age 18–24
- Salary: 0.4× the same-tier regular hire
- Contract: 2 in-game months (`InternContractMonths = 2`)
- Takes a desk (`TakesDesk = true`)
- One slot only — `HasActiveIntern` blocks new intern hires until the slot frees

**Contract lifecycle**:
1. Hired → `MonthsRemaining = 2`
2. `OnMonthStart` decrements `MonthsRemaining`
3. When `MonthsRemaining ≤ 0`: auto-fired, alumni record queued

**Alumni record**:
- Return chance: 60% (60.0% per spec)
- **First intern ever hired is guaranteed to return** (`!_firstInternHired` → guaranteed = true). This is the tutorial moment: the player must see the alumni payoff at least once.
- Cooldown: 3 in-game months before the alumnus may re-enter the inbox

**Returning intern**:
- Forced Senior tier, max abilities (2), age 25–55, same name as before
- Salary: regular formula × 0.4× (intern multiplier) × 0.7× (alumni discount) = **0.28× a normal senior salary**
- Re-enters the inbox as a returning intern (still the Intern kind for the slot rule)
- If the inbox is full or the slot is occupied, the cooldown extends by 1 month and retries next month

**Design Rationale**: An entire month-2 of an intern's contract is the player paying training cost. The alumni return is the payoff. The first intern's guaranteed return ensures the player learns this is a real mechanic and not random noise.

---

#### 2.1.11 Relationships (cross-reference)

**Description**: Employees develop bonds with each other. The headline mechanic
is that an idle employee whose pillar has finished will *walk over and help a
friend* on an unfinished pillar instead of going Training/Distracted, scaling
their contribution by relationship strength.

**Sources of bond growth** (summary): same project, same pillar, adjacent desks,
shipping a game together, water-cooler overlap, NPC↔NPC conversations, and
player→employee gifts.

**Surfaces this system touches**:
- Adds `Helping` and `Chatting` values to `EmployeeState`
- Requires a stable `Guid Id` on `EmployeeNPC` (and a constant Id on
  `PlayerStats` for the founder) to key bond pairs
- Returning interns reuse their old `Id` so prior bonds reattach on re-hire —
  a key alumni-payoff mechanic
- `GameProjectManager.TickProduction` consults a new `RelationshipManager`
  for the "best help target" routing
- HR Stats panel displays per-employee relationship lists and hosts the
  gift UI

**Status**: Design-only, not yet implemented. **Full GDD**: see
`design/gdd/relationships.md`.

---

### 2.2 Rules and Formulas

| # | Formula | Expression | Purpose | Verified? |
|---|---|---|---|---|
| F1 | Salary | `max(1200, overall × per_pt × role_mult × variance)` where `per_pt = rng[$22..$34]`, `variance = rng[0.88..1.12]` | Per-applicant monthly wage | ✅ |
| F2 | Role multiplier | Programmer 1.10 · SoundDesigner 1.05 · Designer 1.00 · Artist 1.00 · Creative 0.95 · Researcher 0.90 | Role-based salary tilt | ✅ — confirm tuning |
| F3 | Tier weighting | jr%=`60−55q` · mid%=`30+5q` · sr%=remainder, where q = job quality | Distribute applicants across jr/mid/sr | ✅ |
| F4a | Junior stat range | primary [40, 260] · secondary [20, 180] | Stat generation floor/ceiling | ✅ — confirm tuning |
| F4b | Mid stat range | primary [200, 560] · secondary [80, 390] | Stat generation floor/ceiling | ✅ — confirm tuning |
| F4c | Senior stat range | primary [420, 860] · secondary [200, 580] | Stat generation floor/ceiling | ✅ — confirm tuning |
| F5 | Job quality lift | primary lo `+ jobQuality × 60`, hi `+ jobQuality × 30` · secondary lo `+ jobQuality × 40`, hi `+ jobQuality × 20` | Better postings raise floor more than ceiling | ✅ |
| F6 | Progression lift | primary lo `+ pf × 80`, hi `+ pf × 60` · secondary lo `+ pf × 50`, hi `+ pf × 40` | Late-game studio reputation lifts both | ✅ |
| F7 | Progression factor | `clamp((years_since_2026)/5, 0, 1)` — 0 at start, 1 at year 2031 | Maps in-game time → progression | ✅ |
| F8 | Job quality by tier | None 0.0 · Bulletin 0.2 · Career 0.55 · Headhunter 0.9 | Posting tier → applicant quality | ✅ — confirm tuning |
| F9 | Effective stat | `clamp(stat_avg × morale, 1, 1000)` | Morale-modulated contribution | ✅ |
| F10 | Interview energy | `5 + tier × 4 + years_played` | Per-applicant interview gate | ✅ |
| F11 | Distraction roll | `max(0.05, 0.30 − focus_avg/1000 × 0.25)` per state cycle | NPC chance to wander off | ✅ — confirm 5% floor intent |
| F12 | Training roll | flat 0.10 after distraction roll | NPC chance to study | ✅ |
| F13 | Salary morale | paid: `+0.05` (cap 1.0) · missed: `−0.25` · quit ≤ `0.15` | Morale per pay event | ✅ |
| F14 | Desk price | `$2,500 × 2.0^(unlocked-1)` | Per-desk unlock cost | ✅ — confirm curve |
| F15 | Special-hire salary | regular formula × `kind_mult` (Mentor 1.4 · Marketing 1.5 · Remote 1.3 · Intern 0.4) | Special-hire salary scaling | ✅ |
| F16 | Returning intern salary | `(senior_formula × 0.4) × 0.7` ≈ 0.28× senior baseline | Alumni discount stacking | ⚠️ — verify not over-bargain |

**Clarifications & Open Decisions** (per user 2026-04-27):
- **Special applicant rate (currently 20% flat)**: needs redesign. Mark as a balance-pass story.
- **Severance amount**: suggested 2 months of salary as a one-off. Awaiting confirmation.
- **Fire morale penalty**: suggested −10% per remaining employee. Awaiting confirmation.

---

### 2.3 State and Data

**Data Structures**:

```text
Employee (DTO — applicant pool, immutable post-generation)
├── Identity:    Name, Age, Role, Salary, Tier, Kind
├── Stats:       EmployeeStats (6 main × 3 sub)
├── Abilities:   IReadOnlyList<EmployeeAbility>  (1–2)
├── Appearance:  AppearanceSeed (deterministic)
├── Workplace:   DeskSpot (-1 = applicant)
└── Returning:   IsReturningIntern (alumni flag)

EmployeeStats (FIFA-style 6×3 = 18 sub-stats, all 1–1000)
├── Programming: CodeQuality · BugResistance · ImplementationSpeed
├── Design:      MechanicDesign · PlayerSatisfaction · GameFeel
├── Creativity:  Innovation · ResearchAptitude · GenreVersatility
├── Artistry:    VisualPolish · AestheticSense · AssetQuality
├── Sound:       SoundEffects · MusicComposition · SoundAesthetics
└── Focus:       ResearchSpeed · WorkConsistency · AttentionToDetail

EmployeeNPC (live actor — Component on cloned prefab)
├── (data mirror of Employee, copied in Assign())
├── State (Idle/Working/Training/Distracted)
├── Morale (0..1, mutable)
├── MonthsRemaining (-1 permanent / 0+ contract)
└── Movement bookkeeping (deskPos, deskRot, targetPos, timers)
```

**State Machine** (`EmployeeNPC`):
```
   Idle (initial — pre-Assign() only)
     │
     │  Assign() called
     ▼
   Working ─────── Training (10% per cycle)
     ▲              │
     │              ▼
     └────────── Working (always returns)
     ▲              ▲
     │              │
     └─ Distracted ─┘  (5–30% per cycle, scales with Focus)
```

**Invariant**: Every non-Working state transitions back to Working before re-rolling. This keeps the floor mostly productive — chaotic floors are designed against.

**Persistence**:
- ⚠️ **No save system exists yet** — the entire HRManager state (Applicants list, Staff list, Alumni queue, PostingTier, UnlockedDesks, Interview state) is session-only.
- Per the game-concept doc, save system is an MVP-blocking gap. This system will need full serialization.

**What MUST persist when saves exist**:
- `_staff` (every EmployeeNPC's full stats, morale, contract state, kind, ability slots, desk index)
- `_alumni` (full queue with months-until-return)
- `_applicants` (current inbox — losing the inbox on save/load is a UX failure)
- `_firstInternHired` (so the guaranteed-first-return rule survives saves)
- `PostingTier`, `UnlockedDesks`, `InterviewSubject`

**What can be recomputed**:
- `_desks` list (rediscovered from scene on load)
- `_applicantTimer` (reset to 0 on load is acceptable)
- NPC position/rotation (snap to desk on load)

---

### 2.4 Integration Points

**Dependencies (this system reads from):**
- `GameManager` — `Money`, `Energy`, `OnMonthStart` event, `Year/Month/Day`, `TimeMultiplier`
- `Achievements` — gate checks for posting tiers, special kinds, desk unlocks
- `s&box scene` — `DesksRoot.Children` (for `EmployeeDesk` discovery), `WorkplaceAnchor` markers, `EmployeeNpcPrefab` reference
- `Notifications` — push helpers for hire/fire/quit/posting/lock messages

**Dependents (consume this system):**
- `GameDev` (`GameProject`, `GameProjectManager`) — consumes `IDevWorker` instances from `HRManager.Staff`. The whole project assignment grid is built on this.
- `Achievements` — `RecordHire(npc)` is called on every successful hire; achievements like Hire 5 / Hire 10 fire here
- `Gallery` — (planned) shipped-game records will reference the team that built each game; needs employee snapshot at ship time
- `PlayerStats` (founder) — implements the same `IDevWorker` interface, sits alongside `_staff` in project assignments

**API Surface (HRManager public methods):**
- `TrySetPostingTier(tier)` — switch posting; returns false if locked
- `TryStartInterview(applicant)` — spend energy to interview; gate
- `HireInterviewSubject()` — finalize hire from current interview
- `PassOnInterviewSubject()` — decline current interview
- `Reject(applicant)` — discard from inbox without interviewing
- `Fire(npc)` — dismiss employee (currently morale-free; severance/ripple is MVP work)
- `TryBuyNextDesk()` — pay for the next desk unlock

---

## 3. Edge Cases

### Handled in Code

- ✅ **No active posting → no inbound applicants**: timer still ticks but `RollApplicant()` is gated on `PostingTier != None`
- ✅ **Inbox full (>=8)**: new applicant rolls are silently dropped
- ✅ **Posting unaffordable on month-start**: posting auto-switches to None with a notification
- ✅ **Salary unaffordable for one employee**: that employee's morale drops, others paid normally
- ✅ **Morale collapse (≤15%)**: employee auto-quits cleanly via `_fireQueue`
- ✅ **Hire with full desks**: `HireInterviewSubject` returns false with notification
- ✅ **Hire with missing prefab**: returns false with notification (no orphan EmployeeNPC)
- ✅ **Hire intern with active intern**: blocked at both roll-time (`PickApplicantKind`) and hire-time (`HireInterviewSubject`)
- ✅ **Returning intern + full inbox**: alumni cooldown re-extended by 1 month, retries next pay-day
- ✅ **Returning intern + active intern slot**: same behavior as full-inbox case
- ✅ **Disabled desks survive scene queries**: `DesksRoot.Children` includes disabled (vs `Scene.GetAllComponents` which skips disabled)
- ✅ **UnlockedDesks drift past pool size**: clamped at startup
- ✅ **Non-desk hires**: skip state machine + movement entirely; spawn at HRManager transform
- ✅ **Spawn-but-not-Assigned**: `IsAssigned` guard at top of `OnUpdate` prevents ticking unconfigured NPCs
- ✅ **Invalid stat range**: `Math.Max(lo+1, hi)` guards `rng.Next` from empty ranges

### ⚠️ Not Handled — MVP Work

- ⚠️ **Training stat-growth**: state exists, no stat gain occurs (MVP-required)
- ⚠️ **Severance bonus**: no UI, no payment path, no morale-suppression (MVP-required)
- ⚠️ **Fire morale ripple**: TODO comment in `Fire()`, not implemented (MVP-required)
- ⚠️ **Posting tier 'None' canceling mid-interview**: no behavior — the in-progress interview survives, but no test confirms this works
- ⚠️ **Save/load**: nothing serializes (entire system, MVP-blocker per game-concept)

### ❓ Ambiguous — Need Decision

- ❓ **5% distraction floor**: at max Focus (1000), `max(0.05, 0.30 − 0.25) = 0.05`. Even maxed-out employees still slack 5% of the time. Is this a deliberate floor ("everyone slacks sometimes") or accidental clamp?
- ❓ **Walking speed (80 units/sec)**: in s&box, units are inches. 80 in/sec ≈ 4.5 mph — reasonable office pace. Confirm.
- ❓ **Quit ≠ fire ripple**: when an employee quits from morale collapse, does the same morale ripple apply to remaining staff? **Suggested: NO** (the ripple is already implicit in the morale collapse that caused the quit). Confirm.
- ❓ **Multiple intern slots later?**: currently capped at one. Should office-size unlocks also unlock a second/third intern slot? Defer to post-MVP.
- ❓ **Appearance system**: `EmployeeNPC.ApplyAppearance(seed)` is a stub. Wired to `ClothingContainer` later, per the comment.

---

## 4. Dependencies

**Technical Dependencies**:
- s&box `Component` lifecycle (`OnAwake` / `OnStart` / `OnUpdate` / `OnDestroy`)
- s&box `[Property]` and `[Sync]` attributes (already used; networking will matter if multiplayer is ever revisited)
- s&box `GameObject.Clone(pos, rot)` API (used for hire spawn)
- `System.Random` for stat/applicant generation (no determinism guarantees)

**Design Dependencies (other GDDs)**:
- `game-concept.md` — Pillar 3 ("Hiring is meaningful, not noise") is the design north star
- `gamedev.md` (TBD via `/reverse-document design Code/GameDev/`) — consumer of `IDevWorker`; project-role assignment lives there
- `economy.md` (TBD) — money pressure model; salary is a primary cost driver
- `achievements.md` (TBD) — gates posting tiers, special kinds, desks
- `save-system.md` (TBD ADR) — required before this system is shippable

**Content Dependencies**:
- `EmployeeNpcPrefab` — citizen prefab with EmployeeNPC + SkinnedModelRenderer + collision wired
- `DesksRoot` — empty parent GameObject in the scene; every `EmployeeDesk` marker parented under it
- `WorkplaceAnchor` markers (Training + Break) sprinkled through the office
- `ClothingContainer` (eventually) — for `ApplyAppearance` to drive variation

---

## 5. Balance and Tuning

### Tuning Knobs (parameters with rationale)

| Parameter | Current | Rationale | Tune? |
|---|---|---|---|
| `SecondsBetweenApplicants` | 180 (3 real min) | Roughly one new applicant every in-game 3 days | ⚠️ may need play-testing |
| Inbox cap | 8 | Prevents unbounded queue | ✅ ok |
| `SpecialApplicantChance` | 0.20 | Placeholder — see balance concerns below | ❌ **rebalance required** |
| Interview energy base | 5 | Cheap first interview | ✅ ok |
| Interview tier mult | ×4 | Senior costs ~2.6× junior at year 0 | ⚠️ confirm in playtest |
| Interview year mult | +1/yr | Late-game interviews cost real energy | ⚠️ confirm |
| `MissedPayMoralePenalty` | 0.25 | One missed paycheck is severe | ✅ ok |
| `QuitMoraleThreshold` | 0.15 | One missed paycheck doesn't quit; two does | ✅ ok |
| Salary per-point base | $22–$34 | Senior ~600 stat × $28 × 1.0 = $16,800/mo | ⚠️ confirm vs revenue curve |
| `DeskBaseCost` | $2,500 | First paid desk affordable in early game | ✅ ok |
| `DeskCostMultiplier` | 2.0 | Each desk doubles — rapid late-game cap | ⚠️ may be too steep |
| `WalkSpeed` | 80 in/sec | ≈ 4.5 mph office pace | ✅ ok |
| `MinStateDuration` | 12s | NPCs don't change states too rapidly | ✅ ok |
| `MaxStateDuration` | 30s | Caps any one state | ✅ ok |
| Distraction floor | 0.05 | "Everyone slacks sometimes" | ❓ confirm intent |
| Training chance | 0.10 | NPCs visibly use Training anchors | ⚠️ may need tuning vs Mentor effect |
| `InternContractMonths` | 2 | Two months of pay before payoff | ✅ ok |
| `InternReturnCooldownMonths` | 3 | Player has time to forget; return feels like a callback | ✅ ok |
| Intern return chance | 0.60 | Most do return — payoff is reliable | ⚠️ tune if alumni become OP |
| Returning intern salary | × 0.7 (on top of × 0.4 = 0.28× senior baseline) | Heavy bargain by design (alumni payoff) | ⚠️ may be too cheap; verify |
| Mentor salary | × 1.4 | Specialists are pricier than regulars | ⚠️ confirm vs effect strength |
| MarketingAgent salary | × 1.5 | Same | ⚠️ confirm |
| RemoteWorker salary | × 1.3 | Same | ⚠️ confirm |
| Intern salary | × 0.4 | Cheap junior; subsidy for the alumni hook | ✅ ok |
| Bulletin Board fee | $200/mo | Always-affordable starter posting | ✅ ok |
| Career Site fee | $1,500/mo | Substantial commitment | ⚠️ vs early-game revenue |
| Headhunter fee | $6,000/mo | Late-game tier | ⚠️ vs late-game revenue |
| Bulletin job quality | 0.20 | Mostly juniors | ✅ ok |
| Career Site job quality | 0.55 | Balanced | ✅ ok |
| Headhunter job quality | 0.90 | Mostly seniors | ✅ ok |

### Balance Concerns Identified

- ⚠️ **Special applicant rate** (`SpecialApplicantChance = 0.20`): with all 4 specials unlocked, ~80% of those rolls are non-Regular, crowding regular hires out of the late-game inbox. **Rebalance required before MVP.** Likely fixes:
  1. Scale by unlock count: `0.20 / max(1, unlocked.count)` → caps total specials at ~20% regardless
  2. Tier-gate by posting: Mentors only at CareerSite+; top specials only at Headhunter
  3. Per-kind weights (Interns common, Mentors rare)
- ⚠️ **Returning intern stacking**: Senior tier + max abilities + 0.28× senior salary may make alumni the dominant late-game hiring path. Confirm with simulation; may need to lift the discount to 0.5× or cap to one alumni at a time.
- ⚠️ **Desk cost curve (×2)**: doubling means desk #10 is ~$1.28M. Past mid-game the curve becomes the office-size cap. Intentional or too aggressive?
- ⚠️ **Interview energy late-game**: at year 5+ a senior interview costs 18⚡ on a 50⚡ cap. Player can only run ~2 senior interviews before depleting. Either intentional gating or too restrictive.
- ⚠️ **Distraction floor (5%)**: even max-Focus employees slack 5% of cycles. Probably intentional ("everyone slacks sometimes") but confirm.

### Recommended Balance Pass

- Run `/balance-check` after the special-rate rebalance is committed
- Playtest the first 3 in-game years end-to-end to validate: (a) salary curve vs revenue curve, (b) intern/alumni payoff timing, (c) desk-unlock pacing
- Simulate "year 5 inbox of 8" with all specials unlocked to verify the rebalance landed

---

## 6. Acceptance Criteria

### What Exists (implemented)
- ✅ Job posting tiers + monthly fee + auto-cancel on bankruptcy
- ✅ Applicant generation with stats / abilities / salary / appearance seed
- ✅ Inbox cap, applicant timer, kind roll
- ✅ Interview gate (energy cost, single-active enforcement)
- ✅ Hire flow (desk allocation, prefab clone, prefab missing fail-safe, salary upfront)
- ✅ Desk unlocks (per-desk price curve, achievement gates, world-object enable/disable)
- ✅ Monthly cycle (posting fee → salaries → intern contracts → alumni return)
- ✅ Morale system (paid/missed deltas, quit threshold)
- ✅ Intern contracts + alumni queue + 60% return + first guaranteed
- ✅ NPC state machine (Idle/Working/Training/Distracted) + movement
- ✅ Special-hire kinds (data layer + spawn behavior + desk-or-not rule)
- ✅ HasAbility / EffectiveStat helpers
- ✅ Achievement integration (`RecordHire`)

### ⚠️ Partially Implemented
- ⚠️ **Training**: state exists and walks NPCs to anchors, but **no stat gain** — must implement
- ⚠️ **Special-hire effects**: Mentor's training boost and MarketingAgent's marketing boost are *named* but their effects are not wired to anything (because training and marketing don't exist yet)
- ⚠️ **Appearance system**: `ApplyAppearance(seed)` is a stub
- ⚠️ **Founder treatment in HRManager**: founder is part of the staff list via `IDevWorker`, but the founder isn't in `_staff` — this works correctly today but should be tested across all flows

### ❌ Missing — MVP Required
- ❌ **Training stat-growth** (per user 2026-04-27)
- ❌ **Severance bonus** + morale ripple suppression (per user 2026-04-27)
- ❌ **Fire morale ripple** (per user 2026-04-27)
- ❌ **Special applicant rate rebalance** (per user 2026-04-27)
- ❌ **Save persistence** (blocks shippability for the whole game)

### Definition of Done

- [ ] Training stat-growth implemented; verified via test (employee at Training anchor for N seconds gains M sub-stat points; mentors multiply N or M as designed)
- [ ] Severance/firing UI + morale ripple + suppression flow implemented; verified via test
- [ ] Special applicant rate rebalanced; simulation confirms ≤30% specials in late-game inbox
- [ ] All 16 formulas have at least one balance-check test
- [ ] Save/load roundtrip preserves: staff, alumni queue, applicants, posting tier, unlocked desks, interview state, first-intern flag
- [ ] Playtest: a fresh player understands the alumni payoff within their first intern lifecycle
- [ ] Playtest: morale recovery loop is forgiving enough that one bad month doesn't cascade to studio collapse

---

## 7. Open Questions and Follow-Up Work

### Questions Needing User Decision

1. **Severance amount**: Suggested 2 months of fired employee's salary as one-off. Confirm or override.
2. **Fire morale penalty per remaining employee**: Suggested −10%. Confirm or override.
3. **Quit (morale collapse) ≠ fire ripple**: Suggested NO additional ripple when an employee quits — the morale state that drove them out IS the ripple. Confirm.
4. **Special applicant rebalance approach**: pick from (a) scale-by-unlock-count, (b) tier-gate by posting, (c) per-kind weights, or (d) hybrid. Decision affects implementation.
5. **5% distraction floor**: confirm "everyone slacks sometimes" is intentional vs accidental clamp.
6. **Returning intern discount tuning**: 0.28× senior baseline may be too cheap. Verify with simulation; consider 0.5× as a fallback.
7. **Multi-intern slot post-MVP**: should office expansion eventually unlock a 2nd/3rd intern slot?

### Flagged Follow-Up Work

- [ ] **Implement Training stat-growth**: design the per-tick gain rate; integrate Mentor/Leader/QuickLearner multipliers
- [ ] **Implement severance flow**: UI option in HR Stats panel ("Fire" / "Fire with severance"), money charge, morale ripple suppression logic
- [ ] **Implement fire morale ripple**: −10% (TBD) per remaining staff on any non-severance fire
- [ ] **Rebalance special applicant rate**: pick approach, implement, simulation-test
- [ ] **Wire `ApplyAppearance(seed)` to ClothingContainer**: needs art assets + clothing system
- [ ] **Design save serialization**: ADR for save system; this system is one of its biggest dependents
- [ ] **Add balance-check tests**: one per formula in §2.2
- [ ] **Address `MyComponent.cs` cleanup**: orthogonal but flagged in stage report
- [ ] **Consider founder in _staff list**: the founder is currently NOT in `HRManager._staff`. Verify all assignment-grid code handles this correctly (`PlayerStats.Instance` is iterated separately in `GameProject.UnassignedStaff`).

---

## 8. Version History

| Date | Author | Changes |
|---|---|---|
| 2026-04-27 | Claude (reverse-doc) | Initial reverse-documentation from `Code/Employees/` (11 files) |
| 2026-04-27 | Kaito Tagawa | Clarified: training is MVP-required, specials need rebalance, interns are investment-not-productivity, firing hurts morale + severance bonus mitigates |

---

**Next Steps**:
1. Decide on severance amount and fire-morale penalty values (Open Question 1 & 2) — blocks implementation
2. Pick the special-applicant-rebalance approach (Open Question 4) — blocks implementation
3. Author the save-system ADR — blocks shippability
4. Implement training stat-growth — MVP-critical
5. `/reverse-document design Code/GameDev/` next — that's the consumer of this system

**Related Skills**:
- `/balance-check` — Validate formulas and progression once tuning settles
- `/architecture-decision` — Author save-system ADR
- `/design-system training` — When training implementation begins, author its own GDD

---

*This document was generated by `/reverse-document design Code/Employees/` on 2026-04-27 and updated with user clarifications the same day.*
