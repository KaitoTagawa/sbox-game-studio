# Project Stage Analysis Report

**Generated**: 2026-04-27
**Stage**: Production (Brownfield)
**Stage Confidence**: CONCERNS — Production-level code volume conflicts with Concept-level documentation
**Analysis Scope**: Full project

---

## Executive Summary

This is an actively-developed game-development tycoon built in **s&box** (Facepunch's engine). The codebase is substantial — 29 C# files, ~4,700 lines of code, organized into clear domain folders (Employees, GameDev, Player, UI, Achievements). The implementation is well past the prototype stage; multiple interconnected systems already exist.

However, the project is operating at Concept-stage documentation maturity: no game concept document, no system GDDs, no architecture decision records, no sprint plans, no tests, and the engine itself is not yet recorded in `.claude/docs/technical-preferences.md`. All planning and intent currently lives in the code and the developer's head.

**Current Focus**: Active feature development on existing systems (Employees, GameDev project management).
**Blocking Issues**: Documentation absence makes it impossible for any agent or future contributor to make informed design or architectural decisions without re-reading the entire codebase.
**Estimated Time to Documentation Parity**: 2–4 sessions of reverse-documentation work.

---

## Completeness Overview

### Design Documentation
- **Status**: 0% complete
- **Files Found**: 0 documents in `design/`
  - GDD sections: 0 files in `design/gdd/`
  - Narrative docs: 0 files in `design/narrative/`
  - Level designs: 0 files in `design/levels/`
- **Key Gaps**:
  - [ ] **`game-concept.md`** — No high-level pitch, no genre/pillars/fantasy on record. Every design conversation will require re-explaining the game.
  - [ ] **`systems-index.md`** — No mapping of which systems exist, their owners, or their dependencies.
  - [ ] **System GDDs** — No mechanical specs for Employees, GameDev, Shop, Achievements, or Player. Balance values, formulas, and rules live only in code.

### Source Code
- **Status**: ~70% (relative to a complete tycoon — substantial, but no tests and missing systems likely)
- **Files Found**: 29 C# files in `Code/` (note: s&box convention, not the template's `src/`)
- **Major Systems Identified**:
  - ✅ **Employees** (`Code/Employees/`) — 11 files — Most developed system: Employee, EmployeeNPC, EmployeeAbility, EmployeeStats, EmployeeKind, EmployeeState, EmployeeDesk, WorkplaceAnchor, JobPosting, HRManager, SpecialHires
  - ✅ **GameDev** (`Code/GameDev/`) — 5 files — Project management: GameProject, GameProjectManager, GameGenre, GameDevRole, IDevWorker
  - ✅ **Player** (`Code/Player/`) — 2 files — PlayerStats, PlayerAbility
  - ✅ **Achievements** (`Code/Achievements/`) — 2 files — Achievement, Achievements
  - ⚠️ **UI** (`Code/UI/`) — 1 file (Notifications) — Likely under-developed for a tycoon UI surface
  - ⚠️ **Editor** (`Editor/`) — 2 files (Assembly, MyEditorMenu) — Custom editor tooling exists but is sparse
  - ✅ **Root systems** — GameManager, GameMenu, GameSpeed, Settings, Shop, Gallery
- **Key Gaps**:
  - [ ] **Save/Load** — No `Save.cs` or persistence file visible. May be in-engine, but not obvious.
  - [ ] **Test directory** — `tests/` is empty.
  - [ ] **`MyComponent.cs`** — Default s&box scaffolding still in repo; either purpose it or remove it.

### Architecture Documentation
- **Status**: 0% complete
- **ADRs Found**: 0 decisions documented in `docs/architecture/`
- **Coverage**:
  - ❌ **Engine selection** — s&box chosen but not recorded in technical-preferences or as an ADR. Why s&box vs alternatives is not on record.
  - ❌ **Component architecture** — s&box's component model is implicitly used but not justified.
  - ❌ **Save system** — No design decision visible.
  - ❌ **UI framework** — s&box has Razor-based UI; usage pattern not documented.
  - ❌ **Time/simulation model** — `GameSpeed.cs` exists but the simulation tick model is not specified.
- **Key Gaps**:
  - [ ] **ADR-001: Engine Choice (s&box)** — needed to justify the non-template engine and document its constraints
  - [ ] **ADR-002: Save System** — tycoons require robust saves; absence is a launch blocker
  - [ ] **ADR-003: Simulation Tick Model** — fast-forwarding tycoon time needs explicit rules

### Production Management
- **Status**: 5% complete (review-mode set, nothing else)
- **Found**:
  - Sprint plans: 0 in `production/sprints/`
  - Milestones: 0 in `production/milestones/`
  - Roadmap: Missing
  - Review mode: ✅ `lean` (set during /start)
- **Key Gaps**:
  - [ ] No work tracking — features in progress are invisible to any incoming agent or collaborator
  - [ ] No release target — undefined what "done" looks like

### Testing
- **Status**: 0% coverage (no test framework configured)
- **Test Files**: 0 in `tests/`
- **Coverage by System**: N/A — no tests exist
- **Key Gaps**:
  - [ ] **Logic tests for Employee/GameDev formulas** — tycoons live and die by tuning; untested formulas guarantee balance regressions
  - [ ] **Save/load roundtrip tests** — losing player progress is unforgivable in this genre
  - [ ] **CI configuration** — no automated test gate

### Prototypes
- **Active Prototypes**: 0 in `prototypes/`
- **Archived**: 0
- **Key Gaps**: None — this is a real project, not an experiment.
- **Note**: Root-level `CCGS Skill Testing Framework/` directory exists but is a separate template-testing artifact, not a game prototype.

---

## Stage Classification Rationale

**Why Production (Brownfield) — with CONCERNS confidence?**

The project hits the heuristic threshold for Production stage (10+ source files in active development) by a wide margin (29 files, ~4,700 LOC, multiple integrated systems). However, the customary upstream artifacts that should accompany Production-stage code — a game concept doc, system GDDs, ADRs, sprint plans — are entirely absent. Code exists without its planning context.

**Indicators for Production stage**:
- 29 C# source files across 6 organized system folders
- Inter-system dependencies visible (HRManager hires for GameDev projects; Employees consume PlayerStats/economy)
- s&box project files (`game2.sbproj`, `game2.sln`) are configured and buildable
- Custom editor tooling exists (`Editor/MyEditorMenu.cs`)
- User confirmed: "actively in development"

**Why CONCERNS, not PASS**:
- Documentation maturity is at Concept stage (0% across every doc category)
- Engine is not even recorded in `technical-preferences.md`
- s&box is outside the template's officially supported engines (Godot/Unity/Unreal), so engine-specialist agents will not have native expertise
- No tests means refactoring or rebalancing is high-risk

**Next stage requirements (Polish)**:
- [ ] Documentation parity: at minimum a game concept and GDDs for the two largest systems (Employees, GameDev)
- [ ] At least 3 ADRs covering engine, save system, and simulation model
- [ ] Test framework configured with logic tests for core formulas
- [ ] Sprint planning capturing in-flight work

---

## Gaps Identified (with Clarifying Questions)

### Critical Gaps (block progress)

1. **Engine not recorded in template config**
   - **Impact**: All template skills and agents read `.claude/docs/technical-preferences.md` to know what engine they're working with. Currently it says `[TO BE CONFIGURED]`. Every agent invocation will either ask or guess.
   - **Question**: Should `/setup-engine` record s&box as a custom engine, or do you want to map it to the closest-supported engine (Unity, since both are C#) for agent routing purposes?
   - **Suggested Action**: Run `/setup-engine` with a custom s&box configuration. Add a knowledge-gap warning that s&box-specific patterns are not in the model's training data.

2. **No game concept document**
   - **Impact**: Without a written concept, every design discussion restarts from zero. The "what game is this" question has no canonical answer.
   - **Question**: Want me to reverse-document the concept from the code (employee tycoon premise inferred from Employees + GameDev + Shop), or do you want to author it from scratch?
   - **Suggested Action**: `/reverse-document concept Code/` to produce a draft `design/gdd/game-concept.md` — you review and correct.

3. **No system GDDs for any of the 6 implemented systems**
   - **Impact**: Balance values, rules, and formulas live only in code. Any tuning conversation requires re-reading source. Future you (in 6 months) will not remember why values are what they are.
   - **Question**: Which system would benefit most from documentation first — Employees (largest, 11 files) or GameDev (most central, 5 files)?
   - **Suggested Action**: Reverse-document Employees first (highest file count = highest cognitive load), then GameDev.

### Important Gaps (affect quality/velocity)

4. **No save system ADR**
   - **Impact**: Tycoons are unplayable without robust saves. If the save format isn't designed deliberately, every new system added now risks breaking compatibility later.
   - **Question**: Does a save system already exist that I haven't found, or is it not yet implemented?
   - **Suggested Action**: If unimplemented, write `/architecture-decision` to design it before adding more systems. If implemented, reverse-document the existing approach as ADR-002.

5. **No tests**
   - **Impact**: With ~4,700 LOC and active development, every change carries regression risk. Formula tuning (employee productivity, project completion) is especially fragile.
   - **Question**: Has s&box's test runner been chosen, or do you need to pick one? (s&box supports unit testing via attributes; specific framework needs setup.)
   - **Suggested Action**: `/test-setup` once engine is configured.

6. **No sprint planning**
   - **Impact**: In-flight work is invisible. If you stop and resume in two weeks, you'll lose the thread.
   - **Question**: What are you actively building right now? (That's our first sprint.)
   - **Suggested Action**: `/sprint-plan` to capture current work.

### Nice-to-Have Gaps (polish/best practices)

7. **Default scaffold file (`MyComponent.cs`) still in repo**
   - **Impact**: Minor — clutter signal.
   - **Question**: Is this used anywhere, or is it leftover from `sbox new`?
   - **Suggested Action**: Delete if unused; rename + repurpose if it's actually a real component.

8. **Editor tooling sparse**
   - **Impact**: A tycoon with this much data benefits enormously from custom editor inspectors. `Editor/MyEditorMenu.cs` is a starting point but only one file.
   - **Question**: Is editor tooling on the roadmap, or are you fine with default inspectors?
   - **Suggested Action**: Defer until production planning, but flag for future sprint.

---

## Recommended Next Steps

### Immediate Priority (Do First)

1. **Run `/setup-engine`** — record s&box as the engine, add knowledge-gap warning, populate platform/input fields
   - Suggested skill: `/setup-engine`
   - Estimated effort: S (15–30 min, mostly Q&A)

2. **Run `/reverse-document concept`** — produce `design/gdd/game-concept.md` from code
   - Suggested skill: `/reverse-document concept Code/`
   - Estimated effort: M (1–2 hours, you review the draft)

### Short-Term (This Sprint/Week)

3. **Run `/reverse-document design Code/Employees/`** — produce the Employees GDD (largest system)
   - Estimated effort: M

4. **Run `/reverse-document design Code/GameDev/`** — produce the GameDev GDD
   - Estimated effort: M

5. **Run `/architecture-decision` for engine choice** — capture s&box selection rationale (ADR-001)
   - Estimated effort: S

6. **Run `/sprint-plan`** — capture currently in-flight work
   - Estimated effort: S

### Medium-Term (Next Milestone)

7. **Reverse-document remaining systems** — Shop, Achievements, Player, UI
8. **Run `/test-setup`** — pick s&box test framework, scaffold tests/ directory
9. **Author save system ADR** — `/architecture-decision` for ADR-002
10. **Run `/architecture-review`** — validate coverage once 3+ ADRs exist

---

## Special Note: s&box Engine Compatibility

The CCGS template officially supports **Godot 4, Unity, and Unreal Engine 5**. s&box is not on the list. Practical implications:

| Concern | Workaround |
|---|---|
| No `sbox-specialist` agent | The C# language path is closest — use general-purpose agents and validate against s&box docs at https://sbox.game/dev/doc |
| Path conventions differ (`Code/` vs `src/`) | Update `.claude/docs/directory-structure.md` references after `/setup-engine` |
| s&box's `Component` model isn't in training data | All component-architecture suggestions need human review against s&box patterns |
| s&box uses Razor for UI, not standard Unity/Godot UI | UI agents will need explicit prompting about Razor |
| s&box updates ship frequently | Pin a specific s&box version in `docs/engine-reference/sbox/VERSION.md` (mirroring how Godot 4.6 is pinned) |

**Recommendation**: After `/setup-engine`, manually create `docs/engine-reference/sbox/VERSION.md` with the current s&box version pinned and a knowledge-gap warning, mirroring the existing Godot reference.

---

## Follow-Up Skills to Run

Based on gaps identified, in priority order:

- `/setup-engine` — record s&box configuration **(do first)**
- `/reverse-document concept Code/` — create the game concept doc
- `/reverse-document design Code/Employees/` — create the Employees GDD
- `/reverse-document design Code/GameDev/` — create the GameDev GDD
- `/architecture-decision` — record engine choice as ADR-001
- `/sprint-plan` — capture in-flight work
- `/test-setup` — scaffold testing once engine is configured
- `/adopt` — once GDDs exist, audit format compliance

---

## Appendix: File Counts by Directory

```
design/
  gdd/           0 files
  narrative/     0 files
  levels/        0 files

Code/                      29 files (s&box convention, not src/)
  Achievements/             2 files
  Employees/               11 files
  GameDev/                  5 files
  Player/                   2 files
  UI/                       1 file
  (root)                    8 files

Editor/                     2 files (custom editor tooling)

docs/
  architecture/             0 ADRs
  engine-reference/godot/   1 file (VERSION.md — but engine is s&box, not Godot)

production/
  sprints/                  0 plans
  milestones/               0 definitions
  review-mode.txt           ✅ set to 'lean'

tests/                      0 test files (directory may not exist)
prototypes/                 0 directories
```

---

**End of Report**

*Generated by `/project-stage-detect` skill*
