/// <summary>
/// Fullscreen Save / Load modal — extracted from the GameMenu's inline
/// content area as part of ADR-0003 Phase 1. Owns open/close state for
/// <c>SavePanel</c>; the panel itself reads slot state directly from
/// <see cref="GameSaveManager"/>.
///
/// Naming: deliberately distinct from the <c>Code/Save/</c> folder
/// (which holds the save-system internals — DTOs, manager, migrator)
/// so this file's responsibility — being the modal-state singleton —
/// is unambiguous at first glance.
/// </summary>
public sealed class SaveMenu : Component
{
	public static SaveMenu Instance { get; private set; }

	public bool IsOpen { get; private set; }

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
		if ( open ) Modals.CloseAllExcept( this );
		GameManager.RefreshPlayerLock();
	}
}
