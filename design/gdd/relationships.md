---
status: design-only
source: forward-design — no implementation yet
date: 2026-04-27
verified-by: Kaito Tagawa
---

# Employee Relationships — Design Document

**Status**: Design-Only (no implementation yet)
**Source**: Forward design, building on `Code/Employees/`
**Date**: 2026-04-27
**Verified By**: Kaito Tagawa
**Implementation Status**: Not implemented — full design pending build

> **⚠️ Design-Only Notice**
>
> Unlike sibling system docs in this folder, this GDD describes a system that
> does **not yet exist in code**. Every formula, rate, and edge case below is a
> design proposal awaiting implementation. The Employees GDD references this
> document under §2.1.11.

---

## 1. Overview

**Purpose**: Track how well any two employees know and like each other, and let
that bond drive emergent gameplay — most importantly, an idle employee will
*walk over and help a friend* on an unfinished project pillar instead of
slacking off or training.

**Pillar Alignment**:
- **Pillar 1 — The Studio Is a Place**: bonds form through proximity (adjacent
  desks, shared break moments, in-world conversations). Reinforces the
  physical-presence layer as a real driver of mechanics, not just atmosphere.
- **Pillar 3 — Hiring Is Meaningful**: keeping a team together across multiple
  projects becomes mechanically rewarding. Firing or losing a long-tenured
  pair has weight beyond the morale ripple alone.

**Scope**:
- ✅ In scope: pair-keyed bond score, 6 growth sources, 5 named relationship
  levels, idle-help mechanic, NPC↔NPC conversation behavior, player→employee
  gift system, decay, persistence, HR Stats panel surface
- ❌ Out of scope (Phase 2): rivalries / negative bonds, employee↔employee
  autonomous gifting, relationship-driven training boosts, social-graph
  visualization UI

**Design Intent** (clarified 2026-04-27):
- Relationships are **friendly-only** (0–100). No rivalries in MVP — drama
  comes from morale + firing, not paired hostility
- Bonds come from **time spent + shared experience**, not from a stat or trait
- The headline loop is **"my friend finished her pillar — there she goes,
  helping me with mine"** — visible in-world, not just a number going up
- Gifts are a **money sink** that translates wealth into team cohesion (and
  morale) without paying salary

---

## 2. Detailed Design

### 2.1 Core Mechanics

#### 2.1.1 Relationship Score

**Description**: A `float` value in `[0, 100]` per unordered pair of employees.
Symmetric — `score(A, B) == score(B, A)`. Stored in a single dictionary keyed
on a sorted-id tuple.

**Named Levels** (UI labels):
| Range | Level | Help × | UI hint |
|---|---|---|---|
| 0 – 19 | Strangers | 0% | grey chip |
| 20 – 39 | Acquaintances | 6 – 12% | faint blue chip |
| 40 – 59 | Colleagues | 12 – 18% | blue chip |
| 60 – 79 | Friends | 18 – 24% | green chip |
| 80 – 100 | Best Friends | 24 – 30% | gold chip |

**Help × column** is the linear curve `0.30 × (score / 100)` — see §2.1.4.
Levels are display-only; the underlying math always uses the raw score.

**Initial state**: every new pair starts at 0. **Returning interns preserve
their previous bonds** — that's part of the alumni payoff (see Employees GDD
§2.1.10 and Edge Cases below).

---

#### 2.1.2 Growth Sources

Bonds grow from six concurrent sources. Rates are tunable; suggested defaults:

**A) Project Work Together**
- Both assigned to the same `GameProject` (any role): **+0.5 per in-game day**
- Both assigned to the same role (same pillar): **+1.5 per in-game day**
- These stack: same-pillar work earns +0.5 (project) + +1.5 (pillar) = **+2.0/day**
- Tick: at the end of each in-game day in `GameProjectManager.TickProduction`

**B) Adjacent Desks (passive)**
- Two desks are "adjacent" if their world-position distance is below
  `AdjacentDeskRadius` (suggested: 200 s&box inches ≈ 5 m office distance)
