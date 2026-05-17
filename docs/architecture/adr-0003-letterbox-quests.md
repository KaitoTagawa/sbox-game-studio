# ADR-0003: Letterbox + Quests + Menu Modal Extraction

## Status
Accepted (2026-05-09)

## Date
2026-05-09

## Last Verified
2026-05-10

## Decision Makers
Kaito Tagawa (creative + technical)

## Summary
The studio loop has shipped, sold, and reviewed games — but no qualitative
feedback from the audience. We add a Letterbox system that produces a
fan letter for ~35% of shipped games (7 in-game days after launch),
surfaces it in a dedicated modal, and gates the first-letter notification
behind an explicit player ack. ~30% of letters become Quest letters that
request a specific genre; shipping a game in that genre fulfils the quest
and multiplies its lifetime sales by 1.20×. Two new GameMenu tiles
(Letterbox 📬, Quest 📜) plus an extraction of the inline Save UI into
its own modal (`SavePanel`) finish the menu's transition to
"every tile opens a fullscreen UI." Save slot count grows from
1 autosave + 1 manual to 1 autosave + 2 manual.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | s&box 1.0 (rolling release, build pinned 2026-04-27) |
| **Domain** | Gameplay / UI / Persistence |
| **Knowledge Risk** | LOW — uses only APIs already exercised by this codebase (Component, PanelComponent, FileSystem.Data, Notifications, OnDayStart event, ShippedGame). No new platform services. |
| **References Consulted** | ADR-0001 (Save System), ADR-0002 (Leaderboards), `Code/Gallery.cs` for the post-ship review-reveal pattern, `Code/Tutorial/TutorialManager.cs` for the sticky-notification ack pattern. |
| **Post-Cutoff APIs Used** | None new. |
| **Verification Required** | Round-trip a v2 → v3 migration with a save that has shipped games. Confirm the first-letter sticky toast clears the moment the Letterbox panel is opened. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Save System) — extends the per-run save with Letterbox sub-DTO. ADR-0002 (Leaderboards) — only for the schema-version chain (we bump v2 → v3). |
| **Enables** | Future "fan community" or "DLC request" content can build on the same letter pipeline. |
| **Blocks** | None — additive. |
| **Ordering Note** | Implementation phases land sequentially (see Migration Plan); each phase ships before the next begins. |

## Context

### Problem Statement
Players have asked for qualitative feedback after shipping a game. The
review system tells them whether a game scored well numerically; nothing
tells them *what about the game resonated*. Game Dev Tycoon's fan-mail
loop is the closest reference — short, in-character notes that make the
audience feel real instead of statistical. The decision now is **how
that feedback flows through the existing systems** (notifications, save,
gallery, achievements) without bloating any of them.

The menu also needs cleanup. Save is currently the only tile that
renders inline content beneath the tile grid; every other tile pops a
fullscreen modal. Adding two new tiles (Letterbox + Quest) is the right
moment to extract Save into its own modal so the menu becomes a
consistent launcher and the now-empty bottom area can be deleted.

### Current State
- `Gallery.RecordShippedGame` already runs at ship time and the
  `OnDayStart` calendar tick is the existing event for "do something N
  in-game days later" (used for review reveals).
- `Notifications` already supports sticky toasts (`duration: 0f`) +
  tag-based clearing — used for the post-first-ship "check your game"
  tip and for the tutorial flow.
- `GameMenuPanel.razor` still has a `.content` div for Save-only inline
  rendering; everything else routes to a modal.
- `GameSaveManager.ManualSlotIds = ["slot-1", "slot-2"]` — slot 1 is
  the autosave, slot 2 is the only manual slot.
- `Achievements` already has the per-stat-record + Evaluate pattern;
  adding letter-count milestones is the same shape as the existing
  Earn-N achievements.

### Constraints
- **Single-player**: no multiplayer auth needed. Letters are generated
  client-side from local game state.
- **Save shape**: every persisted system goes through ADR-0001's
  property-only DTO + migrator pattern. No exceptions.
- **No new external services**: letter generation is template-based
  in-engine. We're not calling an LLM at runtime.
