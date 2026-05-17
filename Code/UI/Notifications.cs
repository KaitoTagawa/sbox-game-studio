/// <summary>
/// Tiny in-game toast system. Any system can call Notifications.Push(...) and
/// the Hud renders the active list as floating cards in the top-right.
/// Toasts auto-expire after their duration.
///
/// While ANY full-screen menu is open (GameMenu, Shop, Settings, or the
/// Create-Game modal) new notifications are held in a pending queue and
/// only released to the visible stack once every menu closes — so the
/// player never has toasts firing behind a UI they can't see them through.
///
/// GameManager calls Notifications.Tick() each frame so we don't need our
/// own Component for it.
/// </summary>
public static class Notifications
{
	public sealed class Toast
	{
		public string Title    { get; init; } = "";
		public string Body     { get; init; } = "";
		public string Kind     { get; init; } = "info";   // "info" | "success" | "warning" | "danger"
		public string Tag      { get; init; } = "";       // optional grouping key for RemoveByTag
		public float  ShownAt  { get; init; }
		public float  Duration { get; init; }             // <= 0 → sticky (never auto-expires)

		public float Age       => RealTime.Now - ShownAt;
		public bool  IsExpired => Duration > 0f && Age >= Duration;
	}

	// Visible toasts — what the HUD renders.
	static readonly List<Toast> _toasts = new();

	// Pending queue — filled while a menu is open, drained when none are.
	// We hold the raw fields rather than Toast instances because Toast.ShownAt
	// is init-only; we want a fresh ShownAt at the moment the toast actually
	// becomes visible, not when it was originally pushed.
	readonly record struct Pending( string Title, string Body, string Kind, float Duration, string Tag );
	static readonly Queue<Pending> _pending = new();

	public static IReadOnlyList<Toast> Active => _toasts;

	/// True if any blocking modal is currently open.
	static bool AnyMenuOpen() =>
		(GameMenu.Instance?.IsOpen           ?? false) ||
		(Shop.Instance?.IsOpen               ?? false) ||
		(Settings.Instance?.IsOpen           ?? false) ||
		(GameProjectManager.Instance?.IsOpen ?? false);

	/// Add a new toast. Default lifetime is 5 seconds; pass duration ≤ 0 for
	/// a sticky toast that never auto-expires (caller is responsible for
	/// dismissing or replacing it via <see cref="RemoveByTag"/>). Toasts
	/// always render immediately — the notification overlay sits above
	/// open modals so the player still sees them as a low-priority UI
	/// signal (no click capture; modal still owns input).
	public static void Push( string title, string body, string kind = "info", float duration = 5f, string tag = "" )
	{
		Show( title, body, kind, duration, tag );
	}

	/// True if any visible OR queued toast carries the given tag. Used by
	/// systems that own a sticky toast (e.g. tutorial objective) to detect
	/// when it's been cleared and needs to be re-pushed.
	public static bool HasTag( string tag )
	{
		if ( string.IsNullOrEmpty( tag ) ) return false;
		foreach ( var t in _toasts )
			if ( t.Tag == tag ) return true;
		foreach ( var p in _pending )
			if ( p.Tag == tag ) return true;
		return false;
	}

	/// Drop every visible OR queued toast whose tag matches. Used to
	/// replace a sticky toast (e.g. tutorial objective) when the underlying
	/// state advances.
	public static void RemoveByTag( string tag )
	{
		if ( string.IsNullOrEmpty( tag ) ) return;
		_toasts.RemoveAll( t => t.Tag == tag );
		if ( _pending.Count > 0 )
		{
			var keep = new Queue<Pending>();
			while ( _pending.Count > 0 )
			{
				var p = _pending.Dequeue();
				if ( p.Tag != tag ) keep.Enqueue( p );
			}
			while ( keep.Count > 0 ) _pending.Enqueue( keep.Dequeue() );
		}
	}

	/// Remove expired toasts and drain any queued ones (legacy from when
	/// toasts deferred behind menus — now Push shows immediately, but the
	/// drain stays in case something queues via Pending directly).
	/// Call once per frame.
	public static void Tick()
	{
		_toasts.RemoveAll( t => t.IsExpired );

		if ( _pending.Count == 0 )
			return;

		// Drain everything that piled up while the menus were open.
		while ( _pending.Count > 0 )
		{
			var p = _pending.Dequeue();
			Show( p.Title, p.Body, p.Kind, p.Duration, p.Tag );
		}
	}

	/// Manually dismiss a toast (e.g. clicked).
	public static void Dismiss( Toast t )
	{
		_toasts.Remove( t );
	}

	/// Clear everything (e.g. on game reset).
	public static void Clear()
	{
		_toasts.Clear();
		_pending.Clear();
	}

	// ── Internal ─────────────────────────────────────────────────────────────

	static void Show( string title, string body, string kind, float duration, string tag = "" )
	{
		_toasts.Add( new Toast
		{
			Title    = title,
			Body     = body,
			Kind     = kind,
			Tag      = tag,
			ShownAt  = RealTime.Now,
			Duration = duration,
		} );

		// Audio cue — warning toasts get the red-notification chime.
		// "danger" kept as a future-proof alias even though the codebase
		// only uses "warning" today.
		if ( kind == "warning" || kind == "danger" )
			SFX.PlayWarning();
	}
}