- Both employees must be at their desks (`State == Working`) for the tick to count
- **+0.2 per in-game day** when both are working at adjacent desks
- Tick: end of each in-game day; checks current desk distance and state

**C) Shipped a Game Together (one-shot)**
- When `GameProject.Phase` flips `Production → Finished`:
- **+10 to every pair** where both were assigned anywhere on that project
- The "we made this thing together" bonus. Big enough to feel like a milestone.

**D) Water-Cooler Moments (overlapping break time)**
- When both employees are in `Distracted` state at the same `WorkplaceAnchor`
  of kind `Break` (or two break anchors within `BreakClusterRadius`):
- **+0.1 per real-second of overlap**, capped at +1.0 per single overlap
  (so one long break doesn't max out a relationship in one go)

**E) NPC ↔ NPC Conversations (new emergent behavior)**
- New sub-behavior of `Distracted`: when an NPC enters Distracted and there is
  another Distracted NPC within `ChatRadius` (suggested: 96 inches), there is
  a `ChatChance` (suggested: 30%) that they pause and have a brief chat
  instead of wandering to a Break anchor
- Visual: both NPCs face each other for 4–8 random seconds. Optional speech
  bubble icon overhead
- Effect: **+2 to that pair's score** per conversation
- Cooldown: same pair cannot chat again until a `ChatCooldown` (suggested:
  120 in-game seconds = 2 in-game days) has passed

**F) Player → Employee Gifts (founder bond)**
- The player can give gifts to any employee from the HR Stats panel
- Gifts are a money sink that translates wealth into team cohesion
- Gifts boost both **morale** and the **founder ↔ recipient relationship**
- One gift per employee per in-game month (cooldown)

| Gift | Cost | Morale | Founder Relationship |
|---|---|---|---|
| Small (coffee, lunch) | $500 | +5 | +5 |
| Medium (gear, tickets) | $2,000 | +10 | +10 |
| Large (bonus, retreat) | $10,000 | +20 | +20 |

**Phase 2 (post-MVP)**: Employee → Employee gifts on autonomous triggers
(shipping a game with a friend, friend's bad-morale month, etc.). Not part
of MVP scope.

---

#### 2.1.3 Decay

**Description**: Relationships slowly fade if two employees never interact.
Without decay, late-game relationships pile up to 100 across the entire staff
and lose discriminative meaning.

**Rule**: At `OnMonthStart`, for every pair where the employees had **no
interaction** this in-game month:
- score `-= 1.0` (clamped at 0, never negative — friendly-only model)

**Definition of "no interaction" (this month)**:
- They were not assigned to the same project at any point
- They were never at adjacent desks while both Working
- They had no water-cooler overlap
- No conversation triggered
- No gift involving the founder side

**Tracking**: a per-pair `LastInteractionMonth` field. If that field equals
the current month-year, the pair is exempt from decay this month. Cheap and
robust — single int per pair.

**Edge case**: a fired or quit employee's pairs decay the same as anyone
else's. If a returning intern comes back, their old bonds (preserved via
alumni record) may have decayed somewhat — fair, since time has passed.
*See §3 Edge Cases for "alumni return preserves bonds."*

---

#### 2.1.4 The Help Mechanic (headline loop)

**Trigger**: in `GameProjectManager.TickProduction`, after the per-pillar
`AdvancePillar` runs, check every assigned employee:

For an employee **E**:
- If **E** is still on an assigned pillar that's `<100%` → continue normally (E is busy)
- If E is assigned but their *primary pillar is at 100%* (or they're unassigned
  on a project where they have at least one Friend assigned and working):
  - Look up E's bonds with every other staff member assigned to the project
  - Sort descending by score; for the top-scoring bond:
    - If that friend's pillar is `<100%`: **E enters `Helping` state and contributes**
    - Else: try the next friend down the sorted list
  - If no qualifying friend found: fall through to current behavior (Training / Distracted)

**Help formula**:
```
help_contribution = friend_stat × 0.30 × (E.relationship_with_friend / 100)
```

Where `friend_stat` is the same stat the friend's pillar uses (e.g., `Design.Average`
for the GameDirector pillar), evaluated against **E's** stat block — not the
friend's. (You're using your own talents to help theirs.)

