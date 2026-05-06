using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Studio-wide inventory: the list of <see cref="InventoryItem"/>s the
/// player owns, plus the auto-place machinery that binds owned items to
/// scene-authored <see cref="PlacementSlot"/>s.
///
/// Visuals are scene-authored, not prefab-spawned: every PlacementSlot's
/// GameObject IS the visual (e.g., a "deskspace1" GameObject containing a
/// pre-built desk + laptop + chair). Placing an item at a slot ENABLES the
/// slot's GameObject; removing the item disables it. No prefab cloning
/// happens at runtime.
///
/// Lifecycle:
///   1. OnAwake — seed 1 Desk into _owned if the list is empty (first-hire
///      guarantee — even a brand-new save can hire on day 1).
///   2. OnStart — discover all PlacementSlots (walks SlotsRoot.Children
///      recursively when SlotsRoot is set, so disabled slots are still
///      found; falls back to Scene.GetAllComponents otherwise).
///   3. Run an auto-place pass over every unplaced item — enables the
///      bound slot's GameObject for each item placed.
///   4. After every TryBuy — run another auto-place pass for the new item.
///
/// Replaces:
///   • Shop.cs hardcoded item list (purchasing flow)
///   • HRManager.DiscoverDesks (desk-specific scene discovery, now generalised)
/// </summary>
public sealed class InventoryManager : Component
{
	public static InventoryManager Instance { get; private set; }

	// ── Modal state (mirrors Shop / Settings / Gallery / Training) ──────────

