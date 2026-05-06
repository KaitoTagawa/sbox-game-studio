---
status: design-only
source: forward-design — extends existing EmployeeNPC state machine
date: 2026-04-28
verified-by: Kaito Tagawa
---

# Employee Ambient Behavior — Design Document

**Status**: Design-Only — fully drafted (sections 1–9), implementation pending
**Source**: Forward design, extending `Code/Employees/EmployeeNPC.cs` state machine
**Date**: 2026-04-28
**Implementation Status**: Not implemented — design pending build

> **⚠️ Design-Only Notice**
>
> This GDD describes ambient NPC behavior — what employees do *between*
> productive work — and the on-floor objects they interact with. It extends
> the existing 4-state machine (Working / Training / Distracted / Idle) into
> a richer system that delivers Pillar 1 ("the studio is a place").
>
> Cross-references:
> - `employees.md` — employee data model, stats, abilities
> - `relationships.md` — chat / helping spec lives there mechanically; this
>   doc owns the *behavior* of those states (visuals, transitions, target
>   selection)
> - `gamedev.md` — Whiteboard's tiny Design tick + Working state are owned there

---

## 1. Overview

**Purpose**: Make the office feel inhabited. Employees aren't just data points
sitting at desks — they walk to a coffee machine when their Focus drains,
gather at a whiteboard to brainstorm, browse the bookshelf during Training,
and have brief conversations with each other at break time. Every visible
behavior is driven by their actual stats, morale, and bonds with coworkers,
so the player reads the office as alive without ever opening a UI.

**Pillar Alignment**:
- **Pillar 1 — The Studio Is a Place**: this is the clearest delivery of the
  pillar. The whole point of being inside the studio in first person is to
  *see* the studio working. Without ambient behavior, the office is a stage
  set with mannequins. With it, the office is a living workplace.
- **Pillar 3 — Hiring Is Meaningful, Not Noise**: each hire becomes a
  recognisable character on the floor — Peyton always at the coffee machine,
  the senior always at the whiteboard, the intern wandering plants.
  Reinforces that the hire was a person, not just a stat block.
- **Reinforces Pillar 4** indirectly: across a 3-year run, the studio's
  *visible character* is part of what makes restarting feel like leaving a
  place, not a save file.

**Scope**:
- ✅ In scope: extending the 4-state machine to track interactive object
  targets, 4 interactive object types (CoffeeMachine / Bookshelf / Plant /
  Whiteboard), chat behavior between two Distracted NPCs, helping behavior
  between bonded employees, behavior selection driven by stats + morale +
  cooldowns
- ❌ Out of scope (deferred to Phase 2+): time-of-day rhythms, per-employee
  personality archetypes, object queue/wait behavior, custom per-object idle
  animations, NPCs reacting to player presence, NPCs reacting to other NPCs
  being fired

**Design Intent** (clarified 2026-04-28):
- Behavior is **legible** — a player should be able to identify a behavior
  at a glance from across the office without inspecting numbers
- Mechanical effects are **small but present** — every behavior produces a
  tiny stat/morale/bond effect so the system isn't pure window dressing, but
  no single behavior is a meaningful optimisation target
- The system **reuses existing infrastructure** — same `WorkplaceAnchor`
  placement, same state-machine tick rate
- **Scene-author-friendly** — adding/removing an object should be drop-a-
  marker simple

## 2. Player Fantasy

The player fantasy of ambient behavior is **"watching your studio be a
studio"** — the quiet, voyeuristic pleasure of running a workplace where
things happen even when you're not looking.

You walk in from the bulletin board side of the office and there's already
a small scene playing out: two of your hires are standing at the whiteboard
mid-conversation, your senior designer is at the coffee machine holding a
fresh cup, the intern is wandering past the plant by the window. Nobody is
performing for you. They're just *being there*, and that's the point.

Over an hour-long session, behaviors become **signature**. Peyton the
Programmer always seems to end up at the bookshelf during the afternoon
stretch — that's just her. The mentor and the senior animator have a
recurring chat at the whiteboard that you start to recognise as their thing.
The new hire who hasn't bonded with anyone yet wanders the plants alone.
**The office develops a cast of characters without a single line of
scripted dialogue.**

This is also the layer that **makes hiring/firing feel weighty beyond morale
ripples** (Pillar 3). Firing the employee who always made coffee at 3pm
leaves a visible hole in the floor's rhythm. Firing the bookworm at the
bookshelf removes a face from the corner. The player feels the loss
spatially, not just numerically.

**The fantasy ladder**:
1. **First five minutes after first hire**: *Wait — they got up. They walked
   to the bookshelf on their own.*
2. **First chat witnessed** (~hour 1): *Did those two just talk to each
   other?*
3. **First "that's just her" moment** (~hour 3): *Of course Peyton's at the
   coffee machine again.*
4. **First emotional firing** (~hour 5+): *I don't want to fire him, he's
   the one who always uses the whiteboard.*

This system **does not generate stories on its own** — there's no scripted
dialogue, no assigned personalities, no relationships beyond bond scores.
The stories are the player's: emergent recognition driven by repeated small
behaviors. That emergence is the deliverable.

## 3. Detailed Design

### 3.1 State Machine

Extends the existing 4-state model in `EmployeeNPC.cs` with **one new
top-level state** — `Helping` — and adds three **sub-modes for Distracted**
to surface object interactions and chats. Total: **5 states**, 3 Distracted
sub-modes.

**States** (5):

| State | Description | Productive? | Tick effect |
|---|---|---|---|
| **Idle** | Default for unassigned / freshly hired pre-walk | No | None |
| **Working** | At assigned desk, full stat output | Yes | Project pillar progress (existing) |
| **Training** | At a Bookshelf object, slow long-term stat gain | No | Stat-up tick (currently scaffolded; see Formulas §F-TRAIN) |
| **Distracted** | Away from desk, taking a break | No | Sub-mode determines the effect |
| **Helping** | Walking to / standing at a friend's desk, contributing to *their* pillar | Partial | Pillar contribution (`relationships.md` §2.1.4 / R1) |

**Distracted sub-modes** (3, stored as a `DistractedMode` field on
`EmployeeNPC`, only meaningful when `State == Distracted`):

| Sub-mode | Description | Tick effect |
|---|---|---|
| **AtObject** | NPC chose an interactive object (CoffeeMachine / Plant / Whiteboard) and stands at it for the state duration | Object-specific (Focus restore, morale tick, etc.) — see §3.2 |
| **InChat** | NPC encountered another Distracted NPC within `ChatRadius`; both pause and face each other | Bond +2 to the pair (`relationships.md` §2.1.5 / R7) |
| **Wandering** | Fallback — no object available, no chat partner; existing behavior (random offset from desk) | None |

**State entry rules**:
- `Idle → Working`: on `Assign()` *(existing)*
- `Working → {Training | Distracted | Working}`: timer roll, weights driven
  by Focus stat *(existing)*
