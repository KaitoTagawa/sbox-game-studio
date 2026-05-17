/// <summary>
/// Fullscreen HR modal — Hire / Fire / Stats / Job Posting. Opens from
/// the "Human Resource" tile in the GameMenu (see
/// <see cref="GameMenu.Activate"/>). Mirrors the Shop / Inventory chrome.
/// </summary>
public sealed class HR : Component
{
	public static HR Instance { get; private set; }

	public bool IsOpen { get; private set; }

	/// Sub-view inside the HR modal. Stored on the singleton so the
	/// selection survives close/reopen, matching the Shop tab pattern.
	public enum Tab { Hire, Fire, Stats, Posting }
	public Tab ActiveTab { get; set; } = Tab.Hire;

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
