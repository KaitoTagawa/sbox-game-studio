public sealed class GameManager : Component
{
	public static GameManager Instance { get; private set; }

	/// Editor-tunable starting balance. Applied to <see cref="Money"/> on awake.
	[Property, Category( "Economy" ), Description( "Money the player starts the game with." )]
	public long StartingMoney { get; set; } = 1_000;

	[Sync] public long Money { get; set; }

	// ── Game speed ───────────────────────────────────────────────────────────
	// All in-game time progresses at this multiplier. 1× is the design baseline
	// (1 real minute = 1 in-game day). Higher tiers unlock through achievements
	// — see GameSpeed.cs / the Settings menu.

	[Property, Sync] public float         TimeMultiplier { get; set; } = 1f;
	[Property, Sync] public GameSpeedTier ActiveSpeed    { get; set; } = GameSpeedTier.Normal;

	// ── Calendar ─────────────────────────────────────────────────────────────
	// 10 real seconds = 1 in-game day
	// 30 in-game days = 1 in-game month  (30 × 10 s = 300 s = 5 real minutes)
	// 12 months       = 1 in-game year   (≈ 1 real hour at 1× game speed)
	// Calibrated 2026-04-29 against the fixed 120 s ProjectDuration: a default
	// project spans 12 in-game days, salary month lands every 5 real minutes.

	public int Day   { get; private set; } = 1;
	public int Month { get; private set; } = 1;
	public int Year  { get; private set; } = 2026;

	/// Short month name for display ("Jan", "Feb", …).
	public string MonthShort => MonthNames[Month - 1];

	/// Fires at the very start of each new month (Day just became 1).
	/// HRManager and any other system subscribe here for month-boundary logic.
	public static event Action OnMonthStart;

	/// Fires every time the calendar advances by one day (Day++). TrainingManager
	/// subscribes here to tick research drips and check off-site session
	/// completion. Cheap to subscribe to — only fires once per in-game day.
	public static event Action OnDayStart;

	static readonly string[] MonthNames =
	{
		"Jan", "Feb", "Mar", "Apr", "May", "Jun",
		"Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
	};

	/// 10 real seconds == 1 in-game day. Public so the dev-game UI can
	/// convert real-time durations (project ETAs) into in-game days.
	public const float SecondsPerDay = 10f;
	const int          DaysPerMonth  = 30;
	const int          MonthsPerYear = 12;

	float _dayAccum;

	// ── Energy ───────────────────────────────────────────────────────────────

	[Property] public int   Energy              { get; set; } = 20;

	/// Passive energy regen per real second at 1× game speed. Default is
	/// 0 — energy is meant to accrue from active development, not passive
	/// idling. The project-driven bonus (see <see cref="ProjectEnergyBonusPerSecond"/>)
	/// is the primary refill source. Keep this knob exposed in case
	/// playtesting reveals players are gated on energy when they're
	/// between projects.
	[Property] public float EnergyRegenPerSecond { get; set; } = 0f;

	/// Per-(1000-Focus-equivalent) per-second energy bonus while a project
	/// is in production. Scales linearly with the project's
	/// <c>TotalTeamFocus</c> — both per-worker Focus quality and headcount
	/// raise the rate, so a 5-person studio refills meaningfully faster
	/// than a solo founder. On top of <see cref="EnergyRegenPerSecond"/>.
	///
	/// Const rather than [Property] because s&amp;box doesn't backfill new
	/// [Property] fields into existing scenes — would default to 0 in the
	/// inspector and silently disable the bonus. Bump the value here to
	/// retune.
	///
	/// Tuning history: 0.5, 0.15 (old AvgTeamFocus), 0.3, 0.9, 0.45,
	/// 0.15, 0.075, 0.1125. Now back to **0.15** with TotalTeamFocus —
	/// solid mid-ground: 2 mid workers ≈ 9 energy / 60s project; 5-person
	/// peak team ≈ 45.
	const float ProjectEnergyBonusPerSecond = 0.15f;

	float _energyAccum;

	protected override void OnAwake()
	{
		Instance = this;
		Money    = StartingMoney;
		Tips.Reset();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		ApplyPlayerLock();
		TickCalendar();
		TickEnergyRegen();
		// Per-frame ratchet for the "Peak Wallet" leaderboard. One long
		// compare; assigns only when the wallet hits a new high. See
		// ADR-0002 + Achievements.PeakBalance.
		Achievements.RecordBalance( Money );
		Notifications.Tick();
		Tips.Tick();
	}

	// ── Time pause ────────────────────────────────────────────────────────────

	/// True when in-game calendar / project / energy time should freeze.
	/// Out-of-game configuration UIs (Create Game / Settings / Save)
	/// trigger this. Real-time UI (notifications, tips) keeps ticking —
	/// only in-game systems consult this gate.
	///
	/// Other in-game modals (HR / Shop / Inventory / etc.) deliberately do
	/// NOT pause time — those are decisions made *during* play, where the
	/// passing day is part of the cost.
	///
	/// **Engine pause not handled.** The s&amp;box Resume/Leave menu (Escape
	/// key) is treated as a player exiting the session — not a true pause.
	/// A few seconds of time advancing while they're on their way out
	/// doesn't matter. If a proper engine-pause signal becomes available
	/// in a future s&amp;box build, add it here.
	public bool IsTimePaused
	{
		get
		{
			// Create Game modal: only pauses during the SETUP phases
			// (configuring genres, team, time allocation). Once the
			// project flips to Production, the same modal becomes a
			// live status view of the in-flight game — time MUST keep
			// advancing because the project is actively producing.
			var pm = GameProjectManager.Instance;
			if ( pm is { IsOpen: true } && pm.Current?.Phase != GameProjectPhase.Production )
				return true;

			if ( Settings.Instance is { IsOpen: true } ) return true;
			if ( SaveMenu.Instance is { IsOpen: true } ) return true;
			return false;
		}
	}

	// ── Calendar tick ─────────────────────────────────────────────────────────

	void TickCalendar()
	{
		// Tutorial freeze: while the first-launch tutorial is active, the
		// calendar (and every OnDayStart / OnMonthStart subscriber that
		// depends on it — applicant timer, project tick, salary anniversary,
		// energy regen) is paused so the player can't accidentally let the
		// game advance before they make their first hire.
		if ( TutorialManager.Instance is { IsBlockingTime: true } ) return;

		// Modal / Escape-menu pause: out-of-game UIs and the engine pause
		// freeze the calendar so no day rollovers happen while configuring.
		if ( IsTimePaused ) return;

		_dayAccum += Time.Delta * TimeMultiplier;
		if ( _dayAccum < SecondsPerDay ) return;

		_dayAccum -= SecondsPerDay;
		AdvanceOneDay();
	}

	/// Single-day advance: bumps Day, handles month/year rollover, fires
	/// OnMonthStart (if rolled) then OnDayStart. Pulled out of TickCalendar
	/// so consumables / cheats / event scripts can advance the calendar
	/// without re-implementing the rollover semantics.
	void AdvanceOneDay()
	{
		Day++;

		// Roll month / year first so OnDayStart subscribers see a valid Day
		// in [1, DaysPerMonth] — never the transient 31. This matters for
		// per-employee salary anniversaries: HireDay = 1 employees would
		// otherwise be skipped, since OnDayStart used to fire with Day = 31
		// just before the rollover snapped Day back to 1.
		if ( Day > DaysPerMonth )
		{
			Day = 1;
			Month++;

			if ( Month > MonthsPerYear )
			{
				Month = 1;
				Year++;
			}

			// Month-aligned subscribers (HRManager posting fee / intern ticks)
			// fire before the day tick so a "day 1 of new month" sees the
			// month bookkeeping already done.
			OnMonthStart?.Invoke();
		}

		OnDayStart?.Invoke();
	}

	/// Advance the in-game calendar by N days, firing OnDayStart / OnMonthStart
	/// for each step exactly as the natural <see cref="TickCalendar"/> path
	/// would. Used by the Smoke Break consumable to reflect that the player
	/// "skipped" the remaining dev time on the active project — salaries
	/// land on the right anniversary days, research drips, gallery reveal
	/// timers all stay in sync. No-op when N &lt;= 0.
	public void AdvanceDays( int days )
	{
		if ( days <= 0 ) return;
		for ( int i = 0; i < days; i++ )
			AdvanceOneDay();
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────────
	// Calendar + economy are flat scalar state, captured/applied directly.
	// Day/Month/Year have private setters; the GameSaveManager calls these
	// from the same assembly and must go through them (no reflection).

	public CalendarSave SaveCalendar() => new()
	{
		Day            = Day,
		Month          = Month,
		Year           = Year,
		TimeMultiplier = TimeMultiplier,
		ActiveSpeed    = ActiveSpeed,
	};

	public void LoadCalendar( CalendarSave dto )
	{
		if ( dto is null ) return;
		Day            = dto.Day;
		Month          = dto.Month;
		Year           = dto.Year;
		TimeMultiplier = dto.TimeMultiplier;
		ActiveSpeed    = dto.ActiveSpeed;
		_dayAccum      = 0f;
	}

	public EconomySave SaveEconomy() => new()
	{
		Money  = Money,
		Energy = Energy,
	};

	public void LoadEconomy( EconomySave dto )
	{
		if ( dto is null ) return;
		Money        = dto.Money;
		Energy       = dto.Energy;
		_energyAccum = 0f;
	}

	/// Reset calendar + economy to a brand-new-studio baseline. Called by
	/// <c>GameSaveManager.RestartRun</c> when the player chooses
	/// "Start New Game" from the Save menu. Keeps the scene-authored
	/// <see cref="StartingMoney"/> as the starting wallet (so debug-overrides
	/// in the scene still apply for prototype playtest sessions).
	public void RestartRun()
	{
		Day            = 1;
		Month          = 1;
		Year           = 2026;
		_dayAccum      = 0f;
		Money          = StartingMoney;
		Energy         = 20;
		_energyAccum   = 0f;
		TimeMultiplier = 1;
	}

	// ── Money ─────────────────────────────────────────────────────────────────

	public void AddMoney( long amount )
	{
		// Plain wallet credit. Does NOT count toward Earn-N achievements —
		// only Gallery game sales do, via an explicit
		// Achievements.RecordMoneyEarned call at the sales path. Furniture
		// sell-back refunds, achievement-bonus payouts, future tutorial
		// freebies, etc. shouldn't inflate lifetime-earnings tracking.
		Money += amount;
	}

	public bool TrySpend( long amount )
	{
		if ( Money < amount ) return false;
		Money -= amount;
		return true;
	}

	// ── Energy ────────────────────────────────────────────────────────────────

	public void AddEnergy( int amount )
	{
		Energy = Math.Max( 0, Energy + amount );
	}

	/// Returns true and deducts <paramref name="amount"/> if affordable.
	public bool TrySpendEnergy( int amount )
	{
		if ( Energy < amount ) return false;
		Energy -= amount;
		return true;
	}

	private void TickEnergyRegen()
	{
		// Mirror the calendar's pause gate so energy doesn't tick during
		// out-of-game UIs or the engine pause menu.
		if ( IsTimePaused ) return;

		// Energy ONLY accrues during active project production.
		// `EnergyRegenPerSecond` is intentionally ignored so a stale
		// scene-inspector value (0.5 default) can't quietly leak passive
		// regen back in — the field stays in code as a save-DTO target
		// but is no longer wired to the regen pipeline.
		var pm = GameProjectManager.Instance;
		if ( pm?.Current is not { } project )                  return;
		if ( project.Phase != GameProjectPhase.Production )    return;

		// Total Focus across the team (sum, not avg) — divided by 1000 so
		// "one fully-focused worker" equals 1.0× the per-second base.
		// Five Focus-1000 workers = 5.0× base. No clamp at 1× because more
		// employees should mean more energy, by design.
		float teamFactor = project.TotalTeamFocus / 1000f;
		float rate       = ProjectEnergyBonusPerSecond * teamFactor;
		if ( rate <= 0f )                                       return;

		_energyAccum += Time.Delta * rate * TimeMultiplier;
		if ( _energyAccum < 1f ) return;

		int toAdd = (int)_energyAccum;
		_energyAccum -= toAdd;
		AddEnergy( toAdd );
	}

	// ── Game speed ───────────────────────────────────────────────────────────

	/// Switch the active speed tier. Fails silently and pushes a "Locked"
	/// notification if the achievement gate hasn't been satisfied.
	public bool TrySetSpeed( GameSpeedTier tier )
	{
		if ( !GameSpeed.IsUnlocked( tier ) )
		{
			var locked = GameSpeed.Get( tier );
			var ach    = locked.RequiredAchievement is { } id ? Achievements.Get( id ) : null;
			Notifications.Push( "Speed Locked",
				ach is null
					? $"{locked.Name} is not yet available."
					: $"{locked.Name} unlocks via \"{ach.Name}\".",
				"warning" );
			return false;
		}

		var info       = GameSpeed.Get( tier );
		ActiveSpeed    = tier;
		TimeMultiplier = info.Multiplier;

		Notifications.Push( "Game Speed",
			$"Now running at {info.Multiplier:0.#}× ({info.Name}).", "info" );
		return true;
	}

	// Called from UI open/close for an instant response; OnUpdate keeps it sticky.
	public static void RefreshPlayerLock()
	{
		Instance?.ApplyPlayerLock();
	}

	private void ApplyPlayerLock()
	{
		var anyOpen = (Shop.Instance?.IsOpen                   ?? false)
		           // HR-PROTOTYPE-2026-05-08: fullscreen HR modal. Remove this
		           // single line + delete Code/HR.cs + Code/UI/HRPanel.razor*
		           // to revert the prototype cleanly.
		           || (HR.Instance?.IsOpen                     ?? false)
		           || (Leaderboards.Instance?.IsOpen           ?? false)
		           || (SaveMenu.Instance?.IsOpen               ?? false)
		           || (Letterbox.Instance?.IsOpen              ?? false)
		           || (Letterbox.Instance?.IsReadingLetter     ?? false)
		           || (Letterbox.Instance?.IsQuestOpen         ?? false)
		           || (GameMenu.Instance?.IsOpen               ?? false)
		           || (Settings.Instance?.IsOpen               ?? false)
		           || (GameProjectManager.Instance?.IsOpen     ?? false)
		           || (Gallery.Instance?.IsOpen                ?? false)
		           || (TrainingManager.Instance?.IsOpen        ?? false)
		           || (InventoryManager.Instance?.IsOpen       ?? false)
		           || (Workstations.Instance?.IsOpen           ?? false)
		           || (HRManager.Instance?.IsInterviewing      ?? false)
		           || (HRManager.Instance?.IsViewingStaff      ?? false)
		           || (InventoryManager.Instance?.IsAssigning  ?? false)
		           // Training-completion result modal — must release the
		           // cursor even when nothing else is open, otherwise the
		           // Continue button is unclickable.
		           || ((TrainingManager.Instance?.PendingResults.Count ?? 0) > 0)
		           // Tutorial modals — same input lock as the others so the
		           // player can't run around / pick up the cursor while any
		           // tutorial step (Welcome / CreateAndBin / Complete) is up.
		           || (TutorialManager.Instance?.IsAnyModalVisible ?? false)
		           // Employee chat panel — cursor must be unlocked so the
		           // reply buttons receive clicks. Mirrors the other modals.
		           || (EmployeeInteractor.Instance?.IsChatting ?? false);

		var player = Scene.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
		if ( player is not null )
		{
			player.UseLookControls  = !anyOpen;
			player.UseInputControls = !anyOpen;

			// UseInputControls=false stops new movement input from being read,
			// but the controller can keep coasting on a non-zero WishVelocity
			// (and the rigidbody on residual horizontal momentum) — so the
			// founder is visibly still walking through the menu. Zero
			// horizontal axes only, every frame: vertical (Z) is left alone
			// so a player who opens a menu mid-air keeps falling at the
			// normal gravity rate instead of getting their fall velocity
			// reset to 0 every frame (which produced a slow descent).
			if ( anyOpen )
			{
				player.WishVelocity = Vector3.Zero;
				if ( player.Body is not null )
				{
					var v = player.Body.Velocity;
					player.Body.Velocity = new Vector3( 0f, 0f, v.z );
				}
			}
		}

		// Show the mouse cursor whenever any modal UI is open so the player can click tiles.
		// (Mouse.Visible is deprecated; Mouse.Visibility is the current API.)
		Sandbox.Mouse.Visibility = anyOpen ? Sandbox.MouseVisibility.Visible : Sandbox.MouseVisibility.Hidden;
	}
}
