/// <summary>
/// Marks a child GameObject of a <see cref="PlacementSlot"/> as the visual
/// for one specific <see cref="ItemKind"/>. When that item is bound to the
/// parent slot, this GameObject is enabled and other variants of the same
/// slot are disabled.
///
/// Setup: drop this component on each "upgrade visual" inside a Mount-mode
/// PlacementSlot (e.g., the DesktopPC GameObject inside ComputerMount).
/// The mount's <see cref="PlacementSlot.DefaultVariant"/> handles the
/// fallback visual when no upgrade is bound.
/// </summary>
public sealed class ItemVariant : Component
{
	/// Which inventory item kind this child GameObject represents. The
	/// InventoryManager enables this GameObject when a matching item is
	/// bound to the parent PlacementSlot.
	[Property] public ItemKind Kind { get; set; } = ItemKind.Desk;
}