- `Distracted → {AtObject | InChat | Wandering}`: sub-mode chosen on entry —
  see §3.3
- `Distracted → Working`: state timer expiry *(existing)*
- `Working → Helping`: when `relationships.md` §2.1.4 conditions hit (NPC's
  own state would be Distracted, but a bonded friend has an unfinished
  pillar — helping pre-empts the Distract roll)
- `Helping → Working`: friend's pillar completes OR helper's contribution-
  window expires (~1 in-game day)
- `Training → Working`: state timer expiry *(existing)*

**State durations**: 12–30s real-time (random per state), unchanged from
existing `MinStateDuration` / `MaxStateDuration`. Helping uses a longer
1-in-game-day window per `relationships.md`.

**Persistence (save/load)**:
- `State` and `DistractedMode` persist
- `_targetObject` reference persists by `PlacementSlot.Id` (not by
  GameObject ref — survives scene rebuild)
- On load: any mid-chat NPC reverts to `Distracted/Wandering` (chat partner
  not restored — re-rolls naturally)
- On load: any mid-helping NPC reverts to `Working` (friend may have
  moved on)

### 3.2 Interactive Objects

Four object types extend the scene's set of NPC destinations. Each is a
placement marker (extending `WorkplaceAnchorKind`) plus a visual prop chosen
by scene authors. The object's *mechanical* identity lives in the anchor
kind; the *visual* prop is freely swappable.

**WorkplaceAnchorKind extension**:

```csharp
public enum WorkplaceAnchorKind
{
    Training,    // existing — visually a Bookshelf
    Break,       // existing — fallback wander destination
    Coffee,      // NEW
    Plant,       // NEW
    Whiteboard,  // NEW
}
```

#### Coffee Machine

| Aspect | Spec |
|---|---|
| Anchor kind | `Coffee` |
| Visual | Stylised coffee machine prop; NPC stands briefly, then walks away holding a mug attached to their hand |
| State context | `Distracted / AtObject` |
| Mechanical effect | Restores effective Focus by **+5%** (capped at base) for the next state cycle. Tiny morale tick `+0.01`. |
| Selection bias | NPCs with low effective Focus (<60% of base) are 2× more likely to pick this object |
| Cooldown | 90 in-game seconds per NPC |
| Mug prop | Persists for one full state cycle after leaving; cosmetic only |

#### Bookshelf

| Aspect | Spec |
|---|---|
| Anchor kind | `Training` *(existing)* |
| Visual | Bookshelf prop; NPC stands facing it, occasionally tilts head as if reading |
| State context | `Training` *(top-level state, not a Distracted sub-mode)* |
| Mechanical effect | Long-term stat gain — see §F-TRAIN. Each Training cycle adds a small amount to one of the NPC's sub-stats, weighted by their Role. |
| Selection bias | All NPCs equally — Training is rolled in `PickNextState` independent of Focus or morale |
| Cooldown | None at the object level; the Training state timer is the natural cap |

#### Plant

| Aspect | Spec |
|---|---|
| Anchor kind | `Plant` |
| Visual | Office plant prop; NPC stands beside it, looks at it, occasionally turns away |
| State context | `Distracted / AtObject` |
| Mechanical effect | Tiny morale tick `+0.02`. No stat effect. The "stop and breathe" object. |
| Selection bias | NPCs with low morale (<0.6) are 1.5× more likely to pick this object |
| Cooldown | 60 in-game seconds per NPC |

#### Whiteboard

| Aspect | Spec |
|---|---|
| Anchor kind | `Whiteboard` |
| Visual | Whiteboard prop; NPC stands facing it, occasionally gestures or tilts head |
| State context | `Distracted / AtObject`; when 2+ NPCs gather, prefers triggering chat |
| Mechanical effect | Pure ambience by default. **If a project is in `Production`** AND the NPC is on its team, also adds `+0.5` to that pillar's Design progress (one-shot per visit). Encourages placing a Whiteboard near working desks. |
| Selection bias | NPCs on a project in Production prefer it 1.5×; if another Distracted NPC is already there, +1.0× chat-bias on top |
| Cooldown | 120 in-game seconds per NPC |

**Author placement rules** (apply to all new kinds):
- Each anchor is a `WorkplaceAnchor` component with the appropriate kind set
- The anchor's `Position` is where the NPC stands; rotate the marker so the
  NPC faces the prop
- Multiple anchors per kind are fine — selection picks one at random (or
  biased — see §3.3)
- If no anchor of a needed kind exists in the scene, that selection branch
  is skipped (NPC falls through to next-priority object or Wandering)

**Visual placeholders for MVP**: standard s&box props are fine — no custom
modeling required to ship. Custom props are Polish-tier (Phase 2).

### 3.3 Behavior Selection (which object, when)

Triggered on every transition into `Distracted`. The selection algorithm
runs once per state entry and writes to:
- `DistractedMode` — `AtObject` / `InChat` / `Wandering`
- `_targetObject` — `PlacementSlot.Id` (when AtObject); `null` otherwise
- `_chatPartner` — EmployeeNPC reference (when InChat); `null` otherwise
- `_targetPos` — final destination

**Pre-check — global non-working cap**:

Before the algorithm runs, count NPCs not in `Working` state. If
`count(non_working) >= MAX_NON_WORKING`, the NPC's transition is denied:
it stays in `Working` and re-rolls its state duration. This is a hard cap
— overrides all selection biases. Prevents the "everyone leaves their
desk at once" failure mode (e.g., low morale spike across the team).

The cap counts NPCs in **Distracted (any sub-mode), Training, Helping,
or Idle**. The cap does *not* retroactively recall NPCs already in those
states — only gates new transitions.

**Algorithm** (single pass per Distracted entry):

```
1. Build candidate object list:
     For each WorkplaceAnchor with kind in { Coffee, Plant, Whiteboard }:
       - Skip if NPC's per-object cooldown not yet expired
       - Compute weight (see formula below)

2. Build candidate chat list:
     For each other EmployeeNPC with State == Distracted, DistractedMode == AtObject:
       - Skip if WithinChatRadius(this, that) is false
       - Skip if pair chat cooldown not yet expired (relationships.md §2.1.5)
       - Weight = 1 + bond / 100

3. Choose sub-mode (priority order):
     - If chat candidates exist AND random() < ChatChance × bias:  → InChat
         (pick highest-weighted partner; both NPCs transition together)
     - Else if object candidates exist:                            → AtObject
         (weighted-random pick from candidate list)
     - Else:                                                       → Wandering
         (fallback to existing logic — Break anchor or random offset)
```

**Object weighting formula** (per anchor):

```
weight = 1.0
       × selection_bias        // per §3.2 — e.g. ×2.0 for Coffee when low Focus
       × distance_decay        // 1.0 / (1.0 + dist_units / 500)
       × occupancy_modifier    // ×0.5 if another NPC is already there — EXCEPT
                               //   Whiteboard, where presence adds +1.0 (clusters)
```

The 500-unit half-distance prevents NPCs from walking across the whole
office to a coffee machine when there's one nearby. Roughly one room width.

