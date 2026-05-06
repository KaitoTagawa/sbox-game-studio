using System;

/// <summary>
/// One owned piece of inventory — a desk, a coffee machine, etc.
/// Lives in <see cref="InventoryManager._owned"/>; persisted (when save
/// system exists) by serialising the owned list.
///
/// An item can be in two states:
///   • Unplaced: <see cref="PlacedAtSlotId"/> is null and
///     <see cref="SpawnedGameObject"/> is null. Owned but not visible in the world.
///   • Placed: both fields are set. The bound <see cref="PlacementSlot"/>'s
///     GameObject (a scene-authored visual like a complete deskspace) is
///     enabled.
/// </summary>
public sealed class InventoryItem
{
	/// Unique per item — survives saves so placement and references stay stable.
	public Guid Id { get; init; } = Guid.NewGuid();

	/// What kind of furniture/equipment this is. Keyed into
	/// <see cref="InventoryCatalogue"/> for price, category, slot kind, etc.
	public ItemKind Kind { get; init; }

	/// Which <see cref="PlacementSlot.Id"/> this item currently occupies, or
	/// null if owned-but-not-placed. Mutable so InventoryManager can move
	/// items between slots later (sell, rearrange).
	public Guid? PlacedAtSlotId { get; set; }

	/// Runtime reference to the bound slot's scene-authored GameObject (the
	/// deskspace, the coffee-corner model, etc.). Set when InventoryManager
	/// places the item — at which point the GameObject is also enabled.
	/// Not persisted; recomputed on load by replaying PlacedAtSlotId against
	/// scene PlacementSlot lookups.
	public GameObject SpawnedGameObject { get; set; }

	/// True when this item is bound to a slot. Source of truth is
	/// <see cref="PlacedAtSlotId"/> alone — <see cref="SpawnedGameObject"/>
	/// is a runtime-only visual cache that some paths (desk-bound assigns)
	/// don't bother filling, and using it here would lie to callers like
	/// the Edit Desks picker about whether the item is available.
	public bool IsPlaced => PlacedAtSlotId.HasValue;
}