Help_contribution is added to `PillarContribution` for the friend's pillar
during that tick. Modulated by E's own Morale (same as any other contribution).

**Worst case**: at score 100, help adds 30% of E's primary stat to the friend's
pillar. Strong but not OP — a Best-Friends pair earns the equivalent of a
0.3× extra teammate on that pillar.

**Visual**: while `Helping`, the NPC walks to the friend's desk and stands at
or beside it. Reinforces Pillar 1 — you literally see them go help.

**State machine extension**:
```
Working → (pillar done, friend in need) → Helping → Working (when friend done)
Working → (no friend in need) → Training / Distracted (existing behavior)
```

`Helping` is a new `EmployeeState` value alongside the existing four
(Idle / Working / Training / Distracted).

---

#### 2.1.5 NPC ↔ NPC Conversations

**Description**: When two NPCs are both in `Distracted` state and within
`ChatRadius`, they may pause and have a brief chat instead of wandering to
their respective break anchors. Surfaces relationships in-world without UI.

**Trigger**:
1. NPC `A` enters `Distracted` state
2. Find any other NPC `B` in `Distracted` state within `ChatRadius`
3. If `B` is not on its `ChatCooldown` for pair `(A,B)`:
4. Roll `ChatChance` (suggested 30%)
5. If passed: A and B both transition to a `Chatting` sub-state for 4–8 random seconds

**During chat**:
- Both NPCs face each other (smooth-rotate, not snap)
- Both stop walking
- Optional speech-bubble icon overhead (Phase 2 polish)
- On exit: both transition to wandering toward their break anchor as usual

**Effect**: pair score `+= 2`. Pair cooldown set to current time + `ChatCooldown`.

**Edge case**: if NPC `A` is interrupted (player closes a modal that re-locks
input, project assignment changes, NPC fired) the chat ends silently and the
+2 still applies if the chat ran > 1 second.

---

#### 2.1.6 Player → Employee Gifts

**Description**: From the HR Stats panel, the player can give a tiered gift
to any employee. Gifts cost money, boost the recipient's morale, and grow the
founder ↔ recipient relationship score.

**UI**: each employee's HR Stats page shows a "Gift" button. Clicking opens
a small picker with the three tiers (cost / effect summary). Cooldown state
shown on the button.

**Cooldown**: one gift per employee per in-game month. Tracked by
`LastGiftMonth` on `EmployeeNPC`.

**Effects** (all applied immediately on purchase):
- `GameManager.TrySpend(cost)` — fail with notification if unaffordable
- `npc.Morale = clamp(Morale + moraleBonus / 100, 0, 1)`
- `RelationshipManager.AdjustBond(founder.Id, npc.Id, +scoreBonus)`
- Notification: `"Gave [Name] [GiftLabel]. They appreciated it."`
- Achievement trigger: track lifetime gifts given (one for "first gift",
  one for "10 gifts", etc.)

**Founder ID**: founder needs a stable ID (suggested: a fixed `Guid.Empty`
or a constant `Guid.Parse("00000000-0000-0000-0000-000000000001")`) so that
founder ↔ employee bonds key consistently.

---

### 2.2 Rules and Formulas

| # | Formula | Expression | Purpose |
|---|---|---|---|
| R1 | Help contribution | `friend_stat × 0.30 × (score/100)` | Idle-friend pillar boost |
| R2 | Same-project growth | `+0.5 per in-game day` | Project work bond |
| R3 | Same-pillar growth | `+1.5 per in-game day` (stacks with R2) | Same role on project |
| R4 | Adjacent-desk growth | `+0.2 per in-game day` (both Working) | Passive proximity |
| R5 | Ship-together bonus | `+10` one-shot on phase Finished | Co-shipped game |
| R6 | Water-cooler growth | `+0.1 per real-second overlap`, cap `+1.0` per overlap | Shared break time |
| R7 | Conversation bonus | `+2.0` per chat | NPC-NPC organic interaction |
| R8 | Decay | `-1.0` per in-game month with no interaction | Prevents late-game saturation |
| R9 | Gift Small | morale `+0.05`, score `+5` | $500 cost |
| R10 | Gift Medium | morale `+0.10`, score `+10` | $2,000 cost |
| R11 | Gift Large | morale `+0.20`, score `+20` | $10,000 cost |
| R12 | Chat trigger | `ChatChance = 30%` per Distracted entry near another Distracted NPC | Conversation frequency |

