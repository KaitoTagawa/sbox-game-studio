/// <summary>
/// What kind of furniture or equipment an inventory item is.
/// Each kind maps to a single <see cref="InventoryCatalogue.Entry"/> with its
/// price, prefab, slot kind, and unlock gate.
///
/// Adding a new kind: append a value here, then add a matching entry in
/// <see cref="InventoryCatalogue.All"/>. The InventoryShop UI picks it up
/// automatically.
/// </summary>
public enum ItemKind
{
	Desk           = 0,

	// Deprecated — kept for backward compat with any in-flight references.
	// Don't add catalogue entries for these; they're not purchasable.
	OfficeChair    = 1,
	Computer       = 2,

	// Workstation upgrade slots use the tiered Standard/Premium/Elite items
	// below (each per-slot mount has its own progression). Defaults are
	// scene-authored and free; buyable tiers swap them in.

	Monitor        = 3,    // Deprecated — use MonitorStandard/Premium/Elite

	CoffeeMachine  = 4,
	ServerRack     = 5,
	Whiteboard     = 6,
	PottedPlant    = 7,

	// Deprecated single-upgrade names — superseded by the tier system below.
	DesktopPC      = 8,
	MonitorFancy   = 9,
	FancyChair     = 10,

	// ── Tiered upgrades ──────────────────────────────────────────────────
	// Each mount kind has 3 buyable tiers (Standard / Premium / Elite).
	// Default visuals (laptop on Computer, basic chair on Chair, none on
	// Monitor) are scene-authored and free.

	// Computer mount: laptop is the free default; these replace it.
	ComputerStandard = 20,
	ComputerPremium  = 21,
	ComputerElite    = 22,

	// Monitor mount: no default; standard is the cheapest "I have a monitor".
	MonitorStandard  = 30,
	MonitorPremium   = 31,
	MonitorElite     = 32,

	// Chair mount: default chair is free; these replace it.
	ChairStandard    = 40,
	ChairPremium     = 41,
	ChairElite       = 42,

	// ── Consumables (give to employee for a buff) ────────────────────────
	// Bought stockpiles like furniture but never placed — instead, the
	// player picks an employee from the InventoryPanel's Consumable tab and
	// hands one over. Each consumable applies a small morale bump on use
	// (v1 effect; full buff system is a follow-up).

	ChocolateBar     = 60,
	EnergyDrink      = 61,
	ProteinBar       = 62,

	/// "Skip" consumable — when used, instantly completes the active game
	/// project at 70 % of the points it would have shipped with. Price
	/// scales exponentially per use; see InventoryManager.PriceFor.
	SmokeBreak       = 63,

	/// Budget Good-mood booster. Same effect as <see cref="EnergyDrink"/>
	/// (P(Good) × 1.5 per tick) but the buff window is 14 in-game days
	/// instead of 30, and the base price is half. Shares the consumable
	/// unlock gate (3 lifetime ships).
	ChewingGum       = 64,

	// ── Office decoration (single SKUs) ─────────────────────────────────
	Bin              = 70,

	/// Studio-wide server. No physical placement (Slot = None) — buying it
	/// just flips a flag that boosts production output and unlocks the
	/// Multiplayer genre. Gated behind <see cref="AchievementId.Hire8"/>.
	Server           = 75,

	// ── Lounge area (LC1 unlocks first, then LC2) ───────────────────────
	// LC1 buy → unlocks LoungeChair (bulk × 2) and Whiteboard.
	// LC1 + LoungeChair + Whiteboard all owned → unlocks LC2.
	// LC2 → unlocks Bookshelf1, Bookshelf2, TV.
	LoungeCarpet1    = 80,
	LoungeCarpet2    = 81,
	LoungeChair      = 82,    // bulk × 2

	// ── Bookshelves + TV + Couch (gated behind LC2) ────────────────────
	Bookshelf1       = 90,
	Bookshelf2       = 91,
	TV               = 92,
	Couch            = 93,
	Toilet           = 94,

