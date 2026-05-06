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

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
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
		if ( open ) GameMenu.Instance?.SetOpen( false );
		GameManager.RefreshPlayerLock();
	}
}