**Worked example** — two employees assigned to the same pillar at adjacent desks:
- R2 + R3 = `+2.0/day` while production runs
- R4 = `+0.2/day` whenever both are at desks (most of production)
- Estimated: `+2.2/day` ≈ `+44 in-game days = 44 in-game days`
- A 4–6 in-game month project (~120–180 days) yields ~250–400 score → caps at 100
- So **a single project together** gets two employees from Strangers → Best Friends
- Without decay, every pair that has worked together once would max out — hence R8

---

### 2.3 State and Data

**Data structures (proposed):**

```text
RelationshipManager (Component, singleton — like HRManager)
└── _bonds : Dictionary<PairKey, BondRecord>

PairKey  (struct, IEquatable)
├── A : Guid     // sorted ascending
└── B : Guid     // ensures (A,B) == (B,A)

BondRecord
├── Score                : float (0–100)
└── LastInteractionMonth : int (year * 12 + month, for decay check)

EmployeeNPC (existing — add 2 fields)
├── Id            : Guid (init-only, generated at hire time)
└── LastGiftMonth : int (-1 = never)

PlayerStats (existing — add 1 field)
└── Id : Guid (constant — see "Founder ID" below)

EmployeeState (existing — add 2 values)
├── Helping   : new — assigned and contributing to a friend's pillar
└── Chatting  : new — paused mid-Distracted to talk to another NPC
```

**Founder ID**: a constant Guid (e.g., `Guid.Parse("00000000-0000-0000-0000-000000000001")`)
so founder ↔ employee bonds key consistently across runs and saves. Document this
constant; do NOT use `Guid.Empty` (too easy to confuse with "no employee").

**Persistence (when save system exists)**:
- ✅ `_bonds` dictionary (every pair score + last-interaction month)
- ✅ Every NPC's `Id` and `LastGiftMonth`
- ✅ Founder ID is constant — no save needed
- ✅ Returning-intern alumni records keep their old `Id` so bonds reattach
  on re-hire (a key alumni-payoff mechanic — see §3)

**Memory cost**: with N employees, the dictionary has N×(N-1)/2 entries. At
maximum office size (suggested 20 desks → 21 with founder), that's 210
entries × ~24 bytes = ~5 KB. Trivially cheap.

---

### 2.4 Integration Points

**This system reads from:**
- `HRManager` — staff list (iterate to find pairs, friends, etc.)
- `GameProjectManager` — current project, assignments, pillar progress
- `GameManager` — calendar (decay, cooldowns), money (gift cost), notifications
- `EmployeeNPC` — state, position, desk, morale
- `WorkplaceAnchor` — Break anchor positions
- `EmployeeDesk` — desk positions for adjacency

**Systems that depend on this:**
- `GameProjectManager.TickProduction` — must call into RelationshipManager
  to determine if an idle employee should help a friend, and which friend
- `EmployeeNPC` — `OnUpdate` calls `RelationshipManager.TryStartChat()` on
  Distracted-state entry
- `HR Stats panel` (UI) — reads bond scores, displays per-employee list, hosts gift UI

**Public API surface (RelationshipManager):**
- `GetBond(idA, idB) : float` — current pair score
- `GetLevel(idA, idB) : RelationshipLevel` — named tier for UI
- `AdjustBond(idA, idB, delta) : void` — apply growth or decay; updates LastInteractionMonth
- `RecordSameProject(idA, idB)` — call once per in-game day per assigned pair
- `RecordSamePillar(idA, idB)` — additional call when same role
- `RecordAdjacentDesk(idA, idB)` — call once per in-game day per adjacent pair
- `RecordShippedTogether(idA, idB)` — call when project Finished
- `RecordWaterCoolerSecond(idA, idB)` — per-second tick during overlap
- `RecordConversation(idA, idB)` — per chat
- `TryStartChat(npc) : EmployeeNPC?` — returns chat partner if eligible
- `OnMonthStart()` — apply decay; call from HRManager event chain
- `BestHelpTarget(idleEmployee, project) : (npc, pillar)?` — used by TickProduction