- **Performance**: Letterbox tick runs on `OnDayStart` (≤ once per
  in-game day), not per frame. Letter pool is small.
- **Tutorial-aware**: TutorialManager already gates non-essential
  systems during early phases. The Letterbox / Quest tiles must
  respect that gating.

### Requirements
- **R1**: Each shipped game has a 35% chance (`LetterSpawnChance`) to
  produce a letter, delivered 7 in-game days after the ship date if the
  roll succeeds. Letters are *earned* events — not every ship triggers
  one — so receiving one carries weight. Idempotency guard ensures at
  most one letter per game across save / load / day-fast-forward.
- **R2**: ~30% of letters are quest letters (genre request). Pure RNG,
  no streak protection.
- **R3**: Quest letters choose a genre *other than* the praised game's
  primary genre, from the player's currently-unlocked genres. If no
  alternative is available, the letter falls back to a normal letter.
- **R4**: Open quests stay open across runs of new games until
  fulfilled. The first matching-genre ship consumes ONE quest (oldest
  first) and applies a 1.20× multiplier to that game's `TotalSales`.
- **R5**: First letter ever received (across the run) pushes a sticky
  notification (`duration: 0f`) tagged so opening the Letterbox panel
  clears it. Subsequent letters use a normal duration.
- **R6**: Letter-count achievements at 1 / 10 / 50 lifetime letters.
- **R7**: Save tile becomes a fullscreen `SavePanel` modal. Inline
  content area in `GameMenuPanel.razor` is removed entirely.
- **R8**: Save slot count: 1 dedicated autosave + 2 free manual slots.

## Decision

Build three Components (`Letterbox`, `SaveMenu`, plus the existing
`Gallery` extended) and three PanelComponents (`LetterboxPanel`,
`QuestsPanel`, `SavePanel`). Letter state is the source of truth;
QuestsPanel is a read-only filtered view over letters with `IsQuest`
set. Tile dispatch follows the existing Shop / Inventory / HR / etc.
hand-off pattern.

### Architecture

```
                        ┌─ on day-tick ───────────────────┐
                        │                                 ▼
   GameManager ─────────┤                       Letterbox.TickPostShipLetters
   .OnDayStart          │                       (walks Gallery.ShippedGames,
                        │                        spawns 1 letter per ship at
                        │                        day == ShipDay + 7)
                        ▼
   Gallery.ShippedGames                          │ adds FanLetter to
                        ▲                        │ Letterbox.Letters
                        │                        ▼
   ComputeScoreAndMetrics ──reads─► open quests ─► fulfill match? +20%×TotalSales
                        │
   GameMenu.Activate("Letterbox") ─► Letterbox.SetOpen(true) ─► LetterboxPanel
   GameMenu.Activate("Quest")     ─► Letterbox.SetOpen(true, view: Quests)
                                  OR a separate Quests singleton — see below
   GameMenu.Activate("Save")      ─► SaveMenu.SetOpen(true) ─► SavePanel
```

**Letterbox vs. Quests modal split**: One Component (`Letterbox`) owns
all letter state. QuestsPanel is *its own* PanelComponent that reads
the same data, filtered to `IsQuest == true`. This avoids a second
singleton without conflating the two views.

### Key Interfaces

