# ADR-0001: Save System

## Status
Accepted

## Date
2026-04-30

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | s&box 1.0 (rolling release, build pinned 2026-04-27) |
| **Domain** | Core / Persistence |
| **Knowledge Risk** | HIGH — engine launched after LLM training cutoff (May 2025). Save APIs verified 2026-04-30 via sbox.game/dev/doc and must be re-verified before implementation. |
| **References Consulted** | `docs/engine-reference/sbox/VERSION.md`; `https://sbox.game/dev/doc/assets/file-system`; `https://sbox.game/api/Sandbox.FileSystem.Data`; `https://sbox.game/dev/doc/systems/file-system/` |
| **Post-Cutoff APIs Used** | `Sandbox.Storage.CreateEntry`, `saveEntry.Files.WriteJson`, `saveEntry.SetMeta`, `saveEntry.SetThumbnail`, `Storage.GetAll`, `FileSystem.Data.WriteJson`, `FileSystem.Data.ReadJson<T>`. All are 1.0-era APIs and have not been exercised by this codebase yet. |
| **Verification Required** | Round-trip a trivial save/load through `Sandbox.Storage` and confirm Property-only serialization behaviour before the implementation epic begins. Confirm thumbnail capture method (likely `Screen.CaptureScene` or similar — TBD). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | All future ADRs touching persisted state (relationships, meta-progression, cloud sync) |
| **Blocks** | Launch — every shippable build needs this. Per-system stories that touch state already cite "save-system.md (TBD ADR)" as a dependency in `employees.md`, `relationships.md`, `gamedev.md`. |
| **Ordering Note** | Implementation epic for ADR-0001 is the highest-priority item once the ADR is Accepted. |

## Context

### Problem Statement
The studio simulation runs entirely in-memory. Closing the game loses the
calendar, money, every hire, every inventory item, every active project,
and every progression flag. A tycoon is unshippable in this state. All
existing GDDs cite a "save-system.md (TBD ADR)" as a load-bearing
dependency, and `production/project-stage-report.md` flags it as a launch
blocker.

The decision to make now is the **shape of the persistence layer**, not
its UI: which engine API to commit to, how state is aggregated across the
many components that own it, how schema evolves over the life of the
project, and which categories of state live in which file.

### Constraints
- **Engine**: s&box 1.0 — single-player tycoon, no network state to reconcile.
- **Stack**: C# / .NET 8 — JSON via the engine's built-in serializers.
- **Property-only serialization**: `WriteJson` and `ReadJson<T>` persist
  *public properties only*. DTOs cannot use plain fields.
- **No fields with Component references**: components are scene objects,
  not data — DTOs store identifiers (Guids, names, enum kinds), never
  live references.
- **Hot-load aware**: code reload must not corrupt an in-flight save.
  Saves are written atomically (Sandbox.Storage handles the temp-file
  rename) and reads are immutable snapshots.
- **Pre-1.0 schema churn**: every dev build today changes one or more DTO
  shapes. The save format must be versioned from day one or every internal
  build silently breaks the previous build's saves.

### Requirements
- Persist the full per-run state with enough fidelity to resume on the
  same in-game day with no perceptible loss.
- Multi-slot saves with player-meaningful labels (studio name, year,
  money, day) so the player can keep parallel playthroughs.
- Three independent persistence files: per-run state, meta-progression
  (Hall of Fame + carryover unlocks), and settings (audio, time
  multiplier, accessibility).
- Schema versioning with forward migration — old saves on newer builds
  must load if at all possible, and explicitly fail-with-a-message if not.
- Autosave on the three highest-stakes moments (month-tick, project
  shipped, scene exit) without blocking the main thread for noticeable
  time.

## Decision

Adopt **Sandbox.Storage** as the per-run save host (multi-slot, with
metadata and thumbnails) and **`FileSystem.Data`** as the simpler host
for the two single-file domains (meta-progression and settings). Aggregate
the per-run state through a single **`GameSave` DTO** composed of typed
sub-DTOs, with each owning system contributing a `ToSave()` /
`LoadFrom(dto)` pair invoked by a central `GameSaveManager`. Version every
file with a `SchemaVersion` int and route old files through a migrator
chain on load.

### Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                          GameSaveManager                             │
│   • SaveRun(slotId)   • LoadRun(slotId)   • DeleteRun(slotId)        │
│   • SaveMeta()        • LoadMeta()                                   │
│   • SaveSettings()    • LoadSettings()                               │
│                                                                      │
│   Triggers: GameManager.OnMonthStart                                 │
│             GameProjectManager.OnGameShipped                         │
│             Scene OnDestroy / Quit-To-Menu                           │
└──────────────────────────────────────────────────────────────────────┘
       │                          │                          │
       ▼                          ▼                          ▼
 Sandbox.Storage             FileSystem.Data            FileSystem.Data
 ───────────────             ───────────────            ───────────────
 Entry "run-{slot}"          "meta.json"                "settings.json"
 ├ Files/run.json   ←── GameSave DTO
 ├ Meta             ←── studio name, year, day, money, hire count
 └ Thumbnail        ←── scene capture (deferred; placeholder OK at MVP)


GameSave (per-run DTO)
├ SchemaVersion : int
├ GameVersion   : string   // build hash for diagnostics; not used for migration
├ SavedAt       : DateTimeOffset
├ Calendar      : CalendarSave   { Day, Month, Year, TimeMultiplier, ActiveSpeed }
├ Economy       : EconomySave    { Money, Energy }
├ Founder       : FounderSave    { Name, Role, Stats, Level, Xp, Abilities[],
│                                  ActiveTraining?, ResearchTopicId,
│                                  DaysIntoCurrentResearch }
├ Hires         : List<EmployeeSave>     // identity, stats, abilities, salary,
│                                        // morale, deskSlotId, kind, bias,
│                                        // SkipFirstSalary, training, research
├ Inventory     : InventorySave  { Counts: Dict<ItemKind,int>,
│                                  Placements: List<{ slotId, kind, variantId? }> }
├ ActiveProject : GameProjectSave?       // null if no project in flight
├ Research      : ResearchSave   { CompletedTopicIds[], UnlockedGenres[] }
├ Achievements  : AchievementsSave       { Earned[], Counters }
├ Relationships : RelationshipsSave      // bonds + interaction counters
└ HRMisc        : HRSave         { ApplicantPool snapshot, IsTutorialDone }


MetaSave (cross-run DTO)        SettingsSave (player prefs DTO)
├ SchemaVersion                 ├ SchemaVersion
├ HallOfFame    : Run[]         ├ AudioMixer    : Dict<bus,float>
└ CarryoverUnlocks : Set<Id>    ├ TimePreference : float
                                └ Accessibility : Dict<flag,bool>
```

### Key Interfaces

```csharp
// Central orchestrator. One per game; lives on the GameManager GameObject.
public sealed class GameSaveManager : Component
{
    public const int    SchemaVersion = 1;
    public const string AutosaveSlotId = "autosave";

    // Per-run
    public bool TrySaveRun( string slotId, out string error );
    public bool TryLoadRun( string slotId, out string error );
    public void DeleteRun( string slotId );
    public IReadOnlyList<RunSlotSummary> ListSlots();

    // Cross-run (single file each)
    public void SaveMeta();
    public void LoadMeta();      // called on game boot
    public void SaveSettings();
    public void LoadSettings();  // called on game boot
}

// Each owning system exposes a ToSave / LoadFrom pair. Not an interface —
// the methods are typed to the system's specific DTO. GameSaveManager
// composes them by name.
public sealed class GameManager
{
    public CalendarSave SaveCalendar();
    public void         LoadCalendar( CalendarSave dto );
    // ... EconomySave too
}

// DTO contract: public class, public properties only, primitive/string/enum
// fields and lists/dictionaries thereof. No Component references.
public sealed class CalendarSave
{
    public int           Day            { get; set; }
    public int           Month          { get; set; }
    public int           Year           { get; set; }
    public float         TimeMultiplier { get; set; }
    public GameSpeedTier ActiveSpeed    { get; set; }
}