---

## 3. Edge Cases

### Handled by Design

- ✅ **Symmetric scores**: PairKey sorts ids ascending so `(A,B)` and `(B,A)`
  hash identically
- ✅ **Decay never goes negative**: clamp at 0 (friendly-only model)
- ✅ **Returning intern preserves bonds**: alumni record stores the original
  `Id`. On re-hire, the new EmployeeNPC reuses that Id; existing pair scores
  reattach automatically. This is the **alumni payoff cementing**: a returning
  intern keeps their friendships from before.
- ✅ **Best-Friends in multiple roles**: even if A and B are both on a pillar
  via the multi-role rule, R3 fires once per day (not per role), no double-credit
- ✅ **Two friends both finish their pillars**: each runs the help check
  independently; both will look for *other* friends with unfinished pillars.
  If neither finds anyone else, both fall through to Training/Distracted.
  No deadlock.
- ✅ **Adjacent-desk recompute**: distance is checked per-tick from current
  desk positions; if the player rearranges desks via Shop expansion, the
  adjacency mapping naturally updates next tick

### Resolved by Open Question (see §7)

- ❓ **Founder bonds**: does the founder bond with everyone via the same sources,
  or only via gifts? **Recommended: yes, same sources apply** (founder is just
  another `IDevWorker`). Founder still doesn't have a Help mechanic since they
  rarely "finish a pillar early" — they typically aren't on a single project pillar.
- ❓ **Gift cooldown crossing months**: if a player gives a Small gift on Day 30
  and the calendar advances to Day 1 of the next month one frame later, does
  the cooldown reset immediately? Recommended: yes — `LastGiftMonth` comparison
  is integer (`year*12 + month`), so any month change clears it.

### Not Yet Handled (will need design)

- ⚠️ **Chat during Working**: not in MVP. Could be Phase 2 polish — high-bond
  pairs occasionally exchange a brief chat at adjacent desks while Working
- ⚠️ **Helping target is a Remote/Mentor (no desk)**: visual is awkward —
  the helper walks to a stand-in spawn near HRManager. Two options:
  - (a) Helper walks toward the helped NPC's transform (which IS at HRManager)
  - (b) Helper goes to their own desk and contributes silently
  - Recommended (a) for visibility, but the visual will be cluster-y
- ⚠️ **Multiple pillars finished mid-tick**: if A finishes Sound and B finishes
  Graphics in the same tick, the help-ranking depends on iteration order. Make
  iteration deterministic (sort by hire-order) so behavior is predictable
- ⚠️ **Project canceled/abandoned mid-production**: when a project is dropped,
  do partial-work bonds still grow that day? Recommended: yes, same as a
  finished day — the work happened
- ⚠️ **Save load mid-chat**: the conversation should not survive save/load.
  Recommended: on load, snap any `Chatting` state back to `Distracted`

---

## 4. Dependencies

**Technical Dependencies**:
- New `Guid Id` field on `EmployeeNPC` (init-only, generated in `HRManager.HireInterviewSubject`)
- New `Guid Id` constant on `PlayerStats` for the founder
- New `RelationshipManager` Component (singleton, like HRManager)
- New `EmployeeState.Helping` and `EmployeeState.Chatting` values
- s&box `[Sync]` consideration: the bond dictionary is currently single-player;
  if multiplayer is ever revisited, this needs a serializer

**Design Dependencies (other GDDs)**:
- `employees.md` §2.1.11 — cross-references this doc; updated this session
- `gamedev.md` (TBD) — TickProduction must consult RelationshipManager
- `save-system.md` (TBD ADR) — bonds + per-NPC Ids must persist
- `economy.md` (TBD) — gifts are a new money sink to balance against salary outflow
- `ui-hr-panel.md` (TBD) — gift UI + relationship list panel design

