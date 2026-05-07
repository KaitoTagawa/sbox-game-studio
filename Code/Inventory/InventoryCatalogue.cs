using System.Collections.Generic;

/// <summary>
/// Static catalogue of every purchasable item kind. Replaces the hardcoded
/// list in the old <c>Shop.cs</c> with proper per-kind metadata that the
/// <see cref="InventoryManager"/> and InventoryShop UI both consume.
///
/// Adding a new item:
///   1. Add a value to <see cref="ItemKind"/>.
///   2. Add an entry to <see cref="All"/> below with name / description /
///      price / category / slot kind / prefab path / unlock gate.
///   3. Drop matching <see cref="PlacementSlot"/>s in the scene if the
///      item needs slots to be placed.
/// </summary>
public static class InventoryCatalogue
{
	/// <summary>
	/// Per-kind metadata. Mirrors the shape of <see cref="JobPosting.Info"/>
	/// and friends so the rest of the codebase recognises the pattern.
	///
	/// Note: prefab references are NOT here — they live on
	/// <see cref="InventoryManager"/> as [Property] fields so the level
	/// designer can wire them in the inspector. The catalogue is pure
	/// gameplay metadata.
	/// </summary>
	public sealed class Entry
	{
		public ItemKind        Kind                { get; init; }
		public string          Name                { get; init; } = "";
		public string          Description         { get; init; } = "";
		public long            Price               { get; init; }
		public ItemCategory    Category            { get; init; }
		public SlotKind        Slot                { get; init; }

		/// Tier within this slot kind. 0 = standalone (untiered), 1 = lowest
		/// buyable tier, 2 = mid, 3 = highest. Drives the auto-replace logic
		/// in <see cref="InventoryManager.TryBuy"/>: buying a higher tier
		/// displaces a lower tier of the same SlotKind already bound.
		public int             Tier                { get; init; } = 0;

		/// Optional achievement gate. Items locked here stay hidden from
		/// the InventoryShop picker until the achievement fires.
		public AchievementId?  RequiredAchievement { get; init; }

		/// Optional item-prerequisites — every kind in this list must be
		/// owned (placed or in inventory) before this one is unlockable.
		/// Drives the lounge progression (LC1 → unlocks lounge chair +
		/// whiteboard → all owned unlocks LC2 → unlocks bookshelves + TV
		/// → bookshelves unlock the Books pool).
		public ItemKind[]      RequiredItems       { get; init; } = System.Array.Empty<ItemKind>();

		/// How many copies of this item land in inventory per single
		/// purchase. 1 by default. >1 turns the entry into a "bulk SKU"
		/// (Lounge Chair pair of 2). Pricing is per-SKU, so the per-unit
		/// price is <c>Price / BulkSize</c>.
		public int             BulkSize            { get; init; } = 1;

		/// True for "studio infrastructure" decorations like Plants — one
		/// purchase, one inventory entry, every matching scene slot enables
		/// at once. The InventoryItem isn't bound to any single
		/// <see cref="PlacementSlot"/>; instead, every slot whose
		/// <see cref="PlacementSlot.PreferredItemKind"/> matches enables
		/// while the player owns at least one of <see cref="Kind"/>.
		/// Mutually exclusive with <see cref="BulkSize"/> &gt; 1 (one
		/// purchase = one item by definition).
		public bool            FillsAllMatchingSlots { get; init; } = false;

		/// Hide this entry from the shop entirely. Catalogue still exists
		/// so save/load and any cross-references compile, but the player
		/// can't see or buy it. Used for items whose scene authoring
		/// isn't done yet — flipping back to false re-enables them once
		/// the placement slots / visuals land.
		public bool            Hidden              { get; init; } = false;

		/// Hidden flat add to every employee stat. For desk-bound items
		/// (chair / computer / monitor) the boost only applies to the
		/// employee whose desk this item is mounted on. For non-desk-bound
		/// furniture the boost applies studio-wide (any employee benefits
		/// while the player owns at least one of <see cref="Kind"/>).
		/// Numbers are intentionally hidden from UI — the player just
		/// notices their team performing better.
		public int             StatBoost           { get; init; } = 0;
	}

