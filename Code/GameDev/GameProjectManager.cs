/// <summary>
/// Orchestrates the Create-Game flow and the production loop that follows.
///
/// Lifecycle:
///   1. Player clicks the "Create Game" tile in the menu.
///   2. <see cref="StartNewProject"/> opens a Shop-style modal with three
///      setup pages (title/genre, employee allocation, time allocation).
///   3. After page 3 the player commits — <see cref="BeginProduction"/> flips
///      the phase to Production and the tick loop starts accumulating
///      progress / quality points (Step 5).
///   4. Once every pillar hits 100 %, the project advances to Finished and
///      the studio collects the payoff (Step 5+).
///
/// Open/close is mutually exclusive with <see cref="GameMenu"/> and
/// <see cref="Shop"/> — opening this modal closes the others, mirroring the
/// pattern those two already use between themselves.
/// </summary>
public sealed class GameProjectManager : Component
{
	public static GameProjectManager Instance { get; private set; }

	// ── Tuning constants ────────────────────────────────────────────────────

	/// Multiplier applied to <see cref="GameProject.Synergy"/> before it
	/// scales pillar points. Final quality multiplier = clamp(1 + S × this,
	/// MinSynergy, MaxSynergy). 0.5 is tuned so a perfectly-stacked 5-tag
	/// project (10 pairs × +0.30 average) lands exactly at MaxSynergy — a
	/// 4-tag stack stays under the cap even at all +0.30, leaving headroom
	/// for the 5th tag to feel meaningful.
	const float SynergyScalar = 0.5f;

	/// Hard floor on the synergy multiplier — even a max-clash tag pile
	/// can't reduce pillar points below 10% of baseline.
	const float MinSynergy    = 0.1f;

	/// Hard ceiling on the synergy multiplier. Caps the upside from a
	/// well-synergised tag stack so synergy can't run away with the run —
	/// past this point, more good pairs are "free wins" that don't compound,
	/// nudging the player to optimise revenue / effort multipliers instead.
	const float MaxSynergy    = 2.5f;

	// ── Open state ──────────────────────────────────────────────────────────

	public bool IsOpen { get; private set; }

	/// The project the player is currently designing or running. Null when no
	/// project is in flight (clean slate / between games).
	public GameProject Current { get; private set; }

	/// Setup-page index, 0..2. Only meaningful while
	/// <c>Current.Phase == Setup</c>; ignored once production starts.
	public int SetupPage { get; private set; } = 0;

	// ── Genre-slot rules ────────────────────────────────────────────────────

	/// Slots available on a fresh studio (before any tier unlock).
	[Property] public int MaxGenresEarly { get; set; } = 2;

	/// Slot-count progression. Highest matching tier wins; falls back to
	/// <see cref="MaxGenresEarly"/> if nothing is unlocked yet. Tiers are
	/// listed highest-slots first so the lookup short-circuits naturally.
	///
	/// Progression — tuned so wider stacks unlock with studio progression:
	///   • Ship 5 games        → 3 slots
	///   • Earn $100k lifetime → 4 slots
	///   • Earn $1M lifetime   → 5 slots
	static readonly (AchievementId achievement, int slots)[] _genreSlotTiers =
	{
		( AchievementId.Earn1M,     5 ),
		( AchievementId.Earn100k,   4 ),
		( AchievementId.Ship5Games, 3 ),
	};

	/// How many genre tags the player can attach to a project right now.
	public int GenreSlots
	{
		get
		{
			foreach ( var tier in _genreSlotTiers )
				if ( Achievements.IsUnlocked( tier.achievement ) )
					return tier.slots;
			return MaxGenresEarly;
		}
	}