**Content Dependencies**:
- Brief speech-bubble overhead icon for chat state (Phase 2 polish, optional)
- Three gift-tier item icons for the gift picker (small / medium / large)
- Animation: NPC turning to face another NPC (smooth-rotate, ≤ 0.5s)
- Animation: NPC standing at a friend's desk while Helping (could reuse Working idle)

---

## 5. Balance and Tuning

### Tuning Knobs

| Parameter | Suggested | Rationale |
|---|---|---|
| `MaxScore` | 100 | Pleasant round number; matches percentage feel |
| `HelpMaxFraction` | 0.30 | Best Friends contribute up to 30% of stat; meaningful but not OP |
| `SameProjectGrowth` | +0.5/day | Steady but slow; needs project completion to feel real |
| `SamePillarGrowth` | +1.5/day | Reward direct collaboration over loose project membership |
| `AdjacentDeskRadius` | 200 inches | Approx 5 m; tune by office model |
| `AdjacentDeskGrowth` | +0.2/day | Background effect, reinforces office layout choices |
| `ShipTogetherBonus` | +10 | One project = solid milestone, not a max-out |
| `BreakOverlapGrowth` | +0.1/sec | Caps at +1.0 per overlap |
| `ChatRadius` | 96 inches | About 2.5 m — close enough to "talk" |
| `ChatChance` | 0.30 | 30% trigger rate when conditions met |
| `ChatDuration` | 4–8 s | Visible but not blocking the simulation |
| `ChatCooldown` | 120 in-game seconds (≈2 in-game days) | Same pair can't chat-spam |
| `ChatBonus` | +2 | Small but additive over time |
| `MonthlyDecay` | -1/month | Slow enough to forgive a vacation; fast enough to discriminate |
| `GiftCooldown` | 1 in-game month per employee | Money-for-bond, but pacing prevents whaling |
| `GiftSmallCost / GiftMediumCost / GiftLargeCost` | $500 / $2,000 / $10,000 | Escalating with budget tiers |
| `GiftSmallEffect / Medium / Large` | morale +5/+10/+20, bond +5/+10/+20 | Linear |

### Balance Concerns to Watch

- ⚠️ **Decay rate vs growth rate**: a pair on the same pillar gains +2.0/day,
  loses 1.0/month. Once they're not co-assigned, decay is real but slow —
  ~3 months of separation drops 30 points (one full level). Tune if playtest
  shows late-game pairs maxing out and never decaying meaningfully.
- ⚠️ **Help mechanic + multi-role efficiency**: the existing 50%/25%/12.5%
  efficiency penalty for stacked roles already discourages putting one
  employee on multiple pillars. The Help mechanic creates a new pressure
  toward "specialize hires + leverage Best Friends" — exactly what a
  social-tycoon should reward. Validate this isn't OP via simulation.
- ⚠️ **Gift money sink vs revenue**: $10,000/month per employee on Large
  gifts could swallow late-game cash. Monitor in playtest; the cooldown
  is the main brake.
- ⚠️ **Founder relationships saturating**: the founder works alongside
  everyone, attends every project; founder bonds will trend toward
  Best-Friends-with-everyone. This may be fine (the founder is Beloved!)
  but should be intentional.

---

## 6. Acceptance Criteria

### What Exists
- ❌ **Nothing — this entire system is a forward design, not yet built.**

### What's Required for MVP
- [ ] `RelationshipManager` singleton component
- [ ] `Guid Id` on `EmployeeNPC` and constant Id on `PlayerStats` (founder)
- [ ] Pair-keyed bond dictionary with sorted-id PairKey
- [ ] All 6 growth sources implemented and tested (R2–R7)
- [ ] Decay (R8) firing on `OnMonthStart`
- [ ] `Helping` and `Chatting` state-machine extensions on `EmployeeNPC`
- [ ] `BestHelpTarget` integration in `GameProjectManager.TickProduction`
- [ ] HR Stats panel: per-employee relationship list with named levels
- [ ] HR Stats panel: gift button + gift picker UI (3 tiers)
- [ ] Save/load roundtrip for bonds + per-NPC Ids
- [ ] Returning intern bond preservation (Id reused on re-hire)
- [ ] One regression test per formula (R1–R12)

