/// <summary>
/// **Deprecated — kept for backward compatibility with existing scenes.**
///
/// EmployeeDesk used to be a Desk-specific scene marker discovered by
/// <see cref="HRManager.DiscoverDesks"/>. With the introduction of the
/// Inventory system, the generalised <see cref="PlacementSlot"/> replaces it
/// (one component for every furniture-slot kind, not just desks).
///
/// This class now subclasses PlacementSlot with <see cref="PlacementSlot.Kind"/>
/// defaulted to <see cref="SlotKind.Desk"/>, so EmployeeDesk components in
/// existing scene files continue to function identically — they're discovered
/// by InventoryManager along with regular PlacementSlot components.
///
/// **For new scene authoring**: drop a <c>PlacementSlot</c> component with
/// <c>Kind = SlotKind.Desk</c> instead. New code should not reference
/// EmployeeDesk; this shim exists purely for scene-file compatibility.
/// </summary>
public sealed class EmployeeDesk : PlacementSlot
{
	protected override void OnAwake()
	{
		// Force the Desk kind regardless of how the scene serialised this
		// component (legacy EmployeeDesk markers don't carry a Kind field).
		Kind = SlotKind.Desk;
	}
}
