/// <summary>
/// Modal singleton that owns the Workstations management hub.
///
/// The hub is a list of every placed Desk in the office, with inline
/// pickers for chair / computer / monitor on each one. Replaces the
/// (formerly unimplemented) top-level Stats menu — desk equipment was
/// previously edited only via the Inventory panel's per-item Assign
/// flow, which made it hard to compare desks side-by-side.
///
/// Modal-state mirror of <see cref="Shop"/> / <see cref="Settings"/> /
/// <see cref="Gallery"/>; opening closes the others.
/// </summary>
public sealed class Workstations : Component
{
	public static Workstations Instance { get; private set; }

	public bool IsOpen { get; private set; }

	/// While non-null, the panel renders an inline item picker for this
	/// (desk, mount-kind) pair. Set by <see cref="BeginPick"/> when the
	/// player clicks a desk's Chair / Computer / Monitor button; cleared
	/// by <see cref="CancelPick"/> or after a successful assignment.
	public System.Guid? PickingDeskSlotId { get; private set; }
	public SlotKind     PickingMountKind  { get; private set; }
	public bool         IsPicking         => PickingDeskSlotId.HasValue;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			Modals.CloseAllExcept( this );
		}
		else
		{
			// Close any open inline picker so the next time the panel
			// opens it lands on the desk list, not a half-finished pick.
			CancelPick();
		}
		GameManager.RefreshPlayerLock();
	}

	public void BeginPick( System.Guid deskSlotId, SlotKind mountKind )
	{
		if ( mountKind != SlotKind.Chair
			&& mountKind != SlotKind.Computer
			&& mountKind != SlotKind.Monitor )
			return;

		PickingDeskSlotId = deskSlotId;
		PickingMountKind  = mountKind;
	}

	public void CancelPick()
	{
		PickingDeskSlotId = null;
	}

	/// Assign the chosen <paramref name="item"/> to the currently-picking
	/// desk + mount. Returns false if no pick is open or the assignment
	/// fails (e.g., desk has no mount of that kind, item not in storage).
	public bool ConfirmPick( InventoryItem item )
	{
		if ( !PickingDeskSlotId.HasValue )                    return false;
		if ( item is null || item.IsPlaced )                  return false;
		var inv = InventoryManager.Instance;
		if ( inv is null )                                    return false;

		var entry = InventoryCatalogue.Get( item.Kind );
		if ( entry is null )                                  return false;
		if ( entry.Slot != PickingMountKind )                 return false;

		bool ok = inv.TryAssignItemToDesk( item, PickingDeskSlotId.Value, notify: true );
		if ( ok ) CancelPick();
		return ok;
	}
}
