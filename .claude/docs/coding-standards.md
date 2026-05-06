# Coding Standards

- All game code must include doc comments on public APIs
- Every system must have a corresponding architecture decision record in `docs/architecture/`
- Gameplay values must be data-driven (external config), never hardcoded
- All public methods must be unit-testable (dependency injection over singletons)
- Commits must reference the relevant design document or task ID
- **Verification-driven development**: Write tests first when adding gameplay systems.
  For UI changes, verify with screenshots. Compare expected output to actual output
  before marking work complete. Every implementation should have a way to prove it works.

# Save DTO Standards (per ADR-0001)

Classes that participate in `GameSaveManager` persistence — anything passed to
`FileSystem.Data.WriteJson` / `ReadJson<T>` or referenced from `GameSave`,
`MetaSave`, or `SettingsSave` — follow these rules. Enforcement is **manual at
review time**: s&box's compile sandbox blocks `System.Reflection`
(`Assembly.GetTypes`, `Type.GetProperties`, etc.), so an automated boot-time
validator is not buildable in this engine. Reviewers must check new DTOs
against this list when persistence changes land.

- **Public auto-properties only.** Every persisted member is `public T Foo { get; set; }`.
  Plain fields are silently ignored by `WriteJson`/`ReadJson<T>` — they load as
  default values with no error and corrupt the save invisibly.
- **No `init`-only setters.** s&box's deserializer can't write through `init`.
  Use `{ get; set; }` even when the value is conceptually immutable.
- **No `Component` references.** Components are scene objects, not data.
  Persist a stable identifier instead — `Guid` for slots, `string` for hires
  by name, `ItemKind` for items, an enum value, etc.
- **No `GameObject` references.** Same reason as `Component`.
- **DTO classes live in `Code/Save/`** (or named with a `Save` suffix when
  they have to live alongside their owning system for friend-access reasons).
- **Schema changes go through a migrator.** Bumping `GameSaveManager.SchemaVersion`
  requires a matching `MigrateFromVN` method in `GameSaveMigrations`.

# Design Document Standards

- All design docs use Markdown
- Each mechanic has a dedicated document in `design/gdd/`
- Documents must include these 8 required sections:
  1. **Overview** -- one-paragraph summary
  2. **Player Fantasy** -- intended feeling and experience
  3. **Detailed Rules** -- unambiguous mechanics
  4. **Formulas** -- all math defined with variables
  5. **Edge Cases** -- unusual situations handled
  6. **Dependencies** -- other systems listed
  7. **Tuning Knobs** -- configurable values identified
  8. **Acceptance Criteria** -- testable success conditions
- Balance values must link to their source formula or rationale

# Testing Standards

## Test Evidence by Story Type

All stories must have appropriate test evidence before they can be marked Done:

| Story Type | Required Evidence | Location | Gate Level |
|---|---|---|---|
| **Logic** (formulas, AI, state machines) | Automated unit test — must pass | `tests/unit/[system]/` | BLOCKING |
| **Integration** (multi-system) | Integration test OR documented playtest | `tests/integration/[system]/` | BLOCKING |
| **Visual/Feel** (animation, VFX, feel) | Screenshot + lead sign-off | `production/qa/evidence/` | ADVISORY |
| **UI** (menus, HUD, screens) | Manual walkthrough doc OR interaction test | `production/qa/evidence/` | ADVISORY |
| **Config/Data** (balance tuning) | Smoke check pass | `production/qa/smoke-[date].md` | ADVISORY |

## Automated Test Rules

- **Naming**: `[system]_[feature]_test.[ext]` for files; `test_[scenario]_[expected]` for functions
- **Determinism**: Tests must produce the same result every run — no random seeds, no time-dependent assertions
- **Isolation**: Each test sets up and tears down its own state; tests must not depend on execution order
- **No hardcoded data**: Test fixtures use constant files or factory functions, not inline magic numbers
  (exception: boundary value tests where the exact number IS the point)
- **Independence**: Unit tests do not call external APIs, databases, or file I/O — use dependency injection

## What NOT to Automate

- Visual fidelity (shader output, VFX appearance, animation curves)
- "Feel" qualities (input responsiveness, perceived weight, timing)
- Platform-specific rendering (test on target hardware, not headlessly)
- Full gameplay sessions (covered by playtesting, not automation)

## CI/CD Rules

- Automated test suite runs on every push to main and every PR
- No merge if tests fail — tests are a blocking gate in CI
- Never disable or skip failing tests to make CI pass — fix the underlying issue
- Engine-specific CI commands:
  - **Godot**: `godot --headless --script tests/gdunit4_runner.gd`
  - **Unity**: `game-ci/unity-test-runner@v4` (GitHub Actions)
  - **Unreal**: headless runner with `-nullrhi` flag