	/// All purchasable items. Order is the default UI order within each
	/// category — keep workstation items first (Desk is the most important).
	///
	/// Implemented as a getter (rather than a `static readonly` array)
	/// because s&amp;box's hot-reload doesn't re-run static initializers —
	/// editing this list under `static readonly` left old entry data live
	/// in memory until a full editor restart. A fresh list per access is
	/// ~50 entries of allocation; not measurable.
	public static IReadOnlyList<Entry> All => BuildEntries();

	static Entry[] BuildEntries() => new Entry[]
	{
		// ── Workstation ─────────────────────────────────────────────────
		// Each Desk represents one full workstation. Default visuals
		// (laptop / basic monitor / basic chair) are scene-authored and
		// come for free with the workstation — they aren't separately
		// purchasable. Upgrade those defaults via the entries further down.
		new() {
			Kind                = ItemKind.Desk,
			Name                = "New Desk",
			Description         = "Lets you hire one more employee. Comes with a laptop, monitor, and chair",
			Price               = 1_000,
			Category            = ItemCategory.Workstation,
			Slot                = SlotKind.Desk,
			RequiredAchievement = null,
		},

		// ── Equipment ───────────────────────────────────────────────────
		new() {
			Kind                = ItemKind.ServerRack,
			Name                = "Server Rack",
			Description         = "Host your own builds.",
			Price               = 2_500,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Equipment,
			RequiredAchievement = AchievementId.Hire10,
			Hidden              = true,   // scene authoring TBD
		},

		// ── Amenities ───────────────────────────────────────────────────
		new() {
			Kind                = ItemKind.CoffeeMachine,
			Name                = "Coffee Machine",
			Description         = "Keeps the team awake.",
			Price               = 500,
			Category            = ItemCategory.Amenities,
			Slot                = SlotKind.Decoration,
			StatBoost           = 10,
			RequiredAchievement = null,
			Hidden              = true,   // scene authoring TBD
		},
		new() {
			Kind                = ItemKind.Whiteboard,
			Name                = "Whiteboard",
			Description         = "Plan sprints and diagrams.",
			Price               = 2_500,
			Category            = ItemCategory.Amenities,
			Slot                = SlotKind.Decoration,
			StatBoost           = 50,
			RequiredItems       = new[] { ItemKind.LoungeCarpet1 },
			RequiredAchievement = null,
		},

		// ── Decoration ──────────────────────────────────────────────────
		// Plants is a "fills-all" decoration: one purchase, one inventory
		// entry, every plant slot in the scene enables at once. Sized this
		// way because the player isn't picking individual plant placements
		// — they're choosing whether the office has greenery at all.
		new() {
			Kind                = ItemKind.PottedPlant,
			Name                = "Plants",
			Description         = "Greenery throughout the office. Lifts the mood and unlocks the lounge progression.",
			Price               = 320,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Decoration,
			FillsAllMatchingSlots = true,
			StatBoost           = 6,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.Bin,
			Name                = "Bin",
			Description         = "An office bin. Where the bad pitches go.",
			Price               = 50,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Decoration,
			// Bin is intentionally the cheapest stat-boosting item — it's
			// the player's first taste of the studio-wide-boost concept.
			// Dollar-per-stat being top-of-class is the point: it gives
			// a satisfying "this matters" moment on the very first buy.
			StatBoost           = 2,
			RequiredAchievement = null,
		},

		// ── Studio infrastructure (boost-only, no placement) ─────────────
		// Server's Slot=None means it never enters the auto-place flow —
		// the InventoryItem just sits in storage and OwnsAny(Server) reads
		// true everywhere. Drives StudioProductivityMultiplier and unlocks
		// the Multiplayer genre.
		new() {
			Kind                = ItemKind.Server,
			Name                = "Server",
			Description         = "Studio-wide infrastructure boost. Productivity +20% and unlocks Multiplayer projects. No physical placement.",
			Price               = 12_000,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.None,
			// Studio-wide × N employees made this dollar-for-stat ~3× more
			// efficient than an Elite mount. Reduced 8 → 3 so the Server
			// reads as "another piece of office infrastructure" not "the
			// best item in the game".
			StatBoost           = 6,
			RequiredAchievement = AchievementId.Hire8,
			Hidden              = true,   // not yet wired to gameplay
		},

		// ── Lounge progression ──────────────────────────────────────────
		// Plants gate LC1; LC1 unlocks Lounge Chair and Whiteboard; owning
		// LC1 + a chair pack + a whiteboard unlocks LC2; LC2 gates the
		// bookshelves + TV.
		new() {
			Kind                = ItemKind.LoungeCarpet1,
			Name                = "Lounge Carpet 1",
			Description         = "Anchor a chill-out corner. Unlocks lounge chairs and the whiteboard.",
			Price               = 1_500,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.LoungeCarpet1,
			StatBoost           = 30,
			RequiredItems       = new[] { ItemKind.PottedPlant },
			RequiredAchievement = null,
		},
		// Lounge Chairs is a "fills-all" decoration like Plants — one
		// purchase, every lounge-chair slot enables at once. The scene's
		// chair slots have Kind = SlotKind.LoungeChair (no PreferredItemKind
		// filter), and ApplySlotVisibility treats Kind-matching slots as
		// fills-all targets when the catalogue entry has the flag set.
		new() {
			Kind                = ItemKind.LoungeChair,
			Name                = "Lounge Chairs",
			Description         = "Soft chairs throughout the lounge area.",
			Price               = 5_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.LoungeChair,
			FillsAllMatchingSlots = true,
			StatBoost           = 100,
			RequiredItems       = new[] { ItemKind.LoungeCarpet1 },
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.LoungeCarpet2,
			Name                = "Lounge Carpet 2",
			Description         = "Expand the lounge. Unlocks bookshelves and a TV.",
			Price               = 10_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.LoungeCarpet2,
			StatBoost           = 200,
			RequiredItems       = new[]
			{
				ItemKind.LoungeCarpet1,
				ItemKind.LoungeChair,
				ItemKind.Whiteboard,
			},
			RequiredAchievement = null,
		},

		// ── Bookshelves + TV (gated behind LC2) ─────────────────────────
		new() {
			Kind                = ItemKind.Bookshelf1,
			Name                = "Bookshelf I",
			Description         = "First bookshelf. Holds books as you collect them.",
			Price               = 15_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Bookshelf,
			StatBoost           = 300,
			RequiredItems       = new[] { ItemKind.LoungeCarpet2 },
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.Bookshelf2,
			Name                = "Bookshelf II",
			Description         = "A second bookshelf — for once you've outgrown the first.",
			Price               = 25_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Bookshelf,
			StatBoost           = 500,
			RequiredItems       = new[] { ItemKind.LoungeCarpet2, ItemKind.Bookshelf1 },
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.TV,
			Name                = "TV",
			Description         = "Office entertainment. Morale-positive ambient noise.",
			Price               = 30_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.TV,
			StatBoost           = 600,
			RequiredItems       = new[] { ItemKind.LoungeCarpet2 },
			RequiredAchievement = null,
		},
		// Couch is a fills-all decoration like Plants / LoungeChair — the
		// scene has one couch slot in the LC2 lounge area, and one purchase
		// enables it. Hidden from inventory so the player isn't told they
		// have "1 in storage".
		new() {
			Kind                = ItemKind.Couch,
			Name                = "Couch",
			Description         = "A soft couch for the lounge corner.",
			Price               = 20_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Couch,
			FillsAllMatchingSlots = true,
			StatBoost           = 400,
			RequiredItems       = new[] { ItemKind.LoungeCarpet2 },
			RequiredAchievement = null,
		},

		// ── Toilet (lounge capstone) ────────────────────────────────────────
		// Gated behind every other lounge-progression item being owned. One
		// purchase, one toilet slot — the scene's toilet GameObject carries
		// a PlacementSlot with Kind = SlotKind.Toilet so it starts hidden
		// and flips visible on purchase. Massive studio-wide stat boost
		// matches the $1M sticker price; this is the endgame trophy item.
		new() {
			Kind                = ItemKind.Toilet,
			Name                = "Toilet",
			Description         = "Need to do it in the office somehow...",
			Price               = 1_000_000,
			Category            = ItemCategory.Amenities,
			Slot                = SlotKind.Toilet,
			FillsAllMatchingSlots = true,
			StatBoost           = 2_000,
			RequiredItems       = new[]
			{
				ItemKind.LoungeCarpet1,
				ItemKind.LoungeCarpet2,
				ItemKind.LoungeChair,
				ItemKind.Whiteboard,
				ItemKind.Bookshelf1,
				ItemKind.Bookshelf2,
				ItemKind.TV,
				ItemKind.Couch,
			},
			RequiredAchievement = null,
		},

		// ── Books — each gated by a milestone, all need a bookshelf ────
		// Achievement gates run from "early studio" → "late dynasty";
		// flavour-text picks reflect the rhythm of the run. Every book
		// also requires Bookshelf1 to be owned (the bookshelves themselves
		// are gated behind LC2's chain, so books cannot leak forward).
		new() {
			Kind                = ItemKind.Book1,
			Name                = "Book I",
			Description         = "A worn programming primer. Stocked with your first bookshelf.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.Book2,
			Name                = "Book II",
			Description         = "An indie-dev memoir. Found after shipping a few games.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Ship3Games,
		},
		new() {
			Kind                = ItemKind.Book3,
			Name                = "Book III",
			Description         = "Notes on building a team. Mid-studio reading.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Hire5,
		},
		new() {
			Kind                = ItemKind.Book4,
			Name                = "Book IV",
			Description         = "Studio finance basics. Earned the hard way.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Earn10k,
		},
		new() {
			Kind                = ItemKind.Book5,
			Name                = "Book V",
			Description         = "Audience-building playbook. Comes with your first hundred fans.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Players100,
		},
		new() {
			Kind                = ItemKind.Book6,
			Name                = "Book VI",
			Description         = "Six-figure-studio retrospective.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Earn100k,
		},
		new() {
			Kind                = ItemKind.Book7,
			Name                = "Book VII",
			Description         = "Org-design at scale. Required reading once the studio's veteran.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Hire10,
		},
		new() {
			Kind                = ItemKind.Book8,
			Name                = "Book VIII",
			Description         = "Ten-game retrospective. The kind of book a veteran writes.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Ship10Games,
		},
		new() {
			Kind                = ItemKind.Book9,
			Name                = "Book IX",
			Description         = "Cult-following case studies.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Players10k,
		},
		new() {
			Kind                = ItemKind.Book10,
			Name                = "Book X",
			Description         = "Million-dollar studio retrospective.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Earn1M,
		},
		new() {
			Kind                = ItemKind.Book11,
			Name                = "Book XI",
			Description         = "Talent-spotting masterclass.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.DiscoverAllAbilities,
		},
		new() {
			Kind                = ItemKind.Book12,
			Name                = "Book XII",
			Description         = "Mainstream-studio lore. The capstone of the shelf.",
			Price               = 2_000,
			Category            = ItemCategory.Decoration,
			Slot                = SlotKind.Book,
			StatBoost           = 40,
			RequiredItems       = new[] { ItemKind.Bookshelf1 },
			RequiredAchievement = AchievementId.Players1M,
		},

		// ── Tiered upgrades (Standard / Premium / Elite per mount kind) ──
		// 8× pricing per tier — Premium and Elite are deliberately steep
		// luxury tiers, not "buy three Elites in act 1" upgrades. StatBoost
		// only doubles per tier so the boost ramp stays modest while the
		// price ramp is dramatic. Computer and Chair have free defaults
		// (laptop / starter chair) authored in the scene; Monitor has no
		// default (first monitor purchase IS the basic visual appearing).
		// Auto-replace logic in InventoryManager.TryBuy displaces lower-
		// tier items of the same SlotKind when a higher tier is bought.

		// ── Computer mount (laptop is the free Tier 0 default) ───────────
		new() {
			Kind                = ItemKind.ComputerStandard,
			Name                = "Standard PC",
			Description         = "Replaces the laptop with a desktop tower.",
			Price               = 1_500,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Computer,
			Tier                = 1,
			StatBoost           = 90,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.ComputerPremium,
			Name                = "Premium PC",
			Description         = "Pro-grade tower with more cores and faster builds.",
			Price               = 12_000,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Computer,
			Tier                = 2,
			StatBoost           = 180,
			RequiredAchievement = AchievementId.ShipFirstGame,
		},
		new() {
			Kind                = ItemKind.ComputerElite,
			Name                = "Elite Workstation",
			Description         = "The top of the line. Compiles your dreams.",
			Price               = 96_000,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Computer,
			Tier                = 3,
			StatBoost           = 360,
			RequiredAchievement = AchievementId.Ship3Games,
		},

		// ── Monitor mount (no default — first purchase IS the visual) ────
		new() {
			Kind                = ItemKind.MonitorStandard,
			Name                = "Standard Monitor",
			Description         = "A basic external display. Pairs with a desktop.",
			Price               = 400,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Monitor,
			Tier                = 1,
			StatBoost           = 24,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.MonitorPremium,
			Name                = "Premium Monitor",
			Description         = "Higher refresh rate, better panel.",
			Price               = 3_200,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Monitor,
			Tier                = 2,
			StatBoost           = 48,
			RequiredAchievement = AchievementId.ShipFirstGame,
		},
		new() {
			Kind                = ItemKind.MonitorElite,
			Name                = "Elite Display",
			Description         = "Ultrawide, calibrated, beautiful.",
			Price               = 25_600,
			Category            = ItemCategory.Equipment,
			Slot                = SlotKind.Monitor,
			Tier                = 3,
			StatBoost           = 96,
			RequiredAchievement = AchievementId.Ship3Games,
		},

		// ── Chair mount (default chair is the free Tier 0) ───────────────
		new() {
			Kind                = ItemKind.ChairStandard,
			Name                = "Standard Chair",
			Description         = "An upgrade from the entry-level chair.",
			Price               = 300,
			Category            = ItemCategory.Workstation,
			Slot                = SlotKind.Chair,
			Tier                = 1,
			StatBoost           = 18,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.ChairPremium,
			Name                = "Premium Chair",
			Description         = "Ergonomic, posture-friendly. Worth every dollar.",
			Price               = 2_400,
			Category            = ItemCategory.Workstation,
			Slot                = SlotKind.Chair,
			Tier                = 2,
			StatBoost           = 36,
			RequiredAchievement = AchievementId.ShipFirstGame,
		},
		new() {
			Kind                = ItemKind.ChairElite,
			Name                = "Elite Chair",
			Description         = "Hand-stitched leather, lumbar memory. Statement piece.",
			Price               = 19_200,
			Category            = ItemCategory.Workstation,
			Slot                = SlotKind.Chair,
			Tier                = 3,
			StatBoost           = 72,
			RequiredAchievement = AchievementId.Ship3Games,
		},

		// ── Consumables ─────────────────────────────────────────────────
		// Bought into the stash, never placed. Player picks a target hire
		// from the InventoryPanel's Consumable tab and hands one over →
		// `InventoryManager.TryGiveConsumable` applies a small morale bump
		// (v1 effect; a proper timed buff system is a follow-up).
		new() {
			Kind                = ItemKind.ChocolateBar,
			Name                = "Chocolate Bar",
			Description         = "A small treat. Lifts spirits a bit.",
			Price               = 50,
			Category            = ItemCategory.Consumable,
			Slot                = SlotKind.None,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.EnergyDrink,
			Name                = "Energy Drink",
			Description         = "More likely to be in an innovative mood.",
			Price               = 800,
			Category            = ItemCategory.Consumable,
			Slot                = SlotKind.None,
			RequiredAchievement = null,
		},
		new() {
			Kind                = ItemKind.ProteinBar,
			Name                = "Protein Bar",
			Description         = "Healthy snack. Steady morale top-up.",
			Price               = 30,
			Category            = ItemCategory.Consumable,
			Slot                = SlotKind.None,
			RequiredAchievement = null,
		},

		// Bad-mood immuniser. Branch in InventoryManager.TryGiveConsumable
		// calls EmployeeNPC.GrantBadMoodImmunity(30) instead of the morale
		// bump path. Gated behind 3 lifetime hires — by then the player
		// has multiple employees that can roll Bad and is starting to feel
		// the production-disruption pressure the consumable mitigates.
		new() {
			Kind                = ItemKind.ChewingGum,
			Name                = "Chewing Gum",
			Description         = "Calms the nerves. Prevents bad mood for 30 days.",
			Price               = 800,
			Category            = ItemCategory.Consumable,
			Slot                = SlotKind.None,
			RequiredAchievement = null,
		},

		// "Skip-the-grind" consumable. Buying decrements normal consumable
		// stock; using applies a 70 % completion to the active project (see
		// InventoryManager.TryUseSmokeBreak + GameProjectManager.CompleteWithPenalty).
		// Catalogue Price is the BASE — InventoryManager.PriceFor scales it
		// exponentially per use up to the $1M cap.
		new() {
			Kind                = ItemKind.SmokeBreak,
			Name                = "Outsource Everything",
			Description         = "Hand the remaining work to a contractor. Ships at 70% potential.",
			Price               = 100,
			Category            = ItemCategory.Consumable,
			Slot                = SlotKind.None,
			RequiredAchievement = null,
		},
	};

