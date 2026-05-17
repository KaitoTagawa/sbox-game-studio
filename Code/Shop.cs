/// <summary>
/// The Shop modal — a fullscreen popup where the player buys office furniture.
///
/// This component is now just the **modal state owner**: it tracks IsOpen,
/// owns the Shop input toggle, and integrates with the other modal singletons
/// (GameMenu, Settings, Gallery, GameProjectManager) for mutex behaviour.
///
/// All actual purchasable-item data and buy logic live in
/// <see cref="InventoryCatalogue"/> + <see cref="InventoryManager"/>. The
/// matching Razor panel (<c>ShopPanel.razor</c>) reads from those systems
/// directly — no buy logic in the shop component.
///
/// Pre-Inventory-system, Shop.cs held a hardcoded list of <c>ShopItem</c>
/// records and a <c>TryPurchase</c> method that just deducted money. Both
/// have moved: the catalogue replaces the list, InventoryManager.TryBuy
/// replaces the purchase path.
/// </summary>
public sealed class Shop : Component
{
	public static Shop Instance { get; private set; }

	public bool IsOpen { get; private set; }

	/// Tag for the sticky "consumables unlocked at 3 games" toast.
	/// ShopPanel clears it the moment the player clicks the Consumable tab.
	public const string ConsumablesUnlockedToastTag = "consumables-unlocked";

	protected override void OnAwake()
	{
		Instance = this;
		Gallery.OnGameShipped += OnGameShipped;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		Gallery.OnGameShipped -= OnGameShipped;
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( "Shop" ) )
			Toggle();
	}

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open ) Modals.CloseAllExcept( this );
		GameManager.RefreshPlayerLock();
	}

	/// Fired by Gallery the moment a project publishes. The 3rd ship is the
	/// "all consumables unlocked" milestone (matches the GamesShipped >= 3
	/// gate in InventoryCatalogue.IsUnlocked) — push a sticky toast that
	/// only goes away when the player visits the Consumable tab in the
	/// Shop. Subsequent ships don't re-arm the toast: ship #3 is a one-time
	/// reveal, not a recurring nag.
	void OnGameShipped()
	{
		if ( Achievements.GamesShipped != 3 ) return;
		if ( Notifications.HasTag( ConsumablesUnlockedToastTag ) ) return;

		Notifications.Push(
			"Consumables unlocked!",
			"Open the Shop and check the Consumable tab — three new items are available.",
			"success",
			duration: 0f,                            // sticky — see ShopPanel.SetTab
			tag: ConsumablesUnlockedToastTag );
	}
}
