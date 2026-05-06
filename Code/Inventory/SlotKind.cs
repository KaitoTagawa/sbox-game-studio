/// <summary>
/// What kind of <see cref="InventoryItem"/> a <see cref="PlacementSlot"/>
/// will accept. Items can only auto-place at slots whose Kind matches the
/// item's catalogue <see cref="InventoryCatalogue.Entry.Slot"/>.
///
/// Slot kinds intentionally don't 1-to-1 match item kinds — multiple items
/// can share a slot kind (e.g. Computer and Monitor both go in
/// <see cref="DeskAccessory"/>).
/// </summary>
public enum SlotKind
{
	/// Sentinel for items that don't occupy a physical slot — Consumables
	/// in particular. <see cref="InventoryManager.TryBuy"/> skips the
	/// auto-place pass when the catalogue entry's slot is None.
	None          = -1,

	/// Where a hire's desk goes. Drives <see cref="HRManager"/>'s free-desk
	/// queries, since hiring an employee needs a placed Desk.
	Desk          = 0,

	/// A chair next to a desk. Cosmetic; doesn't gate hires today.
	Chair         = 1,

	/// Generic on-desk accessory slot — kept for items that don't need a
	/// dedicated mount kind.
	DeskAccessory = 2,

	/// Free-standing furniture / atmosphere objects (coffee machines,
	/// whiteboards, plants). Placed wherever the level designer drops a
	/// matching slot.
	Decoration    = 3,

	/// Specialised equipment with its own footprint (server racks, etc.).
	/// Often achievement-gated.
	Equipment     = 4,

	/// Computer mount inside a workstation. Default visual is typically a
	/// laptop; upgrade items (DesktopPC, etc.) swap in.
	Computer      = 5,

	/// Monitor mount inside a workstation. Default is typically a basic
	/// monitor; upgrades (MonitorFancy) swap in.
	Monitor       = 6,

	// ── Lounge / common-area slots ─────────────────────────────────────────
	// Each carpet has its own slot kind so LoungeCarpet1 and LoungeCarpet2
	// land in distinct scene positions (the auto-place pass picks any free
	// slot of matching kind, so identical kinds = interchangeable).

	LoungeCarpet1 = 7,
	LoungeCarpet2 = 8,

	/// Free-standing chair in the lounge. Distinct from <see cref="Chair"/>
	/// (which is a desk-bound seat tied to <see cref="HRManager"/>).
	LoungeChair   = 9,

	/// Big standing furniture — bookshelves, TVs. Either bookshelf can land
	/// at any free Bookshelf slot.
	Bookshelf     = 10,
	TV            = 11,

	/// Slot inside a bookshelf prefab. Each book lands at one Book slot;
	/// bookshelves should ship with several Book slots authored as children.
	Book          = 12,

	/// Couch in the lounge area. Gated behind LoungeCarpet2.
	Couch         = 13,

	/// Toilet in the office. Endgame "lounge capstone" — gated behind every
	/// other lounge item being owned.
	Toilet        = 14,
}

public static class SlotKindExtensions
{
	public static string DisplayName( this SlotKind k ) => k switch
	{
		SlotKind.None          => "—",
		SlotKind.Desk          => "Desk",
		SlotKind.Chair         => "Chair",
		SlotKind.DeskAccessory => "Desk Accessory",
		SlotKind.Decoration    => "Decoration",
		SlotKind.Equipment     => "Equipment",
		SlotKind.Computer      => "Computer",
		SlotKind.Monitor       => "Monitor",
		SlotKind.LoungeCarpet1 => "Lounge Carpet 1",
		SlotKind.LoungeCarpet2 => "Lounge Carpet 2",
		SlotKind.LoungeChair   => "Lounge Chair",
		SlotKind.Bookshelf     => "Bookshelf",
		SlotKind.TV            => "TV",
		SlotKind.Book          => "Book",
		SlotKind.Couch         => "Couch",
		SlotKind.Toilet        => "Toilet",
		_                      => k.ToString(),
	};
}