	public static Entry Get( ItemKind kind )
	{
		foreach ( var e in All )
			if ( e.Kind == kind ) return e;
		return null;
	}

	/// True if this kind's achievement AND item prerequisites are satisfied.
	/// Achievement gate (single optional <see cref="Entry.RequiredAchievement"/>)
	/// AND every kind in <see cref="Entry.RequiredItems"/> currently owned
	/// (placed or stashed). Selling a prerequisite item re-locks the gate.
	public static bool IsUnlocked( ItemKind kind )
	{
		var e = Get( kind );
		if ( e is null ) return false;
		if ( e.RequiredAchievement is { } id && !Achievements.IsUnlocked( id ) )
			return false;

		// SmokeBreak (Outsource Everything) is gated to the player's 5th
		// game onward — they need to have lived through full Production
		// cycles before they get the skip-the-grind option, otherwise the
		// tutorial / early-game pacing collapses. No Ship4Games achievement
		// exists, so the gate lives here as a hardcoded GamesShipped check.
		if ( kind == ItemKind.SmokeBreak && Achievements.GamesShipped < 4 )
			return false;

		// ChewingGum (bad-mood immuniser) needs at least 3 lifetime hires.
		// No Hire3 achievement exists; falls back to the same hardcoded
		// counter pattern as SmokeBreak above.
		if ( kind == ItemKind.ChewingGum && Achievements.TotalHires < 3 )
			return false;

		var inv = InventoryManager.Instance;
		if ( e.RequiredItems is { Length: > 0 } reqs )
		{
			if ( inv is null ) return false;
			foreach ( var prereq in reqs )
				if ( inv.OwnsAny( prereq ) == false ) return false;
		}
		return true;
	}

	public static IEnumerable<Entry> ByCategory( ItemCategory cat )
	{
		foreach ( var e in All )
			if ( e.Category == cat ) yield return e;
	}
}