### Definition of Done
- [ ] Playtest: a player who hires the same 3 staff for 3 successive projects
  observes their bonds going Strangers → Best Friends and feels the help
  mechanic kick in visibly
- [ ] Playtest: when a Best-Friends employee is fired, the player
  notices their team's productivity drop (because the help mechanic is gone)
- [ ] Simulation: with all six growth sources active, no pair saturates at
  100 if either employee leaves the project for ≥ 4 in-game months
- [ ] The HR Stats panel displays the relationship list in under 100ms even
  at max staff (20 employees)

---

## 7. Open Questions and Follow-Up Work

### Questions Needing User Decision

1. **Decay on/off**: confirm yes (suggested default). Without decay, late-game
   bonds saturate and lose meaning. With decay, the player must keep teams
   together to maintain bonds.
2. **Founder bonds**: confirm yes (suggested). The founder is `IDevWorker`
   and walks the office; should bond like anyone else. Help mechanic does
   not apply to the founder (different control model).
3. **Gift cost tiers**: confirm Small $500 / Medium $2,000 / Large $10,000.
   Tune up or down depending on early-game money curve.
4. **Help-target cap**: should an idle employee help only ONE friend per tick
   (the highest-scoring friend with an unfinished pillar), or split among
   multiple? **Suggested: one only** — keeps the visual "I went to help my
   friend" clean and the math simple.
5. **Phase 2 features list**: confirm scope. Suggested:
   - Employee → employee autonomous gifts on triggers
   - Chat during Working at adjacent desks (high-bond pairs only)
   - Relationship-driven training boost (friends train each other faster)
   - Optional: rivalries via the "rare bad blood" event (per Question 1
     of the Sign question, which we resolved as "friendly-only" for MVP)

### Flagged Follow-Up Work

- [ ] **Decide on decay rate**: confirm `-1/month` or override
- [ ] **Decide on founder bonds**: confirm "yes, same sources" or override
- [ ] **Add `Id` to EmployeeNPC**: design a generation strategy (suggested:
  `Guid.NewGuid()` in `HireInterviewSubject`)
- [ ] **Founder constant Id**: pick a stable constant (e.g.,
  `Guid.Parse("00000000-0000-0000-0000-000000000001")`)
- [ ] **Save/load story**: add this system to the save-system ADR scope
- [ ] **HR Stats panel UX**: design the relationship list rendering and gift UI
- [ ] **Speech bubble VFX**: optional Phase 2 polish for chat visibility
- [ ] **Simulate decay vs growth**: simulation-test 5 in-game years with
  different team compositions to confirm balance

---

## 8. Version History

| Date | Author | Changes |
|---|---|---|
| 2026-04-27 | Claude (forward design) | Initial design from user request: relationships + idle-help mechanic, 6 growth sources, gifts |
| 2026-04-27 | Kaito Tagawa | Confirmed: friendly-only, all 6 growth sources (incl. NPC chats + gifts), linear help curve, HR Stats panel surface |

---

**Next Steps**:
1. Confirm decay default (Q1) — easy
2. Confirm founder bond rule (Q2) — easy
3. Confirm gift cost tiers (Q3) — easy
4. Add §2.1.11 cross-reference to `employees.md` (next, this session)
5. When implementation begins, route through `gameplay-programmer` agent —
   this system has cross-cutting reach into Employees, GameDev, UI, and Save

**Related Skills**:
- `/balance-check` — once implemented, validate growth/decay rates
- `/architecture-decision` — pair-keyed dictionary storage; founder Id constant
- `/design-system` — companion GDDs (gamedev.md, ui-hr-panel.md)

---

*This document was generated by `/reverse-document design Code/Employees/`
follow-up on 2026-04-27. Unlike sibling reverse-doc GDDs, the system itself
does not yet exist in code — this is a forward design.*