**Chat trigger rules** (consistent with `relationships.md` §2.1.5):
- Only triggered between two NPCs *both* transitioning into Distracted in
  the same tick window
- Bond score weights but doesn't gate — low-bond pairs can still chat
  (that's how bonds form)
- `ChatChance = 30%` baseline when ≥1 valid partner exists
- Bias multiplier: ×1.0 normally; ×2.0 when both are at the same Whiteboard
  candidate
- Pair cooldown: `ChatCooldown = 1 in-game day` — a pair can't chat again
  until then

**Interaction with Helping (§3.5)**:
- Helping selection runs *before* the Distracted roll. If `BestHelpTarget`
  returns a friend with an unfinished pillar, the NPC transitions to
  `Helping` instead of `Distracted`; object selection is bypassed entirely.
- A high-bond NPC with friends in active production will rarely hit
  Distracted — they'll be Helping instead. By design.

**Edge cases handled here**:
- **No anchors of new kinds placed** → falls through to Wandering, original
  behavior unchanged
- **All objects on cooldown for this NPC** → falls through to Wandering
- **NPC's only chat candidate is on chat cooldown** → no chat; falls
  through to AtObject or Wandering
- **Two NPCs simultaneously try to chat (same tick)** → first-resolved
  wins; second NPC re-rolls without that partner
- **Two NPCs simultaneously target the same single-occupancy object** (e.g.
  Plant or Coffee) → first-resolved wins; second NPC re-rolls excluding
  that anchor from its candidate list. If the re-roll has no remaining
  candidates, falls through to Wandering. Whiteboard is exempt — multiple
  NPCs can share one (and gathering there increases chat-trigger bias).
- **3+ NPCs targeting the same single-occupancy object same-tick** → same
  rule cascades: first wins, second and third sequentially re-roll against
  remaining candidates.

### 3.4 NPC ↔ NPC Chat

The chat behavior was specified mechanically in `relationships.md` §2.1.5
(bond growth, cooldown, trigger probability). This section owns the
**behavioral execution** — what the NPCs actually do when a chat triggers.

**Trigger** (recap of selection logic from §3.3):
- Both NPCs are transitioning into `Distracted` in the same tick window
- They're within `ChatRadius = 192 units` of each other (~6 metres in s&box)
- Pair cooldown not active (`ChatCooldown = 1 in-game day` since their
  last chat)
- `ChatChance = 30%` × bias

**Behavior on trigger**:

1. Both NPCs set `State = Distracted`, `DistractedMode = InChat`
2. Each NPC stores the other as `_chatPartner`
3. Each NPC's `_targetPos` is set to a midpoint *between* them, offset
   slightly so they're **stand-distance ~96 units apart** (≈3 metres)
   facing each other — not nose-to-nose
4. They walk to those positions, then rotate to face each other (smooth-
   rotate, ≤ 0.5s)
5. They stand and idle for the chat duration

**Chat duration**: random 4–8 real seconds (rolled once per chat, per
`relationships.md`).

**Chat exit**:
- On duration expiry: both transition to `Distracted/Wandering` and walk
  away to a random offset from each other (so they don't immediately
  re-trigger). Their state timer is reset to a fresh 12–30s, but the
  *remaining* chat-state time is consumed.
- If either NPC is force-fired or otherwise disabled mid-chat: surviving
  NPC reverts to `Distracted/Wandering`. No bond effect awarded (chat
  must complete).
- Chat does not get interrupted by player presence (Pillar 1 — they don't
  perform for the player).

**Visual cues**:
- **MVP**: NPCs face each other, stand idle. No speech bubble, no audio.
- **Phase 2 polish** *(deferred)*: small floating "💬" or speech-bubble
  icon overhead while chatting; subtle ambient murmur sound; mirror-tilt
  heads occasionally.

**Bond effect**: applied **on chat completion**, not on trigger. Per
`relationships.md` R7: `+2.0` to that pair's score. If chat is
interrupted, no effect.

**Edge cases**:
- **One partner walks too far before chat starts** (rare — happens if
  their pre-chat walk goes through geometry) → if either NPC's walk-to-
  midpoint takes >5 seconds, both abort to Wandering. No bond effect.
- **Both partners are at the same Whiteboard** → chat happens *at the
  Whiteboard* rather than the midpoint. Their `_targetPos` becomes the
  Whiteboard anchor's position (one stays, one walks to within
  ChatRadius of the anchor). Visual reads as "two people brainstorming
  at the whiteboard".
- **Save/load mid-chat** → `relationships.md` §Edge Cases already
  specifies: snap to `Distracted/Wandering` on load; partner reference
  dropped.

### 3.5 Helping a Friend

The helping mechanic was specified mechanically in `relationships.md`
§2.1.4 (the `BestHelpTarget` selector, the contribution formula
`friend_stat × 0.30 × (score/100)`, the requirement that helper and target
are bonded). This section owns the **behavioral execution**.

**Trigger** (pre-empts the Distracted roll in §3.3):
- NPC's state-machine timer expires while in `Working`
- A `GameProject` is currently in `Production` phase
- The NPC would otherwise transition to `Distracted` (per existing roll)
- `RelationshipManager.BestHelpTarget(this, project)` returns a non-null
  `(friendNpc, pillar)` tuple — i.e., a bonded friend who is **assigned
  to the project** AND has an **unfinished pillar** the helper can
  contribute to

If those four conditions all hold, NPC transitions to `Helping` instead
of `Distracted`.

