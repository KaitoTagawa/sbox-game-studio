/// <summary>
/// Centralises modal mutual exclusion. Every fullscreen modal calls
/// <see cref="CloseAllExcept"/> in its <c>SetOpen(true)</c> path, passing
/// itself as the exception. Adding a new modal in future only requires
/// updating this list — no change needed to existing modals' close logic.
///
/// History (2026-05-10): pre-refactor, every modal maintained its own
/// bespoke list of "modals to close on open." That list drifted between
/// modals (Shop only closed GameMenu; Letterbox closed everyone; HR closed
/// three; etc.), leaving holes where two modals could end up open
/// simultaneously after a hot-reload or unusual interaction. This helper
/// consolidates the lot.
///
/// <see cref="GameMenu"/> is intentionally excluded from the asymmetric
/// pattern — it remains the launcher (others close it, not vice versa)
/// because every tile click already routes <c>GameMenu.SetOpen(false)</c>
/// before the target modal opens. Keeping its own <c>SetOpen</c> sparse
/// avoids redundant churn on tab-press.
/// </summary>
public static class Modals
{
	/// Close every fullscreen-modal singleton except <paramref name="except"/>
	/// (pass <c>null</c> to close all of them). <see cref="Letterbox"/> has
	/// two distinct open states (inbox + quests) — both close together when
	/// Letterbox isn't the exception, since they're sibling views over the
	/// same data. When Letterbox IS the exception, the caller
	/// (<c>Letterbox.SetOpen</c> / <c>SetQuestOpen</c>) handles toggling its
	/// sibling state itself.
	public static void CloseAllExcept( object except = null )
	{
		var gm = GameMenu.Instance;
		if ( gm is not null && !ReferenceEquals( gm, except ) ) gm.SetOpen( false );

		var shop = Shop.Instance;
		if ( shop is not null && !ReferenceEquals( shop, except ) ) shop.SetOpen( false );

		var hr = HR.Instance;
		if ( hr is not null && !ReferenceEquals( hr, except ) ) hr.SetOpen( false );

		var settings = Settings.Instance;
		if ( settings is not null && !ReferenceEquals( settings, except ) ) settings.SetOpen( false );

		var leaderboards = Leaderboards.Instance;
		if ( leaderboards is not null && !ReferenceEquals( leaderboards, except ) ) leaderboards.SetOpen( false );

		var save = SaveMenu.Instance;
		if ( save is not null && !ReferenceEquals( save, except ) ) save.SetOpen( false );

		var gallery = Gallery.Instance;
		if ( gallery is not null && !ReferenceEquals( gallery, except ) ) gallery.SetOpen( false );

		var inv = InventoryManager.Instance;
		if ( inv is not null && !ReferenceEquals( inv, except ) ) inv.SetOpen( false );

		var training = TrainingManager.Instance;
		if ( training is not null && !ReferenceEquals( training, except ) ) training.SetOpen( false );

		var workstations = Workstations.Instance;
		if ( workstations is not null && !ReferenceEquals( workstations, except ) ) workstations.SetOpen( false );

		var pm = GameProjectManager.Instance;
		if ( pm is not null && !ReferenceEquals( pm, except ) ) pm.SetOpen( false );

		var letterbox = Letterbox.Instance;
		if ( letterbox is not null && !ReferenceEquals( letterbox, except ) )
		{
			letterbox.SetOpen( false );
			letterbox.SetQuestOpen( false );
		}
	}
}
