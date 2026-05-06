---
status: reverse-documented
source: Code/
date: 2026-04-27
verified-by: Kaito Tagawa
---

# Game Concept: game2 (working title)

*Created: 2026-04-27*
*Status: Draft (Reverse-Documented)*

> **Note**: This document was reverse-engineered from the existing implementation
> in `Code/`. It captures current behavior and clarified design intent. Some
> sections are flagged as incomplete where implementation is partial or design
> intent has not yet been pinned.

---

## Elevator Pitch

> It's a **game-development tycoon** where you run a studio from inside it —
> walking the office, hiring developers, and shipping games — set in a near-future
> 2026 where the genres on offer span everything from cozy fishing games to gacha
> and pack openings.

The differentiator from past dev-tycoon games is that the studio is a **physical
place you inhabit**, not a menu skin over an office diorama.

---

## Core Identity

| Aspect | Detail |
| ---- | ---- |
| **Genre** | Management / Tycoon (game-development sim) |
| **Platform** | PC (Steam, standalone via Valve license) — s&box 1.0 |
| **Target Audience** | Tycoon-game fans who want sensory-richer studios than past dev-tycoons offered |
| **Player Count** | Single-player (`.sbproj` currently has Multiplayer set as default boilerplate — should revert) |
| **Session Length** | 30–90 min (calendar runs at 1 real min = 1 in-game day) |
| **Monetization** | Premium (one-time purchase) — TBD |
| **Estimated Scope** | Medium (3–9 months from current state to MVP-shippable) |
| **Comparable Titles** | Game Dev Tycoon, Game Dev Story, Mad Games Tycoon 2 |

---

## Core Fantasy

You are a studio founder building a real games company from a single desk to a
floor full of developers. The fantasy is the **lived-in studio** — you are not
running spreadsheets over a stylised icon of an office. You walk to the HR
board, you watch your team at their desks, you see the office grow around you
as you ship games. The decisions still happen in menus, but the *place* is
constantly there underneath them.

That physical-presence layer is what sets this apart from the menu-driven
ancestors of the genre.

---

## Unique Hook

**The studio is a place, not a screen.**

Past game-dev-tycoon titles (Game Dev Tycoon, Game Dev Story, Mad Games Tycoon)
treat the office as an isometric diorama you supervise from above. This one
puts you *inside* the office in first person, walking past your team as they
work. Modal UIs (HR, Shop, Project) still carry the heavy decisions — but the
ambient experience between modals is genuinely diegetic.

The "and also" test: *"It's like Game Dev Tycoon, **and also** you actually walk
around your studio in first person, watching your hires at their real desks."*

---

## Player Experience Analysis (MDA Framework)

### Target Aesthetics (What the player FEELS)

| Aesthetic | Priority | How We Deliver It |
| ---- | ---- | ---- |
| **Challenge** (mastery) | 1 | Money pressure, monthly salaries, morale system, achievement-gated progression |
| **Expression** (creativity) | 2 | Every project = a unique cocktail of 1–N genre tags + 5 role assignments + 3 time-allocation sliders |
| **Discovery** (exploration) | 3 | 17 genres + game-dev abilities + employee specials all gated behind achievements; you *find* what's next |
| **Sensation** (sensory) | 4 | Physical office walk-through; s&box's Source 2 visuals; furniture from the Shop populating real space |
| **Submission** (low-stress) | 5 | Speed tiers up to 8× let the player zone out and watch the studio run itself |
| **Fantasy** (role-play) | 6 | Studio-founder role; founder has unique perks (ChiefRecruiter, MidnightOilCoder) regular hires can't roll |
| **Narrative** | N/A | No scripted story — emergent narratives from the studio's history |
| **Fellowship** | N/A | Single-player; multiplayer is anti-pillar |

### Key Dynamics (Emergent player behaviors)

- Players will **deliberately overwork the founder** in the early game (energy regen + interview cost) and shift to staff-driven production as the studio grows
- Players will **chase synergy combos** in genre selection (Mobile + Idle + Gacha for revenue stacking; 2D + Roguelike + SinglePlayer for low-effort steady output)
- Players will **hoard returning interns** as bargain seniors (intentional design — first intern is guaranteed to return)
- Players will **sacrifice pillars** on early projects (0% time on Sound or Graphics) to ship cheap proof-of-concepts faster
- Players will **stack abilities** — Polymath + multi-role assignment to dodge the 50% efficiency penalty

