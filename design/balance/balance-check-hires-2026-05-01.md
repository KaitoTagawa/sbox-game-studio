# Balance Check — Hires (2026-05-01)

**Pass**: First hires audit since salary mechanics landed.
**Run by**: `/balance-check`
**Status**: Partial pass applied — special-hire wiring deferred.

## Data sources analyzed

- `Code/Employees/Employee.cs`
- `Code/Employees/EmployeeStats.cs`
- `Code/Employees/HRManager.cs`
- `Code/Employees/SpecialHires.cs`
- `Code/Employees/JobPosting.cs`
- `Code/GameManager.cs`
- `design/gdd/employees.md`

## Verdict at audit time: CONCERNS

Salary curve is internally consistent and the bias system is well-designed.
Two real issues: (1) GDD documents salary at ~4× the code's actual values
(stale doc), and (2) the Programmer / SoundDesigner premiums tax the player
for unwired mechanics. ROI validation is blocked until Step 5 ship payoff
lands.

## Fixes applied

| File | Knob | Before | After | Why |
|---|---|---|---|---|
| `Code/Employees/Employee.cs` | `RoleMultiplier(Programmer)` | 1.10× | 1.00× | Producer-Programming routing (gamedev §2.1.14) not wired — salary tax for nothing |
| `Code/Employees/Employee.cs` | `RoleMultiplier(SoundDesigner)` | 1.05× | 1.00× | Ability multipliers (gamedev §2.1.13) not wired — same tax-for-nothing |
| `Code/Employees/HRManager.cs` `PickApplicantKind` | normal pool roll | early-return Regular | Special hires temporarily disabled (Mentor / Marketing / Remote / Intern). Pool-roll preserved as comment for re-enable. |
| `Code/UI/GalleryPanel.razor` `BuildEntries` | per-special-kind loop | single "Coming Soon" placeholder | Stops the Special Hires gallery filter from being an empty shelf while specials are paused |

## New mechanic landed: project-driven energy regen

Energy used to refill purely passively at `EnergyRegenPerSecond = 0.5/sec`.
Now while a project is in `Production` phase, a Focus-scaled bonus is
added on top:

```
rate = EnergyRegenPerSecond + ProjectEnergyBonusPerSecond × clamp(avgTeamFocus / 1000, 0, 1)
```

| State | Avg Team Focus | Effective Regen |
|---|---|---|
| Idle (no project) | — | 0.5/sec |
| Production, weak team (avg Focus 100) | 100 | 0.55/sec |
| Production, mid team (avg Focus 400) | 400 | 0.70/sec |
| Production, senior team (avg Focus 700) | 700 | 0.85/sec |
| Production, legendary team (Focus 1000) | 1000 | 1.00/sec |

`ProjectEnergyBonusPerSecond = 0.5f` is a `const` (not `[Property]`)
because s&box doesn't backfill new `[Property]` fields into existing
scenes and would default to 0, silently killing the bonus. To retune,
edit the constant in `GameManager.cs`.

**Implementation**: `GameProject.AvgTeamFocus` (new) computes the average
Focus across uniquely-assigned workers (counted once even if covering
multiple roles); `GameManager.TickEnergyRegen` reads it when the project
is in Production phase.

**Player-fantasy hook**: a focused team developing a game generates
"buzz" that energizes the founder — they hire faster while the team is
in production, slower when idle. Encourages the player to start a
project early in each in-game month so the energy is flowing when
applicants land.

## Decisions deferred

| Item | Status | Reason |
|---|---|---|
| `design/gdd/employees.md` salary numbers stale (4× off) | Deferred | Multi-section doc rewrite — separate doc pass |
| Salary floor $400 masks bias signal at low end | Skipped | Wait for playtest signal — may not bite |
| Interview energy `+1/year` creep | Skipped | Ditto — projected to bite at Year 5+; not seen yet |
| Special hire salary multipliers (Mentor 1.4, Marketing 1.5, Remote 1.3) | Naturally moot | Specials disabled — multipliers don't fire until they re-enable |
| Hire ROI vs salary (3× target) | Blocked | Needs Step 5 ship payoff implemented to validate |

## Re-enable specials checklist

When ready to bring specials back:
1. Uncomment the pool-roll in `HRManager.PickApplicantKind`
2. Uncomment the per-kind loop in `GalleryPanel.razor` `BuildEntries`
3. Tune special-hire `SalaryMultiplier` values down (1.4-1.5 → 1.20)
   if their abilities still aren't wired
4. Re-run `/balance-check` to confirm the special-hire economy