```csharp
// New: Code/Letterbox.cs
public sealed class Letterbox : Component
{
    public static Letterbox Instance { get; private set; }

    public bool IsOpen      { get; private set; }   // letterbox modal
    public bool IsQuestOpen { get; private set; }   // quests modal

    public void SetOpen( bool open );
    public void SetQuestOpen( bool open );

    /// All letters received this run, oldest-first. Quest filtering is
    /// done at the panel layer.
    public IReadOnlyList<FanLetter> Letters { get; }

    public IEnumerable<FanLetter> OpenQuests
        => Letters.Where( l => l.IsQuest && !l.QuestFulfilled );

    public int  UnreadCount   { get; }
    public bool HasOpenQuests { get; }

    /// Marks every visible letter as read. Called from LetterboxPanel
    /// on open; clears the sticky first-letter notification too.
    public void MarkAllRead();

    /// Called from Gallery.ComputeScoreAndMetrics when a new game ships.
    /// Returns the fulfilled quest (and its bonus multiplier) if any
    /// matching quest exists; null otherwise. Mutates Letters to set
    /// QuestFulfilled = true on the consumed entry.
    public FanLetter TryFulfillQuest( IReadOnlyList<GameGenre> genres );

    public LetterboxSave Save();
    public void          Load( LetterboxSave dto );
}

// Persisted DTO — see Save / migration below.
public sealed class FanLetter
{
    public string         Id              { get; set; } = "";   // GUID-ish unique id
    public string         GameTitle       { get; set; } = "";   // referenced game
    public GameGenre      GameGenre       { get; set; }         // praised game's primary genre
    public int            ReviewScore     { get; set; }
    public string         SenderName      { get; set; } = "";   // generated handle
    public string         Body            { get; set; } = "";   // generated copy
    public DateTimeOffset ReceivedAt      { get; set; }
    public bool           IsQuest         { get; set; }
    public GameGenre?     QuestGenre      { get; set; }         // null when !IsQuest
    public bool           QuestFulfilled  { get; set; }
    public string         FulfilledByGame { get; set; } = "";   // title of the game that fulfilled it
    public bool           Read            { get; set; }
}

// Save extension to Code/Save/GameSave.cs
public sealed class GameSave
{
    // ... existing fields ...
    public LetterboxSave Letterbox { get; set; } = new();   // NEW
}

public sealed class LetterboxSave
{
    public List<FanLetter> Letters              { get; set; } = new();
    public bool            FirstLetterSeen      { get; set; }
    public int             TotalLettersReceived { get; set; }   // lifetime ratchet for achievements
}
```

```csharp
// New: Code/SaveMenu.cs
public sealed class SaveMenu : Component
{
    public static SaveMenu Instance { get; private set; }
    public bool IsOpen { get; private set; }
    public void Toggle();
    public void SetOpen( bool open );
}
```

```csharp
// Edit: Code/Save/GameSaveManager.cs
public static readonly string[] ManualSlotIds = new[]
{
    "slot-1",   // dedicated autosave
    "slot-2",   // manual #1
    "slot-3",   // manual #2 (NEW)
};
```

```csharp
// Edit: Code/Gallery.cs (inside ComputeScoreAndMetrics, before TotalSales rounding)
var quest = Letterbox.Instance?.TryFulfillQuest( game.Genres );
double questBonus = quest != null ? 1.20 : 1.0;
// applied to lifetimePlayers (which propagates into TotalSales via
// totalPlayMinutes * PricePerMinute), so all three pillars of the
// Gallery card move in lockstep with the bonus.
lifetimePlayers = (long)MathF.Round( lifetimePlayers * (float)questBonus );
```

### Implementation Guidelines

**Letter generation (templated, in-engine)**:

- Sender names come from a small fixed pool (~20 entries: "FrostbyteJr",
  "GameGirl99", "OldSchoolPlayer", etc.). Random pick per letter.
- Letter bodies pick from 3-5 templates, partitioned by review-score
  band (high ≥ 70, mid 40–69, low < 40). Templates use placeholders:
  `{GAME}`, `{GENRE}`, `{PRAISED_GENRE}`, `{REQUESTED_GENRE}`.
- Quest letters are a *separate* template pool that always uses
  `{REQUESTED_GENRE}` and `{PRAISED_GENRE}`.
- All copy lives in `Code/Letterbox.cs` as `static readonly string[]`
  arrays so it's easy to edit without touching scene data. Future
  localisation passes through the same arrays.

**Letter spawn timing (`OnDayStart` subscriber)**:

- On every day tick, walk `Gallery.ShippedGames`. For each game where
  `AbsoluteDay(now) - AbsoluteDay(game.ShipDate) == 7` AND no letter
  has yet been *decided for* this game, evaluate the spawn roll.