**`BestHelpTarget` resolution rules** (delegated to `relationships.md`,
summarised here for clarity):
- Iterates the helper's bonded friends (`bond.score >= R3-R6 thresholds —
  see relationships.md`)
- For each friend, finds their **least-complete pillar** that is not yet
  at 100%
- Picks the highest-bonded friend with the largest gap-to-100%
- Returns `null` if no eligible friend exists — caller falls through to
  normal Distracted roll

**Behavior on trigger**:

1. NPC sets `State = Helping`, `_helpTarget = friendNpc`, `_helpPillar = pillar`
2. `_targetPos = friendNpc.DeskPosition + offset` (helper stands beside
   the friend's desk, **96-unit offset perpendicular to the desk facing**,
   so they're shoulder-to-shoulder)
3. Helper walks to that offset position, rotates to face the same
   direction as the friend (looking *with* them at the work, not at them)
4. Helper idles in the Working idle pose at that position for the help-
   window duration

**Help-window duration**: 1 in-game day (per `relationships.md`). At
normal 1× speed, that's 60 real seconds. At 8× speed, 7.5 real seconds.

**Pillar contribution tick**:
- While `State == Helping`, the helper contributes to `_helpPillar` at a
  rate of `friend_stat × 0.30 × (bond.score / 100)` per second of
  `Production` tick
- Capped: helper's contribution cannot push the pillar above 100% on its
  own (clamps at the pillar boundary)
- The helper does **not** contribute to their own assigned pillar during
  this window — they're helping, not working

**Helping exit conditions** (whichever fires first):
- Help-window expires (1 in-game day) → `Helping → Working`
- `_helpPillar` reaches 100% complete → `Helping → Working` (early success)
- `_helpTarget` is no longer assigned to the project → `Helping → Working`
- Project transitions out of `Production` → `Helping → Working`
- Save/load → `Helping → Working` (per relationships.md §Edge Cases)

**Visual cues**:
- **MVP**: helper stands beside friend's desk in the Working idle pose,
  facing the same direction. Reuse existing Working anim.
- **Phase 2 polish** *(deferred)*: subtle gesture loop (pointing, head-
  tilt) suggesting collaboration; floating "🤝" icon overhead; helper
  occasionally glances at friend.

**Edge cases**:
- **Friend is a non-desk hire** (Mentor / MarketingAgent / RemoteWorker)
  → `BestHelpTarget` skips them
- **Friend is themselves Helping** someone else's pillar → still eligible
  as a help target; helper joins them at the *helped* desk
- **Two helpers picked the same friend's same pillar** → both contribute
  simultaneously. Stacked contribution is allowed — "everyone's helping
  Peyton ship the demo" is a desirable emergent moment
- **Friend's pillar completes mid-walk** → helper aborts to `Working`.
  No credit for time spent walking, only for time actually in `Helping`
- **Helper's own desk is removed mid-help** → helper exits to `Idle`
  after the help window ends; `HRManager` re-seats next tick

**Mutual-exclusion with Distracted**: a single NPC cannot be in both
`Helping` and `Distracted/AtObject` simultaneously. Helping selection
runs first; if it returns a target, object selection is bypassed.

> ⚠️ **Forward-compatibility note (2026-04-28)**: the user has flagged a
> planned rework of the **project progression model** — replacing time-
> accrual on three pillars (Design / Sound / Graphics) with an "ideas earn
> points" scheme. When that rework happens, this section's pillar-
> contribution mechanism will need a parallel rework. Cross-references
> that will need updating: `gamedev.md` (the source of pillars),
> `relationships.md` §2.1.4 (the `friend_stat × 0.30 × (score/100)`
> formula), and `step-5-payoff.md` §F-RS (review score reads pillar
> points). Tracked in `production/session-state/active.md` under Open
> Cross-Cutting Decisions.

## 4. Formulas

> 📌 **Scalability note**: every numerical constant in this section (chance
> thresholds, bias multipliers, distance constants, cooldowns, effect
> magnitudes) is surfaced as a named tuning knob in §7. The formulas
> below show the inline default values for readability; the
> **implementation must read these from a tuning config** (e.g. a
> `BehaviorTuning` resource with `[Property]` fields, or a JSON file
> loaded at game start) so the user can rebalance without touching code
> or shaders. Hot-reload friendly.

### F-DIST — Distract Roll *(existing in code, codified here)*

```
// Global cap gate — applied before the chance roll.
// Prevents mass-departure from desks regardless of individual rolls.
if count(npcs where State != Working) >= MAX_NON_WORKING:
    state = Working (re-roll duration)
    skip everything below

distract_chance = max(MIN_DISTRACT, BASE_DISTRACT - effective_focus × FOCUS_WEIGHT)
                  // effective_focus = EmployeeNPC.Stats.Focus.Average / 1000  (range 0..1)
                  // defaults: MIN_DISTRACT=0.05, BASE_DISTRACT=0.30, FOCUS_WEIGHT=0.25
                  // chance range: 5 % (Focus=1000) → 30 % (Focus=0)

train_chance    = TRAIN_CHANCE   // default 0.10

roll = random [0, 1)
  roll < distract_chance               → state = Distracted
  roll < distract_chance + train_chance → state = Training
  else                                 → state = Working (re-roll duration)
```

### F-OBJW — Object Weight (Distracted/AtObject candidate)

```
weight(anchor, npc) = base_weight
                    × selection_bias(anchor.kind, npc)
                    × distance_decay(npc.position, anchor.position)
                    × occupancy_modifier(anchor)

base_weight = 1.0

selection_bias(Coffee, npc)     = COFFEE_BIAS_LOW_FOCUS   if effective_focus < FOCUS_THRESHOLD else 1.0
selection_bias(Plant, npc)      = PLANT_BIAS_LOW_MORALE   if morale          < MORALE_THRESHOLD else 1.0
selection_bias(Whiteboard, npc) = WHITEBOARD_BIAS_PROJECT if npc.AssignedProject?.Phase == Production else 1.0

distance_decay(p, q) = 1.0 / (1.0 + |p − q| / DISTANCE_HALF)

occupancy_modifier(anchor) =
    if anchor.kind == Whiteboard AND occupants(anchor) >= 1:  +WHITEBOARD_CLUSTER_BONUS  (additive)
    else if occupants(anchor) >= 1:                            ×OCCUPIED_PENALTY
    else:                                                      ×1.0

// defaults:
//   COFFEE_BIAS_LOW_FOCUS=2.0, PLANT_BIAS_LOW_MORALE=1.5, WHITEBOARD_BIAS_PROJECT=1.5
//   FOCUS_THRESHOLD=0.6, MORALE_THRESHOLD=0.6
//   DISTANCE_HALF=500, OCCUPIED_PENALTY=0.5, WHITEBOARD_CLUSTER_BONUS=1.0
```

### F-CHAT — Chat Trigger Roll

```
chat_trigger(npcA) =
  candidates = { N : N is Distracted,
                 |npcA.pos − N.pos| <= CHAT_RADIUS,
                 pair_cooldown elapsed (CHAT_COOLDOWN) }
  if candidates is empty: → no chat

  bias = WHITEBOARD_CHAT_BIAS  if npcA._targetObject == partner._targetObject AND target.kind == Whiteboard
       = 1.0                   otherwise

  if random() < CHAT_CHANCE × bias:
    pick partner ~ weighted_random where w(N) = 1 + bond(npcA, N).score / 100
    → InChat

// defaults:
//   CHAT_RADIUS=192 units, CHAT_COOLDOWN=1 in-game day
//   CHAT_CHANCE=0.30, WHITEBOARD_CHAT_BIAS=2.0
```

### F-HELP — Help Contribution (per second of Production tick)

```
help_contribution(helper, friend, pillar) =
    friend.EffectiveStat(pillar) × HELP_CONTRIB_FACTOR × (bond(helper, friend).score / 100)

// friend.EffectiveStat(pillar) returns stat average × morale, clamped 1..1000
// bond.score is 0..100 (5 named relationship tiers — see relationships.md)
// Result clamped so total pillar progress never exceeds 100 %
//
// defaults: HELP_CONTRIB_FACTOR=0.30
```

### F-OBJEFFECT — Per-Object Effect (applied on visit completion)

```
on Coffee visit complete:
    effective_focus = min(base_focus, effective_focus + COFFEE_FOCUS_RESTORE)
    morale          = min(1.0, morale + COFFEE_MORALE_TICK)
    cooldown[Coffee] = now + COFFEE_COOLDOWN