// Migrator chain — one method per version step. Invoked on load when
// the file's SchemaVersion is below GameSaveManager.SchemaVersion.
public static class GameSaveMigrations
{
    public static GameSave MigrateFromV1( GameSave dto ) => dto; // placeholder
    // public static GameSave MigrateFromV2( GameSave dto ) { ... }
    // ...
}
```

**File layout**:
- Per-run save (one Sandbox.Storage entry per slot):
  - Slot id `"autosave"` is reserved for the autosave path.
  - Slot ids `"slot-1"` .. `"slot-N"` are player-named manual saves.
  - Inside each entry, the canonical file is `Files/run.json` containing the
    full `GameSave` DTO.
  - `SetMeta(...)` carries the surface fields (studio name, year, day, money,
    hire count) so the load screen can render slot summaries without parsing
    the JSON body.
- Meta-progression: `FileSystem.Data` → `meta.json`.
- Settings: `FileSystem.Data` → `settings.json`.

**Autosave triggers**:
- `GameManager.OnMonthStart` → `SaveRun("autosave")` (~ once per 5 real minutes
  at 1× speed).
- `GameProjectManager.OnGameShipped` → `SaveRun("autosave")` (high-stakes,
  once per project completion).
- Scene exit / quit-to-menu → `SaveRun("autosave")` once on `OnDestroy`.

**Manual saves**: written to a player-chosen named slot via the load/save UI
(scoped to a separate ADR — this ADR commits to the data layer only).

## Alternatives Considered

### Alternative 1: FileSystem.Data single-save
- **Description**: One `save.json` written through `FileSystem.Data.WriteJson`.
  No multi-slot, no metadata, no thumbnails.
- **Pros**: Smallest possible API surface. One file, one method, no engine
  ceremony. Trivial to migrate.
- **Cons**: Tycoons routinely run multiple parallel studios — losing that to
  a single-save flow erodes a core genre expectation. Slot UI would still
  end up wrapping `FileSystem.Data`, reinventing what `Sandbox.Storage`
  already provides.
- **Rejection Reason**: The save-UI cost is the same either way; the engine's
  multi-slot system already includes thumbnails and metadata that the load
  screen wants.

### Alternative 2: Per-component `ISaveable` interface
- **Description**: Every component implements `Save(BinaryWriter)` /
  `Load(BinaryReader)`. `GameSaveManager` walks the scene and dispatches
  calls.
- **Pros**: Components own their own format; no central DTO file; new
  systems are self-registering.
- **Cons**: Schema is scattered across N files, making migration nearly
  impossible — you can't see the shape of the save in one place. Binary
  format hides corruption until load. Hot-reload reorders components and
  risks load-order mismatches.
- **Rejection Reason**: Migration is the make-or-break property here; scattering
  the schema undermines it from day one.

### Alternative 3: Reflect on `[Property]` fields / scene snapshot
- **Description**: Re-use s&box's prefab-JSON serialization and snapshot the
  whole scene's component graph as the save.
- **Pros**: Zero DTO authoring. Anything marked `[Property]` is automatically
  persisted.
- **Cons**: Couples the save format directly to scene structure — renaming a
  Property breaks every old save with no migration path. The s&box prefab
  property-serialization quirk we already have on file
  (`memory: sbox_property_serialization_quirk.md`) means new fields silently
  default rather than backfilling from existing data, which would corrupt
  saves invisibly. Cannot persist anything that isn't `[Property]` (e.g.,
  pure-C# data structures like `HRManager._applicantPool`).
- **Rejection Reason**: The known property-serialization quirk makes this
  approach actively dangerous for this project.

## Consequences

### Positive
- Multi-slot saves match tycoon-genre expectations and let the player
  experiment without nuking their main run.
- Centralised DTO schema is migratable: every breaking change goes through a
  named migrator that's reviewable in one file.
- Three independent persistence files cleanly separate concerns: a corrupt
  per-run save doesn't take settings or Hall of Fame down with it.
- Autosave on month/project/exit covers the highest-pain loss windows.
- `Sandbox.Storage`'s built-in metadata + thumbnail support means the load
  screen has the data it needs without parsing the per-run JSON body.

### Negative
- DTO duplication: every persisted system maintains a DTO and a
  `ToSave/LoadFrom` pair. New systems pay a one-time tax to opt in.
- Migrators must be written for every breaking change. The cost of
  adding/removing a stat or DTO field is no longer free.
- Property-only serialization rules out using fields in DTOs — easy to
  forget; will require a coding-standards update (see Migration Plan).
- `Sandbox.Storage` is post-LLM-cutoff API surface; until at least one
  end-to-end save round-trip ships, there's residual risk that the engine
  diverges from documented behaviour.

### Risks

- **Risk**: `Sandbox.Storage` semantics differ from the docs (e.g.,
  thumbnail capture API absent, metadata field limits).
  **Mitigation**: Implementation epic begins with a 1-day spike that
  round-trips a stub `GameSave` and confirms metadata + thumbnail behaviour.
  ADR is unblocked from Accepted only after the spike succeeds.

- **Risk**: Hot-reload during a save window leaves a half-written file.
  **Mitigation**: `Sandbox.Storage` writes atomically (temp + rename); for
  `FileSystem.Data` writes, do the write-temp / rename pattern manually.

- **Risk**: A schema change ships without a matching migrator and silently
  loads the wrong field types.
  **Mitigation**: `GameSaveManager.SchemaVersion` is a `const`. CI test
  loads a representative save from each previous version and asserts no
  exceptions — the test fails the moment a migrator is missing.

- **Risk**: Player saves a corrupt run by autosaving during a buggy state
  (e.g., null `ActiveTraining` recurs).
  **Mitigation**: Manual-save slots are never overwritten by autosave —
  the autosave slot is reserved. A defensive validator runs on load and
  surfaces any DTO that fails a sanity check; the player can roll back to
  a manual save.

- **Risk**: DTO accidentally references a `Component` and the JSON serializer
  loops or throws.
  **Mitigation**: A unit test reflects over every DTO in the assembly and
  asserts properties are limited to value types, strings, enums, lists, and
  other DTOs. Fails CI if a Component reference creeps in.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `gamedev.md` | "save-system.md (TBD ADR) — project state persistence" | `GameProjectSave` DTO captures active project, pillar state, sales tail, marketing. Autosave on `OnGameShipped` ensures shipping is durable. |
| `employees.md` | "save-system.md (TBD ADR) — required before this system is shippable" | `EmployeeSave` DTO captures every hire (identity, stats, abilities, salary, morale, desk, training/research, returning-intern). Hires are restored from list — `EmployeeNPC` GameObjects are reconstructed on load. |
| `relationships.md` | "bonds + per-NPC Ids must persist" | `RelationshipsSave` DTO carries the per-NPC bond graph keyed by stable employee Guid. Per-NPC Id is part of `EmployeeSave`. |
| `step-5-payoff.md` | "meta-progression carryover persists … Hall of Fame" | Meta-progression is a separate `meta.json` — survives Restart, never overwritten by per-run save. |
| `inventory.md` | (implicit — placement must persist) | `InventorySave` carries per-item counts + per-`PlacementSlot` occupant. Slot Guids are scene-stable; load re-applies `TryPlace` against the current scene's slot graph. |
| `game-concept.md` | "Author save-system ADR — `/architecture-decision`" | This document. |

## Performance Implications
- **CPU**: JSON serialization of a steady-state save (~60 hires, full inventory)
  estimated ≤ 50 ms. Run on a fire-and-forget Task to keep main-thread under
  16 ms frame budget.
- **Memory**: DTOs allocate transiently during save; expected steady-state
  growth is bounded by hire/inventory counts, both tycoon-scaled (low MB).
- **Load Time**: Per-run load is a one-shot read + reconstruction; budget
  ≤ 250 ms for the largest expected save (full studio, year 5+).
- **Network**: N/A — single-player only.

## Migration Plan
This ADR introduces persistence to a previously in-memory codebase; there
is no existing save format to migrate from. Implementation steps:

1. **Spike** (Sprint 1, day 1): round-trip a stub `GameSave` through
   `Sandbox.Storage`. Confirm metadata, thumbnail, and atomic-write
   behaviour. If any assumption fails, revise the ADR before proceeding.
2. **DTO scaffolding**: define `GameSave` and every sub-DTO (one PR per
   subsystem to keep review small).
3. **`GameSaveManager`**: build the central orchestrator with
   `TrySaveRun` / `TryLoadRun` and the autosave triggers wired up.
4. **`MetaSave` + `SettingsSave`**: add the two single-file domains.
5. **Coding-standards update**: add to `.claude/docs/coding-standards.md`:
   "DTOs persisted by `GameSaveManager` use public properties only — no
   fields, no `Component` references."
6. **CI guard**: assembly-walking unit test that fails if any DTO violates
   the rule.
7. **UI** (separate ADR): load/save panel design — out of scope here.

## Validation Criteria

- A new game can be saved, the app closed, the app reopened, and the same
  in-game day resumed with all of: founder stats + level + abilities, every
  hire's identity/stats/morale/desk, every inventory item placed where it
  was placed, the active project's progress, achievements earned, and
  research progress.
- Two parallel manual save slots can be maintained without one corrupting
  the other.
- The autosave slot is overwritten exactly on month-tick, project-shipped,
  and scene-exit, and never by a manual save action.
- A save written under `SchemaVersion = 1` continues to load after the
  version bumps to 2, via a migrator that handles the breaking change.
- A corrupt save file (truncated JSON, mismatched type) is detected on
  load and surfaces a player-facing error rather than crashing.
- Save-write latency ≤ 50 ms in profiler under steady-state studio
  (60 hires, full inventory).

## Related Decisions
- Save UI/UX (load/save panel layout, slot naming, delete confirmation,
  thumbnail rendering) — **separate ADR pending**.
- Cloud save / Steam Cloud sync — **out of scope; future ADR if pursued**.
- Achievement persistence model (per-run vs cross-run) — already decided in
  `step-5-payoff.md` (per-run; persistent unlocks live in `MetaSave`).
- s&box prefab `[Property]` serialization quirk
  (`memory: sbox_property_serialization_quirk.md`) — informs why
  Alternative 3 was rejected.