- **Spawn-rate gate**: 35% (`LetterSpawnChance = 0.35`). Most ships do
  not produce a letter — receiving one is meant to feel like an
  unprompted note from a real fan, not an automated reward. The 35%
  rate landed during Phase 2 implementation (the original ADR R1 said
  "exactly one letter per ship" — amended 2026-05-10 to reflect the
  shipped behaviour).
- Idempotency guard: track a `HashSet<string> _decidedFor` of
  ShippedGame titles. *Decided* (not *spawned*) so a 35%-roll skip
  also locks the game out — the roll happens exactly once per ship.
  Resilient against an OnDayStart event firing twice on the same day
  for any reason.
- Quest decision at spawn time (only on rolls that produced a letter):
  if `rng.NextDouble() < 0.30` AND there's at least one alternative
  unlocked genre, this is a quest letter; otherwise normal.

**First-letter sticky notification**:

- On letter spawn: if `LetterboxSave.FirstLetterSeen == false`, push a
  sticky toast (`duration: 0f`) with tag `"letterbox-first"`.
  Subsequent letters use the normal `duration: 6f` and no tag.
- `MarkAllRead()` (called from LetterboxPanel `OnOpen`) sets
  `FirstLetterSeen = true` and `Notifications.RemoveByTag(
  "letterbox-first" )`.

**Quest fulfillment**:

- Hook in `Gallery.ComputeScoreAndMetrics`, before final
  `TotalSales` is computed.
- Iterate open quests in the order they were received. First quest
  whose `QuestGenre` matches any of the game's `Genres` consumes the
  quest, sets `QuestFulfilled = true` + `FulfilledByGame = game.Title`,
  and applies a 1.20× multiplier to `lifetimePlayers` for that game.
- Only ONE quest fulfills per ship — even if multiple match. Stacking
  was deferred per the user's design choice.
- Pushes a notification: `"Quest fulfilled — {GAME} sales boosted +20%"`.

**Achievement integration**:

- Three new entries in `Achievements.Registry` (`AchievementId.FirstLetter`,
  `Letters10`, `Letters50`).
- Tied to a new tracker `Achievements.LettersReceived` (per ADR-0001
  pattern). `Letterbox.OnLetterSpawned` increments it via
  `Achievements.RecordLetterReceived()`.
- `RecordLetterReceived` calls `EvaluateAll` so achievement unlocks
  fire immediately.

**SavePanel extraction**:

- Move all save markup from `GameMenuPanel.razor` to a new
  `Code/UI/SavePanel.razor`. Includes the slot cards, "Start New
  Game" button, and the takeover confirm overlay.
- `_confirmingNewGame` flag moves with the markup to SavePanel's
  `@code` block.
- All `.save-*`, `.restart-takeover-*`, `.new-game-btn-*` SCSS rules
  move to a new `Code/UI/SavePanel.razor.scss`.
- `Code/UI/GameMenuPanel.razor` loses the entire `.content` div and
  any `@code` members that supported it (`SaveSlot`, `LoadSlot`,
  `_slotPeeks`, `_confirmingNewGame`, etc.).
- `Code/UI/GameMenuPanel.razor.scss` loses `.content`, `.placeholder*`,
  and the entire save block.
- `GameMenu.Activate("Save")` routes to `SaveMenu.Instance.SetOpen(true)`
  (close-then-open pattern).

**Save slot count change (1 autosave + 2 manual)**:

- `GameSaveManager.ManualSlotIds` becomes `["slot-1", "slot-2", "slot-3"]`.
- SavePanel renders three cards. The autosave (slot-1) shows the LOAD
  button only; manual slots show SAVE + LOAD.
- No data migration: existing slot-1 / slot-2 saves are preserved
  in place; slot-3 starts empty and is filled the first time the
  player saves to it.

**Tile + scene wiring**:

- `GameMenu.Tiles` array gets two new entries:
  ```csharp
  new( "Letterbox", "Read fan letters about your shipped games.",          "📬" ),
  new( "Quest",     "View open requests from fans for specific genres.",   "📜" ),
  ```
- Inserted between `Gallery` and `Save` in display order.
- `GameMenu.Activate` gets two new branches mirroring the HR /
  Leaderboards pattern.
