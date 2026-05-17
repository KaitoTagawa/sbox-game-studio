using System;

/// <summary>
/// Surfaces gentle gameplay tips as info-toast notifications during quiet
/// stretches of play. Two-stage gate per tip:
///
///   1. <see cref="TipCooldownSeconds"/> hard cooldown after the previous
///      tip fired. Nothing else is checked during this window.
///   2. After cooldown, watch for <see cref="QuietRequiredSeconds"/> of
///      continuous quiet (no other notification visible, no fullscreen
///      modal open, no tutorial active). Any disruption restarts the
///      watch from zero.
///
/// Minimum cadence between tips: cooldown + quiet = 2:00 + 0:20 = 2:20.
///
/// Static class with <see cref="Tick"/> called from
/// <see cref="GameManager.OnUpdate"/>, mirroring the Notifications.Tick
/// pattern. <see cref="Reset"/> clears rotation + timer state and is
/// called from <see cref="GameManager.OnAwake"/> so a new run or hot-reload
/// starts clean.
/// </summary>
public static class Tips
{
	/// Real-time seconds of hard cooldown after a tip fires. Nothing
	/// is checked during this window — the quiet-watch only starts after
	/// the cooldown elapses.
	const float TipCooldownSeconds = 120f;

	/// Real-time seconds of continuous quiet required after the cooldown
	/// elapses before the next tip can fire.
	const float QuietRequiredSeconds = 20f;

	/// Visible duration of each tip toast.
	const float TipDurationSeconds = 10f;

	// Sentinel -1 means "not yet initialised / not currently watching".
	// _lastTipTime: when the previous tip fired (cooldown anchor).
	// _quietWatchStart: when the post-cooldown 20s quiet window began.
	static float _lastTipTime     = -1f;
	static float _quietWatchStart = -1f;
	static int   _lastIndex       = -1;

	/// Called once per frame from <see cref="GameManager.OnUpdate"/>.
	public static void Tick()
	{
		// Lazy init the cooldown anchor so static field default 0f can't
		// make 120s elapse instantly on first call.
		if ( _lastTipTime < 0f ) _lastTipTime = RealTime.Now;

		// Skip during the first-launch tutorial — tips would compete for
		// attention with guided objectives.
		if ( TutorialManager.Instance is { IsBlockingTime: true } )
		{
			_quietWatchStart = -1f;
			return;
		}

		// Skip while any fullscreen modal is open — don't push "Open HR
		// to review traits" while the player's already in HR.
		if ( AnyMenuOpen() )
		{
			_quietWatchStart = -1f;
			return;
		}

		// Stage 1 — cooldown gate. Nothing else happens until 2 min have
		// elapsed since the last tip.
		if ( RealTime.Now - _lastTipTime < TipCooldownSeconds )
		{
			_quietWatchStart = -1f;
			return;
		}

		// Stage 2 — quiet watch. Any visible notification restarts the
		// 20s clock. The watch only begins AFTER the cooldown ends, so
		// the minimum spacing is cooldown + quiet (2:20), even if the
		// screen was already empty during the cooldown.
		if ( Notifications.Active.Count > 0 )
		{
			_quietWatchStart = -1f;
			return;
		}

		if ( _quietWatchStart < 0f )
		{
			_quietWatchStart = RealTime.Now;
			return;
		}

		if ( RealTime.Now - _quietWatchStart < QuietRequiredSeconds ) return;

		// Both gates passed — fire and reset both timers.
		PushNextTip();
		_lastTipTime     = RealTime.Now;
		_quietWatchStart = -1f;
	}

	/// Clear rotation + timer state. Called from
	/// <see cref="GameManager.OnAwake"/> so each fresh run starts clean.
	public static void Reset()
	{
		_lastTipTime     = -1f;
		_quietWatchStart = -1f;
		_lastIndex       = -1;
	}

	static bool AnyMenuOpen() =>
		(GameMenu.Instance?.IsOpen           ?? false) ||
		(Shop.Instance?.IsOpen               ?? false) ||
		(Settings.Instance?.IsOpen           ?? false) ||
		(HR.Instance?.IsOpen                 ?? false) ||
		(Letterbox.Instance?.IsOpen          ?? false) ||
		(Letterbox.Instance?.IsQuestOpen     ?? false) ||
		(Leaderboards.Instance?.IsOpen       ?? false) ||
		(SaveMenu.Instance?.IsOpen           ?? false) ||
		(GameProjectManager.Instance?.IsOpen ?? false);

	static void PushNextTip()
	{
		if ( Pool.Length == 0 ) return;

		var rng = new Random();
		int idx;
		if ( Pool.Length == 1 )
		{
			idx = 0;
		}
		else
		{
			idx = rng.Next( Pool.Length );
			// Linear bump on repeat so the same tip never fires twice
			// in a row. Cheap, no rejection-sampling loop.
			if ( idx == _lastIndex ) idx = (idx + 1) % Pool.Length;
		}

		_lastIndex = idx;
		var (title, body) = Pool[idx];
		Notifications.Push( title, body, "info", duration: TipDurationSeconds );
	}

	// ── Tip pool ─────────────────────────────────────────────────────────
	// Edit freely — no scene data, no save data, just text. Aim for tips
	// that surface mechanics players easily forget or never discover.
	// Avoid duplicating tutorial coverage.

	static readonly (string Title, string Body)[] Pool =
	{
		( "Train your team",
		  "A few rounds in the Training panel lift stats faster than months on the job." ),

		( "Know your hires",
		  "Each hire has unique traits. Open HR to see what your team brings beyond raw stats." ),

		( "Save your boosts",
		  "Mood consumables are one-shot. Save them for the big project pushes." ),

		( "Climb the boards",
		  "Leaderboards track your best games across every run. Push for the top three." ),

		( "Save freely",
		  "Three manual save slots plus an autosave — experiment without fear of losing progress." ),

		( "Letters inspire",
		  "Reading a fan letter quietly lifts studio spirits for two in-game weeks." ),

		( "Quiet wins",
		  "Achievements unlock in the background as you play. The Gallery quietly tracks every one." ),

		( "Inspiration spreads",
		  "A motivated studio produces better work even on average days. Keep the letters flowing." ),

		( "Cure a grump with a gift",
		  "A grumpy employee can be turned around with the right gift. Open the chat menu while looking at them." ),
	};
}