on Plant visit complete:
    morale          = min(1.0, morale + PLANT_MORALE_TICK)
    cooldown[Plant] = now + PLANT_COOLDOWN

on Whiteboard visit complete:
    if npc.AssignedProject?.Phase == Production:
        npc.AssignedProject.DesignProgress += WHITEBOARD_DESIGN_TICK   // fraction (0..1)
    cooldown[Whiteboard] = now + WHITEBOARD_COOLDOWN

on Bookshelf / Training visit complete:
    see F-TRAIN (owned by employees.md)

// defaults:
//   COFFEE_FOCUS_RESTORE=0.05, COFFEE_MORALE_TICK=0.01, COFFEE_COOLDOWN=90s
//   PLANT_MORALE_TICK=0.02, PLANT_COOLDOWN=60s
//   WHITEBOARD_DESIGN_TICK=0.005, WHITEBOARD_COOLDOWN=120s
```

### F-TRAIN — Training Stat Gain *(placeholder — owned by `employees.md`)*

```
on Training cycle complete:
    pick a sub-stat weighted by npc.Role (Programmer → Logic/Focus/Tools, etc.)
    sub_stat += training_increment   // value TBD in employees.md training section

// Currently scaffolded in code (EmployeeState.Training exists) but
// unimplemented. This GDD consumes the result; it does not define
// training_increment.
```

> ⚠️ All numerical values in F-OBJW (thresholds, biases, distance_decay)
> and F-OBJEFFECT (cooldowns, magnitudes) are first-pass estimates.
> Surfaced as tuning knobs in §7 — adjust there during MVP balancing.

## 5. Edge Cases

Cases not already covered inline in §3.

### 5.1 Empty office / one-person studio

When the founder is the only inhabitant (Year 1, before first hire), there
are no Employee NPCs ticking this system at all — the founder is the
player, not an `EmployeeNPC`. As soon as the first hire lands, that NPC
has no chat candidates and no help targets, so they fall through cleanly
to AtObject (if any anchor exists) or Wandering. No special-case code
needed.

### 5.2 NPC pathing fails (blocked route, geometry change)

`EmployeeNPC.TickMovement` currently does straight-line lerp toward
`_targetPos` — there is no NavMesh today. If the player rebuilds the
office mid-tick and a desk now sits in the path, the NPC will visually
clip through it but still reach the target. Acceptable for MVP.

**Future-proofing**: when a NavMesh is introduced, add a 5-second walk-
timeout per state — if not arrived in 5s of `Time.Delta` accumulated,
abort the current state and re-roll. Same pattern as the chat-walk-
timeout in §3.4.

### 5.3 Game speed (1× / 2× / 4× / 8×)

Cooldowns are stored as **real-time** seconds (`now + 90s`). State
durations (12–30s) are also real-time. This means at 8× speed, cooldowns
elapse 8× faster in *game-time* — an NPC at 8× could visit the coffee
machine every ~11 in-game seconds, which is fine because the game-time
has compressed proportionally.

**No special handling needed**, but tuning should be done at 1× speed;
values that feel right at 1× will feel right at all speeds because
everything compresses uniformly.

### 5.4 Mid-state desk reassignment

If `HRManager` moves an NPC's desk while the NPC is `Distracted` or
`Helping`:
- On state expiry, `ResolveTargetPosition(Working)` reads `_deskPos` —
  which would be stale
- **Fix**: `HRManager.ReassignDesk(npc, newSlot)` updates `_deskPos`
  and `_deskRot` directly. The NPC's next Working state picks up the
  new desk seamlessly.
- If the NPC is *currently Working* during reassignment, snap them to
  the new desk immediately (existing pattern).

### 5.5 Special hires excluded

`Mentor`, `MarketingAgent`, `RemoteWorker` already short-circuit at
`EmployeeNPC.OnUpdate` (line 139: `if (!SpecialHires.TakesDesk(Kind))
return;`). Confirmed: this system applies only to `Regular` and `Intern`
desk-takers. The `Helping` selector also skips them (per §3.5).

### 5.6 Founder excluded

The founder is `PlayerStats`, not `EmployeeNPC`. They don't tick this
state machine, don't appear in chat candidate lists, don't appear in
help target lists. Confirmed by code structure.

### 5.7 Performance — large studios (>30 NPCs)

Per-NPC chat candidate scan is O(N) over Distracted NPCs; per-NPC anchor
scan is O(M) over scene anchors. At a 30-employee Year-3+ studio with
~20 anchors, worst case ~600 distance checks per Distracted entry. State
entries fire once per 12–30s per NPC, so ~1–2 entries/sec total at 30
NPCs.

**Acceptable for MVP** at expected scales. If late-game studios exceed
50 NPCs, switch to a spatial grid for chat candidates (defer to Phase 2).

### 5.8 Save file pre-dating this system

Existing saves were taken when `DistractedMode` and `_targetObject` did
not exist. On load:
- Default `DistractedMode = Wandering` for any NPC currently in
  `Distracted` state
- Default `_targetObject = null`
- Cooldowns initialise empty (NPC may immediately revisit a coffee
  machine — acceptable since save-load already wipes most transient
  state)

### 5.9 Anchor of needed kind exists but is unreachable

If a `Coffee` anchor is placed but inside a locked room or behind
geometry the NPC can't reach (shouldn't happen in practice — the player
places anchors in playable space), the NPC will still be selected for it
and walk-clip. Same as §5.2 — accepted limitation for MVP.

### 5.10 Two NPCs both qualify as each other's `BestHelpTarget`

Helper selection is per-NPC and runs in tick order. NPC-A picks NPC-B as
their help target. NPC-B (one tick later) might also pick NPC-A as
theirs. **This is allowed** — the office reads as "two friends working
together"; both contribute to whichever pillar they targeted. Their own
pillars stall briefly but recover when one finishes Helping.

## 6. Dependencies

### Upstream (this system depends on)

**Code:**
- `Code/Employees/EmployeeNPC.cs` — extends with `DistractedMode`,
  `_targetObject`, `_chatPartner`, `_helpTarget`, `_helpPillar`, per-
  object `cooldowns` dictionary
- `Code/Employees/EmployeeState.cs` — adds `Helping` to the enum
- `Code/Employees/WorkplaceAnchor.cs` (and `WorkplaceAnchorKind`) — adds
  `Coffee`, `Plant`, `Whiteboard` kinds
- `Code/Employees/EmployeeStats.cs` — reads `Focus.Average` for bias
  decisions; reads stat blocks for help contribution
- `Code/Employees/SpecialHires.cs` — `TakesDesk()` already gates non-
  desk hires
- `Code/Employees/HRManager.cs` — desk reassignment hook (§5.4)
- `Code/GameDev/GameProject.cs` and `GameProjectManager.cs` — reads
  `Phase`, `Current.Team`, pillar progress for Whiteboard tick + Helping
  selection
- *Future* `RelationshipManager` (designed in `relationships.md`, not
  yet implemented) — provides `BestHelpTarget(npc, project)`,
  `BondScore(idA, idB)`, pair cooldowns, conversation recording

**Design docs:**
- `design/gdd/employees.md` — owns `EmployeeStats`, the Training stat-
  gain formula (F-TRAIN), and the Role → sub-stat weight table
- `design/gdd/relationships.md` — owns bond score, R1 help formula, R7
  chat bond effect, ChatCooldown
- `design/gdd/gamedev.md` — owns `GameProject.Phase` lifecycle, pillar
  definitions (Design / Sound / Graphics)
- `design/gdd/game-concept.md` — Pillar 1 ("the studio is a place") is
  the reason this system exists

**Scene-author dependencies:**
- Each office scene needs `WorkplaceAnchor` markers placed for at least
  one of each new kind (Coffee, Plant, Whiteboard). Without them, NPCs
  fall through to Wandering — system still functions but loses richness.
- A `Bookshelf` placement at every `Training` anchor is recommended for
  visual consistency, but not enforced.

### Downstream (systems that depend on this)

- **Save system ADR** *(not yet authored)* — must persist
  `DistractedMode`, `_targetObject` (by `PlacementSlot.Id`, not GameObject
  ref), `_helpTarget` (by EmployeeNPC.Id), per-NPC object cooldowns,
  per-pair chat cooldowns
- **`relationships.md`** — its chat-trigger and help mechanics are
  *executed* here; bond effects are *applied* there. Bidirectional
  dependency: design-time both systems must agree on the contract
  (`RelationshipManager.RecordConversation`, `BestHelpTarget` signature)
- **Animation / VFX (Phase 2)** — speech bubble overhead during chat
  (§3.4), mug prop after Coffee visit (§3.2), collaboration gesture
  during Helping (§3.5)
- **Audio (Phase 2)** — ambient murmur during chat, coffee machine sfx,
  page-turn sfx at bookshelf
- **Tutorial / Onboarding** *(not yet authored)* — should highlight that
  the player can place these anchors (or pre-place a starter set in the
  spawn office)

### Cross-references between GDDs

| If you change... | Also update... | Why |
|---|---|---|
| `gamedev.md` project pillar model | `employee-behavior.md §3.5 + §F-HELP` | Help contribution reads pillar progress |
| `relationships.md` R1 / R7 formulas | `employee-behavior.md §F-HELP / §F-CHAT` | Same formulas reproduced for clarity |
| `employees.md` Role definitions | `employee-behavior.md §F-OBJW` | Selection biases use Role |
| `employee-behavior.md` adds new object | `relationships.md` (if bond) / `gamedev.md` (if project) | New objects may have ripple effects |

## 7. Tuning Knobs

All numerical values in this system are **runtime-tunable** via a single
`BehaviorTuning` GameResource loaded by `EmployeeNPC` at startup. The
resource is hot-reload-friendly (s&box `[Property]` fields) — change a
value in the editor, save the resource, and the next state-machine tick
picks it up. No code recompile, no scene reload.

```csharp
[GameResource("Employee Behavior Tuning", "behavior_tuning",
              "Tuning knobs for ambient NPC behavior — see employee-behavior.md §7")]