- Scene wiring: `Letterbox` Component + `LetterboxPanel` PanelComponent
  + `QuestsPanel` PanelComponent + `SaveMenu` Component +
  `SavePanel` PanelComponent all on the existing `MenuUI` GameObject.
  Same ScreenPanel ZIndex 100 as `GameMenuPanel`.

**Tutorial integration**:

- `TutorialManager.IsTileEnabled` returns true for "Letterbox" and
  "Quest" outside the early phases (`HireFirst` through
  `AssignChair`). The tutorial doesn't need to gate these — they're
  only useful after the first ship anyway, and the first ship lands
  well after the tutorial finishes.

## Alternatives Considered

### Alternative 1: Single Letters tab inside an existing panel
- **Description**: Add a "Letters" sub-tab to the Gallery panel
  (alongside Past Games / Trophies / Unlocks) instead of a dedicated
  modal.
- **Pros**: Zero new singletons, half the UI surface. Letters live
  thematically near the Gallery.
- **Cons**: Buries the feature inside an already-busy panel.
  First-letter sticky-notif → "Open Gallery → Letters tab" is two
  clicks. Players may miss it.
- **Estimated Effort**: Half this ADR's scope.
- **Rejection Reason**: User explicitly asked for a dedicated
  Letterbox tile so the affordance is one click.

### Alternative 2: Procedurally-generated letter copy (LLM)
- **Description**: Generate letter bodies via runtime LLM call with
  the game's metadata as context.
- **Pros**: Infinitely varied, hyper-personalised letters.
- **Cons**: Network dependency on a single-player offline tycoon. Cost
  per call. Latency means letters can't appear instantly. Loss of
  authorial voice.
- **Estimated Effort**: 2–3× this ADR's scope, plus an LLM service.
- **Rejection Reason**: Wrong tradeoff for the genre. Templated copy
  with a few variations per band reads as more authentic in this
  context anyway (Game Dev Tycoon's letters are templated and the
  audience loves them).

### Alternative 3: Persistent fan personas
- **Description**: Track a fixed set of recurring fans across the run
  (each with name + favourite genre + relationship score). Same fan
  writes multiple letters over the studio's life.
