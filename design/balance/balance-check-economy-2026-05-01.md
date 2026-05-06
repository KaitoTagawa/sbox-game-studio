# Balance Check — Economy (2026-05-01)

**Pass**: First economy audit since the `StatBoost` system landed.
**Run by**: `/balance-check`
**Status**: Partial pass applied — `StartingMoney` debug override left in
place at user request (long-running debug session).

## Data sources analyzed

- `design/gdd/inventory.md`
- `design/gdd/gamedev.md`
- `design/gdd/step-5-payoff.md`
- `Code/GameManager.cs`
- `Code/Inventory/InventoryCatalogue.cs`
- `Code/Employees/HRManager.cs`
- `Code/Employees/JobPosting.cs`
- `Code/Employees/EmployeeStats.cs`
- `Assets/game.scene` (StartingMoney override)

## Verdict at audit time: CRITICAL ISSUES

The economy can't be tuned cleanly while `Assets/game.scene` overrides
`StartingMoney` to **$10,000,000,000**. With $10B, every economic decision
is invariant — no item is expensive, no salary painful, no posting
unaffordable. User chose to defer this fix to extend the debug window.

## Fixes applied

| File | Item / Knob | Before | After | Why |
|---|---|---|---|---|
| `Code/Inventory/InventoryCatalogue.cs` | `Server.StatBoost` | 8 | 3 | Studio-wide × N employees made Server ~3× more dollar-efficient than an Elite mount |
| `Code/Inventory/InventoryCatalogue.cs` | `LoungeCarpet1.StatBoost` | 2 | 1 | Trim aggregate studio-wide stack |
| `Code/Inventory/InventoryCatalogue.cs` | `LoungeCarpet2.StatBoost` | 3 | 2 | "" |
| `Code/Inventory/InventoryCatalogue.cs` | `Bookshelf1.StatBoost` | 3 | 2 | "" |
| `Code/Inventory/InventoryCatalogue.cs` | `Bookshelf2.StatBoost` | 4 | 2 | "" |
| `Code/Inventory/InventoryCatalogue.cs` | `TV.StatBoost` | 4 | 3 | "" |
| `Code/Inventory/InventoryCatalogue.cs` | `Couch.StatBoost` | 3 | 2 | "" |
| `Code/Employees/JobPosting.cs` | `CareerSite.MonthlyCost` | $1,500/mo | $3,000/mo | Posting tier choice should carry weight in salary economy |
| `Code/Employees/JobPosting.cs` | `Headhunter.MonthlyCost` | $6,000/mo | $12,000/mo | "" |

**Aggregate studio-wide-boost stack**: was ~40 (visible-items) → now ~26.
Sits below a single Elite-mount tier (35), so upgrading mounts now
out-competes maxing decor — making the player's spend choice meaningful
instead of "buy everything".

## Decisions kept by user

| Item / Knob | Recommended | Kept | Reason |
|---|---|---|---|
| `Bin.StatBoost` | 0 (or price↑) | **1** | Bin is the cheapest stat-boosting item by design — first taste of the studio-wide-boost system, pedagogical hook on the very first buy |
| `Assets/game.scene` `StartingMoney` | $1,000 | **$10,000,000,000** | Long-running debug session in flight; will reset before next balance pass |

## Deferred follow-ups (post-debug)

1. Reset `Assets/game.scene` `StartingMoney` to $1,000 (default in C#).
   Editor must be closed during the edit or it reverts.
2. Re-run `/balance-check` against Economy with the override removed —
   actual money flow will be very different and may surface issues this
   pass missed (e.g., "can the player afford the first PC tier in a
   reasonable time?", "does the salary curve outpace early revenue?").
3. **Books economy** ($5,600 lifetime for all 12) — currently trivial.
   Either price-up 5× to ~$28,000 lifetime, or accept books as pure
   flavour. Defer until Step 5 revenue lands so we know the right
   spend-tier.
4. **YearScale uncapped** (1 + 0.20 × years_since_2026) — open question
   in step-5-payoff GDD. Cap at 5× by Year 5 so late-game revenue
   doesn't run away once Step 5 ships.
5. **Salary curve vs revenue curve** — once Step 5 lands and a player
   can actually earn money in-game, validate that a senior hire
   (~$16,800/mo) is recoverable from one shipped game, not three.

## Next planned action

Re-run `/balance-check` after `StartingMoney` is reset, then move on to
Progression and Hires audits.
