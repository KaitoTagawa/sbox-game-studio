---
status: forward-design
source: forward design
date: 2026-04-27
verified-by: Kaito Tagawa
---

# Inventory — Design Document

**Status**: Forward design (no implementation yet — implementation begins same session)
**Date**: 2026-04-27
**Implements Pillar**: Pillar 1 (The Studio Is a Place — owned furniture lives in the world) + Pillar 3 (Hiring Is Meaningful — desks are tied to owned-and-placed inventory, not magical scene state)

## 1. Overview

The Inventory system is the bridge between **buying things from the Shop** and **those things existing in the world**. Today, `Shop.TryPurchase` deducts money and grants nothing — there is no concept of "owned items". Hiring depends on `EmployeeDesk` scene markers placed directly under a `DesksRoot` GameObject, which is fragile (one missed parent fixup and the player can't hire anyone on day 1).

This system replaces both: a single `InventoryManager` owns the studio's possessions as data, a static `InventoryCatalogue` defines purchasable kinds, and `PlacementSlot` markers in the scene tell the system where each kind can live. Buying a Desk grants an `InventoryItem`; the manager auto-places it at the next free DeskSlot; hiring queries the manager for placed Desks.

## 2. Player Fantasy

Buying furniture is **the studio growing in front of you**. You spend $2,500 on a desk in the InventoryShop modal, and the next time you pan the camera, the desk is *there* — at one of the empty slots you laid out in the office. The same applies to coffee machines, monitors, plants. The office is a real place that fills up as your money grows.

The headline upgrade over the current Shop: today, buying does nothing visible. After this system, every purchase has a physical consequence. That is the entire promise of Pillar 1.

## 3. Detailed Design

### 3.1 Core Concepts

- **ItemKind** (enum): the type of furniture/equipment (Desk, OfficeChair, Computer, Monitor, CoffeeMachine, ServerRack, Whiteboard, PottedPlant — extensible)
- **SlotKind** (enum): the kind of placement target (Desk, Chair, DeskAccessory, Decoration, Equipment)
- **InventoryItem** (data): one owned thing (Guid Id, ItemKind, optional `PlacedAtSlotId : Guid?`)
- **InventoryCatalogue.Entry** (static metadata): per-kind name, description, price, prefab path, slot kind, optional achievement gate
- **PlacementSlot** (scene Component): marker placed in the world (Guid Id, SlotKind, optional achievement gate, optional friendly Label)
- **InventoryManager** (singleton Component): holds `_owned : List<InventoryItem>` and provides queries / mutations

### 3.2 Lifecycle

**Game start (OnAwake)**:
1. InventoryManager seeds **one Desk** into `_owned` if `_owned` is empty (first-hire guarantee)
2. Scene auto-discovers all `PlacementSlot` components anywhere under the scene root (recursive — no fragile parenting)

**Game start (OnStart)**:
3. Auto-place pass: for every unplaced item in `_owned`, find the first compatible empty slot and place it (spawn the prefab at the slot's transform, set `PlacedAtSlotId`)
4. Result: 1 Desk placed; player can hire one employee immediately

**Buy flow**:
1. Player opens InventoryShop; clicks Buy on a catalogue entry
2. `InventoryManager.TryBuy(kind)` deducts price via `GameManager.TrySpend`; on success, appends a new `InventoryItem`
3. Auto-place pass runs again (only the new item); if a free compatible slot exists, place it; else item stays "owned-but-unplaced" and the UI shows it as such

**Sell flow** (if added later — Open Question):
1. Player picks an owned item from inventory; clicks Sell
2. Manager removes from `_owned`, despawns the prefab, refunds 50% of price, frees the slot

### 3.3 Auto-Placement Rule

For every unplaced `InventoryItem`:
1. Find all `PlacementSlot`s in the scene where:
   - `slot.SlotKind` matches the item's catalogue `SlotKind`
   - `slot.RequiredAchievement` is null OR satisfied
   - No other `InventoryItem` already references this slot's `Id`
2. Pick the first matching slot (in scene-discovery order — deterministic)
3. Spawn the catalogue's `Prefab` at the slot's `WorldPosition` / `WorldRotation`
4. Record `item.PlacedAtSlotId = slot.Id` and `item.SpawnedGameObject = clone`

Items without a compatible free slot remain `PlacedAtSlotId = null` (owned but invisible).

### 3.4 Interactions with Other Systems

**HRManager** changes: stop using `DesksRoot.Children` and `EmployeeDesk` discovery. Instead query:
- `InventoryManager.PlacedDesks()` returns `IReadOnlyList<(InventoryItem item, PlacementSlot slot, GameObject spawned)>`
- `MaxDesks = PlacedDesks.Count` (replaces UnlockedDesks)
- `FindFreeDeskIndex` finds first placed Desk where no `_staff` member's `DeskSpot` matches its `Id`
- Hire spawns the EmployeeNPC at the placed Desk's `WorldPosition` / `WorldRotation`

**Shop.cs**: retired. The InventoryShop UI replaces it.

**EmployeeDesk.cs**: retired. Existing scenes need to swap `EmployeeDesk` components for `PlacementSlot` (SlotKind.Desk) — see Open Questions.

**Achievements**: catalogue entries can be achievement-gated (e.g., ServerRack unlocks at "Hire 10"). The InventoryShop UI shows lock state.

**Save system** (when it exists): `_owned` list (with Ids and `PlacedAtSlotId`) must persist; `PlacementSlot.Id` must be stable across saves (assigned in scene-time, persisted in scene file via [Property]).

## 4. Formulas

| # | Formula | Definition |
|---|---|---|
| I1 | Auto-place compatibility | `slot.Kind == catalogueEntry.SlotKind` AND `(slot.RequiredAchievement is null OR achievement satisfied)` AND `slot.Id not in placed_set` |
| I2 | Sell refund (if added) | `0.5 × catalogueEntry.Price` (rounded down) |

(This system is mostly logic, not math.)

## 5. Edge Cases

### Handled by Design
- ✅ **Game starts with no PlacementSlots in scene**: auto-place finds no slot for the seeded Desk → item stays unplaced → first hire fails ("No free desks") with notification. Player must add a slot. **Mitigated by**: documenting the requirement and including at least one DeskSlot in the project's starter scene.
- ✅ **Buying when all slots full**: item enters inventory as "unplaced". UI shows count of unplaced items. Player can place by removing other items or adding more slots.
- ✅ **Achievement-gated slot becomes available mid-run**: next auto-place pass picks it up. (Auto-place runs OnStart and after every purchase.)
- ✅ **Multiple compatible slots free**: deterministic — first scene-discovery-order slot wins. (Stable for save/load.)
- ✅ **First hire guarantee**: 1 Desk seeded in `_owned` at OnAwake regardless of scene state. Even an empty scene (no Slots) shows the player owns a desk.

### Not Yet Handled — Need Design
- ⚠️ **Sell flow**: should items be sellable? Refund amount? Defer to Open Question.
- ⚠️ **Player rearrangement**: out of MVP scope per design choice. Future feature.
- ⚠️ **Existing scene migration**: scenes with `EmployeeDesk` components need conversion. Either delete EmployeeDesk or write migration code (Open Question).

### Ambiguous
- ❓ **What happens to a placed item if its slot's achievement is REVOKED** (theoretical — achievements don't lock today)? **Recommend**: items stay placed (one-way gate).
- ❓ **Item destroyed in scene at runtime** (e.g., physics): InventoryItem still has `PlacedAtSlotId` but `SpawnedGameObject` is null. Recommend: log + treat as unplaced on next pass.

## 6. Dependencies

**Hard**:
- `GameManager.TrySpend` (for purchases)
- `Achievements` (for slot/catalogue gating)
- `Notifications` (purchase feedback)

**Will be modified**:
- `HRManager` (queries InventoryManager instead of EmployeeDesk discovery)

**Retired**:
- `Shop.cs`, `EmployeeDesk.cs`, `ShopPanel.razor`

## 7. Tuning Knobs

| Knob | Default | Notes |
|---|---|---|
| Catalogue prices | $80 – $2,500 | Mirrors current Shop.cs prices; tune per economy curve |
| Sell refund % (if added) | 50% | Standard tycoon refund rate |
| Desk price | $2,500 | Aligns with old `DeskBaseCost` (the unlock cost — now a buy cost) |

## 8. Visual/Audio Requirements

- Furniture spawn: optional small VFX/sound on placement (Phase 2 polish)
- InventoryShop UI: per-item card with name, description, price, owned count, placed count, lock state
- Empty/unfilled slots: optionally render a faint placeholder mesh (visible only in placement mode? Phase 2)

## 9. UI Requirements

**InventoryShopPanel** (replaces ShopPanel.razor):
- List of catalogue entries grouped by category (Desks / Chairs / Equipment / Decoration)
- Per-entry: name, description, price, "Buy" button (disabled if unaffordable / locked), owned count badge, placed count badge
- "Owned but not placed" filter toggle: shows items in inventory but no available slot

**Modal mutex**: same pattern as existing Shop / Settings / GameMenu.

## 10. Acceptance Criteria

- **GIVEN** a fresh game with at least one DeskSlot in the scene, **WHEN** the player loads the game, **THEN** 1 Desk is auto-placed at the slot and `HRManager.MaxDesks == 1`
- **GIVEN** an empty `_owned`, **WHEN** InventoryManager.OnAwake runs, **THEN** `_owned` contains 1 Desk
- **GIVEN** a player with $5,000 and an empty Desk slot, **WHEN** they buy a Desk from InventoryShop, **THEN** money becomes $2,500, `_owned` has 2 Desks, both placed, HR sees `MaxDesks == 2`
- **GIVEN** a player buys a Coffee Machine with no DecorationSlot in the scene, **WHEN** the buy completes, **THEN** money is deducted and the item is owned-but-unplaced
- **GIVEN** an HRManager hiring an employee, **WHEN** there's a free placed Desk, **THEN** the EmployeeNPC spawns at that desk's transform
- **GIVEN** the InventoryShop is open, **WHEN** the player views it, **THEN** every catalogue entry shows correct owned/placed counts
- **GIVEN** a Desk that's been placed, **WHEN** the game saves and reloads (once save exists), **THEN** the same Desk is at the same slot

## 11. Open Questions

1. **Existing scene migration**: scenes with `EmployeeDesk` components need conversion to `PlacementSlot` (SlotKind.Desk). Do we (a) delete EmployeeDesk and force re-authoring, (b) keep EmployeeDesk as a deprecation shim, (c) write a migration tool? Recommend (a) since the project is single-dev.
2. **Sell flow**: in MVP or post-MVP? Recommend post-MVP — keeps initial implementation tight.
3. **Player rearrangement**: post-MVP. Future system, possibly Pillar-1 polish work.
4. **Default starter scene**: should the project's starter scene include a known set of slots (1 Desk, 1 Coffee, 1 Whiteboard, 4 Decoration) so the game "just works" on first load? Recommend yes — flag for scene-authoring task.
5. **Catalogue extensibility**: today, ItemKind is an enum. If we want runtime-data-driven catalogue (CSV / JSON), revisit later. Enum is fine for MVP.