### Core Mechanics (Systems we build)

1. **Calendar simulation** — 1 real min = 1 in-game day; monthly tick drives salary, posting fees, intern contracts, alumni returns
2. **Studio HR pipeline** — job posting tier → applicant inbox → energy-gated interview → hire/pass → desk assignment → monthly salary cycle
3. **Game project flow** — 3-page setup (title+genre / role assignment / time allocation) → real-time production tick across three pillars (Design / Sound / Graphics) → ship → payoff (incomplete)
4. **Genre system** — 17 tags across 5 categories, pair-additive synergy table, per-tag effort & revenue multipliers, achievement-gated unlocks
5. **Founder progression** — 6-stat block + level/XP + founder-only ability slots distinct from regular hires
6. **Achievement-driven progression** — unlocks for game speed tiers, posting tiers, special hires, genres, desks

---

## Player Motivation Profile

### Primary Psychological Needs Served

| Need | How This Game Satisfies It | Strength |
| ---- | ---- | ---- |
| **Autonomy** | Players choose what genres to ship, who to hire, what to focus on | **Core** |
| **Competence** | Mastery shows up in money trajectory, GOTY trophies, achievement unlocks | **Core** |
| **Relatedness** | The studio is your team — but it's parasocial; no real social connection | **Minimal** |

### Player Type Appeal (Bartle Taxonomy)

- [x] **Achievers** — money totals, achievement list, GOTY trophy wall, Year-3 Run Score + Hall of Fame
- [x] **Explorers** — 17 genres, special hires, ability table, all gated; figuring out what unlocks what is real gameplay
- [ ] **Socializers** — single-player; minimal appeal
- [ ] **Killers/Competitors** — no PvP, no leaderboards (yet); minimal appeal

### Flow State Design

- **Onboarding curve**: First in-game month — founder alone, $1,000 budget, only Bulletin Board posting unlocked. Player runs first interview within ~5 min real-time. First hire establishes the desk-unlock loop.
- **Difficulty scaling**: Years 1–2 are wage-pressure; year 3+ shifts to *what to ship*. Late-game (5+ years) the studio runs itself unless the player chases ambitious synergies.
- **Feedback clarity**: Notifications system surfaces every state change (hire, fire, posting cancellation, project complete, achievement). Money + energy bars are always visible.
- **Recovery from failure**: Missed payroll → morale drop, not bankruptcy. Quitters are replaceable. No "game over" state — failures are recoverable.

---

## Core Loop

### Moment-to-Moment (30 seconds)

The player checks the **Notifications panel** for new applicants, opens the
**HR Menu** to skim the inbox, picks a candidate to interview (spending energy),
and accepts or rejects them based on the revealed stats. Or they're inside an
active **Game Project** modal, dragging assignments around and watching pillar
progress bars tick up. Between modals, they walk between desks in the office.

### Short-Term (5–15 minutes)

**A single in-game month**: pay salaries on Day 1, decide whether to upgrade the
posting tier, run 1–3 interviews, possibly start or finish a game project.
Months feel like punctuated events because the OnMonthStart hook drives the
biggest costs and unlocks.

### Session-Level (30–120 minutes)

**Ship 1–3 games**: a player session typically spans 1–2 in-game years. The
arc: post a job, hire 2–3 staff, configure a project, watch it ship, collect
revenue, decide whether to expand the office or chase a new genre. End-of-session
a player has visible numerical progress (money, achievements, year on the
calendar) and a clear next thing to work toward.

### Long-Term Progression

- **Studio expansion**: 1 desk → many; furniture from Shop populates the office
- **Year curve**: 2026 → 2031+ — applicant pool stat ceiling rises (`GetProgressionFactor`)
- **Genre catalogue**: starts with 5 always-unlocked tags; opens up to all 17 via achievements
- **Founder levels**: linear XP curve (1k → 2k → 3k…), each level grants an ability slot from 10 founder-only perks
- **Special hires**: Mentor / Marketing Agent / Remote Worker / Intern unlock progressively
- **Speed tiers**: Normal → 2× → 4× → 8× gated by Hire 5 / Earn 100k / Earn 1M