- **Pros**: Strong narrative thread. Players form attachments. Quest
  letters carry more weight ("Sara's been waiting three games for a
  Mystery title").
- **Cons**: 3–5× scope (persona generation, persistent state,
  evolving relationship model). Lots of room for the system to feel
  shallow if not fully fleshed out. Easier to do right after we ship
  v1 and see what players engage with.
- **Estimated Effort**: 3× this ADR's scope.
- **Rejection Reason**: Deferred for a future content pass. The
  current sender-name-pool approach doesn't paint us into a corner —
  personas can be layered on later by reading the existing letter
  history.

## Consequences

### Positive
- Players get qualitative feedback that complements numerical reviews.
- Quest system gives genre experimentation a tangible reward.
- Menu is now consistent: every tile opens a fullscreen modal. The
  inline-content area is gone.
- Save UX gains a real third slot (1 autosave + 2 free).

### Negative
- 5 new code surfaces (Letterbox, SaveMenu, three new panels) means
  more compile-time bytes and more potential hot-reload soreness during
  iteration. Each one is small but the count adds up.
- Players who never ship a game never see the feature. That's by
  design but worth noting for tutorial / onboarding.
- Quest fulfillment touches `ComputeScoreAndMetrics` — the function
  already does a lot. Adding the quest hook risks future
  cross-contamination if not kept tight.

### Neutral
- Schema bumps v2 → v3. Migration is a no-op `MigrateFromV2` because
  the new `LetterboxSave` field defaults safely on legacy saves
  (empty list, `FirstLetterSeen = false`).

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Quest letter spawned for a genre the player can't ship (e.g. requires research not yet completed) | MEDIUM | LOW | Pick from `GameGenres.UnlockedFor( player )` only. Quest letters fall back to normal letters if no valid alternative genre. |
| Multiple OnDayStart triggers on the same in-game day spawn duplicate letters | LOW | MEDIUM | `Letterbox` keeps a `HashSet<string> _lettersSpawnedFor` keyed on ShippedGame title (titles are unique within a run). |
| Schema migration corrupts saves | LOW | HIGH | New field has a non-null DTO default; migrator is the empty no-op pattern from ADR-0001. Existing sub-DTOs are untouched. |
| First-letter sticky toast fails to clear on Letterbox open | MEDIUM | LOW | Idempotent — `Notifications.RemoveByTag` is a no-op if the tag isn't present. Cleared from both `MarkAllRead` and `Letterbox.SetOpen(true)`. |
| Quest +20× bonus stacks unintentionally with future modifiers | LOW | MEDIUM | Multiplier applied as a single named factor `questBonus` in `ComputeScoreAndMetrics`, not folded into `playerMult`. Easy to audit. |

## Performance Implications

| Metric | Before | Expected After | Budget |
|--------|--------|---------------|--------|
| CPU (frame time) | baseline | unchanged (Letterbox ticks once per in-game day, not per frame) | 16.6ms |
| Memory | baseline | +negligible (ShippedGames count × 1 letter each ≈ tens of small DTOs across a long run) | 4GB |
| Save file size | baseline | +KB per shipped game | unbounded |
| Load time | baseline | unchanged | <5s |

## Migration Plan

The implementation lands in **four sequential phases**. Each phase is a
self-contained commit; the project must compile and run cleanly between
phases.

### Phase 1 — SavePanel extraction + 3 slots
1. Bump `GameSaveManager.SchemaVersion` 2 → 3.
2. Add `MigrateFromV2` (no-op for now — `LetterboxSave` has safe
   defaults). Reserved for future v2-shaped saves.
3. `GameSaveManager.ManualSlotIds` becomes `["slot-1", "slot-2", "slot-3"]`.
4. Create `Code/SaveMenu.cs` (Component, singleton).
5. Create `Code/UI/SavePanel.razor` + `.scss`. Move all save UI +
   restart-takeover from `GameMenuPanel.razor` into it.
6. Strip the `.content` div + every save-related `@code` member from
   `GameMenuPanel.razor`. Strip `.content` + `.save-*` +
   `.restart-takeover-*` SCSS rules from `GameMenuPanel.razor.scss`.
7. Add Save tile route in `GameMenu.Activate`.
8. Add `SaveMenu.Instance?.IsOpen` to `GameManager.ApplyPlayerLock`.
9. Scene: add `SaveMenu` Component + `SavePanel` PanelComponent to
   the MenuUI GameObject.

### Phase 2 — Letterbox system + LetterboxPanel + first-letter sticky
1. Add `LetterboxSave`, `FanLetter` DTOs to `Code/Save/GameSave.cs`.
2. Add `LetterboxSave Letterbox { get; set; } = new();` to `GameSave`.
3. Create `Code/Letterbox.cs` (Component, singleton). Subscribe to
   `GameManager.OnDayStart` for the spawn tick. Implement
   `TryFulfillQuest` (used in Phase 3 — keep stub).
4. Create `Code/UI/LetterboxPanel.razor` + `.scss`.
5. Add Letterbox tile to `GameMenu.Tiles` + Activate routing.
6. Add `Letterbox.Instance?.IsOpen` to ApplyPlayerLock.
7. Add `Achievements.LettersReceived` ratchet + three new
   `AchievementId` values + Registry entries + PlatformIdMap entries.
8. Wire `Letterbox.OnLetterSpawned` to push the (sticky-on-first /
   normal-on-rest) notification + call `Achievements.RecordLetterReceived`.
9. Add Letterbox Save / Load wiring to GameSaveManager.Capture / Apply.
10. Scene: add `Letterbox` Component + `LetterboxPanel` PanelComponent.

### Phase 3 — Quest system + QuestsPanel + +20% sales hook
1. Create `Code/UI/QuestsPanel.razor` + `.scss`. Reads from
   `Letterbox.Instance.OpenQuests` and the fulfilled-quest history.
2. Add Quest tile to `GameMenu.Tiles` + Activate routing.
3. Implement the actual `Letterbox.TryFulfillQuest` body (currently a
   stub).
4. Hook `Letterbox.TryFulfillQuest` into `Gallery.ComputeScoreAndMetrics`
   — apply 1.20× to `lifetimePlayers` when a quest matches. Push a
   `"Quest fulfilled"` notification.
5. Add quest-letter generation to the spawn tick: 30% chance per
   letter, choose a random unlocked genre other than the praised one.
6. Scene: add `QuestsPanel` PanelComponent.

### Phase 4 — Polish + smoke test
1. Verify the v2 → v3 migration path (load an existing autosave).
2. Verify the first-letter sticky toast clears on Letterbox open.
3. Verify quest fulfillment fires correctly + the +20% multiplier
   applied is visible in the Gallery card.
4. Verify all five new modals respect `ApplyPlayerLock`.
5. Run a short ship-letter-quest-ship cycle to smoke-test end-to-end.

**Rollback plan**:
- Phase 1 is the largest refactor. If it's broken, revert the entire
  phase 1 commit; saves still work via the old slot-2 path because
  the autosave wrote `slot-2` correctly under v2 too.
- Phases 2–3 are additive. Set `Letterbox.Enabled = false` (kill
  switch) to silence the spawn loop without losing existing letters.
  Removing the feature entirely is a single-revert per phase.

## Validation Criteria

- [ ] All five new modals open / close cleanly via the GameMenu tile
      hand-off pattern. No clicks land in dead space (per the s&box
      scroll-container memory).
- [ ] First letter ever received pushes a sticky toast tagged
      `"letterbox-first"`; opening LetterboxPanel clears it; the
      `FirstLetterSeen` flag persists across save/load.
- [ ] Subsequent letters use a regular 6s notification with no tag.
- [ ] ~35% of shipped games produce a letter on their 7th post-ship
      day (the `LetterSpawnChance` gate). Idempotency holds across
      save / load / day-fast-forward — the 35% roll happens exactly
      once per ship via `_decidedFor`.
- [ ] ~30% of letters on average come back as quests (verify across
      ≥ 30 letters with a debug counter).
- [ ] First matching-genre ship after a quest is filed consumes the
      quest, marks it fulfilled, applies 1.20× to `lifetimePlayers`,
      and pushes the `"Quest fulfilled"` notification.
- [ ] Letter-receiving achievements fire at 1 / 10 / 50.
- [ ] Save panel shows three slot cards: slot-1 (autosave, LOAD-only),
      slot-2 + slot-3 (manual, SAVE + LOAD).
- [ ] Schema migration v2 → v3 round-trips cleanly: existing autosave
      loads, `Letterbox.Letters` is empty + `FirstLetterSeen = false`,
      next ship spawns letter normally.
- [ ] No frame-time regression in `Gallery.OnUpdate` (the only hot
      path that gained a check, via `ComputeScoreAndMetrics`).

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|--------------|--------|-------------|--------------------------|
| _none_       | _n/a_  | Foundational — no GDD requirement. | This decision adds a new optional feature. Future GDDs (live ops, seasonal events) can build on the Letterbox plumbing. |

## Related

- ADR-0001 (Save System) — depends on, extends `GameSave`.
- ADR-0002 (Leaderboards) — sibling system, only for the schema-version
  chain.
- `Code/Gallery.cs:480` (`RecordShippedGame`) — letter spawn trigger
  hooks alongside the existing achievement record.
- `Code/Tutorial/TutorialManager.cs` — sticky-notif ack pattern
  (`FirstGoodMoodToastSeen`) is the model for `FirstLetterSeen`.
- `memory/sbox_screenpanel_zindex_layering.md` — informs scene wiring
  decision (place all new panels on the existing `MenuUI` GameObject
  so internal SCSS z-index works as expected).
- `memory/sbox_services_integration_gotchas.md` — none of those traps
  apply here (no `Sandbox.Services.*` calls), but the hot-reload
  warning is relevant: expect to restart the editor at the end of each
  phase since this touches Components in scene.
