# Balance Check — Progression (2026-05-01)

**Pass**: First progression audit since achievement system stabilised.
**Run by**: `/balance-check`
**Status**: Partial pass applied — Server-related fixes deferred to future
feature work; genre-slot expansion deferred (would have been too large an
impact on first-ship moment per user direction).

## Data sources analyzed

- `Code/Achievements/Achievements.cs`, `Code/Achievements/Achievement.cs`
- `Code/Inventory/InventoryCatalogue.cs`
- `Code/Employees/JobPosting.cs`, `Code/Employees/SpecialHires.cs`
- `Code/GameSpeed.cs`
- `Code/GameDev/GameGenre.cs`, `Code/GameDev/GameProjectManager.cs`
- `Code/ResearchTopic.cs`
- `design/gdd/employees.md`, `design/gdd/gamedev.md`,
  `design/gdd/step-5-payoff.md`

## Verdict at audit time: CONCERNS

Achievement → reward graph is mostly well-shaped, but several achievements
fired with no in-world payoff (Hire8, ShipFirstGame, FullStaff, etc.).
Money-tier cascade (one mid-tier ship trips Earn1k → Earn1M simultaneously)
flagged but accepted as the player's "graduation moment". Server / ServerRack
content still Hidden = true and remains so — flagged as future work, not
addressed in this pass.

## Fixes applied

| File | Change |
|---|---|
| `Code/Achievements/Achievement.cs` | New `MoneyBonus` field on the `Achievement` record. |
| `Code/Achievements/Achievements.cs` `Unlock` | Direct `Money += bonus` (skips `RecordMoneyEarned` so funding ≠ earnings and can't cascade Earn-N). Notification now reads "Achievement Unlocked — [Name] — funding +$[N]". |
| `Code/Achievements/Achievements.cs` Registry | Bonus values set per achievement (see table below). |

## MoneyBonus grant schedule

| Achievement | Bonus | Justification |
|---|---|---|
| FirstHire | $200 | Tiny "first hire incentive" — token reward |
| Hire5 / Hire8 / Hire10 | $1,000 / $2,500 / $5,000 | Staffing-grant feel; gives Hire8 a tangible reward without unhiding Server |
| FullStaff | $1,500 | "Full house" milestone bonus |
| Players100 / Players10k / Players1M | $500 / $5,000 / $50,000 | Investor confidence as audience grows |
| DiscoverFirstAbility / DiscoverAllAbilities | $200 / $5,000 | "Talent agency referral" reward |
| ShipFirstGame | **$300** | Per user — small "do something with this" pocket money after the first ship |
| Ship3Games / Ship10Games | $2,000 / $10,000 | Milestone publisher payouts |
| Earn1k / Earn10k / Earn100k / Earn1M | $0 | No bonus — would feel circular ("earn $1k, here's $1k") |

Total possible "funding" income across a full run (every achievement
unlocked): **~$83,000**. Significant in early/mid game, marginal late-game
where shipped games revenue dwarfs it. Right shape.

## Decisions deferred / declined

| Recommendation | Stance | Reason |
|---|---|---|
| Un-hide Server (`Server.Hidden = false`) | **Declined** | User: Server is a future feature; leave hidden until system designed |
| `GenreExpansionAchievement = ShipFirstGame` | **Declined** | User: ShipFirstGame shouldn't have that big of an impact — $300 cash is the right size of payoff |
| Add intermediate Earn5k / Earn50k / Earn500k tiers | Skipped | Not requested; current cascade is acceptable as "graduation moment" |
| Re-enable desk progression cap | Skipped | Still in debug mode; reset when `StartingMoney` is reset |
| Un-hide ServerRack | Declined | Same as Server — future feature work |

## Notes for future passes

- **Hire8 still has Server as its named target** in description — left
  the description as-is so the Hire8 → Server promise stays in code as a
  reminder for when Server lands.
- **First-ship UX**: with $300 cash + the shipped-game revenue both
  landing on the same frame, the player gets a nice double-pop. No need
  for a bigger structural unlock at this stage.
- **Cascade still possible** at first big ship: Earn1k → Earn10k →
  Earn100k → Earn1M can still trip together if the first ship clears
  $1M. They'll fire as separate notification toasts but with $0 bonus
  each, so no money cascade chain. Acceptable.