	// ── Books — discovered progressively over the run ──────────────────
	// Each book has its own SKU and its own achievement gate, so the
	// player slowly fills bookshelves as the studio grows. All also
	// require Bookshelf1 in inventory (so books can't be bought before
	// there's anything to put them on).
	Book1            = 100,
	Book2            = 101,
	Book3            = 102,
	Book4            = 103,
	Book5            = 104,
	Book6            = 105,
	Book7            = 106,
	Book8            = 107,
	Book9            = 108,
	Book10           = 109,
	Book11           = 110,
	Book12           = 111,
}

/// <summary>
/// Coarse grouping for the InventoryShop UI. Has no gameplay effect on its
/// own — purely for organising the picker into tabs.
/// </summary>
public enum ItemCategory
{
	Workstation,   // Desks, chairs
	Equipment,     // Computers, monitors, server racks
	Amenities,     // Coffee machines, whiteboards
	Decoration,    // Plants, art

	/// Consumable — bought into a stash, given to one employee for a small
	/// short-term buff. Doesn't bind to a PlacementSlot.
	Consumable,
}

/// <summary>
/// Display + theming helpers so UI code never has to switch on the enum
/// directly. Same shape as <see cref="EmployeeKindExtensions"/>.
/// </summary>
public static class ItemKindExtensions
{
	public static string DisplayName( this ItemKind k ) => k switch
	{
		ItemKind.Desk             => "Desk",
		ItemKind.CoffeeMachine    => "Coffee Machine",
		ItemKind.ServerRack       => "Server Rack",
		ItemKind.Whiteboard       => "Whiteboard",
		ItemKind.PottedPlant      => "Potted Plant",

		ItemKind.ComputerStandard => "Standard PC",
		ItemKind.ComputerPremium  => "Premium PC",
		ItemKind.ComputerElite    => "Elite Workstation",

		ItemKind.MonitorStandard  => "Standard Monitor",
		ItemKind.MonitorPremium   => "Premium Monitor",
		ItemKind.MonitorElite     => "Elite Display",

		ItemKind.ChairStandard    => "Standard Chair",
		ItemKind.ChairPremium     => "Premium Chair",
		ItemKind.ChairElite       => "Elite Chair",

		ItemKind.ChocolateBar     => "Chocolate Bar",
		ItemKind.EnergyDrink      => "Energy Drink",
		ItemKind.ProteinBar       => "Protein Bar",
		ItemKind.SmokeBreak       => "Outsource Everything",
		ItemKind.ChewingGum       => "Chewing Gum",

		ItemKind.Bin              => "Bin",
		ItemKind.Server           => "Server",
		ItemKind.LoungeCarpet1    => "Lounge Carpet 1",
		ItemKind.LoungeCarpet2    => "Lounge Carpet 2",
		ItemKind.LoungeChair      => "Lounge Chair",
		ItemKind.Bookshelf1       => "Bookshelf 1",
		ItemKind.Bookshelf2       => "Bookshelf 2",
		ItemKind.TV               => "TV",
		ItemKind.Couch            => "Couch",
		ItemKind.Toilet           => "Toilet",

		ItemKind.Book1            => "Book I",
		ItemKind.Book2            => "Book II",
		ItemKind.Book3            => "Book III",
		ItemKind.Book4            => "Book IV",
		ItemKind.Book5            => "Book V",
		ItemKind.Book6            => "Book VI",
		ItemKind.Book7            => "Book VII",
		ItemKind.Book8            => "Book VIII",
		ItemKind.Book9            => "Book IX",
		ItemKind.Book10           => "Book X",
		ItemKind.Book11           => "Book XI",
		ItemKind.Book12           => "Book XII",

		_                         => k.ToString(),
	};
}

public static class ItemCategoryExtensions
{
	public static string DisplayName( this ItemCategory c ) => c switch
	{
		ItemCategory.Workstation => "Workstation",
		ItemCategory.Equipment   => "Equipment",
		ItemCategory.Amenities   => "Amenities",
		ItemCategory.Decoration  => "Decoration",
		ItemCategory.Consumable  => "Consumable",
		_                        => c.ToString(),
	};

	/// True for everything except <see cref="ItemCategory.Consumable"/> —
	/// the InventoryPanel groups all four "physical" sub-categories under
	/// one Furniture tab and consumables under their own.
	public static bool IsFurniture( this ItemCategory c ) =>
		c != ItemCategory.Consumable;
}