	public bool IsOpen { get; private set; }

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			GameMenu.Instance?.SetOpen( false );
			Shop.Instance?.SetOpen( false );
			Settings.Instance?.SetOpen( false );
			Gallery.Instance?.SetOpen( false );
			TrainingManager.Instance?.SetOpen( false );
			GameProjectManager.Instance?.SetOpen( false );
		}
		GameManager.RefreshPlayerLock();
	}

	// ── Owned inventory ──────────────────────────────────────────────────────

	readonly List<InventoryItem> _owned = new();
	public IReadOnlyList<InventoryItem> Owned => _owned;

	// ── Consumable shop stock ────────────────────────────────────────────────
	// Consumables aren't infinite — the shop holds at most MaxConsumableStock
	// of each kind. Buying one decrements; a per-kind restock timer adds one
	// back at a random interval. Restocks fire silently (no notification) per
	// design intent. Stock is persisted in the per-run save (InventorySave).

	const int   MaxConsumableStock = 5;
	const float MinRestockSeconds  = 60f;
	const float MaxRestockSeconds  = 180f;

	readonly Dictionary<ItemKind, int>   _consumableStock         = new();
	readonly Dictionary<ItemKind, float> _consumableRestockTimers = new();
	readonly System.Random               _rng                      = new();

	// ── Smoke Break (skip-the-grind consumable) ─────────────────────────────
	// Each Use of a Smoke Break finishes the active project at 70 % output
	// and bumps this counter — the next purchase costs 2× as much, capped
	// at $1M. Persisted via InventorySave so the curve survives save/load.
	int _smokeBreakUsesCount;
	public int SmokeBreakUsesCount => _smokeBreakUsesCount;

	const long  SmokeBreakBasePrice    = 100;
	const long  SmokeBreakMaxPrice     = 1_000_000;
	const float SmokeBreakOutputFactor = 0.7f;

	/// In-game days that pass when a Smoke Break is used. Reflects the
	/// "I went on a smoke break and came back to a finished game" fiction
	/// — salaries, research drips, and gallery review timers all get the
	/// week's worth of progress they would have if the project had run
	/// naturally to completion.
	const int   SmokeBreakDaysSkipped  = 7;

	/// Current shop stock for a consumable. Returns 0 for non-consumable kinds.
	public int ConsumableStock( ItemKind kind )
		=> _consumableStock.TryGetValue( kind, out var n ) ? n : 0;

	// ── Scene slots ──────────────────────────────────────────────────────────

	readonly List<PlacementSlot> _slots = new();
	public IReadOnlyList<PlacementSlot> Slots => _slots;

	/// Optional parent GameObject containing every <see cref="PlacementSlot"/>
	/// in the office (deskspaces, decoration spots, etc). Walking this
	/// container's child tree includes DISABLED descendants — that's the
	/// whole point of having it. Without SlotsRoot, only currently-enabled
	/// slots are discovered.
	///
	/// Recommended setup: parent every disabled-by-default slot
	/// (e.g. deskspace1..deskspace8) under one GameObject and drag it here.
	[Property] public GameObject SlotsRoot { get; set; }

	// ── Lifecycle ────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;

		// First-hire guarantee: every fresh studio owns one Desk regardless of
		// scene authoring. If you wipe inventory in a debug menu, this
		// re-seeds on the next OnAwake — safe for restart flows.
		if ( _owned.Count == 0 )
		{
			_owned.Add( new InventoryItem { Kind = ItemKind.Desk } );
			Log.Info( "[Inv] Seeded starter Desk." );
		}

		// Seed each consumable's shop stock at full (5). Save load will
		// overwrite with the persisted values; fresh studios get a full
		// shelf to start so the first relationship interaction can buy
		// without waiting on a restock timer.
		foreach ( var entry in InventoryCatalogue.All )
		{
			if ( entry.Category != ItemCategory.Consumable ) continue;
			_consumableStock[entry.Kind]         = MaxConsumableStock;
			_consumableRestockTimers[entry.Kind] = NextRestockInterval();
		}
	}

	float NextRestockInterval()
		=> MinRestockSeconds + (float)_rng.NextDouble() * (MaxRestockSeconds - MinRestockSeconds);

	protected override void OnStart()
	{
		DiscoverSlots();
		BuildVariantCache();
		AutoPlaceAll();

		Log.Info( $"[Inv] {_owned.Count} item(s) owned · {_slots.Count} slot(s) in scene · {PlacedCount} placed." );

		// Per-Kind breakdown so authoring mistakes are obvious at boot
		// (e.g., "0 LoungeCarpet2 slots" tells you the second-carpet slot
		// in the scene is mis-typed, missing, or not under SlotsRoot).
		var counts = new Dictionary<SlotKind, int>();
		foreach ( var s in _slots )
		{
			counts.TryGetValue( s.Kind, out var n );
			counts[s.Kind] = n + 1;
		}
		var summary = string.Join( ", ", counts.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value}" ) );
		Log.Info( $"[Inv] slots by kind: {summary}" );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		TickConsumableRestocks();
		TickDeskExpansionTip();
	}

	// ── Desk-expansion tip ───────────────────────────────────────────────────
	// Sticky corner toast that nudges the player toward buying their first
	// extra desk after the tutorial wraps. Same self-heal cadence as the
	// gallery tip — survives hot-reload, manual dismissal, and notification
	// clears. Cleared automatically the moment the player buys their second
	// desk (the existing first-extra-desk hook in TryBuy calls
	// Notifications.RemoveByTag( DeskExpansionTipTag )).

	const string DeskExpansionTipTag    = "desk-expansion-tip";
	const float  DeskTipReinforceEvery  = 2f;
	float        _deskTipReinforceAccum;

	void TickDeskExpansionTip()
	{
		_deskTipReinforceAccum += Time.Delta;
		if ( _deskTipReinforceAccum < DeskTipReinforceEvery ) return;
		_deskTipReinforceAccum = 0f;

		// Only after tutorial wraps — pre-Complete the tutorial owns the
		// player's attention with its own sticky phase toasts.
		if ( TutorialManager.Instance is not { Phase: TutorialPhase.Complete } ) return;

		// Already owns a second desk — they're past this hint.
		if ( OwnedCount( ItemKind.Desk ) > 1 ) return;

		// Already on screen — leave it alone.
		if ( Notifications.HasTag( DeskExpansionTipTag ) ) return;

		Notifications.Push( "Ready to grow?",
			"Buy a New Desk in the Shop to make room for another hire.",
			"info", duration: 0f, tag: DeskExpansionTipTag );
	}

	/// Tick each consumable's restock countdown. Each timer expiring adds
	/// one to that kind's stock (capped at <see cref="MaxConsumableStock"/>)
	/// and rolls a fresh random interval. Silent — no notification per
	/// design intent.
	void TickConsumableRestocks()
	{
		// Snapshot keys: the dictionary is mutated below, can't enumerate live.
		var keys = _consumableStock.Keys.ToList();
		foreach ( var kind in keys )
		{
			if ( _consumableStock[kind] >= MaxConsumableStock ) continue;
			_consumableRestockTimers.TryGetValue( kind, out var t );
			t -= Time.Delta;
			if ( t > 0 )
			{
				_consumableRestockTimers[kind] = t;
				continue;
			}
			_consumableStock[kind]++;
			_consumableRestockTimers[kind] = NextRestockInterval();
		}
	}

	// ── Slot discovery ───────────────────────────────────────────────────────

	/// Build the slot list. When <see cref="SlotsRoot"/> is set, walk its
	/// descendants recursively — this picks up DISABLED slot GameObjects
	/// too, which is essential when slots are scene-authored visuals that
	/// start hidden and become visible as the player buys them.
	///
	/// Without SlotsRoot, falls back to <c>Scene.GetAllComponents</c>, which
	/// only returns components on enabled GameObjects. Mixed setups
	/// (some slots under SlotsRoot, others elsewhere and always-enabled)
	/// are merged into one deduplicated list.
	void DiscoverSlots()
	{
		_slots.Clear();

		if ( SlotsRoot is not null )
		{
			CollectSlotsRecursive( SlotsRoot );
		}

		// Pick up any slots that aren't under SlotsRoot, e.g. markers placed
		// at the scene root for decoration spots.
		foreach ( var s in Scene.GetAllComponents<PlacementSlot>() )
		{
			if ( !_slots.Contains( s ) ) _slots.Add( s );
		}
	}

	void CollectSlotsRecursive( GameObject root )
	{
		// Try PlacementSlot directly, then the legacy EmployeeDesk subclass
		// (s&box's Components.Get<T> does exact-type matching).
		PlacementSlot slot   = root.Components.Get<PlacementSlot>();
		PlacementSlot legacy = root.Components.Get<EmployeeDesk>();
		var pick = slot ?? legacy;

		if ( pick is not null && !_slots.Contains( pick ) ) _slots.Add( pick );

		foreach ( var child in root.Children )
		{
			CollectSlotsRecursive( child );
		}
	}

	/// True if any owned item references this slot's id.
	public bool IsSlotOccupied( Guid slotId )
	{
		foreach ( var item in _owned )
			if ( item.PlacedAtSlotId == slotId ) return true;
		return false;
	}

	// ── Auto-place pass ──────────────────────────────────────────────────────

	/// Try to place every unplaced item at the first compatible free slot,
	/// then update every slot's visibility (workstation enable/disable +
	/// variant child swapping).
	///
	/// Desk-bound items (Chair / Computer / Monitor mounts) are skipped —
	/// the player picks which desk they go on via DeskAssignPanel. Without
	/// that skip, the auto-place pass would shove a new chair onto whatever
	/// desk happened to come first in the slot list, ignoring the player's
	/// intent for this hire vs. that one.
	///
	/// Idempotent — calling repeatedly is safe.
	public void AutoPlaceAll()
	{
		foreach ( var item in _owned )
		{
			if ( item.IsPlaced ) continue;
			if ( IsDeskBoundKind( item.Kind ) ) continue;

			// Fills-all decorations (Plants) aren't bound to a single slot;
			// ApplySlotVisibility handles their visuals studio-wide.
			if ( InventoryCatalogue.Get( item.Kind )?.FillsAllMatchingSlots == true )
				continue;

			TryPlace( item );
		}

		ApplySlotVisibility();
	}

	/// True if items of <paramref name="kind"/> mount inside a Desk
	/// workstation (chair / computer / monitor). Drives the skip-auto-place
	/// rule plus the InventoryPanel's "Assign" button visibility.
	public static bool IsDeskBoundKind( ItemKind kind )
	{
		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null ) return false;
		return entry.Slot == SlotKind.Chair
		    || entry.Slot == SlotKind.Computer
		    || entry.Slot == SlotKind.Monitor;
	}

	/// Walk every slot and ensure its GameObject + child visuals match the
	/// current bind state.
	///
	/// Workstation slots: GameObject enabled iff an item is bound.
	/// Mount slots: GameObject stays as-is (controlled by parent slot);
	/// variant children swap based on the bound item kind, falling back to
	/// <see cref="PlacementSlot.DefaultVariant"/> when no item is bound.
	void ApplySlotVisibility()
	{
		foreach ( var slot in _slots )
		{
			if ( slot is null ) continue;
			var go = slot.GameObject;
			if ( go is null )   continue;

			var bound = FindItemBoundTo( slot.Id );

			if ( slot.Mode == SlotMode.Workstation )
			{
				// "Fills-all" rule: if any FillsAll catalogue entry targets
				// this slot, enabling tracks ownership of that kind rather
				// than per-slot binding. Two ways an entry can target the
				// slot: explicit PreferredItemKind on the slot (Plants
				// share SlotKind.Decoration with other items, so the slot
				// has to disambiguate), or dedicated Kind match
				// (LoungeChair has its own SlotKind with no filter needed).
				bool willEnable;
				ItemKind? fillsAllKind = null;
				if ( slot.PreferredItemKind is { } pref
					&& InventoryCatalogue.Get( pref )?.FillsAllMatchingSlots == true )
				{
					fillsAllKind = pref;
				}
				else
				{
					// Only auto-claim a slot by Kind match if the catalogue
					// entry's SlotKind is DEDICATED (no other entry shares
					// it). Without this guard, a Decoration slot with no
					// PreferredItemKind filter would silently enable as
					// soon as any FillsAll Decoration item is owned —
					// Plants would turn on every Bin / Whiteboard /
					// CoffeeMachine slot in the office. Shared-Kind slots
					// must disambiguate via PreferredItemKind.
					//
					// Compare entries by Kind, not reference: the catalogue
					// getter rebuilds entries fresh each access, so two
					// references to "the same" entry never compare equal.
					foreach ( var e in InventoryCatalogue.All )
					{
						if ( !e.FillsAllMatchingSlots ) continue;
						if ( e.Slot != slot.Kind )      continue;

						bool shared = false;
						foreach ( var other in InventoryCatalogue.All )
						{
							if ( other.Kind == e.Kind )  continue;
							if ( other.Slot != e.Slot )  continue;
							shared = true;
							break;
						}
						if ( shared ) continue;

						fillsAllKind = e.Kind;
						break;
					}
				}

				if ( fillsAllKind.HasValue )
					willEnable = OwnsAny( fillsAllKind.Value );
				else
					willEnable = bound is not null;

				// If this slot is about to flip from disabled→enabled and
				// the player is standing inside its footprint, teleport
				// them to the safe anchor first. Without this the slot's
				// collider interpenetrates with the player's collider and
				// the physics solver pops them out unpredictably.
				// Idempotent: re-teleporting to the same spot is a no-op,
				// so multiple slots flipping in one call only move the
				// player once.
				if ( willEnable && !go.Enabled
					&& GameSaveManager.PlayerWouldOverlap( go ) )
				{
					GameSaveManager.Instance?.MovePlayerToSafeAnchor();
				}

				go.Enabled = willEnable;
			}

			// Variant child switching applies in either mode — a Workstation
			// slot can also have inline variants (rare but allowed).
			ApplyVariantChildren( slot, bound );
		}
	}

	// Cached variant info per slot, built once at OnStart while every
	// GameObject is still enabled. We can't read this live, because s&box's
	// Components.Get<T> returns null on disabled GameObjects — the first
	// ApplyVariantChildren pass disables every non-matching variant, and
	// from then on we'd lose sight of their ItemVariant component (and
	// never re-enable the right one when the bound item changes).
	readonly Dictionary<PlacementSlot, List<VariantChild>> _variantCache = new();

	readonly record struct VariantChild( GameObject Go, ItemKind Kind, bool IsDefault );

	void BuildVariantCache()
	{
		_variantCache.Clear();
		foreach ( var slot in _slots )
		{
			if ( slot?.GameObject is null ) continue;

			List<VariantChild> list = null;
			foreach ( var child in slot.GameObject.Children )
			{
				// Nested PlacementSlots manage themselves — skip.
				if ( child.Components.Get<PlacementSlot>() is not null ) continue;

				var iv = child.Components.Get<ItemVariant>();
				if ( iv is not null )
				{
					(list ??= new()).Add( new VariantChild( child, iv.Kind, false ) );
					continue;
				}

				if ( slot.DefaultVariant is not null && child == slot.DefaultVariant )
				{
					(list ??= new()).Add( new VariantChild( child, default, true ) );
				}
			}
			if ( list is not null ) _variantCache[slot] = list;
		}
	}

	/// Enable the variant child whose <see cref="ItemVariant.Kind"/> matches
	/// the bound item; enable the slot's <see cref="PlacementSlot.DefaultVariant"/>
	/// when no item is bound; disable all other variants. Reads from
	/// <see cref="_variantCache"/> rather than walking children live — see
	/// the cache field's note for why.
	void ApplyVariantChildren( PlacementSlot slot, InventoryItem bound )
	{
		if ( !_variantCache.TryGetValue( slot, out var list ) ) return;

		// Cross-mount rule: a desk's laptop default hides when that desk
		// has a Monitor bound — design intent is "monitor implies desktop
		// setup, no laptop hanging off a screen". Computed once per call
		// so the per-child loop stays cheap.
		bool defaultSuppressed = slot.Kind == SlotKind.Computer
			&& DeskHasBoundMount( slot, SlotKind.Monitor );

		foreach ( var v in list )
		{
			if ( v.Go is null ) continue;
			v.Go.Enabled = v.IsDefault
				? bound is null && !defaultSuppressed
				: bound is not null && v.Kind == bound.Kind;
		}
	}

	/// True when the desk containing <paramref name="mount"/> has any item
	/// bound at a sibling mount of <paramref name="otherKind"/>. Used by
	/// the variant logic to suppress one mount's default visual based on
	/// another mount's bind state (e.g., hide the laptop when a monitor
	/// is on the same desk).
	bool DeskHasBoundMount( PlacementSlot mount, SlotKind otherKind )
	{
		if ( mount?.GameObject is null ) return false;

		PlacementSlot desk = null;
		foreach ( var s in _slots )
		{
			if ( s.Kind != SlotKind.Desk ) continue;
			if ( s.GameObject is null )    continue;
			if ( IsDescendantOf( mount.GameObject, s.GameObject ) ) { desk = s; break; }
		}
		if ( desk is null ) return false;

		var other = FindMountInDesk( desk, otherKind );
		return other is not null && FindItemBoundTo( other.Id ) is not null;
	}

	InventoryItem FindItemBoundTo( Guid slotId )
	{
		foreach ( var item in _owned )
			if ( item.PlacedAtSlotId == slotId ) return item;
		return null;
	}

	/// Unbind any currently-placed item of the same <see cref="SlotKind"/>
	/// that is at a lower <see cref="InventoryCatalogue.Entry.Tier"/> than
	/// the incoming entry. The lower-tier item returns to storage (still
	/// owned, just unplaced) — no refund. Called from <see cref="TryBuy"/>
	/// before <see cref="TryPlace"/> so the new higher-tier item can land
	/// at the slot the lower tier was occupying.
	///
	/// Untiered entries (Tier == 0) don't displace anything.
	void UnbindLowerTiersFor( InventoryCatalogue.Entry incoming )
	{
		if ( incoming is null )    return;
		if ( incoming.Tier <= 0 )  return;

		foreach ( var existing in _owned )
		{
			if ( !existing.IsPlaced )                continue;
			var existingEntry = InventoryCatalogue.Get( existing.Kind );
			if ( existingEntry is null )             continue;
			if ( existingEntry.Slot != incoming.Slot ) continue;
			if ( existingEntry.Tier >= incoming.Tier ) continue;

			// Lower tier of the same slot kind — kick it out.
			existing.PlacedAtSlotId    = null;
			existing.SpawnedGameObject = null;
		}
	}

	/// Attempt to place a single item at the first compatible free slot in
	/// scene-discovery order. Returns true on success.
	///
	/// Placement = enable the slot's GameObject. The visual is scene-authored
	/// (e.g. deskspace1's pre-built desk/laptop/chair); we just turn it on.
	/// No runtime prefab spawning.
	public bool TryPlace( InventoryItem item )
	{
		if ( item is null )           return false;
		if ( item.IsPlaced )          return false;

		var entry = InventoryCatalogue.Get( item.Kind );
		if ( entry is null )          return false;

		// First compatible, available slot wins. Order is deterministic
		// (scene-discovery order from DiscoverSlots).
		//
		// Two-pass match: prefer slots whose PreferredItemKind filter
		// matches the item's exact kind, fall back to slots with no filter.
		// Slots with a filter set to a DIFFERENT kind are ignored entirely
		// — that's the whole point of the filter (e.g., a plant slot won't
		// accept a Bin).
		PlacementSlot pick = null;
		int kindMatches = 0, occupiedSkips = 0, filterSkips = 0;
		foreach ( var slot in _slots )
		{
			if ( slot.Kind != entry.Slot ) continue;
			kindMatches++;
			if ( !slot.IsAvailable )       { occupiedSkips++; continue; }
			if ( slot.PreferredItemKind is { } pref && pref != item.Kind )
			{
				filterSkips++;
				continue;
			}
			if ( slot.PreferredItemKind == item.Kind )
			{
				pick = slot;     // exact-match wins immediately
				break;
			}
			pick ??= slot;       // first generic-match remembered as fallback
		}

		if ( pick is null )
		{
			// Demoted to Info: TryBuy's bulk loop calls this once per item
			// and surfaces a "X/N placed; Y kept in storage" toast already.
			// Keep the breakdown for debugging individual placement issues
			// (e.g. the LoungeCarpet2 "no slot" investigation), but don't
			// fire a Warning — the toast carries the user-facing message.
			Log.Info( $"[Inv] TryPlace({item.Kind}) → storage: no free slot of Kind={entry.Slot}. " +
				$"Matching slots in scene: {kindMatches} " +
				$"(occupied: {occupiedSkips}, filter-rejected: {filterSkips})." );
			return false;
		}

		// Bind only; visibility is set uniformly in ApplySlotVisibility,
		// which AutoPlaceAll calls right after the placement pass.
		item.PlacedAtSlotId    = pick.Id;
		item.SpawnedGameObject = pick.GameObject;
		return true;
	}

	// ── Buy ──────────────────────────────────────────────────────────────────

	/// Purchase one item of the given kind. Deducts price from money, appends
	/// a new InventoryItem, and runs the auto-place pass for the new item.
	/// Returns true on success (and pushes a Notification either way).
	public bool TryBuy( ItemKind kind )
	{
		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null )
		{
			Notifications.Push( "Buy failed", $"Unknown item kind: {kind}", "warning" );
			return false;
		}

		// Tutorial gate: during the BuyBinOnly step, the only thing the
		// player can purchase is a Bin. ShopPanel filters the visible list
		// to just the Bin during this phase, so this is mostly belt-and-
		// braces for any other code path that might try to call TryBuy.
		if ( TutorialManager.Instance is { Phase: TutorialPhase.BuyBinOnly }
		     && kind != ItemKind.Bin )
		{
			Notifications.Push( "Tutorial Step",
				"Buy a Bin first — your team needs an inspiring office.",
				"warning", duration: 5f );
			return false;
		}

		// Tutorial gate: during the BuyChair step, only the Standard Chair
		// is purchasable (matches the on-screen instruction). Premium /
		// Elite tiers stay locked until the tutorial completes.
		if ( TutorialManager.Instance is { Phase: TutorialPhase.BuyChair }
		     && kind != ItemKind.ChairStandard )
		{
			Notifications.Push( "Tutorial Step",
				"Buy a Standard Chair — Premium and Elite unlock once the tutorial wraps.",
				"warning", duration: 5f );
			return false;
		}

		if ( !InventoryCatalogue.IsUnlocked( kind ) )
		{
			var ach = entry.RequiredAchievement is { } id ? Achievements.Get( id ) : null;
			Notifications.Push( "Locked",
				ach is null
					? $"{entry.Name} is not yet available."
					: $"{entry.Name} unlocks via \"{ach.Name}\".",
				"warning" );
			return false;
		}

		// Cap check: covers consumable stock, scene free-slot count, the
		// stash-item one-and-done rule, and the desk progression gate.
		// Failure messages are tailored to the rule that bit, since the
		// player needs to know whether to wait, ship a game, or expand
		// the office.
		if ( BuyableCount( kind ) <= 0 )
		{
			string msg;
			if ( entry.Category == ItemCategory.Consumable )
				msg = $"{entry.Name} is out of stock. Check back later.";
			else if ( entry.Slot == SlotKind.None )
				msg = $"You already own a {entry.Name}.";
			else if ( kind == ItemKind.Desk )
				msg = "Ship more games to expand desk capacity.";
			else
				msg = $"No free slot for another {entry.Name}.";
			Notifications.Push( "Unavailable", msg, "warning" );
			return false;
		}

		long price = PriceFor( kind );
		var gm = GameManager.Instance;
		if ( gm is null || !gm.TrySpend( price ) )
		{
			Notifications.Push( "Can't Afford",
				$"{entry.Name} costs ${price:N0}.", "warning" );
			return false;
		}

		// Tutorial step hooks. Single point covers every success path below
		// (fills-all / stash / desk-bound / auto-place).
		if ( kind == ItemKind.Bin )
			TutorialManager.Instance?.NotifyBinBought();
		else if ( kind == ItemKind.ChairStandard )
			TutorialManager.Instance?.NotifyChairBought();

		// First desk past the starter: surface the per-desk-equipment
		// catalogue (Standard PC / Monitor / Chair) and hint at the new
		// hiring capacity. The desk-purchase add to _owned hasn't happened
		// yet at this point, so OwnedCount(Desk) == 1 means this very
		// purchase is the first extra desk. Blue "info" toast — guidance,
		// not urgency.
		if ( kind == ItemKind.Desk && OwnedCount( ItemKind.Desk ) == 1 )
		{
			// Clear the standing "Ready to grow?" tip — they just acted on it.
			Notifications.RemoveByTag( DeskExpansionTipTag );

			Notifications.Push( "Desk unlocked",
				"You can hire another employee — Plants, Standard PC, Monitor, and Chair are now in the shop.",
				"info", duration: 12f );

			// Kick the training system on with a 3-offer burst so the
			// player feels the value of the system right away. See
			// TrainingManager.ReleaseInitialOffers for the cap logic.
			TrainingManager.Instance?.ReleaseInitialOffers( 3 );

			// Force a fresh applicant into the HR inbox immediately —
			// independent of the regular SecondsBetweenApplicants cadence.
			// The player just unlocked their second desk; we want someone
			// to interview right now rather than making them wait another
			// 7 in-game days for the next roll.
			HRManager.Instance?.SpawnApplicantNow();
		}

		// "Fills-all" decorations (Plants) are one-shot: one InventoryItem,
		// no per-slot binding, every matching scene slot enables when the
		// player owns at least one. Bypass the bulk + TryPlace path and
		// just register ownership; ApplySlotVisibility handles the visuals.
		if ( entry.FillsAllMatchingSlots )
		{
			_owned.Add( new InventoryItem { Kind = kind } );
			ApplySlotVisibility();
			NotifyBuySuccess( entry,
				fallbackTitle: "Purchased",
				fallbackBody: $"{entry.Name} are now throughout the office." );
			return true;
		}

		// Bulk SKUs (Lounge Chair pair of 2) add multiple InventoryItems
		// per single purchase — one InventoryItem per copy so each can be
		// placed at its own free slot independently. Single-SKU items
		// collapse to the BulkSize=1 case (one item created).
		int    bulk  = System.Math.Max( 1, entry.BulkSize );
		var    fresh = new System.Collections.Generic.List<InventoryItem>( bulk );
		for ( int i = 0; i < bulk; i++ )
		{
			var copy = new InventoryItem { Kind = kind };
			_owned.Add( copy );
			fresh.Add( copy );
		}

		// Consumables don't bind to a PlacementSlot — they sit in the stash
		// until the player gives one to a hire (see TryGiveConsumable).
		// Decrement shop stock by the same count just added to inventory;
		// stock floor is 0, replenished silently by TickConsumableRestocks.
		if ( entry.Slot == SlotKind.None )
		{
			if ( entry.Category == ItemCategory.Consumable
				&& _consumableStock.TryGetValue( kind, out var stock ) )
			{
				_consumableStock[kind] = System.Math.Max( 0, stock - bulk );
			}

			NotifyBuySuccess( entry,
				fallbackTitle: "Purchased",
				fallbackBody: bulk > 1
					? $"{bulk}× {entry.Name} added to your stash."
					: $"{entry.Name} added to your stash." );
			return true;
		}

		// Desk-bound items (Chair / Computer / Monitor) skip auto-place —
		// they wait in storage until the player explicitly assigns each
		// copy to a specific desk via DeskAssignPanel. Tier-replace also
		// skips here: there's no automatic displacement until the player
		// picks a desk, at which point AssignToDesk handles displacement
		// scoped to that desk only.
		if ( IsDeskBoundKind( kind ) )
		{
			Notifications.Push( "Purchased",
				bulk > 1
					? $"{bulk}× {entry.Name} added to storage. Assign each in Inventory."
					: $"{entry.Name} added to storage. Assign in Inventory.",
				"info" );
			return true;
		}

		// Tier auto-replace: if this is a higher-tier item, displace any
		// currently-bound lower-tier item of the same SlotKind. The lower
		// item returns to storage (still owned, just unplaced) — the player
		// can sell it back via SellItem for 50 % of the catalogue price.
		UnbindLowerTiersFor( entry );

		int placedCount = 0;
		foreach ( var copy in fresh )
		{
			if ( TryPlace( copy ) ) placedCount++;
		}
		ApplySlotVisibility();

		string body = bulk == 1
			? (placedCount == 1
				? $"{entry.Name} placed in the office."
				: $"{entry.Name} bought — no free slot, kept in storage.")
			: $"{placedCount}/{bulk} {entry.Name} placed; {bulk - placedCount} kept in storage.";
		NotifyBuySuccess( entry,
			fallbackTitle: placedCount > 0 ? "Purchased" : "In Storage",
			fallbackBody:  body,
			fallbackLevel: placedCount > 0 ? "success" : "info" );
		return true;
	}

	/// Push a "Purchased" success notification, swapping in the studio-wide
	/// stat-boost flavour text when the catalogue entry carries a non-zero
	/// <see cref="InventoryCatalogue.Entry.StatBoost"/>. Desk-bound items
	/// (chair / computer / monitor) get their boost notification from the
	/// per-desk assign path instead, so they fall through to the fallback
	/// here. Consumables likewise — those route through TryGiveConsumable.
	void NotifyBuySuccess( InventoryCatalogue.Entry entry,
		string fallbackTitle, string fallbackBody, string fallbackLevel = "success" )
	{
		if ( entry.StatBoost > 0
			&& entry.Category != ItemCategory.Consumable
			&& !IsDeskBoundKind( entry.Kind ) )
		{
			Notifications.Push( "Stats Boosted",
				$"{entry.Name} bought — all employee stats got boosted.",
				"success" );
			return;
		}

		Notifications.Push( fallbackTitle, fallbackBody, fallbackLevel );
	}

	/// Live purchase price for <paramref name="kind"/>, including any
	/// dynamic scaling. Use this anywhere the shop or buy flow needs the
	/// price the player would pay right now — it falls back to the static
	/// <see cref="InventoryCatalogue.Entry.Price"/> for items without a
	/// scaling rule.
	///
	/// Desks scale exponentially: each *purchased* desk past the starter
	/// multiplies the catalogue base price by <see cref="DeskPriceMultiplier"/>
	/// raised to the count of prior purchases. Result is rounded to the
	/// nearest <see cref="DeskPriceRoundTo"/> so prices read as clean
	/// figures. With base $1,000 and a 1.8× multiplier rounded to $1k,
	/// the seven buyable desks run: 1k, 2k, 3k, 6k, 10k, 19k, 34k. Office
	/// holds 8 total counting the free starter; filling it costs $75,000.
	/// Slow enough that early-game expansion isn't blocked, steep enough
	/// that filling the office is a real spend.
	public long PriceFor( ItemKind kind )
	{
		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null ) return 0;

		if ( kind == ItemKind.Desk )
		{
			// Starter desk is "free" w.r.t. the surcharge — so the first
			// desk the player buys lands on the catalogue base price, and
			// each subsequent desk multiplies that base by DeskPriceMultiplier.
			// Result is rounded to DeskPriceRoundTo so prices read as clean
			// figures ($2,000 instead of $1,800; $34,000 instead of $34,012).
			int ownedDesks     = OwnedCount( ItemKind.Desk );
			int extraPurchased = Math.Max( 0, ownedDesks - 1 );
			double raw         = entry.Price * Math.Pow( DeskPriceMultiplier, extraPurchased );
			long rounded       = (long)Math.Round( raw / DeskPriceRoundTo ) * DeskPriceRoundTo;
			return Math.Max( DeskPriceRoundTo, rounded );
		}

		if ( kind == ItemKind.SmokeBreak )
		{
			// Doubles per *prior* use: $100 → $200 → $400 → … capped at $1M
			// (≈ 14 uses to hit the cap). Buying a Smoke Break does NOT bump
			// the counter — only Using one does, so the player can stockpile
			// one at the current price without inflating the next sticker.
			double raw = SmokeBreakBasePrice * Math.Pow( 2, _smokeBreakUsesCount );
			if ( raw >= SmokeBreakMaxPrice ) return SmokeBreakMaxPrice;
			return (long)raw;
		}

		return entry.Price;
	}

	/// Each purchased-desk-past-the-starter multiplies the catalogue base
	/// price by this factor. Tune to flatten or steepen the curve.
	const double DeskPriceMultiplier = 1.8;

	/// Round desk prices to the nearest multiple of this so the player
	/// sees clean numbers instead of "$5,832" / "$18,896". Set to 1 to
	/// disable rounding.
	const long DeskPriceRoundTo = 1_000;

	/// Sum of <see cref="InventoryCatalogue.Entry.StatBoost"/> across every
	/// owned non-desk-bound, non-consumable furniture kind. Applies as a
	/// flat add to every employee's <c>EffectiveStat</c> — the studio-wide
	/// half of the workstation/decor boost system. Desk-bound items
	/// (chair / computer / monitor) are excluded here because their boost
	/// is per-employee at the assigned desk, computed inside EmployeeNPC.
	/// Recomputed on demand; cheap given the small catalogue (~30 entries).
	public int StudioWideStatBoost
	{
		get
		{
			int total = 0;
			foreach ( var entry in InventoryCatalogue.All )
			{
				if ( entry.StatBoost <= 0 )                       continue;
				if ( IsDeskBoundKind( entry.Kind ) )              continue;
				if ( entry.Category == ItemCategory.Consumable )  continue;
				if ( !OwnsAny( entry.Kind ) )                     continue;
				total += entry.StatBoost;
			}
			return total;
		}
	}

	/// True if at least one InventoryItem of <paramref name="kind"/> is owned
	/// (placed or stashed). Used by <see cref="InventoryCatalogue.IsUnlocked"/>
	/// to evaluate the <see cref="InventoryCatalogue.Entry.RequiredItems"/>
	/// chain — selling all copies of a prerequisite re-locks dependents.
	public bool OwnsAny( ItemKind kind )
	{
		foreach ( var item in _owned )
			if ( item.Kind == kind ) return true;
		return false;
	}

	/// How many more of <paramref name="kind"/> the player can purchase right
	/// now. Drives shop visibility (entries hide when 0) and gates
	/// <see cref="TryBuy"/> against over-buying. Rules:
	///   • Locked entries → 0.
	///   • Consumables → current shop stock (0–5).
	///   • Stash items (Slot=None, e.g. Server) → 0 if owned, else 1.
	///   • Desks → progression-capped (1 + GamesShipped) minus owned, also
	///     capped by free desk slots in the scene.
	///   • Other furniture → one-shot per kind. Bulk SKUs (Plants pack of
	///     5, LoungeChair pack of 2) are sized to match the scene slot
	///     count for that kind, so one purchase fills every slot. Selling
	///     the item back returns it to the shop (since OwnsAny then false).
	public int BuyableCount( ItemKind kind )
	{
		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null )                        return 0;
		if ( entry.Hidden )                          return 0;
		if ( !InventoryCatalogue.IsUnlocked( kind ) ) return 0;

		if ( entry.Category == ItemCategory.Consumable )
			return ConsumableStock( kind );

		if ( entry.Slot == SlotKind.None )
			return OwnsAny( kind ) ? 0 : 1;

		if ( kind == ItemKind.Desk )
		{
			// Prototype playtest mode: progression cap (1 + GamesShipped)
			// temporarily lifted so the whole office can be outfitted in
			// one session for testing chair / computer / monitor mounts
			// across all 8 desks. Re-enable the gate once balancing
			// starts — see the commented block below for the original
			// progression rule.
			int free = 0;
			foreach ( var s in _slots )
			{
				if ( s.Kind != entry.Slot ) continue;
				if ( !s.IsAvailable )       continue;
				free++;
			}
			return free;

			// Original progression-gated rule (re-enable for shipping):
			// int ownedDesks         = OwnedCount( ItemKind.Desk );
			// int progressionMax     = 1 + Achievements.GamesShipped;
			// int progressionAllowed = System.Math.Max( 0, progressionMax - ownedDesks );
			// return System.Math.Min( free, progressionAllowed );
		}

		// Desk-bound mount items (Chair / Computer / Monitor tiers) cap
		// at the number of mount slots in the scene of *this specific*
		// kind, NOT across the whole tier family. With 8 desks the
		// player can own up to 8 ChairStandards, 8 ChairPremiums, AND
		// 8 ChairElites — buying a higher tier displaces the lower
		// tier back to storage; the player can then sell or keep the
		// downgrades. Each tier hides from the shop when 8 of that
		// exact kind are owned.
		if ( IsDeskBoundKind( kind ) )
		{
			int mountCount = 0;
			foreach ( var s in _slots )
				if ( s.Kind == entry.Slot ) mountCount++;

			return System.Math.Max( 0, mountCount - OwnedCount( kind ) );
		}

		// Every other furniture kind is a one-shot purchase. Bulk SKUs
		// fill all matching slots in a single buy; non-bulk SKUs occupy
		// their single slot. Either way, owning any disables the shop
		// entry. Sell-back resets OwnsAny → kind reappears.
		return OwnsAny( kind ) ? 0 : 1;
	}

	/// Sell one inventory copy back for 50 % of the catalogue price. Only
	/// unplaced (storage) items are sellable — to sell a placed item, the
	/// player has to displace it first (e.g., by buying a higher tier of
	/// the same slot kind, or via a future explicit unplace UI). Returns
	/// false (and pushes a notification) if the item isn't owned, isn't
	/// unplaced, or no catalogue entry exists.
	public bool SellItem( InventoryItem item )
	{
		if ( item is null )                return false;
		if ( !_owned.Contains( item ) )    return false;

		var entry = InventoryCatalogue.Get( item.Kind );
		if ( entry is null ) return false;

		if ( item.IsPlaced )
		{
			Notifications.Push( "Can't sell",
				$"{entry.Name} is currently placed. Replace or unplace it first.",
				"warning" );
			return false;
		}

		// Per-unit refund: the catalogue Price is per-purchase (which can be
		// a bulk SKU), so divide by BulkSize to get the per-item value, then
		// halve. Floored at $1 so a junk item still pays back something.
		long perUnit  = entry.Price / System.Math.Max( 1, entry.BulkSize );
		long refund   = System.Math.Max( 1L, perUnit / 2 );

		_owned.Remove( item );
		GameManager.Instance?.AddMoney( refund );
		Notifications.Push( "Sold",
			$"{entry.Name} sold for ${refund:N0}.", "success" );
		return true;
	}

	/// Sell one unplaced copy of <paramref name="kind"/> from inventory.
	/// Convenience wrapper for UI bindings where the panel knows the kind
	/// but not the specific InventoryItem instance.
	public bool SellOneOf( ItemKind kind )
	{
		foreach ( var item in _owned )
		{
			if ( item.Kind != kind ) continue;
			if ( item.IsPlaced )     continue;
			return SellItem( item );
		}
		Notifications.Push( "Nothing to sell",
			$"No unplaced {InventoryCatalogue.Get( kind )?.Name ?? kind.ToString()} in storage.",
			"warning" );
		return false;
	}

	// ── Desk assignment (desk-bound items only) ──────────────────────────────
	// Chair / Computer / Monitor items don't auto-place — the player picks
	// which desk each copy goes on. The InventoryPanel "Assign" button calls
	// BeginAssign(item); DeskAssignPanel reads AssigningItem and shows a
	// list of desks (by employee name when occupied, or "Empty" for free
	// desks). The player clicks a desk → AssignToDesk runs.

	/// The InventoryItem the player is currently assigning to a desk, or
	/// null when no picker is open. Drives DeskAssignPanel visibility.
	public InventoryItem AssigningItem { get; private set; }

	public bool IsAssigning => AssigningItem != null;

	public void BeginAssign( InventoryItem item )
	{
		if ( item is null )           return;
		if ( !_owned.Contains( item ) ) return;
		if ( item.IsPlaced )           return;
		if ( !IsDeskBoundKind( item.Kind ) )
		{
			Notifications.Push( "Auto-placed",
				$"{InventoryCatalogue.Get( item.Kind )?.Name ?? item.Kind.ToString()} doesn't need a desk.",
				"info" );
			return;
		}
		AssigningItem = item;
		GameManager.RefreshPlayerLock();
	}

	public void CancelAssign()
	{
		AssigningItem = null;
		GameManager.RefreshPlayerLock();
	}

	/// Place the assigning item on the chair/computer/monitor mount inside
	/// the desk identified by <paramref name="deskSlotId"/>. Displaces any
	/// existing item at that mount (returning it to storage). Closes the
	/// picker on success.
	public bool AssignToDesk( Guid deskSlotId )
	{
		if ( !TryAssignItemToDesk( AssigningItem, deskSlotId, notify: true ) )
			return false;

		AssigningItem = null;
		GameManager.RefreshPlayerLock();
		return true;
	}

	/// Direct desk-assignment helper used by the Workstations hub. Takes an
	/// arbitrary unplaced <see cref="InventoryItem"/> (chair / computer /
	/// monitor) and binds it to the matching mount inside the desk
	/// identified by <paramref name="deskSlotId"/>. Displaces any existing
	/// item at that mount back to storage. Doesn't touch
	/// <see cref="AssigningItem"/> — the modal-state path is owned by
	/// <see cref="BeginAssign"/> / <see cref="AssignToDesk"/>.
	public bool TryAssignItemToDesk( InventoryItem item, Guid deskSlotId, bool notify = false )
	{
		if ( item is null )            return false;
		if ( !_owned.Contains( item ) ) return false;
		if ( item.IsPlaced )            return false;

		var entry = InventoryCatalogue.Get( item.Kind );
		if ( entry is null ) return false;

		PlacementSlot deskSlot = null;
		foreach ( var s in _slots )
			if ( s.Id == deskSlotId ) { deskSlot = s; break; }
		if ( deskSlot is null )            return false;
		if ( deskSlot.GameObject is null ) return false;

		PlacementSlot mount = FindMountInDesk( deskSlot, entry.Slot );
		if ( mount is null )
		{
			if ( notify )
				Notifications.Push( "No mount",
					$"That desk has no {entry.Slot.DisplayName()} mount in the scene.",
					"warning" );
			return false;
		}

		// Displace whatever's currently at that mount — returns to storage.
		// Clear both fields so the displaced item's IsPlaced flips back to
		// false and the Edit Desks picker offers it again as a candidate.
		var existing = FindItemBoundTo( mount.Id );
		if ( existing is not null )
		{
			existing.PlacedAtSlotId    = null;
			existing.SpawnedGameObject = null;
		}

		item.PlacedAtSlotId    = mount.Id;
		item.SpawnedGameObject = mount.GameObject;
		ApplySlotVisibility();

		// Tutorial step hook: assigning a chair (any tier) during the
		// AssignChair phase advances the tutorial to Complete. Caught here
		// so it covers all routes into TryAssignItemToDesk (Workstations
		// panel, drag-assign, etc.).
		if ( entry.Slot == SlotKind.Chair )
			TutorialManager.Instance?.NotifyChairAssigned();

		if ( notify )
		{
			// If a hire is sitting at this desk, surface the stat-boost
			// payoff explicitly so the player feels the value of the
			// upgrade. Empty desks fall back to the neutral "placed at"
			// message.
			EmployeeNPC occupant = null;
			var hr = HRManager.Instance;
			if ( hr is not null )
			{
				foreach ( var npc in hr.Staff )
					if ( npc.DeskSlotId == deskSlotId ) { occupant = npc; break; }
			}

			if ( occupant is not null && entry.StatBoost > 0 )
			{
				Notifications.Push( "Stats Boosted",
					$"{entry.Name} bought — {occupant.EmployeeName}'s stats got boosted.",
					"success" );
			}
			else
			{
				Notifications.Push( "Assigned",
					$"{entry.Name} placed at {LabelForDeskSlot( deskSlotId )}.",
					"success" );
			}
		}

		return true;
	}

	/// Find the InventoryItem currently bound to a specific mount slot
	/// inside <paramref name="deskSlotId"/>'s subtree, by mount kind.
	/// Returns null if the mount is empty or the desk has no mount of
	/// that kind. Drives the Workstations panel's "current chair / computer
	/// / monitor" display.
	public InventoryItem ItemAtDeskMount( Guid deskSlotId, SlotKind mountKind )
	{
		PlacementSlot deskSlot = null;
		foreach ( var s in _slots )
			if ( s.Id == deskSlotId ) { deskSlot = s; break; }
		if ( deskSlot is null )            return null;
		if ( deskSlot.GameObject is null ) return null;

		var mount = FindMountInDesk( deskSlot, mountKind );
		if ( mount is null ) return null;

		return FindItemBoundTo( mount.Id );
	}

	/// Walk the desk slot's GameObject subtree looking for a PlacementSlot
	/// whose Kind matches <paramref name="mountKind"/>. Used by AssignToDesk
	/// to translate a desk pick into the specific Chair/Computer/Monitor
	/// mount inside that desk's prefab.
	PlacementSlot FindMountInDesk( PlacementSlot deskSlot, SlotKind mountKind )
	{
		if ( deskSlot?.GameObject is null ) return null;
		var deskGo = deskSlot.GameObject;
		foreach ( var s in _slots )
		{
			if ( s.Kind != mountKind ) continue;
			if ( s.GameObject is null ) continue;
			if ( IsDescendantOf( s.GameObject, deskGo ) ) return s;
		}
		return null;
	}

	static bool IsDescendantOf( GameObject child, GameObject ancestor )
	{
		if ( child is null || ancestor is null ) return false;
		if ( child == ancestor ) return true;
		var go = child.Parent;
		while ( go is not null )
		{
			if ( go == ancestor ) return true;
			go = go.Parent;
		}
		return false;
	}

	/// Hand one consumable from the stash to a target hire. Decrements
	/// the owned count for that ItemKind and applies the consumable's
	/// effect — v1 is a flat +0.10 morale bump (capped at 1.0). Future
	/// passes can layer a real timed-buff system on top.
	public bool TryGiveConsumable( EmployeeNPC target, ItemKind kind )
	{
		if ( target is null )
		{
			Notifications.Push( "No target",
				"Pick an employee first.", "warning" );
			return false;
		}

		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null || entry.Category != ItemCategory.Consumable )
		{
			Notifications.Push( "Not a consumable",
				$"{kind} can't be given to an employee.", "warning" );
			return false;
		}

		InventoryItem found = null;
		foreach ( var item in _owned )
		{
			if ( item.Kind == kind && !item.IsPlaced ) { found = item; break; }
		}

		if ( found is null )
		{
			Notifications.Push( "None in stash",
				$"You don't own any {entry.Name}.", "warning" );
			return false;
		}

		_owned.Remove( found );

		// V1 effect: instant morale bump. Replace with a proper timed-buff
		// system once the consumable mechanic stabilizes.
		target.Morale = MathF.Min( 1f, target.Morale + 0.10f );

		Notifications.Push( "Given",
			$"{target.EmployeeName} took a {entry.Name}. Morale +10%.",
			"success" );
		return true;
	}

	/// One-shot purchase + immediate use of a Smoke Break. Skips the stash
	/// entirely — the player presses BUY in the shop, confirms the warning,
	/// and the active project gets fast-finished at 70 % output in a single
	/// transaction. Validates project state, money, and shop stock; pushes
	/// a warning toast on any failure path.
	public bool TryBuyAndUseSmokeBreak()
	{
		// Need a project in production — otherwise there's nothing to skip.
		var pm = GameProjectManager.Instance;
		if ( pm?.Current is null || pm.Current.Phase != GameProjectPhase.Production )
		{
			Notifications.Push( "Nothing to skip",
				"Smoke Break only works during a project's production phase.",
				"warning" );
			return false;
		}

		// Need shop stock — Smoke Break shares the consumable restock pool.
		if ( ConsumableStock( ItemKind.SmokeBreak ) <= 0 )
		{
			Notifications.Push( "Out of stock",
				"Wait for the shop to restock Smoke Breaks.", "warning" );
			return false;
		}

		// Need the cash. Price is dynamic (PriceFor handles the curve).
		long price = PriceFor( ItemKind.SmokeBreak );
		var gm = GameManager.Instance;
		if ( gm is null || !gm.TrySpend( price ) )
		{
			Notifications.Push( "Not enough money",
				$"Smoke Break costs ${price:N0}.", "warning" );
			return false;
		}

		// Decrement shop stock by one (matches the normal TryBuy path).
		_consumableStock[ItemKind.SmokeBreak] = Math.Max( 0,
			ConsumableStock( ItemKind.SmokeBreak ) - 1 );

		// Bump the use counter, finish the project, advance the calendar.
		_smokeBreakUsesCount++;
		string title = pm.Current?.Title ?? "Project";
		pm.CompleteWithPenalty( SmokeBreakOutputFactor );
		gm.AdvanceDays( SmokeBreakDaysSkipped );

		Notifications.Push( "Smoke Break taken",
			$"\"{title}\" wrapped at {(int)(SmokeBreakOutputFactor * 100)}% output ({SmokeBreakDaysSkipped} days skipped).",
			"warning", duration: 6f );
		return true;
	}

	/// Consume one Smoke Break from the stash, finishing the active project
	/// at <see cref="SmokeBreakOutputFactor"/> of its potential output.
	/// Returns false (and pushes a warning toast) when there's nothing to
	/// skip — no Smoke Break in stash, or no project in Production phase.
	/// Each successful call also bumps <see cref="SmokeBreakUsesCount"/>,
	/// which doubles the next purchase price up to the $1M cap.
	public bool TryUseSmokeBreak()
	{
		// Must have one in stash.
		InventoryItem found = null;
		foreach ( var item in _owned )
		{
			if ( item.Kind == ItemKind.SmokeBreak && !item.IsPlaced ) { found = item; break; }
		}
		if ( found is null )
		{
			Notifications.Push( "No Smoke Break",
				"Buy one from the Shop's Consumable tab first.", "warning" );
			return false;
		}

		// Must have a project to skip — Smoke Break is meaningless during
		// Setup (player can just adjust effort) or Finished (already done).
		var pm = GameProjectManager.Instance;
		if ( pm?.Current is null || pm.Current.Phase != GameProjectPhase.Production )
		{
			Notifications.Push( "Nothing to skip",
				"Smoke Break only finishes a project that's already in production.",
				"warning" );
			return false;
		}

		// Burn the consumable, fast-finish the project, bump the counter,
		// then advance the calendar by SmokeBreakDaysSkipped to reflect
		// that the player just hand-waved a chunk of dev time. Order:
		// finish project FIRST so AdvanceDays' OnDayStart side-effects
		// (training drips, salary anniversaries, gallery review reveals)
		// land on a state where the project is already in Finished phase.
		_owned.Remove( found );
		_smokeBreakUsesCount++;
		string title = pm.Current?.Title ?? "Project";
		pm.CompleteWithPenalty( SmokeBreakOutputFactor );
		GameManager.Instance?.AdvanceDays( SmokeBreakDaysSkipped );

		Notifications.Push( "Smoke Break taken",
			$"\"{title}\" wrapped at {(int)(SmokeBreakOutputFactor * 100)}% output ({SmokeBreakDaysSkipped} days skipped).",
			"warning", duration: 6f );
		return true;
	}

	// ── Queries ──────────────────────────────────────────────────────────────

	public int OwnedCount( ItemKind kind )
	{
		int n = 0;
		foreach ( var item in _owned )
			if ( item.Kind == kind ) n++;
		return n;
	}

	public int PlacedCount
	{
		get
		{
			int n = 0;
			foreach ( var item in _owned )
				if ( item.IsPlaced ) n++;
			return n;
		}
	}

	public int UnplacedCount => _owned.Count - PlacedCount;

	public int PlacedCountOf( ItemKind kind )
	{
		int n = 0;
		foreach ( var item in _owned )
			if ( item.Kind == kind && item.IsPlaced ) n++;
		return n;
	}

	/// All placed items of a given kind, paired with their slot. Used by
	/// HRManager to query placed Desks.
	public IEnumerable<(InventoryItem item, PlacementSlot slot)> PlacedOf( ItemKind kind )
	{
		foreach ( var item in _owned )
		{
			if ( !item.IsPlaced )      continue;
			if ( item.Kind != kind )   continue;
			var slot = SlotById( item.PlacedAtSlotId.Value );
			if ( slot is null )        continue;
			yield return (item, slot);
		}
	}

	public PlacementSlot SlotById( Guid id )
	{
		foreach ( var s in _slots )
			if ( s.Id == id ) return s;
		return null;
	}

	/// Friendly display label for a desk slot. Uses the slot's
	/// <see cref="PlacementSlot.Label"/> if set, otherwise a 1-based index
	/// among the scene's Desk slots ("Desk 1", "Desk 2", …). Returns
	/// "Remote" when slotId is null (the hire is desk-less).
	public string LabelForDeskSlot( Guid? slotId )
	{
		if ( slotId is null ) return "Remote";

		var slot = SlotById( slotId.Value );
		if ( slot is null ) return "—";

		if ( !string.IsNullOrEmpty( slot.Label ) ) return slot.Label;

		int idx = 0;
		foreach ( var s in _slots )
		{
			if ( s.Kind != SlotKind.Desk ) continue;
			idx++;
			if ( s == slot ) return $"Desk {idx}";
		}
		return "—";
	}

	// ── Debug / restart helpers ──────────────────────────────────────────────

	/// Wipe the inventory entirely. Used by the run-restart flow (Step 5
	/// payoff). Re-seeds the starter Desk on next OnAwake / explicit reseed.
	///
	/// Visuals are scene-authored, so we disable each previously-bound slot
	/// GameObject rather than destroying anything. The starter Desk's slot
	/// will re-enable on the next AutoPlaceAll().
	/// Renamed from Reset() so it doesn't shadow Component.Reset(), which
	/// the engine calls on its own schedule. Same pattern as Gallery.
	public void ResetProgress()
	{
		_smokeBreakUsesCount = 0;
		foreach ( var item in _owned )
		{
			if ( item.SpawnedGameObject is not null )
				item.SpawnedGameObject.Enabled = false;
		}
		_owned.Clear();

		// Re-seed the starter Desk and re-run the auto-place pass so the
		// reset state matches a fresh OnAwake → OnStart cycle.
		_owned.Add( new InventoryItem { Kind = ItemKind.Desk } );
		AutoPlaceAll();
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public InventorySave Save()
	{
		var dto = new InventorySave();
		foreach ( var item in _owned )
		{
			dto.Items.Add( new InventoryItemSave
			{
				Id             = item.Id,
				Kind           = item.Kind,
				PlacedAtSlotId = item.PlacedAtSlotId,
			} );
		}
		foreach ( var kv in _consumableStock )
			dto.ConsumableStock[kv.Key] = kv.Value;
		foreach ( var kv in _consumableRestockTimers )
			dto.ConsumableRestockTimers[kv.Key] = kv.Value;
		dto.SmokeBreakUsesCount = _smokeBreakUsesCount;
		return dto;
	}

	public void Load( InventorySave dto )
	{
		// Disable any slot we currently have bound — the load may bind a
		// different item to that slot, or leave it empty entirely.
		foreach ( var item in _owned )
		{
			if ( item.SpawnedGameObject is not null )
				item.SpawnedGameObject.Enabled = false;
		}
		_owned.Clear();

		if ( dto is null )
		{
			// Defensive fallback: empty save → re-seed the starter Desk so the
			// studio isn't left with no workstation.
			_owned.Add( new InventoryItem { Kind = ItemKind.Desk } );
			AutoPlaceAll();
			return;
		}

		// Rebuild every owned item from the DTO. Slot Ids are scene-stable —
		// re-link SpawnedGameObject by walking the live slot list and matching
		// Id. Auto-placed and desk-bound items rebind uniformly here, so we
		// don't need a second pass through AutoPlaceAll for desk-bound items
		// (which would skip them anyway).
		foreach ( var save in dto.Items )
		{
			var item = new InventoryItem
			{
				// InventoryItem.Id is `init` so we can't set it on existing —
				// new instance per save entry preserves the saved Guid.
				Id             = save.Id,
				Kind           = save.Kind,
				PlacedAtSlotId = save.PlacedAtSlotId,
			};

			if ( save.PlacedAtSlotId is { } slotId )
			{
				var slot = SlotById( slotId );
				if ( slot is not null )
					item.SpawnedGameObject = slot.GameObject;
			}

			_owned.Add( item );
		}

		// Migration: any kind that's now FillsAllMatchingSlots collapses to
		// exactly 1 InventoryItem (unplaced). Saves written under the old
		// bulk-pack-of-N model carry N items per fills-all kind; without
		// this fixup they'd persist as ghost stash items in the inventory
		// filter and skew OwnedCount-driven gates. Running on every load
		// is idempotent — already-collapsed kinds are a no-op.
		var fillsAllKinds = new HashSet<ItemKind>();
		foreach ( var e in InventoryCatalogue.All )
			if ( e.FillsAllMatchingSlots ) fillsAllKinds.Add( e.Kind );

		foreach ( var kind in fillsAllKinds )
		{
			int count = 0;
			foreach ( var item in _owned )
				if ( item.Kind == kind ) count++;
			if ( count <= 1 ) continue;

			// Drop every owned copy of this kind, then re-add exactly one
			// unplaced. PlacedAtSlotIds on the old copies are discarded —
			// fills-all visuals run off OwnsAny in ApplySlotVisibility, no
			// per-slot binding needed.
			_owned.RemoveAll( i => i.Kind == kind );
			_owned.Add( new InventoryItem { Kind = kind } );
			Log.Info( $"[Inv] Migrated {count} {kind} → 1 fills-all item." );
		}

		ApplySlotVisibility();

		// Restore consumable shop stock + restock timers. Falls back to
		// the OnAwake-seeded defaults if the save predates the stock
		// system (older v1 saves don't carry these dicts).
		if ( dto.ConsumableStock is { Count: > 0 } )
		{
			foreach ( var kv in dto.ConsumableStock )
				_consumableStock[kv.Key] = System.Math.Clamp( kv.Value, 0, MaxConsumableStock );
		}
		if ( dto.ConsumableRestockTimers is { Count: > 0 } )
		{
			foreach ( var kv in dto.ConsumableRestockTimers )
				_consumableRestockTimers[kv.Key] = kv.Value;
		}
		_smokeBreakUsesCount = Math.Max( 0, dto.SmokeBreakUsesCount );
	}
}