### Retention Hooks

- **Curiosity**: Locked genres show in the Gallery; players want to see what's behind each gate
- **Investment**: Returning interns are a multi-month payoff; alumni track creates a "your past staff come back stronger" story
- **Social**: N/A
- **Mastery**: Genre synergy tables reward optimisation; the right tag stack with the right team can ship a Legendary-quality game

---

## Game Pillars

### Pillar 1: The Studio Is a Place

The office is a real space the player walks through, not a menu skin. Modals
carry the heavy decisions, but the diegetic layer underneath is constantly
present. Past dev-tycoons treat the studio as a top-down icon; this one treats
it as a building.

*Design test*: When deciding whether to add a new feature as a modal vs an
in-world interaction, prefer in-world wherever the action benefits from
spatial presence (e.g., approaching a hire's desk to fire them, walking up to
a real bulletin board to change posting tier).

### Pillar 2: Every Project Is an Authored Mix

A game in this game is a unique cocktail of 1–N genre tags + 5 role
assignments + 3 time-allocation sliders. The combinatorial space is the point.
"You shipped a 2D Roguelike Single-Player Story Action title with the founder
on Game Director and a Senior Sound Designer on Sound" should describe a
specifically *yours* game.

*Design test*: Resist any change that flattens the genre / role / time-cost
combinatorial space. Don't add "presets" or "auto-assign" buttons that bypass
the choice — let the choice itself be the gameplay.

### Pillar 3: Hiring Is Meaningful, Not Noise

Interviews cost energy. Stats reveal only after interview, not before. Each
hire is a deliberate cost — money, energy, desk slot — not a button-spam.
Special hires (Mentor, Intern with alumni return) extend the "people matter"
loop with multi-month payoffs.

*Design test*: Resist any change that lets the player auto-process the inbox
or batch-hire. The friction in the HR pipeline IS the game.

### Pillar 4: A Scored Run, With a Choice to Continue or Restart

Every three in-game years (Year 4, 7, 10, ...) the studio's run is scored —
trophies, lifetime revenue, games shipped, and average review quality combine
into a single Run Score. The player then chooses: **Continue** the studio as
it stands, or **Restart** with meta-progression carryover (unlocked genres
and speed tiers persist; the studio itself wipes). The Hall of Fame records
every past run forever.

There is no failure state — even a 0-score run is a valid result, and the
Restart option means the player always has somewhere to go. Failures inside a
run (missed payroll, mass quitters) are recoverable; the run ends on the
player's terms or at a Run Score event, never on a "game over" screen.

*Design test*: When designing failure states, default to recoverable. When
designing late-game systems, ask whether they read as content for *this* run
or as carryover for the *next* run — and be deliberate about which. No
bankruptcy condition, no studio-closure cutscene.

### Anti-Pillars (What This Game Is NOT)

- **NOT satirical about the games industry** — Gambling, Gacha, and Pack Opening
  genres are mechanically neutral. The player can chase predatory revenue without
  the game judging them. *(Confirmed: this is mechanically neutral, not commentary.)*
- **NOT multiplayer** — `.sbproj` has Multiplayer set as default boilerplate
  and should revert. The studio is *yours*, single-player.
- **NOT a hard roguelike** — Restart is *opt-in*, not forced. A run can last
  indefinitely if the player keeps choosing Continue at each Year-3 Score
  event. The roguelike-adjacent layer is the meta-progression carryover, not
  permadeath. Save system is not yet designed but assumes persistence within
  a run and across runs.
- **NOT a story-driven game** — no scripted plot, no cutscenes. Narrative is
  emergent from the studio's history.

---

## Inspiration and References

| Reference | What We Take From It | What We Do Differently | Why It Matters |
| ---- | ---- | ---- | ---- |
| **Game Dev Tycoon** | Genre+topic combination; player-as-studio fantasy; year-based progression | First-person physical office instead of isometric diorama | Validates the core fantasy and combinatorial design as commercially proven |
| **Game Dev Story** | Hire-train-ship loop; genre-topic synergies | Modern setting (2026); s&box visuals; physical-office layer | Validates the loop's longevity (Kairosoft's tycoons sustained for 15+ years) |
| **Mad Games Tycoon 2** | Office building, desk-by-desk expansion; staff specialisations | Walk-through perspective rather than top-down | Validates depth-of-management as engaging tycoon content |