public sealed class BehaviorTuning : GameResource
{
    // …all fields below, each [Property] with [Range] hints
}
```

### 7.1 State Machine

| Knob | Default | Range | Effect of pushing it up | Effect of pushing it down |
|---|---|---|---|---|
| `MinStateDuration` | 12 s | 4–60 | Slower behavior cadence; office feels calm | Hyperactive office; NPCs jitter between states |
| `MaxStateDuration` | 30 s | 8–120 | Same as above, plus more variance | Less variance; behaviors feel more uniform |
| `WalkSpeed` | 80 u/s | 30–200 | Snappier, less believable | Sluggish, walks dominate the visual |
| `MIN_DISTRACT` | 0.05 | 0.00–0.30 | High-Focus NPCs still wander often | High-Focus NPCs basically never wander |
| `BASE_DISTRACT` | 0.30 | 0.10–0.60 | Low-Focus NPCs always wander; office feels chaotic | Office feels overly diligent |
| `FOCUS_WEIGHT` | 0.25 | 0.10–0.50 | Focus stat strongly gates wandering — high-stat hires *visibly different* | Focus stat barely matters for behavior |
| `TRAIN_CHANCE` | 0.10 | 0.00–0.30 | Bookshelves see heavy traffic; Training-vs-Distracted shifts toward Training | Training feels rare |
| `MAX_NON_WORKING` | 4 | 1–20 | Hard cap on simultaneous Distracted + Training + Helping + Idle. Overrides selection biases — when at cap, NPCs forced to re-roll Working | Aggressive throttle: at 1, only one NPC can be off-desk at any time |

### 7.2 Object Selection (F-OBJW)

| Knob | Default | Range | Notes |
|---|---|---|---|
| `FOCUS_THRESHOLD` | 0.6 | 0.3–0.9 | Below this fraction of base Focus, low-Focus bias kicks in |
| `MORALE_THRESHOLD` | 0.6 | 0.3–0.9 | Below this morale, low-morale bias kicks in |
| `COFFEE_BIAS_LOW_FOCUS` | 2.0 | 1.0–4.0 | How much tired NPCs prefer coffee |
| `PLANT_BIAS_LOW_MORALE` | 1.5 | 1.0–4.0 | How much sad NPCs prefer plants |
| `WHITEBOARD_BIAS_PROJECT` | 1.5 | 1.0–4.0 | How much project-team NPCs prefer the whiteboard |
| `DISTANCE_HALF` | 500 u | 200–1500 | Distance at which weight halves; small = NPCs stay local, large = whole-office wandering |
| `OCCUPIED_PENALTY` | 0.5 | 0.1–1.0 | How strongly NPCs avoid occupied single-occupancy objects |
| `WHITEBOARD_CLUSTER_BONUS` | +1.0 | 0.0–3.0 | Additive bonus when whiteboard already has someone |

### 7.3 Object Effects (F-OBJEFFECT)

| Knob | Default | Range | Notes |
|---|---|---|---|
| `COFFEE_FOCUS_RESTORE` | 0.05 | 0.00–0.20 | Effective-Focus restore per visit (capped at base) |
| `COFFEE_MORALE_TICK` | 0.01 | 0.00–0.10 | Tiny morale bump per visit |
| `COFFEE_COOLDOWN` | 90 s | 15–600 | Per-NPC cooldown to prevent camping |
| `PLANT_MORALE_TICK` | 0.02 | 0.00–0.10 | Morale bump |
| `PLANT_COOLDOWN` | 60 s | 15–600 | Per-NPC cooldown |
| `WHITEBOARD_DESIGN_TICK` | 0.005 | 0.000–0.050 | Fraction added to project Design pillar (0.005 = +0.5 %) |
| `WHITEBOARD_COOLDOWN` | 120 s | 15–600 | Per-NPC cooldown |

### 7.4 Chat (F-CHAT)

| Knob | Default | Range | Notes |
|---|---|---|---|
| `CHAT_RADIUS` | 192 u | 96–512 | Detection radius for Distracted-NPC pair |
| `CHAT_CHANCE` | 0.30 | 0.00–1.00 | Per-eligible-pair trigger chance |
| `WHITEBOARD_CHAT_BIAS` | 2.0 | 1.0–4.0 | Multiplier when both NPCs target same whiteboard |
| `CHAT_COOLDOWN` | 1 in-game day | 0.25–7 in-game days | Per-pair cooldown |
| `CHAT_DURATION_MIN` | 4 s | 2–15 | Real-seconds, lower bound of randomised duration |
| `CHAT_DURATION_MAX` | 8 s | 4–30 | Real-seconds, upper bound |
| `CHAT_STAND_DISTANCE` | 96 u | 48–192 | Face-to-face distance during chat |
| `CHAT_WALK_TIMEOUT` | 5 s | 2–15 | Abort chat if walk-to-midpoint exceeds this |

### 7.5 Helping (F-HELP)

| Knob | Default | Range | Notes |
|---|---|---|---|
| `HELP_CONTRIB_FACTOR` | 0.30 | 0.00–1.00 | Multiplier on `friend_stat × bond/100` per second |
| `HELP_WINDOW_DURATION` | 1 in-game day | 0.5–3 in-game days | How long a helping session lasts |
| `HELP_OFFSET_DISTANCE` | 96 u | 48–192 | Helper's stand-distance from friend's desk |

### 7.6 Tuning Workflow

When balance feels off:

1. **Symptom**: "office feels too chaotic" → raise `MinStateDuration` and lower `BASE_DISTRACT`
2. **Symptom**: "NPCs ignore the coffee machine" → raise `COFFEE_BIAS_LOW_FOCUS` or lower `FOCUS_THRESHOLD`
3. **Symptom**: "chat triggers too rare" → raise `CHAT_CHANCE` or `CHAT_RADIUS`
4. **Symptom**: "Helping makes pillars trivial" → lower `HELP_CONTRIB_FACTOR` (this is the dominant lever)
5. **Symptom**: "NPCs camping coffee machine" → raise `COFFEE_COOLDOWN`
6. **Symptom**: "office feels dead — nothing happens" → lower `MinStateDuration` and `MaxStateDuration` together so transitions fire more often
7. **Symptom**: "more than half the office is on break at once" → lower `MAX_NON_WORKING`. This overrides every other knob — it's the strongest lever for floor productivity.

> 📌 **Recommendation**: keep tuning sessions to **one cluster at a time**
> (state-machine OR selection OR effects OR chat OR helping). Cross-cluster
> changes interact — e.g., raising `BASE_DISTRACT` and lowering
> `COFFEE_COOLDOWN` together compounds. Test each cluster at 1× speed
> before validating at higher speeds.

## 8. Acceptance Criteria

### State Machine Extension

- **GIVEN** an `EmployeeNPC` with no relationships, no project assigned,
  default tuning, **WHEN** Distract roll fires, **THEN** sub-mode is
  selected: `AtObject` if any new-kind anchor exists, `Wandering`
  otherwise.
- **GIVEN** `MAX_NON_WORKING = 4` and 4 NPCs already in non-Working
  states, **WHEN** a 5th NPC's state timer expires, **THEN** that NPC
  remains in `Working` with a re-rolled duration; the cap is honored.
- **GIVEN** a Special Hire (Mentor / MarketingAgent / RemoteWorker),
  **WHEN** `OnUpdate` ticks, **THEN** no state machine runs (existing
  `SpecialHires.TakesDesk` exclusion is preserved).

### Object Selection (F-OBJW)

- **GIVEN** an NPC with `effective_focus < FOCUS_THRESHOLD` and a Coffee
  anchor placed, **WHEN** Distract roll fires, **THEN** that NPC has
  ≥2× weight on Coffee versus Plant or Whiteboard.
- **GIVEN** no anchors of any new kind placed in scene, **WHEN** Distract
  roll fires, **THEN** sub-mode = `Wandering`; behavior identical to
  legacy.
- **GIVEN** two Plant anchors at different distances, **WHEN** Distract
  roll fires, **THEN** the closer Plant has higher final weight
  (distance_decay).
- **GIVEN** a Coffee anchor with one NPC currently `AtObject` there,
  **WHEN** another NPC rolls Distract, **THEN** the second NPC's weight
  on that Coffee is halved (`OCCUPIED_PENALTY`).
- **GIVEN** a Whiteboard anchor with one NPC `AtObject` there, **WHEN**
  another NPC rolls Distract, **THEN** the second NPC's weight on that
  Whiteboard gains `+WHITEBOARD_CLUSTER_BONUS` (additive).

### Object Effects (F-OBJEFFECT)

- **GIVEN** an NPC completes a full `Distracted/AtObject` state at a
  Coffee anchor, **WHEN** state ends, **THEN** their `effective_focus`
  is restored by `COFFEE_FOCUS_RESTORE` (capped at `base_focus`) and
  `morale` ticks up by `COFFEE_MORALE_TICK`.
- **GIVEN** an NPC visits a Whiteboard while their assigned project is
  in `Production`, **WHEN** visit completes, **THEN** the project's
  `DesignProgress` increases by `WHITEBOARD_DESIGN_TICK` (one-shot per
  visit).
- **GIVEN** an NPC just completed a Coffee visit, **WHEN** they enter
  Distracted within `COFFEE_COOLDOWN`, **THEN** Coffee anchors are
  excluded from their candidate list.

### Chat (F-CHAT)

- **GIVEN** two NPCs both transitioning to Distracted within
  `CHAT_RADIUS`, no pair cooldown active, **WHEN** chat roll fires with
  `random() < CHAT_CHANCE`, **THEN** both enter `Distracted/InChat`,
  walk to midpoint, face each other.
- **GIVEN** two NPCs in InChat, **WHEN** chat duration expires,
  **THEN** `RelationshipManager.RecordConversation(idA, idB)` is called
  once (bond `+R7` per relationships.md) and both NPCs transition to
  `Distracted/Wandering`.
- **GIVEN** two NPCs InChat, **WHEN** one is fired or destroyed,
  **THEN** the surviving NPC reverts to `Distracted/Wandering` with
  **no** bond effect awarded.
- **GIVEN** a chat is in progress, **WHEN** save/load triggers, **THEN**
  both NPCs reload as `Distracted/Wandering` with `_chatPartner = null`.
- **GIVEN** a pair has just completed a chat, **WHEN** they're both
  Distracted again before `CHAT_COOLDOWN` elapses, **THEN** they are
  excluded from each other's chat candidate list.

### Helping (F-HELP)

- **GIVEN** an NPC has a bonded friend on the same project with an
  unfinished pillar, **WHEN** the NPC's state-timer would normally roll
  Distracted, **THEN** they transition to `Helping`, walk to
  `friend.DeskPosition + HELP_OFFSET_DISTANCE` perpendicular offset, and
  face the same direction as the friend.
- **GIVEN** an NPC in `Helping` for `HELP_WINDOW_DURATION`, **WHEN**
  window expires, **THEN** they revert to `Working` at their own desk.
- **GIVEN** an NPC in `Helping`, **WHEN** the friend's `_helpPillar`
  reaches 100 %, **THEN** helper exits early to `Working`.
- **GIVEN** an NPC in `Helping`, **WHEN** the project transitions out of
  `Production`, **THEN** helper exits to `Working`.
- **GIVEN** no bonded friends with eligible pillars exist, **WHEN**
  `BestHelpTarget` is called, **THEN** it returns `null` and the NPC
  continues to the normal Distracted roll.
- **GIVEN** an NPC in `Helping`, **WHEN** save/load triggers, **THEN**
  NPC reloads as `Working` with `_helpTarget = null`.

### Tuning Surface

- **GIVEN** any field in the `BehaviorTuning` GameResource is changed,
  **WHEN** the next state-machine tick fires for any NPC, **THEN** the
  new value is honored — no scene reload, code recompile, or game
  restart required.
- **GIVEN** `MAX_NON_WORKING` is lowered to 1 mid-game while 3 NPCs are
  already non-Working, **WHEN** those NPCs' states expire, **THEN** they
  sequentially revert to Working (one at a time can leave); existing 3
  are not retroactively recalled (cap only gates new transitions).

### Performance

- **GIVEN** a 30-employee studio with 20 anchors of various kinds,
  **WHEN** the state machine ticks at 60 fps, **THEN** average frame
  budget impact from this system is `< 0.5 ms` (target).
- **GIVEN** every NPC enters Distracted simultaneously (worst-case
  stress), **WHEN** one tick processes all of them, **THEN** no frame
  exceeds the 16.6 ms 60 fps budget.

## 9. Out of Scope (Phase 2+)

Items deliberately excluded from the MVP build of this system. Each is
captured here so the cuts are visible and intentional, not lost.

### 9.1 Time and Rhythm

- **Time-of-day rhythms** — morning coffee rush, lunch cluster, afternoon
  slump. Requires Hour-of-day tracking (game currently tracks Day/Month/
  Year only). Rejected for MVP because the speed system (1× → 8×) makes
  brief time windows fleeting.
- **End-of-day departure animation** — NPCs walking out at "5pm" then
  back in at "9am". Same time-of-day blocker.
- **Lunch break clustering** — multiple NPCs all heading to a kitchen/
  lunchroom at once. Requires a kitchen object (not in the 4 starter
  objects) and time-of-day.

### 9.2 Additional Objects

The MVP ships with 4 objects (Coffee, Bookshelf, Plant, Whiteboard).
Trimmed from earlier brainstorm:
- Kitchen / lunchroom
- Snack bar
- Smoking area
- Vending machine
- Printer
- Water cooler (referenced in `relationships.md` R6 — currently
  delivered by the Coffee object as a stand-in)
- Plant variants (could add bigger plants, fish tank, etc.)
- Whiteboard variants (different sizes, locations)

These can be added incrementally — the `WorkplaceAnchorKind` enum extends
cleanly, and §3.2's pattern (kind + visual + effect + bias + cooldown)
gives a template.

### 9.3 Personality and Variation

- **Per-employee personality archetypes** — "the bookworm" always picks
  Bookshelf, "the social one" weights chat 2×, "the loner" weights Plant.
  Could be derived from existing stats/abilities but adds combinatorial
  complexity.
- **NPC reactions to other NPCs being fired** — the surviving team
  huddling at the whiteboard the day a coworker is let go. Requires
  event hooks from `HRManager.Fire`.
- **NPC reactions to player presence** — NPCs glancing at the player as
  they walk past, getting "back to work" when the player approaches
  their desk. Explicitly rejected as anti-Pillar-1 (the studio doesn't
  perform for the player).

### 9.4 Behavior Polish

- **Custom per-object idle animations** — currently MVP reuses the
  Working idle pose at all object stands. Phase 2 adds:
  - Coffee: sip-from-mug loop
  - Bookshelf: page-turn / reaching for spine
  - Plant: looking at it, occasional touch
  - Whiteboard: pointing / tilting head / pretend-write
- **Speech bubble during chat** — "💬" overhead, possibly with mirror-
  flip every few seconds to suggest back-and-forth
- **Helping gesture loop** — collaboration animation (helper pointing at
  friend's screen, head-tilt synced)
- **Mug prop after Coffee** — held in hand for one state cycle. Phase 2
  polish — MVP ships without held props.
- **Object queue/wait behavior** — NPC arrives at occupied Coffee,
  queues behind in line. MVP just penalises occupied objects in
  selection (`OCCUPIED_PENALTY`); queueing is a bigger lift requiring
  positional slots.

### 9.5 Audio

- Ambient murmur during chat
- Coffee machine sfx (running brew, mug clink)
- Page-turn / paper rustle at bookshelf
- Whiteboard marker squeak
- Plant leaf rustle (low-priority)

All deferred to the audio polish pass — no MVP audio dependencies.

### 9.6 Performance

- **Spatial grid for chat candidates** — current O(N²) chat-candidate
  scan is fine up to ~50 NPCs. Above that, switch to a spatial grid
  keyed by anchor neighbourhood. Defer until late-game testing actually
  shows >50 NPCs is reachable.
- **State-machine tick budgeting** — MVP ticks every NPC every frame.
  If perf becomes an issue, stagger NPCs across frames (e.g., 1/4 ticked
  per frame, full state machine eval still happens at 12–30s cadence).

### 9.7 Relationship Polish (cross-system)

These are owned by `relationships.md` Phase 2 but listed here because
they have behavioral surface:
- **Rivalries / negative bonds** — visible avoidance behavior, hostile
  chat refusals
- **Employee-to-employee autonomous gifts** — high-bond pair exchanging
  a small prop
- **Chat during Working** at adjacent desks (high-bond pairs only)
- **Relationship-driven training boost** — friends train each other
  faster at the bookshelf

### 9.8 Reactivity to Game State

- **NPCs reacting to project deadline pressure** — visible "crunch"
  behavior when a project nears 100 %
- **NPCs reacting to studio milestones** — celebration cluster when a
  Trophy is won at year-end
- **Morale spike behaviors** — happy NPCs do longer chats; sad NPCs
  barely move from desks

---

## Change Log

| Date | Author | Note |
|---|---|---|
| 2026-04-28 | Claude (skeleton) | Initial empty skeleton; section 1 next |
| 2026-04-28 | Kaito Tagawa + Claude | Sections 1–9 drafted and approved section-by-section. 4 starter objects (Coffee, Bookshelf, Plant, Whiteboard). 5-state machine with Distracted sub-modes. MAX_NON_WORKING cap added per user request. Forward-compat note for planned project-progression rework. |