	// ── Lifecycle ───────────────────────────────────────────────────────────

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
		EnforceEffortCaps();
		TickProduction();
		AutoShowPublishIfReady();
		TickSlotUnlockToasts();
	}

	// ── Genre-slot unlock toasts ────────────────────────────────────────────
	// Sticky reminders that fire when the player crosses an achievement that
	// widens GenreSlots (3 / 4 / 5). They stay pinned until the player has
	// either shipped or started a project that actually uses that slot count.
	// Self-healing: every 2s we re-evaluate each tier, push a toast if it's
	// unlocked-but-unused, and clear it the moment usage is detected. So the
	// behaviour survives hot-reload, save loads, and manual dismissal.

	float _slotToastReinforceAccum;
	const float SlotToastReinforceInterval = 2f;

	void TickSlotUnlockToasts()
	{
		_slotToastReinforceAccum += Time.Delta;
		if ( _slotToastReinforceAccum < SlotToastReinforceInterval ) return;
		_slotToastReinforceAccum = 0f;

		EvaluateSlotUnlockToast( 3, AchievementId.Ship5Games,
			"3 genre slots unlocked!",
			"Pick three genres in your next Create Game." );
		EvaluateSlotUnlockToast( 4, AchievementId.Earn100k,
			"4 genre slots unlocked!",
			"Pick four genres in your next Create Game." );
		EvaluateSlotUnlockToast( 5, AchievementId.Earn1M,
			"5 genre slots unlocked!",
			"Maximum genre stack — build the ultimate combo." );
	}

	void EvaluateSlotUnlockToast( int slotCount, AchievementId gateAchievement,
		string title, string body )
	{
		string tag = $"slot-unlock-{slotCount}";

		bool unlocked  = Achievements.IsUnlocked( gateAchievement );
		bool usedAlready = SlotCountUsedOrInUse( slotCount );

		if ( !unlocked || usedAlready )
		{
			Notifications.RemoveByTag( tag );
			return;
		}

		if ( !Notifications.HasTag( tag ) )
		{
			Notifications.Push( title, body, "success", duration: 0f, tag: tag );
		}
	}

	/// True if the player has either shipped a game with at least <paramref
	/// name="n"/> genres or is currently in / past Production with that many
	/// genres on the in-flight project. Either condition means the player
	/// has "used" that slot tier and the unlock toast can retire.
	bool SlotCountUsedOrInUse( int n )
	{
		if ( Current is { } cur
		     && (cur.Phase == GameProjectPhase.Production
		         || cur.Phase == GameProjectPhase.Finished)
		     && cur.Genres.Count >= n )
			return true;

		var gallery = Gallery.Instance;
		if ( gallery is null ) return false;
		foreach ( var g in gallery.ShippedGames )
			if ( g.Genres.Count >= n ) return true;

		return false;
	}

	// ── Auto-show publish UI ────────────────────────────────────────────────
	// While a project is in `Finished` (shipped pending publish), the publish
	// modal pops automatically in two situations:
	//
	//   1. First time the conditions are met — Phase just flipped to Finished
	//      and no other modal is open. One-shot, gated by `_publishUiAutoShown`.
	//
	//   2. The player just closed any *other* modal (Shop / Settings / Gallery /
	//      GameMenu) — i.e. they returned from menu to gameplay. We re-pop so
	//      the player can never get stuck without a way back to publish.
	//
	// Closing the publish modal itself (× / Tab) does NOT re-trigger — we only
	// react to OTHER modals closing, otherwise dismissal would loop instantly.
	// The HUD widget and the GameMenu's "Create Game" tile remain explicit
	// re-open paths too (see <see cref="StartNewProject"/>).

	bool _publishUiAutoShown;

	void AutoShowPublishIfReady()
	{
		if ( Current is null
		  || Current.Phase != GameProjectPhase.Finished )
		{
			_publishUiAutoShown = false;
			return;
		}

		// Publish takes priority over every other modal — shipping is a
		// high-stakes moment the player shouldn't accidentally miss while
		// poking through the shop or HR. SetOpen below force-closes any
		// other modal that happens to be up.
		if ( !_publishUiAutoShown && !IsOpen )
		{
			_publishUiAutoShown = true;
			SetOpen( true );
		}
	}

	// ── Open / close ────────────────────────────────────────────────────────

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			// Close every competing modal so Publish always lands on top.
			// Both base modals (z 100) and sub-modals (z 200) are dropped —
			// CreateGamePanel sits at z 400, but in s&box's panel-tree
			// rendering the underlying components can still capture input
			// and compete visually depending on scene ordering. Forcing
			// them shut keeps the publish flow unambiguous.
			GameMenu.Instance?.SetOpen( false );
			Shop.Instance?.SetOpen( false );
			Settings.Instance?.SetOpen( false );
			Gallery.Instance?.SetOpen( false );
			Workstations.Instance?.SetOpen( false );
			TrainingManager.Instance?.SetOpen( false );
			InventoryManager.Instance?.CancelAssign();
			HRManager.Instance?.CloseWorkerView();
		}
		GameManager.RefreshPlayerLock();
	}

	/// Entry point from the menu's "Create Game" tile. Wipes any setup-phase
	/// project and starts fresh on page 1. Refuses to interrupt a project
	/// that's already in production OR pending publish — the player has to
	/// ship or cancel that one first. Re-opening the modal in those cases
	/// lets the tile double as a "show me the current project" affordance.
	public void StartNewProject()
	{
		if ( Current is { Phase: GameProjectPhase.Production }
		  || Current is { Phase: GameProjectPhase.Finished } )
		{
			SetOpen( true );
			return;
		}

		Current   = new GameProject();
		SetupPage = 0;
		SetOpen( true );
	}

	/// Discard the in-flight setup project. No-op once production has begun —
	/// abandoning a running project will need its own confirmation flow later.
	public void CancelSetup()
	{
		if ( Current is null ) return;
		if ( Current.Phase != GameProjectPhase.Setup ) return;
		Current   = null;
		SetupPage = 0;
		SetOpen( false );
	}

	// ── Setup-page navigation ───────────────────────────────────────────────

	public bool CanAdvancePage
	{
		get
		{
			if ( Current is null ) return false;
			return SetupPage switch
			{
				0 => !string.IsNullOrWhiteSpace( Current.Title ) && Current.Genres.Count > 0,
				// Page 2 → 3 requires at least one role assignment. We don't
				// force every pillar to be staffed (the spec explicitly allows
				// sacrificing a pillar) — but greenlighting an entirely empty
				// project would just stall the production phase.
				1 => HasAnyAssignment,
				// Page 3 → production: at least one pillar must get some time
				// allocated. Going over the 10-minute cap is allowed but
				// surfaced as a "crunch" warning in the UI.
				2 => HasAnyTimeAllocated,
				_ => false,
			};
		}
	}

	bool HasAnyAssignment
	{
		get
		{
			if ( Current is null ) return false;
			foreach ( var kv in Current.Assignments )
				if ( kv.Value.Count > 0 ) return true;
			return false;
		}
	}

	bool HasAnyTimeAllocated =>
		Current is not null
		&& ( Current.DesignTime > 0f
		     || Current.SoundTime > 0f
		     || Current.GraphicsTime > 0f );

	public void NextPage()
	{
		if ( !CanAdvancePage ) return;
		SetupPage = Math.Min( SetupPage + 1, 2 );
	}

	public void PreviousPage()
	{
		SetupPage = Math.Max( SetupPage - 1, 0 );
	}

	// ── Page 1 — title + genre selection ────────────────────────────────────

	public void SetTitle( string title )
	{
		if ( Current is null ) return;
		Current.Title = title ?? "";
	}

	/// Add a genre tag to the project. Fails silently if the slot cap is full,
	/// the tag is locked, or it's already in the set.
	public bool TryAddGenre( GameGenre genre )
	{
		if ( Current is null ) return false;
		if ( Current.Genres.Contains( genre ) ) return false;
		if ( Current.Genres.Count >= GenreSlots ) return false;
		if ( !GameGenres.IsUnlocked( genre ) ) return false;
		Current.Genres.Add( genre );
		return true;
	}

	public void RemoveGenre( GameGenre genre )
	{
		Current?.Genres.Remove( genre );
	}

	/// Toggle a tag on/off. Used by the picker UI's per-tag click handler.
	public void ToggleGenre( GameGenre genre )
	{
		if ( Current is null ) return;
		if ( Current.Genres.Contains( genre ) )
			RemoveGenre( genre );
		else
			TryAddGenre( genre );
	}

	// ── Page 2 — employee allocation ────────────────────────────────────────
	// Stubs for now; the UI in Step 3 will call these. Diminishing-returns
	// efficiency lives on GameProject (so anyone computing contributions can
	// read it without going through the manager).

	public void Assign( IDevWorker worker, GameDevRole role )
	{
		if ( Current is null || worker is null ) return;
		var list = Current.Assignments[role];
		if ( !list.Contains( worker ) ) list.Add( worker );
	}

	public void Unassign( IDevWorker worker, GameDevRole role )
	{
		if ( Current is null || worker is null ) return;
		Current.Assignments[role].Remove( worker );
	}

	public void ToggleAssign( IDevWorker worker, GameDevRole role )
	{
		if ( Current is null || worker is null ) return;
		var list = Current.Assignments[role];
		if ( list.Contains( worker ) ) list.Remove( worker );
		else                            list.Add( worker );
	}

	// ── Page 3 — effort allocation ──────────────────────────────────────────
	// Effort is a 0..1 multiplier on each pillar's *final score*. Duration is
	// fixed (see <see cref="ProjectDuration"/>), so pushing the slider higher
	// raises the score ceiling rather than extending development.
	//
	// All three pillar sliders share a single budget pool. The total budget
	// is set by the team's combined PillarContribution across the three
	// primary roles, divided by genre complexity:
	//
	//     TotalEffortBudget = clamp( TotalTeamPower / (EffortBaseline × EffortMultiplier), 0..1 )
	//
	// Decreasing one pillar's slider frees that headroom for the other two.
	// Per-pillar maximums are derived from this budget (Budget - sum of
	// other two pillars), so the player never has to do mental math —
	// pressing "+" just stops at the budget edge.
	//
	// Role assignment still uses the existing 1.0×-primary / 0.3×-secondary
	// weighting in PillarContribution. A Sound Director still helps Design
	// and Graphics a little, but their primary contribution to Sound is
	// what makes Sound's slice of the budget — and Sound's per-effort score
	// ceiling — disproportionately bigger.

	[Property, Description( "Team strength required to unlock 100% total budget on a 1.0x genre. Higher = harder to max out." )]
	public float EffortBaseline { get; set; } = 1000f;

	public float DesignTeamStrength   => PillarContribution( GameDevRole.GameDirector,  static w => w.Stats.Design.Average  );
	public float SoundTeamStrength    => PillarContribution( GameDevRole.SoundDirector, static w => w.Stats.Sound.Average   );
	public float GraphicsTeamStrength => PillarContribution( GameDevRole.ArtDirector,   static w => w.Stats.Artistry.Average );

	/// "Lost" team strength — the contribution that's missing this tick
	/// because one or more NPCs are in <see cref="EmployeeMood.Bad"/>.
	/// Used by the production tick to accumulate
	/// <see cref="GameProject.DesignPenaltyPoints"/> etc., so time spent
	/// at half strength stays as a score tax even after mood heals.
	public float DesignLostStrength   => PillarLostContribution( GameDevRole.GameDirector,  static w => w.Stats.Design.Average  );
	public float SoundLostStrength    => PillarLostContribution( GameDevRole.SoundDirector, static w => w.Stats.Sound.Average   );
	public float GraphicsLostStrength => PillarLostContribution( GameDevRole.ArtDirector,   static w => w.Stats.Artistry.Average );

	/// Sum of the three pillar contributions, scaled by studio infrastructure
	/// upgrades. Drives <see cref="TotalEffortBudget"/>.
	///
	/// <see cref="StudioProductivityMultiplier"/> currently only reflects
	/// Server ownership (+20 %); future office upgrades layer in here so
	/// per-pillar contribution maths stays untouched.
	public float TotalTeamPower =>
		(DesignTeamStrength + SoundTeamStrength + GraphicsTeamStrength)
		* StudioProductivityMultiplier();

	/// Studio-wide productivity multiplier from owned infrastructure items.
	/// 1.0 = baseline; +0.20 per Server owned (cap at one for now since the
	/// catalogue has only one server SKU). Public so other systems can
	/// surface "your studio is N% more productive than baseline" if needed.
	public static float StudioProductivityMultiplier()
	{
		float mult = 1f;
		var inv = InventoryManager.Instance;
		if ( inv is not null && inv.OwnsAny( ItemKind.Server ) )
			mult *= 1.20f;
		return mult;
	}

	/// Single shared 0..1 budget the three pillar sliders draw from.
	/// `Sum(DesignTime, SoundTime, GraphicsTime) ≤ TotalEffortBudget` is
	/// the only allocation rule — there are no separate per-pillar caps.
	public float TotalEffortBudget
	{
		get
		{
			if ( Current is null )      return 0f;
			if ( EffortBaseline <= 0f ) return 1f;
			float complexity = MathF.Max( 0.01f, Current.EffortMultiplier );
			return Math.Clamp( TotalTeamPower / (EffortBaseline * complexity), 0f, 1f );
		}
	}

	/// How much of the budget the player has already allocated. The UI's
	/// "X% / Y% budget" header is just `TotalEffortSpent / TotalEffortBudget`.
	public float TotalEffortSpent =>
		Current is null
			? 0f
			: Current.DesignTime + Current.SoundTime + Current.GraphicsTime;

	/// Per-pillar slider ceiling = remaining headroom in the shared budget,
	/// i.e. `Budget − (sum of the OTHER two pillars)`. Pulling one slider
	/// down lifts the other two ceilings by the same amount.
	public float MaxDesignEffort   => Headroom( Current?.SoundTime    ?? 0f, Current?.GraphicsTime ?? 0f );
	public float MaxSoundEffort    => Headroom( Current?.DesignTime   ?? 0f, Current?.GraphicsTime ?? 0f );
	public float MaxGraphicsEffort => Headroom( Current?.DesignTime   ?? 0f, Current?.SoundTime    ?? 0f );

	float Headroom( float a, float b )
	{
		if ( Current is null ) return 0f;
		return Math.Clamp( TotalEffortBudget - a - b, 0f, 1f );
	}

	public void SetDesignTime  ( float v ) { if ( Current is not null ) Current.DesignTime   = Math.Clamp( v, 0f, MaxDesignEffort   ); }
	public void SetSoundTime   ( float v ) { if ( Current is not null ) Current.SoundTime    = Math.Clamp( v, 0f, MaxSoundEffort    ); }
	public void SetGraphicsTime( float v ) { if ( Current is not null ) Current.GraphicsTime = Math.Clamp( v, 0f, MaxGraphicsEffort ); }

	/// Re-clamp the effort sliders every frame during Setup. Two distinct
	/// jobs:
	///   1. Each slider stays inside [0, 1] — defensive.
	///   2. If the team or genre changes such that the budget shrinks below
	///      the player's current sum, scale all three sliders down
	///      proportionally so the constraint is restored without arbitrarily
	///      preferring one pillar over another.
	void EnforceEffortCaps()
	{
		if ( Current is null )                         return;
		if ( Current.Phase != GameProjectPhase.Setup ) return;

		Current.DesignTime   = Math.Clamp( Current.DesignTime,   0f, 1f );
		Current.SoundTime    = Math.Clamp( Current.SoundTime,    0f, 1f );
		Current.GraphicsTime = Math.Clamp( Current.GraphicsTime, 0f, 1f );

		float sum    = TotalEffortSpent;
		float budget = TotalEffortBudget;
		if ( sum > budget && sum > 0f )
		{
			float k = budget / sum;
			Current.DesignTime   *= k;
			Current.SoundTime    *= k;
			Current.GraphicsTime *= k;
		}
	}

	// ── Commit → production ─────────────────────────────────────────────────

	/// Lock the setup choices in and start the production tick loop. Any
	/// pillar the player allocated 0 % to is treated as pre-completed (the
	/// player consciously chose to sacrifice it) so production can finish
	/// even when only one or two pillars actually get worked on.
	public void BeginProduction()
	{
		if ( Current is null ) return;
		if ( Current.Phase != GameProjectPhase.Setup ) return;
		if ( !CanAdvancePage ) return;

		// Tutorial gate: BeginProduction is only allowed during the
		// CreateGameOnly phase (and outside the tutorial). All other
		// tutorial steps refuse with a toast that nudges the player to
		// follow the modal's instructions.
		if ( TutorialManager.Instance is { } tut && !tut.CanBeginProduction() )
		{
			Notifications.Push( "Tutorial Step",
				"Follow the on-screen instructions before starting production.",
				"warning", duration: 5f );
			return;
		}

		// Pre-complete sacrificed pillars: progress at 1, points at 0 — they
		// don't gate IsProductionComplete and they ship empty.
		if ( Current.DesignTime   <= 0f ) Current.DesignProgress   = 1f;
		if ( Current.SoundTime    <= 0f ) Current.SoundProgress    = 1f;
		if ( Current.GraphicsTime <= 0f ) Current.GraphicsProgress = 1f;

		Current.Phase = GameProjectPhase.Production;

		// Fresh project → reset the per-production mood-toast gates so the
		// player gets a "first frustrated" / "first idea" notification this
		// run regardless of what fired in the previous one.
		Current.BadMoodToastShown  = false;
		Current.GoodMoodToastShown = false;

		// Second-game tutorial nudge: on the project IMMEDIATELY AFTER the
		// tutorial-completing first ship, force one Good and one Bad mood
		// event during this production so the player discovers the mood /
		// chat mechanic. GamesShipped == 1 means exactly the 2nd game is
		// starting now (1 already shipped). After the 2nd game the moods
		// fall back to the regular probabilistic rolls.
		bool isSecondGame = Achievements.GamesShipped == 1;
		Current.GuaranteeGoodMoodPending = isSecondGame;
		Current.GuaranteeBadMoodPending  = isSecondGame;

		Notifications.Push( "Production Started",
			$"\"{Current.Title}\" has entered development.", "info" );

		// Tutorial: production begun → advances CreateGameOnly → BuyBinOnly.
		// Time stays paused — the in-flight project gets fast-completed
		// later via CompleteImmediately when Start Up Training fires, so
		// the player skips the production wait on their first game.
		TutorialManager.Instance?.NotifyProductionBegun();

		// No "unstaffed pillar" warning anymore — pillars now accept partial
		// contributions from anyone on the team, so missing the primary role
		// just slows that pillar down rather than freezing it.
	}

	// ── Publish ─────────────────────────────────────────────────────────────
	// Player-triggered ship action — only valid once Production has rolled
	// over to Finished. Records a ShippedGame in the Gallery (with ReviewScore
	// pending — see Gallery.OnMonthStart for the delayed review release),
	// awards XP scaled with total pillar points, clears Current so the studio
	// can start the next project, and closes the modal.

	[Property, Description( "XP per total pillar point on publish. Default 0.2 -> a 750-pts game = 150 XP." )]
	public float PublishXpPerPoint { get; set; } = 0.2f;

	public void Publish()
	{
		if ( Current is null )                            return;
		if ( Current.Phase != GameProjectPhase.Finished ) return;

		var gm = GameManager.Instance;

		var shipped = new ShippedGame
		{
			Title          = Current.Title,
			Genres         = new List<GameGenre>( Current.Genres ),
			ShipDay        = gm?.Day   ?? 1,
			ShipMonth      = gm?.Month ?? 1,
			ShipYear       = gm?.Year  ?? 2026,
			DesignPoints   = Current.DesignPoints,
			SoundPoints    = Current.SoundPoints,
			GraphicsPoints = Current.GraphicsPoints,
			// Sales metrics + ReviewScore are filled inside RecordShippedGame
			// (which calls Gallery.ComputeScoreAndMetrics). The score stays
			// hidden until OnMonthStart's review reveal — public can play and
			// money can flow before critics weigh in.
		};

		Gallery.Instance?.RecordShippedGame( shipped );

		int totalPts = Current.DesignPoints + Current.SoundPoints + Current.GraphicsPoints;
		long xp      = (long)MathF.Round( totalPts * PublishXpPerPoint );
		if ( xp > 0 ) PlayerStats.Instance?.AddXp( xp );

		Notifications.Push( "Released!",
			$"\"{shipped.Title}\" is out in the wild.",
			"success", duration: 10f );

		// "Check your game in the Gallery" sticky tip is now owned by
		// Gallery itself — it self-heals via OnUpdate while EverOpened
		// is false, and clears the moment the player opens the panel.

		// Award per-worker experience for every role they were assigned
		// to on the project. Magnitude matches a Basic training session
		// (+5 to one main stat per role). Workers in multiple roles get
		// boosts in each — but with the diminishing-role-efficiency rule,
		// most workers will be in one role and get a single +5 stat bump.
		// Smoke-broken projects skip this — workers didn't earn it.
		if ( Current.WasSmokeBroken )
		{
			Notifications.Push( "No experience gained",
				$"\"{Current.Title}\" was outsourced — your team didn't earn project XP.",
				"info", duration: 6f );
		}
		else
		{
			AwardProjectExperience( Current );
		}

		// Tutorial: first publish ends the tutorial — fires the congrats
		// modal and confirms time is fully unlocked. No-op past Complete.
		TutorialManager.Instance?.NotifyGamePublished();

		Current = null;
		_publishUiAutoShown = false;
		SetOpen( false );
	}

	/// XP per dev-role assignment applied at ship time. Originally tuned to
	/// match `TrainingTier.Basic.RewardStatGain`; held at 5 even after the
	/// 2026-05-02 training-significance bump (Basic → 15) because project
	/// XP is *free* — there's no money/energy cost — so the cheaper rate
	/// here is intentional. Bump if shipping should feel like "free Basic
	/// training" again.
	const int ProjectXpPerRole = 5;

	/// Walk the project's role assignments and apply a +<see cref="ProjectXpPerRole"/>
	/// boost to each worker's role-corresponding main stat. Pushes a single
	/// summary notification listing every worker's gain so the player sees
	/// the full XP report in one read instead of a flood of toasts.
	void AwardProjectExperience( GameProject project )
	{
		if ( project is null ) return;

		// Aggregate per-worker so a worker in multiple roles gets one
		// "Alice: +5 Design, +5 Focus" line instead of two toasts.
		var perWorker = new Dictionary<IDevWorker, List<(MainStat stat, int amount)>>();

		foreach ( var kvp in project.Assignments )
		{
			var role = kvp.Key;
			var stat = StatForRole( role );
			if ( stat is null ) continue;          // role with no XP mapping

			foreach ( var worker in kvp.Value )
			{
				if ( worker?.Stats is null ) continue;
				if ( !perWorker.ContainsKey( worker ) )
					perWorker[worker] = new List<(MainStat, int)>();
				perWorker[worker].Add( (stat.Value, ProjectXpPerRole) );
			}
		}

		if ( perWorker.Count == 0 ) return;

		var lines = new List<string>( perWorker.Count );
		foreach ( var kvp in perWorker )
		{
			var worker = kvp.Key;
			var gains  = kvp.Value;
			foreach ( var (stat, amount) in gains )
				worker.Stats.BoostMainStat( stat, amount );

			string summary = string.Join( ", ",
				gains.ConvertAll( g => $"+{g.amount} {g.stat.DisplayName()}" ) );
			lines.Add( $"{worker.Name}: {summary}" );
		}

		Notifications.Push( $"Experience Gained from \"{project.Title}\"",
			string.Join( "  ·  ", lines ),
			"info", duration: 10f );
		Log.Info( $"[Project] AwardProjectExperience — {perWorker.Count} worker(s): " +
		          string.Join( " · ", lines ) );
	}

	/// Maps a dev-role to the main stat that gets practised by performing it.
	/// Returns null for any role without a mapping (which we skip silently).
	static MainStat? StatForRole( GameDevRole role ) => role switch
	{
		GameDevRole.ProjectManager => MainStat.Focus,
		GameDevRole.Producer       => MainStat.Creativity,
		GameDevRole.GameDirector   => MainStat.Design,
		GameDevRole.SoundDirector  => MainStat.Sound,
		GameDevRole.ArtDirector    => MainStat.Artistry,
		_                          => null,
	};

	/// Skip the production timer and fast-complete the in-flight project.
	/// Used by the tutorial's Start Up Training step so the player doesn't
	/// have to sit through a real-time wait on their very first game.
	/// Each pillar lands at progress=1.0 with points = effort × team
	/// strength × quality (the same formula <see cref="AdvancePillar"/>
	/// targets at the natural end of production), then Phase moves to
	/// Finished so the Publish UI auto-pops on the next OnUpdate.
	public void CompleteImmediately()
	{
		if ( Current is null )                               return;
		if ( Current.Phase != GameProjectPhase.Production ) return;

		float pmBoost     = ComputePmBoost();
		float hackerBoost = ComputeHackerBoost();
		// Synergy multiplier: clamp(1 + (sum of pair synergies) × scalar,
		// MinSynergy, MaxSynergy). Floor stops a worst-case clash pile from
		// zeroing out pillar points; ceiling stops a perfect-stack project
		// from running away with the run.
		float synergy     = Math.Clamp( 1f + Current.Synergy * SynergyScalar, MinSynergy, MaxSynergy );
		float quality     = (1f + pmBoost) * (1f + hackerBoost) * synergy;

		void Snap( float effort, float teamStrength, Action<float> setProgress, Action<int> setPoints, int bonus, int penalty )
		{
			if ( effort <= 0f )
			{
				// Sacrificed pillars: pre-completed at progress=1, points=0.
				// Bonus and penalty applied for consistency with the
				// production-tick math, even though sacrificed pillars
				// rarely accumulate either in practice.
				setProgress( 1f );
				setPoints( System.Math.Max( 0, bonus - penalty ) );
				return;
			}
			float target = effort * teamStrength * quality;
			setProgress( 1f );
			setPoints( System.Math.Max( 0, (int)MathF.Round( target ) + bonus - penalty ) );
		}

		Snap( Current.DesignTime,   DesignTeamStrength,   p => Current.DesignProgress   = p, pts => Current.DesignPoints   = pts, Current.DesignBonusPoints,   Current.DesignPenaltyPoints   );
		Snap( Current.SoundTime,    SoundTeamStrength,    p => Current.SoundProgress    = p, pts => Current.SoundPoints    = pts, Current.SoundBonusPoints,    Current.SoundPenaltyPoints    );
		Snap( Current.GraphicsTime, GraphicsTeamStrength, p => Current.GraphicsProgress = p, pts => Current.GraphicsPoints = pts, Current.GraphicsBonusPoints, Current.GraphicsPenaltyPoints );

		Current.Phase = GameProjectPhase.Finished;
		Notifications.Push( "Game Complete",
			$"\"{Current.Title}\" is ready to ship.", "success" );
		Log.Info( $"[Project] CompleteImmediately — \"{Current.Title}\" fast-completed " +
		          $"(D={Current.DesignPoints}, S={Current.SoundPoints}, G={Current.GraphicsPoints})." );
	}

	/// Same fast-finish path as <see cref="CompleteImmediately"/> but every
	/// pillar's final points are multiplied by <paramref name="penalty"/>
	/// (clamped to [0, 1]). Drives the Smoke Break consumable: at penalty
	/// = 0.7, the player ships a game worth 70 % of its potential output.
	/// Sacrificed pillars (effort = 0) stay at 0 — penalty doesn't subtract.
	public void CompleteWithPenalty( float penalty )
	{
		if ( Current is null )                               return;
		if ( Current.Phase != GameProjectPhase.Production ) return;

		penalty = Math.Clamp( penalty, 0f, 1f );

		// Mark the project so the publish path knows to skip per-worker
		// experience gain — the team didn't put in the hours that earn it.
		Current.WasSmokeBroken = true;

		float pmBoost     = ComputePmBoost();
		float hackerBoost = ComputeHackerBoost();
		float synergy     = Math.Clamp( 1f + Current.Synergy * SynergyScalar, MinSynergy, MaxSynergy );
		float quality     = (1f + pmBoost) * (1f + hackerBoost) * synergy;

		// Smoke Break: `penalty` here is the smoke-break shortcut multiplier
		// (not to be confused with the mood-tracker `moodPenalty` below).
		void Snap( float effort, float teamStrength, Action<float> setProgress, Action<int> setPoints, int bonus, int moodPenalty )
		{
			if ( effort <= 0f )
			{
				setProgress( 1f );
				setPoints( System.Math.Max( 0, bonus - moodPenalty ) );
				return;
			}
			float target = effort * teamStrength * quality * penalty;
			setProgress( 1f );
			setPoints( System.Math.Max( 0, (int)MathF.Round( target ) + bonus - moodPenalty ) );
		}

		Snap( Current.DesignTime,   DesignTeamStrength,   p => Current.DesignProgress   = p, pts => Current.DesignPoints   = pts, Current.DesignBonusPoints,   Current.DesignPenaltyPoints   );
		Snap( Current.SoundTime,    SoundTeamStrength,    p => Current.SoundProgress    = p, pts => Current.SoundPoints    = pts, Current.SoundBonusPoints,    Current.SoundPenaltyPoints    );
		Snap( Current.GraphicsTime, GraphicsTeamStrength, p => Current.GraphicsProgress = p, pts => Current.GraphicsPoints = pts, Current.GraphicsBonusPoints, Current.GraphicsPenaltyPoints );

		Current.Phase = GameProjectPhase.Finished;
		Notifications.Push( "Game Complete",
			$"\"{Current.Title}\" is ready to ship.", "success" );
		Log.Info( $"[Project] CompleteWithPenalty({penalty:0.##}) — \"{Current.Title}\" " +
		          $"(D={Current.DesignPoints}, S={Current.SoundPoints}, G={Current.GraphicsPoints})." );
	}

	// ── Production-duration anchor ──────────────────────────────────────────
	// Every project ships in this many real seconds, regardless of slider
	// settings or team strength. <see cref="GameManager.TimeMultiplier"/>
	// still applies, so 2× game speed cuts the wall-clock duration in half.
	//
	// The old TimeCost-curve / per-pillar-seconds model is gone: weak teams
	// no longer take longer to ship, they ship a worse game (effort cap +
	// score-multiplier path on AdvancePillar below).

	[Property, Description( "Real seconds for one project to ship at 1x game speed." )]
	public float ProjectDuration { get; set; } = 60f;

	// ── Production tick ────────────────────────────────────────────────────
	// Each pillar's progress fills linearly to 1.0 over <see cref="ProjectDuration"/>.
	// Final pillar points = effort × team strength × synergy × PM × SuperHacker,
	// clamped 1..1000. PM and SuperHacker used to shorten dev time; with the
	// duration fixed, they show up as score multipliers instead.

	void TickProduction()
	{
		if ( Current is null )                               return;
		if ( Current.Phase != GameProjectPhase.Production ) return;
		if ( ProjectDuration <= 0f )                        return;
		// Tutorial freeze: same pause as the calendar — no in-flight project
		// progress while the player's still in the first-launch flow.
		if ( TutorialManager.Instance is { IsBlockingTime: true } ) return;

		float dt = Time.Delta * (GameManager.Instance?.TimeMultiplier ?? 1f);
		if ( dt <= 0f ) return;

		float pmBoost       = ComputePmBoost();              // 0..~0.5 score uplift
		float hackerBoost   = ComputeHackerBoost();          // +0.10 per unassigned SuperHacker
		// Genre-pair bonus, scaled then clamped between MinSynergy and
		// MaxSynergy — see CompleteImmediately.
		float synergy       = Math.Clamp( 1f + Current.Synergy * SynergyScalar, MinSynergy, MaxSynergy );
		float quality       = (1f + pmBoost) * (1f + hackerBoost) * synergy;
		float progressDelta = dt / ProjectDuration;

		// Per-tick penalty accrual: the score we WOULD have earned this tick
		// at full mood, but didn't because someone is in Bad mood. Same units
		// as bonus (= effort × strength × quality × progressDelta). Persists
		// across mood transitions so heal-late doesn't auto-refund the time
		// spent at half strength.
		Current.DesignPenaltyPoints   += (int)MathF.Round( Current.DesignTime   * DesignLostStrength   * quality * progressDelta );
		Current.SoundPenaltyPoints    += (int)MathF.Round( Current.SoundTime    * SoundLostStrength    * quality * progressDelta );
		Current.GraphicsPenaltyPoints += (int)MathF.Round( Current.GraphicsTime * GraphicsLostStrength * quality * progressDelta );

		AdvancePillar(
			effort:        Current.DesignTime,
			teamStrength:  DesignTeamStrength,
			quality:       quality,
			progressDelta: progressDelta,
			progress:      p   => Current.DesignProgress = p,
			points:        pts => Current.DesignPoints   = pts,
			currentP:      Current.DesignProgress,
			bonus:         Current.DesignBonusPoints,
			penalty:       Current.DesignPenaltyPoints );

		AdvancePillar(
			effort:        Current.SoundTime,
			teamStrength:  SoundTeamStrength,
			quality:       quality,
			progressDelta: progressDelta,
			progress:      p   => Current.SoundProgress = p,
			points:        pts => Current.SoundPoints   = pts,
			currentP:      Current.SoundProgress,
			bonus:         Current.SoundBonusPoints,
			penalty:       Current.SoundPenaltyPoints );

		AdvancePillar(
			effort:        Current.GraphicsTime,
			teamStrength:  GraphicsTeamStrength,
			quality:       quality,
			progressDelta: progressDelta,
			progress:      p   => Current.GraphicsProgress = p,
			points:        pts => Current.GraphicsPoints   = pts,
			currentP:      Current.GraphicsProgress,
			bonus:         Current.GraphicsBonusPoints,
			penalty:       Current.GraphicsPenaltyPoints );

		// Phase transition once every pillar has hit 1.0. Step 5d hooks the
		// payout flow off this notification.
		if ( Current.IsProductionComplete )
		{
			Current.Phase = GameProjectPhase.Finished;
			Notifications.Push( "Game Complete",
				$"\"{Current.Title}\" is ready to ship.", "success" );
		}
	}

	/// Apply an instant pillar-points bonus mid-production. Called by
	/// <see cref="EmployeeInteractor"/> when the player accepts a Good-mood
	/// suggestion. No-op outside Production so a stale suggestion can't
	/// inflate a finished or pre-production score.
	///
	/// Writes both: the tracker (so <see cref="AdvancePillar"/> and the
	/// ship-time Snap include the bonus on top of computed points every
	/// tick) AND the live points field (so the player sees the +N jump
	/// immediately instead of waiting for the next production tick).
	public void AddPillarBonus( ProjectPillar pillar, int amount )
	{
		if ( Current is null )                              return;
		if ( Current.Phase != GameProjectPhase.Production ) return;
		if ( amount <= 0 )                                  return;

		switch ( pillar )
		{
			case ProjectPillar.Design:
				Current.DesignBonusPoints   += amount;
				Current.DesignPoints        += amount;
				break;
			case ProjectPillar.Sound:
				Current.SoundBonusPoints    += amount;
				Current.SoundPoints         += amount;
				break;
			case ProjectPillar.Graphics:
				Current.GraphicsBonusPoints += amount;
				Current.GraphicsPoints      += amount;
				break;
		}
	}

	/// Advance one pillar. Identical for all three; the caller threads the
	/// pillar's effort slider, team strength, and progress/points setters in.
	void AdvancePillar(
		float          effort,
		float          teamStrength,
		float          quality,
		float          progressDelta,
		Action<float>  progress,
		Action<int>    points,
		float          currentP,
		int            bonus,
		int            penalty )
	{
		// 0 effort → pillar was pre-completed in BeginProduction. Skip.
		if ( effort   <= 0f ) return;
		if ( currentP >= 1f ) return;

		float next = MathF.Min( 1f, currentP + progressDelta );
		progress( next );

		// Live points = progress × target + bonus − penalty.
		//   • bonus   persists +N from accepted suggestions across ticks.
		//   • penalty persists −N for time spent at Bad-mood half-strength,
		//     so healing the mood doesn't auto-recover the lost ground.
		// target itself uses *current* teamStrength (mood already applied),
		// so swap-in mid-production still raises the ceiling smoothly —
		// the penalty just locks in the time already spent at low strength.
		float target = effort * teamStrength * quality;
		int   final  = (int)MathF.Round( next * target ) + bonus - penalty;
		points( System.Math.Max( 0, final ) );
	}

	/// Multiplier applied to a worker's stat when they're assigned somewhere
	/// other than the pillar's primary role. The point of having a primary
	/// role is that staffing it is *better* — but other roles still help.
	const float SecondaryRoleWeight = 0.3f;

	/// Sum of the team's contribution to one pillar, weighted by:
	///   • diminishing-returns efficiency for stacked roles,
	///   • per-employee morale (founder always 1.0),
	///   • primary-role coverage — workers in the pillar's primary role
	///     contribute at 1.0; everyone else (different role, or not assigned
	///     at all) contributes at <see cref="SecondaryRoleWeight"/>,
	///   • training state — off-site workers contribute 0; researching
	///     workers contribute at <see cref="TrainingManager.ResearchProductivityFactor"/>.
	///
	/// Pass 1 walks the player's explicit Page-2 assignments. Pass 2 sweeps
	/// every studio member not yet counted (founder + all hires) at the
	/// secondary weight — so a hire signed mid-project starts contributing
	/// on the next tick, and unassigned veterans are never "forgotten" by
	/// the production loop.
	float PillarContribution( GameDevRole primaryRole, Func<IDevWorker, int> pickStat )
	{
		if ( Current is null ) return 0f;

		var   primaryList = Current.Assignments[primaryRole];
		var   seen        = new HashSet<IDevWorker>();
		float total       = 0f;

		// Pass 1 — explicit role assignments (primary 1.0× / other roles 0.3×).
		foreach ( var kv in Current.Assignments )
		{
			foreach ( var worker in kv.Value )
			{
				if ( worker is null )       continue;
				if ( !seen.Add( worker ) )   continue;
				if ( IsOffSite( worker ) )   continue;

				int   roleCount = Current.RoleCountFor( worker );
				float eff       = GameProject.EfficiencyForRoleCount( roleCount );
				float morale    = (worker as EmployeeNPC)?.Morale ?? 1f;
				float mood      = (worker as EmployeeNPC)?.MoodEfficiencyFactor ?? 1f;
				float weight    = primaryList.Contains( worker )
					? 1f
					: SecondaryRoleWeight;
				float research  = ResearchFactor( worker );

				total += BoostedStat( worker, pickStat ) * eff * morale * mood * weight * research;
			}
		}

		// Pass 2 — every other studio member (founder + un-slotted hires) at
		// the secondary weight, so hiring during an in-flight project IS
		// immediately impactful: pillar contribution rises on the next tick.
		var founder = PlayerStats.Instance;
		if ( founder is not null && seen.Add( founder ) && !IsOffSite( founder ) )
		{
			float fres = ResearchFactor( founder );
			total += BoostedStat( founder, pickStat ) * 1f * 1f * SecondaryRoleWeight * fres;
		}

		var hr = HRManager.Instance;
		if ( hr is not null )
		{
			foreach ( var npc in hr.Staff )
			{
				if ( !seen.Add( npc ) )   continue;
				if ( IsOffSite( npc ) )   continue;

				float morale = npc.Morale;
				float mood   = npc.MoodEfficiencyFactor;
				float nres   = ResearchFactor( npc );
				total += BoostedStat( npc, pickStat ) * 1f * morale * mood * SecondaryRoleWeight * nres;
			}
		}

		return total;
	}

	/// Mirror of <see cref="PillarContribution"/> that accumulates the
	/// (1 − mood) × everything-else portion — i.e. the contribution that's
	/// MISSING this tick due to Bad-mood workers. Founder and zero-mood
	/// workers contribute 0 here. Used to grow
	/// <see cref="GameProject.DesignPenaltyPoints"/> etc. each tick so
	/// time spent at half strength stays as a permanent score tax even
	/// after the mood heals.
	float PillarLostContribution( GameDevRole primaryRole, Func<IDevWorker, int> pickStat )
	{
		if ( Current is null ) return 0f;

		var   primaryList = Current.Assignments[primaryRole];
		var   seen        = new HashSet<IDevWorker>();
		float total       = 0f;

		foreach ( var kv in Current.Assignments )
		{
			foreach ( var worker in kv.Value )
			{
				if ( worker is null )      continue;
				if ( !seen.Add( worker ) ) continue;
				if ( IsOffSite( worker ) ) continue;

				int   roleCount = Current.RoleCountFor( worker );
				float eff       = GameProject.EfficiencyForRoleCount( roleCount );
				float morale    = (worker as EmployeeNPC)?.Morale ?? 1f;
				float mood      = (worker as EmployeeNPC)?.MoodEfficiencyFactor ?? 1f;
				float weight    = primaryList.Contains( worker ) ? 1f : SecondaryRoleWeight;
				float research  = ResearchFactor( worker );

				total += BoostedStat( worker, pickStat ) * eff * morale * (1f - mood) * weight * research;
			}
		}

		// Founder is always mood = 1, so contributes 0 to the lost total.
		// Skip the founder branch entirely.
		_ = PlayerStats.Instance;

		var hr = HRManager.Instance;
		if ( hr is not null )
		{
			foreach ( var npc in hr.Staff )
			{
				if ( !seen.Add( npc ) ) continue;
				if ( IsOffSite( npc ) ) continue;

				float morale = npc.Morale;
				float mood   = npc.MoodEfficiencyFactor;
				float nres   = ResearchFactor( npc );
				total += BoostedStat( npc, pickStat ) * 1f * morale * (1f - mood) * SecondaryRoleWeight * nres;
			}
		}

		return total;
	}

	/// Apply the worker's furniture stat boost on top of <paramref name="pickStat"/>'s
	/// raw value before any other multiplier. Boost = the worker's own
	/// desk-mount items (chair / computer / monitor) PLUS the studio-wide
	/// total from owned non-desk furniture (lounge, books, etc). Founder
	/// gets only the studio-wide half (no desk). Floored at 1, no upper cap —
	/// late-game offices can push grown stats above 1000.
	static int BoostedStat( IDevWorker worker, Func<IDevWorker, int> pickStat )
	{
		int raw    = pickStat( worker );
		int studio = InventoryManager.Instance?.StudioWideStatBoost ?? 0;
		int desk   = (worker as EmployeeNPC)?.WorkstationStatBoost ?? 0;
		return System.Math.Max( 1, raw + studio + desk );
	}

	// Mirror EmployeeNPC.IsRealTrainingSession: ignore default-zeroed sessions
	// that the s&box editor sometimes auto-instantiates onto [Property] fields
	// when re-saving a prefab. EndYear > 0 means a real schedule was written.
	static bool IsOffSite( IDevWorker w ) =>
		w?.ActiveTraining is { EndYear: > 0 };

	static float ResearchFactor( IDevWorker w )
	{
		if ( string.IsNullOrEmpty( w?.ResearchTopicId ) ) return 1f;
		return TrainingManager.Instance?.ResearchProductivityFactor ?? 0.8f;
	}

	/// Sum of the assigned team's primary stat for a given role, weighted by
	/// efficiency and morale. Used by <see cref="ComputePmBoost"/> where we
	/// genuinely only care about workers in that one role.
	float TeamStat( GameDevRole role, Func<IDevWorker, int> pickStat )
	{
		if ( Current is null ) return 0f;

		float total = 0f;
		var   list  = Current.Assignments[role];
		foreach ( var worker in list )
		{
			int   roleCount = Current.RoleCountFor( worker );
			float eff       = GameProject.EfficiencyForRoleCount( roleCount );
			float morale    = (worker as EmployeeNPC)?.Morale ?? 1f;
			total += BoostedStat( worker, pickStat ) * eff * morale;
		}
		return total;
	}

	/// Project-Manager throughput uplift, applied to every pillar.
	/// PM Focus stat → up to ~0.5 multiplier when the team has a strong PM.
	float ComputePmBoost()
	{
		if ( Current is null ) return 0f;
		float pm    = TeamStat( GameDevRole.ProjectManager, static w => w.Stats.Focus.Average );
		// Scale: 1000 Focus PM with 100 % efficiency → 0.5 boost.
		return MathF.Min( 0.5f, pm / 2000f );
	}

	/// SuperHacker bench bonus. Each unassigned employee with the ability
	/// adds a flat 10 % to every pillar's progress speed.
	float ComputeHackerBoost()
	{
		if ( Current is null ) return 0f;
		int count = 0;
		foreach ( var w in Current.UnassignedStaff )
			if ( w.HasAbility( EmployeeAbility.SuperHacker ) ) count++;
		return 0.10f * count;
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public GameProjectSave Save()
	{
		if ( Current is null ) return null;

		var dto = new GameProjectSave
		{
			Title            = Current.Title,
			Genres           = new List<GameGenre>( Current.Genres ),
			DesignTime       = Current.DesignTime,
			SoundTime        = Current.SoundTime,
			GraphicsTime     = Current.GraphicsTime,
			DesignProgress   = Current.DesignProgress,
			SoundProgress    = Current.SoundProgress,
			GraphicsProgress = Current.GraphicsProgress,
			DesignPoints    = Current.DesignPoints,
			SoundPoints     = Current.SoundPoints,
			GraphicsPoints  = Current.GraphicsPoints,
			Phase            = Current.Phase,
			SetupPage        = SetupPage,
		};

		foreach ( var kv in Current.Assignments )
		{
			var refs = new List<WorkerRefSave>();
			foreach ( var w in kv.Value )
				refs.Add( WorkerToRef( w ) );
			dto.Assignments[kv.Key] = refs;
		}

		return dto;
	}

	public void Load( GameProjectSave dto )
	{
		if ( dto is null )
		{
			Current   = null;
			SetupPage = 0;
			return;
		}

		var proj = new GameProject
		{
			Title            = dto.Title ?? "",
			DesignTime       = dto.DesignTime,
			SoundTime        = dto.SoundTime,
			GraphicsTime     = dto.GraphicsTime,
			DesignProgress   = dto.DesignProgress,
			SoundProgress    = dto.SoundProgress,
			GraphicsProgress = dto.GraphicsProgress,
			DesignPoints     = dto.DesignPoints,
			SoundPoints      = dto.SoundPoints,
			GraphicsPoints   = dto.GraphicsPoints,
			Phase            = dto.Phase,
		};
		foreach ( var g in dto.Genres ?? new List<GameGenre>() )
			proj.Genres.Add( g );

		// Re-resolve worker assignments: founder by sentinel, hires by name.
		// Names are unique in practice; if a saved name has no matching live
		// hire (e.g. they were fired between save and load) the slot is
		// silently skipped — better than crashing on load.
		foreach ( var kv in dto.Assignments ?? new() )
		{
			if ( !proj.Assignments.ContainsKey( kv.Key ) )
				proj.Assignments[kv.Key] = new List<IDevWorker>();

			foreach ( var refSave in kv.Value )
			{
				var resolved = WorkerFromRef( refSave );
				if ( resolved is not null )
					proj.Assignments[kv.Key].Add( resolved );
			}
		}

		Current   = proj;
		SetupPage = Math.Clamp( dto.SetupPage, 0, 2 );
	}

	static WorkerRefSave WorkerToRef( IDevWorker w ) => new()
	{
		IsFounder    = w?.IsPlayer ?? false,
		EmployeeName = w is null || w.IsPlayer ? "" : w.Name,
	};

	static IDevWorker WorkerFromRef( WorkerRefSave r )
	{
		if ( r is null ) return null;
		if ( r.IsFounder ) return PlayerStats.Instance;

		var staff = HRManager.Instance?.Staff;
		if ( staff is null ) return null;

		foreach ( var npc in staff )
		{
			if ( npc.EmployeeName == r.EmployeeName )
				return npc;
		}
		return null;
	}
}