**Non-game inspirations**:
- Real game-studio tours and behind-the-scenes documentaries (Double Fine,
  Naughty Dog, Failbetter Games) — the lived-in feel of a working studio
- Workplace simulation games like *Two Point Hospital*, *Project Hospital* —
  for the balance of management depth + atmospheric building

---

## Target Player Profile

| Attribute | Detail |
| ---- | ---- |
| **Age range** | 20–40 |
| **Gaming experience** | Mid-core to hardcore tycoon/sim players |
| **Time availability** | 1–2 hour evening sessions; weekends for longer runs |
| **Platform preference** | PC, primarily Steam |
| **Current games they play** | Game Dev Tycoon, Mad Games Tycoon 2, RimWorld, Two Point Hospital, Software Inc. |
| **What they're looking for** | A modern dev-tycoon with sensory richness their old favourites lacked — the same management depth, but in a place that feels real |
| **What would turn them away** | Surface-level depth; UI-only experience that fails to leverage the 3D presence; degenerate dominant strategies that flatten the genre system |

---

## Technical Considerations

| Consideration | Assessment |
| ---- | ---- |
| **Recommended Engine** | s&box 1.0 (already chosen). Source 2 + .NET. Outside the CCGS template's officially supported engines — see `docs/engine-reference/sbox/VERSION.md`. |
| **Key Technical Challenges** | (1) **Save system** — not yet designed, blocks shippability. (2) **First-person + modal hybrid** — locking player controls when modals open works but needs polish. (3) **NPC AI** — desk-bound work animations not yet evaluated. (4) **s&box knowledge gap** — engine launched after May 2025 LLM cutoff, every API needs verification against sbox.game/dev/doc. |
| **Art Style** | 3D stylised — Source 2 default fidelity, leveraging s&box's hotload-friendly art pipeline |
| **Art Pipeline Complexity** | Medium — office furniture, employee NPC variations (driven by `AppearanceSeed`), UI panels in Razor |
| **Audio Needs** | Moderate — office ambience, UI feedback, notification sounds, optional music |
| **Networking** | None (single-player). `.sbproj` currently has Multiplayer set as default boilerplate — revert when convenient. |
| **Content Volume** | 17 genre tags (extensible), 25 game-dev ability slots (5×5), 10 employee abilities, 10 founder abilities, 4 speed tiers, 3 posting tiers, 4 special-hire kinds, ~8 shop items |
| **Procedural Systems** | Applicant generation (names, stats, abilities, salaries, appearance seeds); intern alumni queue |

---

## Risks and Open Questions

### Design Risks

- **Run Score weights untested** — the F-RUN composite (trophies + revenue + games shipped + quality) has not been simulated. Weights may produce a degenerate optimal strategy (e.g., shipping shovelware purely to inflate the games-shipped term). Needs a simulation pass before MVP — flagged in `step-5-payoff.md` Open Question #9.
- **Pillar 1 (physical-presence) under-delivered** — if modals carry every meaningful decision and the office walk-through is purely cosmetic, the unique hook collapses. Risk grows the more decisions stay in modals.
- **Genre dominance** — the synergy table favours Mobile+Gacha+Idle (revenue 2.0 × 0.8 × 1.1 with synergies +0.30 + +0.30) over diverse picks. May produce a degenerate optimal loadout.
- **Returning intern over-power** — Senior tier + max abilities + 70% salary discount may make the alumni loop the dominant hiring strategy. Worth testing.

### Technical Risks

- **No save system** — blocks shippability. Tycoon players will not tolerate session-only runs of this scope.
- **s&box engine knowledge gap (HIGH)** — engine launched April 28 2026, training data cutoff May 2025. Every component lifecycle, networking attribute, Razor pattern, and asset pipeline assumption needs verification.
- **First-person + modal interplay polish** — the player-lock mechanism (`UseLookControls`, `UseInputControls`, `Mouse.Visibility`) is functional but not yet tested across the full UI surface.

### Market Risks

