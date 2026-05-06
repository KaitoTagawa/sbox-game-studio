using System;

/// <summary>
/// Marks a position in the scene where an <see cref="InventoryItem"/> of a
/// matching <see cref="SlotKind"/> can be auto-placed. Drop one of these on
/// any GameObject at the pose where the furniture should land — the
/// <see cref="InventoryManager"/> discovers them recursively at startup
/// (anywhere under the scene root, no fragile parenting required).
///
/// Replaces the old <c>EmployeeDesk</c> component, which was Desk-specific
/// and required strict parenting under HRManager.DesksRoot. This is the
/// generalised version: one component for every furniture slot in the office.
/// </summary>
/// <summary>
/// Determines how a <see cref="PlacementSlot"/> visualises bind/unbind state.
///
/// - <see cref="Workstation"/> (default): the slot's GameObject IS the visual
///   unit. When bound, enable the GameObject; when unbound, disable it.
///   Used for top-level slots like DeskSpace1 (the whole workstation).
///
/// - <see cref="Mount"/>: the slot is an internal swap point inside a parent
///   workstation. The slot's GameObject stays enabled; only the variant
///   children switch. Used for ComputerMount / MonitorMount / ChairMount
///   sub-slots inside a DeskSpace.
/// </summary>
public enum SlotMode
{
	Workstation = 0,
	Mount       = 1,
}

public class PlacementSlot : Component
{
	/// Stable per-slot id, used by <see cref="InventoryItem.PlacedAtSlotId"/>
	/// to bind an owned item to this slot across save/load.
	///
	/// Derived from the owning <see cref="Sandbox.GameObject"/>'s own Id
	/// rather than a stored <c>[Property]</c> field. Reason: when a desk
	/// subtree is copy-pasted in the editor, s&amp;box regenerates the
	/// GameObject's `__guid` per clone but copies any stored
	/// <c>[Property] Guid</c> values verbatim — so a stored Id collided
	/// across all 8 desks and a chair upgrade bound to one mount appeared
	/// on every desk. Reading from <c>GameObject.Id</c> sidesteps the
	/// duplication entirely; the editor's clone behaviour gives us a
	/// guaranteed-unique identifier for free.
	public new Guid Id => GameObject?.Id ?? Guid.Empty;

	/// What kind of item this slot accepts. Items whose catalogue
	/// <see cref="InventoryCatalogue.Entry.Slot"/> matches this Kind can
	/// auto-place here (subject to achievement gates and free-slot rules).
	[Property] public SlotKind Kind { get; set; } = SlotKind.Decoration;

	/// Optional concrete <see cref="ItemKind"/> filter. When set, this slot
	/// only accepts that exact item kind — the broader <see cref="Kind"/>
	/// match is an additional gate, not a substitute. When null, any item
	/// whose catalogue Slot matches <see cref="Kind"/> can bind.
	///
	/// The whole point: <see cref="SlotKind.Decoration"/> is shared between
	/// plants, bins, coffee machines, whiteboards, etc. Without this filter,
	/// auto-place would land a Bin on a slot whose GameObject has a plant
	/// model — silent visual breakage. Set this to the specific kind whose
	/// model lives on this GameObject.
	[Property] public ItemKind? PreferredItemKind { get; set; }

	/// How this slot manages visibility. See <see cref="SlotMode"/> docs.
	/// Defaults to Workstation — the safe choice for any new slot.
	[Property] public SlotMode Mode { get; set; } = SlotMode.Workstation;

	/// Optional child GameObject to enable when no inventory item is bound
	/// to this slot. Used for "default loadout" visuals (e.g., LaptopBasic
	/// shows on a Computer mount before the player has bought any Computer
	/// item). Only consulted in <see cref="SlotMode.Mount"/> mode.
	[Property] public GameObject DefaultVariant { get; set; }

	/// Optional achievement gate. When set, this slot is invisible to the
	/// auto-place pass until the achievement fires. Useful for "this corner
	/// of the office unlocks at 10 hires" without separate scene work.
	[Property] public AchievementId? RequiredAchievement { get; set; }

	/// Optional friendly label for editor / debug. Not used by gameplay.
	[Property] public string Label { get; set; } = "";

	/// Optional sit-spot for desk/chair slots. When set, NPCs walk to this
	/// GameObject's WorldPosition + WorldRotation instead of the slot's
	/// own transform, and the sit animation triggers on arrival. Drop an
	/// empty GameObject at the chair seat and assign here.
	[Property] public GameObject SitSpot { get; set; }

	/// True when this slot is currently free for auto-placement: kind matches
	/// nothing yet, achievement satisfied if any, and no item has bound to it.
	public bool IsAvailable
	{
		get
		{
			if ( RequiredAchievement is { } id && !Achievements.IsUnlocked( id ) )
				return false;

			var inv = InventoryManager.Instance;
			if ( inv is null ) return true;
			return !inv.IsSlotOccupied( Id );
		}
	}
}