- **Saturated subgenre** — game-dev-tycoon is a small but well-served niche (Game Dev Tycoon, Mad Games Tycoon, Software Inc., Computer Tycoon). Differentiation hinges on Pillar 1 (physical-presence) actually landing.
- **s&box platform risk** — s&box 1.0 launched April 28 2026; platform reach and player base are unproven for shippable indie titles. Early-mover advantage but no track record yet.

### Scope Risks

- **"Step 5+" payoff loop unwritten** — projects can finish but there's no revenue / review / payout phase implemented. This is the loop's payoff and it's missing.
- **Office expansion content** — only 8 shop items, only desks as architectural unlocks. The "studio grows" promise needs more content to land.
- **Founder progression depth** — XP/level/perks are scaffolded but training, retraining, and per-stat growth aren't yet implemented.

### Open Questions

1. **Save system architecture** — JSON-on-disk, s&box `[Sync]` persistence, or full FileSystem write? *Resolution path*: `/architecture-decision` for save system before next major system addition.
2. **Project payoff phase** (revenue, reviews, GOTY) — code references "Step 5+". *Resolution path*: `/design-system project-payoff`.
3. **NPC behavior** — are employees just standing/sitting at desks, or do they wander, take breaks, react to morale? *Resolution path*: `/design-system employee-behavior`.
4. **Pillar 1 manifestations** — what specific physical-presence interactions are committed for MVP? Walking up to a real bulletin board to change posting tier? Approaching a desk to fire? Whiteboard interaction for project setup? *Resolution path*: list candidates in next sprint, prototype top 1–2.

---

## MVP Definition

**Core hypothesis**: Players find the *physical-presence office + tycoon-management* hybrid more engaging for 30+ minute sessions than a pure menu-driven dev-tycoon.

**Required for MVP**:
1. Complete game-project loop including ship → revenue → GOTY ("Step 5+") — without this the core gameplay loop has no payoff
2. Save / load system — non-negotiable for tycoon players
3. At least one diegetic in-world interaction that replaces a modal (e.g., walking to a bulletin board to set posting tier) — proves Pillar 1 is more than scenery
4. Year-3 Run Score event firing + meta-progression carryover working (unlocked genres and speed tiers survive Restart)
5. Office visual polish: at least 5–8 unlockable desks, all 8 shop items placeable in the world

**Explicitly NOT in MVP** (defer to later):
- Multiplayer / co-op
- Console / mobile / web ports
- Procedural employee animations beyond stand/sit
- Marketing campaigns as a deep system (currently MarketingAgent is a hire kind only)
- Genre-demand forecasting (`TrendForecaster` ability scaffolded, not delivered)
- Training rooms / per-stat growth UI (`SelfTaught` ability scaffolded, not delivered)
- Custom office layouts (player-placeable walls / floor tiles)
- Localisation

### Scope Tiers (if budget/time shrinks)

| Tier | Content | Features | Timeline |
| ---- | ---- | ---- | ---- |
| **MVP** | 1 office, 8–10 desks, 17 genres, 4 special hires, ship-loop closed | Save, Year-3 Run Score event + meta-progression carryover, 1 diegetic interaction proving Pillar 1 | TBD — depends on save-system design |
| **Vertical Slice** | + Office furniture variety, + employee training, + marketing | All MVP + 3+ diegetic interactions | +6 weeks past MVP |
| **Alpha** | + Founder progression deep, + project payoff polish, + Gallery polish | All Vertical Slice features | +12 weeks past Vertical Slice |
| **Full Vision** | + Custom office layouts, + 30+ shop items, + 30+ genres | + multi-team / multi-floor expansion | TBD |

---

## Next Steps

- [x] Engine configured (s&box) via `/setup-engine`
- [ ] Get concept approval from creative-director (skipped under `lean` review mode — escalate at `/gate-check`)
- [x] **Endgame defined** — Year-3 Run Score + soft-restart with meta-progression carryover (see `step-5-payoff.md`)
- [ ] Reverse-document the major systems: `/reverse-document design Code/Employees/`, then `Code/GameDev/`
- [ ] Author save-system ADR — `/architecture-decision`
- [ ] Run `/map-systems` to decompose this concept into formal systems index
- [ ] Run `/gate-check` Concept → Systems-Design before authoring full system GDDs
- [ ] Revert `.sbproj` `GameNetworkType` to single-player (cleanup, low priority)
