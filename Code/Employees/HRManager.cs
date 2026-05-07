/// <summary>
/// Manages the full hiring pipeline and all ongoing employment:
///   • Applicant inbox      — auto-fills while a job posting is active
///   • Job posting          — chooses applicant quality, costs money each month
///   • Interview state      — one candidate at a time; costs energy to start
///   • Desk assignment      — tracks which of the MaxDesks spots are occupied
///   • Monthly bookkeeping  — posting fee + intern/alumni cooldown ticks on
///                            Day 1 of each in-game month via OnMonthStart.
///                            Salaries are per-employee on each NPC's
///                            HireDay anniversary via OnDayStart, so payroll
///                            staggers across the month. Missed salary drops
///                            Morale; missed posting cost cancels the posting.
/// </summary>
public sealed class HRManager : Component
{
	public static HRManager Instance { get; private set; }

	// ── Applicant inbox ──────────────────────────────────────────────────────

	/// Pending résumés waiting for a decision.
	public IReadOnlyList<Employee> Applicants => _applicants;
	readonly List<Employee> _applicants = new();

	// ── Hired staff ─────────────────────────────────────────────────────────

	/// Everyone currently employed at the studio.
	/// Each entry is the EmployeeNPC component that holds their live data.
	public IReadOnlyList<EmployeeNPC> Staff => _staff;
	readonly List<EmployeeNPC> _staff = new();

	// ── Special-hire mechanics ──────────────────────────────────────────────
	// When a special kind is unlocked, every applicant roll has a small chance
	// (see SpecialApplicantChance) to spawn that kind instead of a regular
	// hire. Among unlocked kinds, the pick is uniform random.
	//
	// Intern contracts last 2 in-game months. When a contract ends, the
	// intern is auto-fired and an Alumni record is queued; after a 3-month
	// cooldown, that alumnus may return as a Senior-tier applicant with
	// max abilities. The first intern ever hired is guaranteed to return
	// (so the player gets to see the alumni payoff at least once).

	/// Probability per applicant generation that the new applicant is a
	/// special kind (instead of a regular hire). Only applies when at least
	/// one special is unlocked.
	[Property] public float SpecialApplicantChance { get; set; } = 0.20f;

	const int InternContractMonths       = 2;
	const int InternReturnCooldownMonths = 3;

	/// Has the player ever hired an intern? Used to guarantee the first
	/// intern returns (the alumni payoff is a tutorial moment).
	bool _firstInternHired;

	/// Tracking record for an alumnus waiting to return to the inbox.
	sealed class InternAlumnus
	{
		public string Name              { get; init; } = "";
		public EmployeeRole Role        { get; init; }
		public int  MonthsUntilReturn   { get; set; }
		public bool GuaranteedReturn    { get; init; }
	}

	readonly List<InternAlumnus> _alumni = new();

	// ── Interview state ──────────────────────────────────────────────────────

	/// The applicant currently being interviewed, or null. While set, the
	/// InterviewPanel modal opens automatically (above the HR menu) and shows
	/// only the stats listed in <c>InterviewSubject.RevealedStats</c> — the
	/// rest render as "?". Set by TryStartInterview (which also picks two
	/// random stats to reveal), cleared by HireInterviewSubject /
	/// PassOnInterviewSubject.
	public Employee InterviewSubject { get; private set; }

	/// True while an interview is open. Drives InterviewPanel visibility and
	/// can be folded into modal-stack / player-lock logic.
	public bool IsInterviewing => InterviewSubject != null;

	/// How many of the six main stats are revealed at the start of an
	/// interview. Future questionnaire feature will let the player reveal
	/// more by spending energy / time on questions.
	[Property] public int InitialRevealedStats { get; set; } = 2;

	// ── Staff-stats view ────────────────────────────────────────────────────
	// Player-driven inspection of an existing hire (or the founder). Drives
	// StaffStatsPanel — a read-only modal that shows the full 6-stat sheet,
	// salary, and abilities. Distinct from the interview flow (which has the
	// 2-of-6 hidden-stats minigame); once you've hired someone, the numbers
	// are your business to see.

	/// The worker (founder or hire) currently being inspected, or null when
	/// no stats card is open. Set via <see cref="ViewWorker"/> and cleared
	/// by <see cref="CloseWorkerView"/>.
	public IDevWorker ViewedWorker { get; private set; }

	public bool IsViewingStaff => ViewedWorker != null;

	public void ViewWorker( IDevWorker worker )
	{
		if ( worker is null ) return;
		ViewedWorker = worker;

		// Treat the view modal like a shop: close the in-game menu so the
		// stats card owns the screen by itself.
		GameMenu.Instance?.SetOpen( false );
		GameManager.RefreshPlayerLock();
	}

	public void CloseWorkerView()
	{
		ViewedWorker = null;
		GameManager.RefreshPlayerLock();
	}

	// ── Desk management ──────────────────────────────────────────────────────
	// Desks are now owned via the Inventory system: every placed Desk
	// (an InventoryItem of ItemKind.Desk bound to a PlacementSlot of
	// SlotKind.Desk) counts as one available workstation. HRManager queries
	// InventoryManager rather than maintaining its own desk pool.

	/// Total number of placed Desks in the office (i.e. capacity for hires).
	/// Replaces the old UnlockedDesks counter — derived live from inventory.
	public int  MaxDesks      => InventoryManager.Instance?.PlacedCountOf( ItemKind.Desk ) ?? 0;

	/// Total Desk slots in the scene (placed or empty). For UI display.
	public int  TotalDesks
	{
		get
		{
			var inv = InventoryManager.Instance;
			if ( inv is null ) return 0;
			int n = 0;
			foreach ( var s in inv.Slots )
				if ( s.Kind == SlotKind.Desk ) n++;
			return n;
		}
	}

	public int  FreeDeskCount => MaxDesks - _staff.Count;
	public bool HasFreeDesk   => FreeDeskCount > 0;

	/// Backward-compatibility alias. UnlockedDesks used to be a separate
	/// integer that grew with paid unlocks; with the inventory system, the
	/// "unlocked" count is the same as the placed Desk count. Kept so older
	/// achievement evaluators (StaffSize >= MaxDesks) keep compiling.
	public int  UnlockedDesks => MaxDesks;

	// ── Prefab ───────────────────────────────────────────────────────────────

	/// Citizen prefab cloned for every hire. Should contain the model,
	/// SkinnedModelRenderer, and EmployeeNPC component already wired up.
	[Property] public GameObject EmployeeNpcPrefab { get; set; }

	/// Optional spawn point — new hires appear here, then walk to their
	/// assigned desk. If unset, they appear at the desk directly (legacy
	/// behavior). Drag the "Employee spawn" GameObject into this slot.
	[Property] public GameObject EmployeeSpawnPoint { get; set; }

	// ── Desk purchase (delegates to InventoryManager) ────────────────────────

	/// Cost of buying another Desk via InventoryManager. Pulled live from
	/// the catalogue so price changes propagate without HRManager edits.
	public long NextDeskCost =>
		InventoryCatalogue.Get( ItemKind.Desk )?.Price ?? 0;

	/// True when there's a free Desk slot in the scene to place a new Desk
	/// at. Money is NOT checked here — UI distinguishes "Locked" / "Full" /
	/// "Can't afford" separately.
	public bool CanBuyDesk
	{
		get
		{
			var inv = InventoryManager.Instance;
			if ( inv is null ) return false;

			// Need: an empty Desk slot exists somewhere in the scene
			foreach ( var slot in inv.Slots )
			{
				if ( slot.Kind != SlotKind.Desk ) continue;
				if ( !inv.IsSlotOccupied( slot.Id ) ) return true;
			}
			return false;
		}
	}

	/// Desks no longer have an achievement gate (the catalogue handles all
	/// item-level gating). Kept so the UI's NextDeskLock binding doesn't break.
	public Achievement NextDeskLock => null;

	// ── Monthly cost ─────────────────────────────────────────────────────────
	// Aggregates that drive the HUD / HR menu "burn rate" displays. The actual
	// payment loop in PaySalaries() still charges per-employee so a single
	// missed wage can drop one person's morale without flipping the whole
	// payroll into the red.

	/// Sum of every active employee's monthly salary.
	public long TotalMonthlySalaries
	{
		get
		{
			long total = 0;
			foreach ( var n in _staff ) total += n.Salary;
			return total;
		}
	}

	/// Salaries + the active job-posting fee. What the studio is "burning" each month.
	public long TotalMonthlyExpenses => TotalMonthlySalaries + PostingInfo.MonthlyCost;

	// ── Salary constants ────────────────────────────────────────────────────

	/// Morale penalty per missed salary payment (0–1).
	const float MissedPayMoralePenalty = 0.25f;

	/// Morale at or below this value causes the employee to quit.
	const float QuitMoraleThreshold = 0.15f;

	// ── Job posting ─────────────────────────────────────────────────────────

	/// Active job posting tier. Determines applicant quality and the monthly
	/// fee charged at month-start. None = no posting → no new applicants.
	[Property] public JobPostingTier PostingTier { get; set; } = JobPostingTier.BulletinBoard;

	public JobPosting.Info PostingInfo => JobPosting.Get( PostingTier );

	// ── New-applicant timer ──────────────────────────────────────────────────

	/// Real seconds between new applicants appearing in the inbox.
	/// 180 s = one every 3 real minutes (tune alongside SecondsPerMonth).
	/// 70 real seconds at 1× speed = 7 in-game days (`SecondsPerDay = 10`).
	/// Tuned for prototype playtesting — tighter cadence makes the hire
	/// loop feel responsive while a single project plays out.
	[Property] public float SecondsBetweenApplicants { get; set; } = 70f;

	float _applicantTimer;

	// ── Internal state ──────────────────────────────────────────────────────

	readonly List<EmployeeNPC> _fireQueue = new();

	// ── Progression helpers ──────────────────────────────────────────────────

	/// Maps the active posting tier to a 0–1 quality float for stat generation.
	float GetJobQuality() => PostingTier switch
	{
		JobPostingTier.BulletinBoard => 0.2f,
		JobPostingTier.CareerSite    => 0.55f,
		JobPostingTier.Headhunter    => 0.9f,
		_                            => 0.1f,   // None / unknown
	};

	/// Returns 0 at game start (Year 2026) rising to 1 at ~Year 2031.
	/// Reflects the studio's growing reputation attracting stronger talent.
	float GetProgressionFactor()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return 0f;
		float years = (gm.Year - 2026) + (gm.Month - 1) / 12f;
		return Math.Clamp( years / 5f, 0f, 1f );
	}

	// ── Internal ─────────────────────────────────────────────────────────────

	readonly Random _rng = new();

	bool _wardrobePrewarmed;

	// ── Lifecycle ────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;
		// Inbox starts empty — applicants only arrive once a posting is active
		// and the timer ticks past SecondsBetweenApplicants.
	}

	protected override void OnStart()
	{
		Log.Info( $"[HR] {TotalDesks} desk slot(s) in scene · {MaxDesks} placed Desk(s) · {FreeDeskCount} free." );
		Log.Info( $"[HR] Boot config — SecondsBetweenApplicants = {SecondsBetweenApplicants:0.##} (= {SecondsBetweenApplicants / GameManager.SecondsPerDay:0.#} in-game days). " +
		          $"PostingTier = {PostingTier}." );

		if ( EmployeeNpcPrefab is null )
		{
			Log.Warning( "[HR] EmployeeNpcPrefab is not assigned — hiring will fail. " +
			             "Drag your citizen prefab into the HRManager component inspector." );
		}

		if ( InventoryManager.Instance is null )
		{
			Log.Warning( "[HR] No InventoryManager in the scene. Add one to enable hiring." );
		}

		// Posting fee + intern/alumni cooldowns tick on month boundaries;
		// salaries pay per-employee on each NPC's anniversary day (their
		// HireDay) via OnDayStart so payroll is staggered across the month.
		GameManager.OnMonthStart += PayMonthly;
		GameManager.OnDayStart   += PayDailySalaries;

		// Tutorial seed: drop one guaranteed-strong applicant in the inbox at
		// game start so the player has someone obviously worth hiring right
		// away. Skipped if there's already an applicant on the books (the
		// save-load and restart flows both call SeedTutorialApplicant which
		// guards on Count == 0).
		SeedTutorialApplicant();

		// Force the citizen wardrobe (models + materials + textures used by
		// Sandbox.Dresser.Randomize) into the resource cache during scene boot
		// so the first real hire doesn't pay the synchronous load cost
		// mid-gameplay (was producing a ~500ms SceneSystem job-wait).
		PrewarmWardrobe();
	}

	/// Sandbox.Dresser.Randomize() synchronously loads the citizen wardrobe
	/// the first time it runs in a session. If that lands on a hire frame the
	/// SceneSystem stalls waiting for asset jobs (visible as a multi-hundred-ms
	/// hitch). We absorb the cost here, at scene start, by cloning the citizen
	/// prefab once below the world, randomizing its Dresser, and immediately
	/// destroying the clone — the loaded assets stay cached for the rest of
	/// the run, so every real hire's Randomize hits warm cache.
	void PrewarmWardrobe()
	{
		if ( _wardrobePrewarmed ) return;
		if ( EmployeeNpcPrefab is null ) return;
		_wardrobePrewarmed = true;

		// Far below the floor — never seen, never collides with anything.
		var go = EmployeeNpcPrefab.Clone(
			new Vector3( 0f, 0f, -100000f ), Rotation.Identity );

		var dresser = go.Components.Get<Sandbox.Dresser>();
		if ( dresser is not null )
		{
			// Neutral sliders — the body shape doesn't matter, we just need
			// Randomize to walk the wardrobe and load every clothing asset.
			dresser.ManualAge    = 0.5f;
			dresser.ManualHeight = 0.5f;
			dresser.ManualTint   = 0.5f;
			dresser.Randomize();
		}

		// Randomize is synchronous (that synchronicity is the whole reason
		// it hitches at hire time). Once it returns the wardrobe is cached;
		// the throwaway clone has done its job.
		go.Destroy();
	}

	/// Drop one guaranteed-strong applicant into the inbox: flat 300 stats,
	/// programmer role, fixed $750/mo salary (anchors the $2.50-per-stat curve),
	/// no salary bias, no hidden
	/// "?" gates. Ensures the player has someone obviously worth hiring on
	/// day 1. Idempotent — does nothing if the inbox already has anyone in
	/// it. Called from <see cref="OnStart"/> for fresh boots and from
	/// <see cref="GameSaveManager.RestartRun"/> for new-game restarts.
	public void SeedTutorialApplicant()
	{
		if ( _applicants.Count > 0 ) return;

		var starter = Employee.GenerateTutorialApplicant( _rng );
		_applicants.Add( starter );
		Notifications.Push( "First Applicant",
			$"{starter.Name} has applied. Open HR to interview them.",
			"info", duration: 8f );
		Log.Info( $"[HR] Seeded tutorial applicant: {starter.Name} ({starter.Role}) — 300/flat." );
	}

	/// Buy another Desk via the Inventory system. Delegates to
	/// <see cref="InventoryManager.TryBuy"/>; that path also handles the
	/// auto-place pass that lands the new Desk at a free slot.
	public bool TryBuyNextDesk()
	{
		var inv = InventoryManager.Instance;
		if ( inv is null )
		{
			Notifications.Push( "Buy failed",
				"No InventoryManager in the scene.", "warning" );
			return false;
		}

		if ( !CanBuyDesk )
		{
			Notifications.Push( "No free slot",
				"Every Desk slot in the office is already occupied. Add more slots to expand.",
				"warning" );
			return false;
		}

		return inv.TryBuy( ItemKind.Desk );
	}

	protected override void OnDestroy()
	{
		GameManager.OnMonthStart -= PayMonthly;
		GameManager.OnDayStart   -= PayDailySalaries;
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		TickApplicantTimer();
	}

	// ── Private ticks ────────────────────────────────────────────────────────

	void TickApplicantTimer()
	{
		// Tutorial freeze: same pause semantics as the calendar — no new
		// applicants pile up while the player's stuck on the welcome modal
		// or hasn't made their first hire yet.
		if ( TutorialManager.Instance is { IsBlockingTime: true } ) return;

		// Tick in IN-GAME seconds, not real seconds, so the cadence is
		// "every N in-game days" regardless of the player's chosen game
		// speed. With SecondsBetweenApplicants = 70 (= 7 in-game days at
		// SecondsPerDay = 10), a 4× speed run still gets one applicant
		// per 7 in-game days — they just arrive 4× faster in real time.
		float gameDelta = Time.Delta * (GameManager.Instance?.TimeMultiplier ?? 1f);
		_applicantTimer += gameDelta;
		if ( _applicantTimer < SecondsBetweenApplicants ) return;

		_applicantTimer = 0f;

		// No posting → no inbound applicants. Player has to switch a tier on.
		if ( PostingTier == JobPostingTier.None ) return;
		if ( _applicants.Count >= 8 ) return;

		PushNewApplicant();

		// Diagnostic: log the threshold value alongside every fire so we
		// can verify (via console) that the right scene value loaded. If
		// you ever see this log report a threshold other than the value
		// in the scene, it's a serialization / hot-reload issue.
		var gm = GameManager.Instance;
		Log.Info( $"[HR] New applicant fired. Threshold={SecondsBetweenApplicants:0.##}s " +
		          $"({SecondsBetweenApplicants / GameManager.SecondsPerDay:0.#} in-game days). " +
		          $"Speed={gm?.TimeMultiplier ?? 1f}× · Day={gm?.Day ?? 0}/Mo{gm?.Month ?? 0}/Y{gm?.Year ?? 0}." );
	}

	/// Force-spawn an applicant into the inbox right now, ignoring the
	/// normal <see cref="SecondsBetweenApplicants"/> cadence. Used by the
	/// desk-purchase onboarding hook so the player has someone fresh to
	/// interview the moment their second workstation is ready. No-op if
	/// the inbox is full or the posting is switched off.
	public void SpawnApplicantNow()
	{
		PushNewApplicant();
	}

	/// Internal applicant-spawn helper shared by the timed roll in
	/// <see cref="TickApplicantTimer"/> and the on-demand
	/// <see cref="SpawnApplicantNow"/> entry. Honours the same posting /
	/// inbox-cap gates as the timer path.
	void PushNewApplicant()
	{
		if ( PostingTier == JobPostingTier.None ) return;
		if ( _applicants.Count >= 8 )             return;

		// Roll: regular hire by default, special kind on a small chance when
		// any specials are unlocked. Intern is filtered out when there's
		// already an intern on staff (one-slot rule).
		var applicant = RollApplicant();
		_applicants.Add( applicant );

		var prefix = applicant.Kind.IsSpecial()
			? $"[{applicant.Kind.PillLabel()}] "
			: "";
		// Role and salary are intentionally hidden — those reveal during the
		// interview. The optional prefix still flags special-kind applicants
		// (e.g. interns, alumni) since that's a hiring-flow signal, not a stat.
		Notifications.Push(
			"New Applicant",
			$"{prefix}{applicant.Name}",
			kind: "info" );
	}

	/// Decide what kind the next applicant is and generate them. Splits the
	/// "what kind" question out so RollApplicant stays one-screen readable.
	Employee RollApplicant()
	{
		EmployeeKind kind = PickApplicantKind();
		return kind == EmployeeKind.Regular
			? Employee.GenerateApplicant(
				_rng,
				jobQuality:        GetJobQuality(),
				progressionFactor: GetProgressionFactor() )
			: Employee.GenerateSpecialApplicant(
				_rng,
				kind:              kind,
				jobQuality:        GetJobQuality(),
				progressionFactor: GetProgressionFactor() );
	}

	EmployeeKind PickApplicantKind()
	{
		// TEMPORARY: special hire kinds (Mentor / Marketing / Remote /
		// Intern) are being reworked. While disabled, every applicant
		// is Regular regardless of unlocked achievements. The Gallery
		// surfaces a "Coming Soon" placeholder under Unlocks → Special
		// Hires so the player knows the feature is paused. Restore the
		// pool-roll below to re-enable.
		return EmployeeKind.Regular;

		/*
		var pool = new List<EmployeeKind>();
		foreach ( var k in SpecialHires.UnlockedSpecials() )
		{
			if ( k == EmployeeKind.Intern && HasActiveIntern ) continue;
			pool.Add( k );
		}

		if ( pool.Count == 0 )                               return EmployeeKind.Regular;
		if ( _rng.NextDouble() >= SpecialApplicantChance )   return EmployeeKind.Regular;
		return pool[_rng.Next( pool.Count )];
		*/
	}

	bool HasActiveIntern
	{
		get
		{
			foreach ( var n in _staff )
				if ( n.Kind == EmployeeKind.Intern ) return true;
			return false;
		}
	}

	/// Called automatically on Day 1 of each new in-game month — handles
	/// every monthly bookkeeping task EXCEPT salaries. Salaries are now
	/// per-employee on each NPC's anniversary day (see <see cref="PayDailySalaries"/>).
	/// Order: 1) charge posting fee, 2) advance intern contracts (auto-fire
	/// those with 0 months left), 3) tick alumni return timers and inject
	/// any due returners into the inbox.
	void PayMonthly()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return;
		var cal = $"{gm.MonthShort} Y{gm.Year}";

		PayPostingFee     ( gm, cal );
		AdvanceInternships( cal );
		AdvanceAlumni     ( cal );
	}

	/// Called every in-game day. Walks staff and pays anyone whose
	/// <see cref="EmployeeNPC.HireDay"/> matches today, so payroll is
	/// staggered across the month — each employee paid on the anniversary
	/// of their hire date rather than everyone draining the bank on Day 1.
	void PayDailySalaries()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return;
		var cal = $"D{gm.Day} {gm.MonthShort} Y{gm.Year}";

		PaySalaries( gm, cal );
	}

	/// Decrement every intern's contract by one month. When a contract
	/// hits zero, fire them and queue an alumni record so they may return
	/// later as a much stronger applicant.
	void AdvanceInternships( string cal )
	{
		// Snapshot first — FireInternal mutates _staff.
		_fireQueue.Clear();
		foreach ( var npc in _staff )
		{
			if ( npc.Kind != EmployeeKind.Intern ) continue;
			if ( npc.MonthsRemaining < 0 )         continue;     // shouldn't happen
			npc.MonthsRemaining--;
			if ( npc.MonthsRemaining <= 0 )
				_fireQueue.Add( npc );
		}

		foreach ( var npc in _fireQueue )
		{
			Log.Info( $"[HR] {cal}: intern {npc.EmployeeName} contract ended." );
			Notifications.Push( "Internship Ended",
				$"{npc.EmployeeName}'s contract is up. They've left the studio.",
				"info", duration: 6f );

			// Decide if they'll come back. The very first intern is guaranteed
			// to return so the player sees the alumni payoff at least once;
			// subsequent interns roll a 60 % return chance.
			bool guaranteed = !_firstInternHired;
			bool willReturn = guaranteed || _rng.NextDouble() < 0.60;
			if ( willReturn )
			{
				_alumni.Add( new InternAlumnus
				{
					Name              = npc.EmployeeName,
					Role              = npc.Role,
					MonthsUntilReturn = InternReturnCooldownMonths,
					GuaranteedReturn  = guaranteed,
				} );
			}
			_firstInternHired = true;

			FireInternal( npc );
		}
	}

	/// Tick alumni cooldowns. Anyone whose timer hits zero re-enters the
	/// inbox as a returning intern (Senior tier, max abilities, discounted
	/// salary). Skips the injection when the inbox is full or there's
	/// already an active intern (one-slot rule still applies).
	void AdvanceAlumni( string cal )
	{
		for ( int i = _alumni.Count - 1; i >= 0; i-- )
		{
			var a = _alumni[i];
			a.MonthsUntilReturn--;
			if ( a.MonthsUntilReturn > 0 ) continue;

			// Cooldown elapsed — try to slot them back into the inbox.
			if ( _applicants.Count >= 8 || HasActiveIntern )
			{
				// No room right now. Push the cooldown out one more month
				// and try again next pay-day.
				a.MonthsUntilReturn = 1;
				continue;
			}

			var returner = Employee.GenerateSpecialApplicant(
				_rng,
				kind:              EmployeeKind.Intern,
				jobQuality:        GetJobQuality(),
				progressionFactor: GetProgressionFactor(),
				isReturningIntern: true,
				forceRole:         a.Role );

			// Same name as before — alumni return as themselves.
			var named = new Employee
			{
				Name              = a.Name,
				Age               = returner.Age,
				Role              = returner.Role,
				Tier              = returner.Tier,
				Stats             = returner.Stats,
				Abilities         = returner.Abilities,
				Salary            = returner.Salary,
				AppearanceSeed    = returner.AppearanceSeed,
				Kind              = EmployeeKind.Intern,
				IsReturningIntern = true,
			};
			_applicants.Add( named );
			_alumni.RemoveAt( i );

			Notifications.Push( "Alumni Return",
				$"{a.Name} is back — now Senior with stronger stats and lower asking salary.",
				"success", duration: 8f );
			Log.Info( $"[HR] {cal}: returning intern {a.Name} re-applied." );
		}
	}

	void PayPostingFee( GameManager gm, string cal )
	{
		var info = PostingInfo;
		if ( info.MonthlyCost <= 0 ) return;

		// Always pay the fee — money goes negative if the studio can't
		// cover it. Same pattern as salary: the listing stays active and
		// the player can see their debt grow until they switch it off.
		bool wasAffordable = gm.Money >= info.MonthlyCost;
		gm.Money -= info.MonthlyCost;

		if ( wasAffordable )
		{
			Notifications.Push( "Posting paid",
				$"{info.Name} — ${info.MonthlyCost:N0}",
				"success", duration: 6f );
			Log.Info( $"[HR] {cal}: paid {info.Name} posting fee ${info.MonthlyCost:N0}." );
		}
		else
		{
			Notifications.Push( "Posting paid from debt",
				$"{info.Name} — ${info.MonthlyCost:N0}. Studio at ${gm.Money:N0}.",
				"danger", duration: 8f );
			Log.Info( $"[HR] {cal}: paid {info.Name} posting fee ${info.MonthlyCost:N0} from debt. " +
			          $"Money now ${gm.Money:N0}." );
		}
	}

	void PaySalaries( GameManager gm, string cal )
	{
		_fireQueue.Clear();

		int today = gm.Day;

		foreach ( var npc in _staff )
		{
			// Only pay employees whose anniversary day is today. HireDay = 0
			// shouldn't occur post-rework (HireInterviewSubject stamps it,
			// RehireFromSave backfills legacy 0 → 1), but treat as "pay on
			// day 1" defensively if it ever leaks through.
			int payday = npc.HireDay > 0 ? npc.HireDay : 1;
			if ( payday != today ) continue;

			// New hires get one free anniversary-tick before payroll touches
			// them. HireInterviewSubject sets SkipFirstSalary=true; the very
			// next time their HireDay rolls around we clear the flag and skip
			// payment, so payroll starts on the SECOND anniversary (one full
			// month after hire). Without this, hiring on day 5 would charge
			// salary the same day.
			if ( npc.SkipFirstSalary )
			{
				npc.SkipFirstSalary = false;
				Log.Info( $"[HR] {cal}: skipping first salary for {npc.EmployeeName} (just hired)." );
				continue;
			}

			// Salary is always paid — money goes negative if the studio
			// can't cover it. Morale still drops on debt-funded payments
			// so the existing quit-spiral pressure stays intact.
			bool wasAffordable = gm.Money >= npc.Salary;
			gm.Money -= npc.Salary;

			if ( wasAffordable )
			{
				npc.Morale = Math.Min( 1f, npc.Morale + 0.05f );
				Notifications.Push( "Salary paid",
					$"{npc.EmployeeName} — ${npc.Salary:N0}",
					"success", duration: 6f );
			}
			else
			{
				npc.Morale = Math.Max( 0f, npc.Morale - MissedPayMoralePenalty );
				Log.Info( $"[HR] {cal}: paid {npc.EmployeeName} from debt " +
				          $"(${npc.Salary:N0}/mo). Money now ${gm.Money:N0}, morale → {npc.Morale:P0}" );

				Notifications.Push( "Salary paid from debt",
					$"{npc.EmployeeName} — ${npc.Salary:N0}. Studio at ${gm.Money:N0}.",
					"danger", duration: 8f );

				if ( npc.Morale <= QuitMoraleThreshold )
					_fireQueue.Add( npc );
			}
		}

		foreach ( var npc in _fireQueue )
		{
			Log.Info( $"[HR] {npc.EmployeeName} quit due to unpaid wages ({cal}, slot {npc.DeskSlotId})." );
			Notifications.Push( "Quit",
				$"{npc.EmployeeName} left over unpaid wages.", "warning" );
			FireInternal( npc );
		}
	}

	// ── Desk assignment ──────────────────────────────────────────────────────

	/// Returns (slotId, slot) for the first placed Desk not occupied by any
	/// hire, or null if the office is full. Iteration order matches
	/// InventoryManager.PlacedOf(Desk) — deterministic by item insertion order.
	(System.Guid slotId, PlacementSlot slot)? FindFreeDeskSlot()
	{
		var inv = InventoryManager.Instance;
		if ( inv is null ) return null;

		foreach ( var (item, slot) in inv.PlacedOf( ItemKind.Desk ) )
		{
			bool taken = false;
			foreach ( var npc in _staff )
			{
				if ( npc.DeskSlotId == slot.Id ) { taken = true; break; }
			}
			if ( !taken ) return (slot.Id, slot);
		}
		return null;
	}

	// ── Interview costing ────────────────────────────────────────────────────

	/// Energy required to interview this applicant.
	/// Scales with applicant tier (better candidates take longer to vet) and
	/// with how far the studio has progressed in time (later years = more
	/// thorough hiring process).
	public static int InterviewEnergyCost( Employee a )
	{
		if ( a == null ) return 0;
		int yearsIn      = Math.Max( 0, (GameManager.Instance?.Year ?? 2026) - 2026 );
		const int Base   = 5;
		int tierCost     = a.Tier * 4;       // 0 / 4 / 8 for jr / mid / sr
		int progressCost = yearsIn;          // +1 per year played
		return Base + tierCost + progressCost;
	}

	// ── Public actions ───────────────────────────────────────────────────────

	/// Switch the active job posting. Fails (no-op) if the tier hasn't been
	/// unlocked at the current in-game year.
	public bool TrySetPostingTier( JobPostingTier tier )
	{
		if ( !JobPosting.IsUnlocked( tier ) )
		{
			var locked = JobPosting.Get( tier );
			var ach    = locked.RequiredAchievement is { } id ? Achievements.Get( id ) : null;
			Notifications.Push( "Locked",
				ach is null
					? $"{locked.Name} is not yet available."
					: $"{locked.Name} unlocks via \"{ach.Name}\".",
				"warning" );
			return false;
		}

		if ( PostingTier == tier ) return false;

		PostingTier = tier;
		var info = JobPosting.Get( tier );
		Notifications.Push( "Job Posting",
			info.MonthlyCost > 0
				? $"Now using {info.Name} (${info.MonthlyCost:N0}/mo)."
				: $"Now using {info.Name}.",
			"info" );
		return true;
	}

	/// Begin interviewing an applicant. Spends energy upfront — the actual
	/// hire/pass decision happens in HireInterviewSubject / PassOnInterviewSubject
	/// after the player sees the candidate's stats.
	public bool TryStartInterview( Employee applicant )
	{
		if ( applicant == null ) return false;
		if ( !_applicants.Contains( applicant ) ) return false;

		if ( InterviewSubject != null )
		{
			Notifications.Push( "Already Interviewing",
				$"Finish your interview with {InterviewSubject.Name} first.", "warning" );
			return false;
		}

		// Block interviewing entirely if there's no desk to seat a hire
		// at — the interview spends energy and burns the applicant; we
		// don't want the player to pay that cost only to have the hire
		// step refuse them. They need to buy/free a desk first.
		if ( !HasFreeDesk )
		{
			Notifications.Push( "No free desk",
				$"Buy another desk before interviewing — the office is full ({_staff.Count}/{MaxDesks}).",
				"warning" );
			return false;
		}

		var gm = GameManager.Instance;
		int cost = InterviewEnergyCost( applicant );
		if ( gm is null || !gm.TrySpendEnergy( cost ) )
		{
			Notifications.Push( "Not enough energy",
				$"Need {cost} ⚡ to interview {applicant.Name}.", "warning" );
			return false;
		}

		InterviewSubject = applicant;
		_applicants.Remove( applicant );

		// Reveal a random subset of main stats. The rest stay hidden until
		// either the future questionnaire feature lets the player buy more
		// reveals, or they hire and find out the hard way.
		RevealRandomStats( applicant, InitialRevealedStats );

		// Treat the interview like a shop: close the in-game menu so the
		// reveal modal owns the screen by itself. Player lock stays applied
		// via GameManager's IsInterviewing check.
		GameMenu.Instance?.SetOpen( false );
		GameManager.RefreshPlayerLock();

		Log.Info( $"[HR] Interview started with {applicant.Name} ({cost} energy). " +
			$"Revealed {applicant.RevealedStats.Count} stat(s)." );
		return true;
	}

	/// Reveal up to <paramref name="count"/> random unrevealed main stats on
	/// <paramref name="applicant"/>. Idempotent — re-running with already-
	/// revealed stats just tops up to the requested count.
	void RevealRandomStats( Employee applicant, int count )
	{
		if ( applicant == null || count <= 0 ) return;
		var all = new List<MainStat>
		{
			MainStat.Programming, MainStat.Design,    MainStat.Creativity,
			MainStat.Artistry,    MainStat.Sound,     MainStat.Focus,
		};
		// Filter to ones we haven't shown yet, then take up to N at random.
		all.RemoveAll( s => applicant.RevealedStats.Contains( s ) );
		for ( int i = 0; i < all.Count; i++ )
		{
			int j = _rng.Next( i, all.Count );
			(all[i], all[j]) = (all[j], all[i]);
		}
		foreach ( var s in all.Take( count ) )
			applicant.RevealedStats.Add( s );
	}

	/// Discard an applicant from the inbox without interviewing them.
	/// Tutorial hires can't be rejected — the player has to interview and
	/// hire them to clear the first-hire gate. Mirrors PassOnInterviewSubject.
	public void Reject( Employee applicant )
	{
		if ( applicant?.IsTutorialHire == true )
		{
			Notifications.Push( "First hire required",
				$"You have to hire {applicant.Name} — they're your starting teammate.",
				"warning" );
			return;
		}
		_applicants.Remove( applicant );
	}

	/// Hire the current interview subject.
	/// Clones the prefab at the chosen desk and adds the spawned NPC to
	/// <see cref="Staff"/>. The first month's salary is deferred — see
	/// <see cref="EmployeeNPC.SkipFirstSalary"/> — so hiring itself is free
	/// and payroll starts on the second month-tick after hire. Returns
	/// false (and does nothing) if any prerequisite fails.
	public bool HireInterviewSubject()
	{
		if ( InterviewSubject == null ) return false;

		if ( EmployeeNpcPrefab is null )
		{
			Notifications.Push( "Hire failed",
				"No citizen prefab assigned to HRManager.", "warning" );
			return false;
		}

		// One-intern-at-a-time rule. PickApplicantKind already blocks new
		// intern applicants from spawning when the slot is full, but the
		// inbox can carry over interns from a previous cycle, so enforce
		// the rule again at hire time.
		if ( InterviewSubject.Kind == EmployeeKind.Intern && HasActiveIntern )
		{
			Notifications.Push( "Hire failed",
				"You already have an intern on staff. Wait until their contract ends.",
				"warning" );
			return false;
		}

		bool needsDesk = SpecialHires.TakesDesk( InterviewSubject.Kind );

		// Desk-taking hires (Regular, Intern) need a placed Desk with no
		// existing occupant. Non-desk kinds (Mentor / MarketingAgent /
		// RemoteWorker) skip this check entirely and spawn at the HRManager's
		// transform — they're "remote" / "off-floor" by design.
		System.Guid?  deskSlotId = null;
		PlacementSlot deskSlot   = null;
		if ( needsDesk )
		{
			var pick = FindFreeDeskSlot();
			if ( pick is null )
			{
				Notifications.Push( "Hire failed",
					"No free desks. Buy a Desk in the Shop or expand the office.",
					"warning" );
				return false;
			}
			deskSlotId = pick.Value.slotId;
			deskSlot   = pick.Value.slot;
		}

		var gm = GameManager.Instance;
		if ( gm == null )
		{
			Notifications.Push( "Hire failed",
				"GameManager is missing from the scene.", "warning" );
			return false;
		}

		// First salary is deferred — see EmployeeNPC.SkipFirstSalary. Hiring
		// itself costs nothing up-front; PaySalaries skips the new hire on
		// the next month-tick and starts charging the cycle after.

		// Desk pose: where the NPC ultimately works. Prefer the slot's
		// SitSpot if assigned (chair-seat-accurate sitting); fall back to
		// the slot's own transform; final fallback is HRManager's transform
		// for non-desk-takers (Mentor / MarketingAgent / RemoteWorker).
		Vector3  deskPos = deskSlot?.SitSpot?.WorldPosition
		                 ?? deskSlot?.WorldPosition
		                 ?? WorldPosition;
		Rotation deskRot = deskSlot?.SitSpot?.WorldRotation
		                 ?? deskSlot?.WorldRotation
		                 ?? WorldRotation;

		// Initial clone position: the inspector-assigned spawn point if
		// present, otherwise the desk itself. With a spawn point set, the
		// NPC appears there and the state machine walks them to their desk
		// on the first tick.
		Vector3  cloneAtPos = EmployeeSpawnPoint?.WorldPosition ?? deskPos;
		Rotation cloneAtRot = EmployeeSpawnPoint?.WorldRotation ?? deskRot;

		var hireT0 = RealTime.Now;
		var go      = EmployeeNpcPrefab.Clone( cloneAtPos, cloneAtRot );
		var cloneMs = (RealTime.Now - hireT0) * 1000f;
		var npc     = go.Components.Get<EmployeeNPC>();
		if ( npc is null )
		{
			go.Destroy();
			Notifications.Push( "Hire failed",
				"Citizen prefab is missing the EmployeeNPC component.", "warning" );
			return false;
		}

		InterviewSubject.DeskSlotId = deskSlotId;
		npc.Assign( InterviewSubject, deskPos, deskRot );
		var assignMs = (RealTime.Now - hireT0) * 1000f;
		Log.Info( $"[HR-DEBUG] Hire {npc.EmployeeName}: Clone {cloneMs:F1}ms · Clone+Assign {assignMs:F1}ms" );
		// Stamp the start time on the NPC so the deferred Randomize block in
		// EmployeeNPC.OnUpdate can log the wall-clock spawn-to-ready elapsed
		// once the wardrobe finishes loading on the next frame.
		npc._spawnDebugStartTime = hireT0;
		// Stamp the day-of-month so this hire's payday is their anniversary,
		// not Day 1 of every month. Falls back to 1 if GameManager is missing
		// (defensive — the modal can't open without one in a healthy scene).
		// First salary lands on the very next anniversary (= 30 in-game days
		// after hire, since months are 30 days). SkipFirstSalary stays false
		// so PaySalaries charges normally on the first anniversary tick.
		npc.HireDay = GameManager.Instance?.Day ?? 1;
		_staff.Add( npc );

		// First-launch tutorial: completing this hire ends the tutorial,
		// unfreezes time, and unlocks every menu tile / sub-tab. No-op past
		// the first hire (TutorialManager early-returns once Complete).
		TutorialManager.Instance?.NotifyFirstHire();

		// Apply per-kind contract state. Today only Intern has one — they get
		// a 2-month timer that AdvanceInternships ticks down each month-start.
		if ( npc.Kind == EmployeeKind.Intern )
			npc.MonthsRemaining = InternContractMonths;

		// Lifetime stats / unlocks.
		Achievements.RecordHire( npc );

		var deskLabel = InventoryManager.Instance?.LabelForDeskSlot( deskSlotId ) ?? "—";
		var deskNote  = needsDesk ? $"starts at {deskLabel}" : "starts (remote)";
		Notifications.Push( "Hired",
			$"{npc.EmployeeName} {deskNote}.", "success" );
		Log.Info( $"[HR] Hired {npc.EmployeeName} ({npc.Kind}) — {deskNote}." );
		InterviewSubject = null;
		return true;
	}

	/// Decline to hire the current interviewee. Energy spent on the interview
	/// is not refunded. The tutorial first hire (<see cref="Employee.IsTutorialHire"/>)
	/// can't be passed on — the panel hides Pass for them, and this method
	/// no-ops as a defensive guard.
	public void PassOnInterviewSubject()
	{
		if ( InterviewSubject == null ) return;
		if ( InterviewSubject.IsTutorialHire )
		{
			Notifications.Push( "First hire required",
				$"You have to hire {InterviewSubject.Name} — they're your starting teammate.",
				"warning" );
			return;
		}
		Log.Info( $"[HR] Passed on {InterviewSubject.Name}." );
		Notifications.Push( "Passed", $"You passed on {InterviewSubject.Name}.", "info" );
		InterviewSubject = null;
	}

	/// Fire an employee. Their desk is freed and the NPC is hidden immediately.
	public void Fire( EmployeeNPC npc )
	{
		Log.Info( $"[HR] Fired {npc.EmployeeName} (slot {npc.DeskSlotId})." );
		Notifications.Push( "Fired", $"{npc.EmployeeName} has left the studio.", "warning" );
		FireInternal( npc );
		// TODO: morale penalty to remaining staff.
	}

	void FireInternal( EmployeeNPC npc )
	{
		_staff.Remove( npc );
		// The NPC was spawned for this hire, so it goes when they go. No
		// pool to return it to, no desk to vacate beyond the staff count.
		npc.GameObject.Destroy();
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public HRSave Save()
	{
		var dto = new HRSave
		{
			FirstInternHired = _firstInternHired,
			PostingTier      = PostingTier,
			ApplicantTimer   = _applicantTimer,
		};

		foreach ( var npc in _staff )
			dto.Staff.Add( SaveEmployee( npc ) );

		foreach ( var a in _applicants )
			dto.Applicants.Add( SaveApplicant( a ) );

		foreach ( var a in _alumni )
		{
			dto.Alumni.Add( new InternAlumnusSave
			{
				Name              = a.Name,
				Role              = a.Role,
				MonthsUntilReturn = a.MonthsUntilReturn,
				GuaranteedReturn  = a.GuaranteedReturn,
			} );
		}

		return dto;
	}

	public void Load( HRSave dto )
	{
		// Drop any in-flight interview pointer up-front — the applicant it
		// references is about to be cleared and rebuilt, and PassOn refuses
		// to clear the tutorial hire so we can't go through that path.
		InterviewSubject = null;

		// Tear the existing studio down. Every spawned NPC's GameObject was
		// born of a prefab clone — destroying releases its visuals. Internal
		// state (alumni, applicants, posting) is just C# objects.
		for ( int i = _staff.Count - 1; i >= 0; i-- )
			_staff[i].GameObject.Destroy();
		_staff.Clear();
		_applicants.Clear();
		_alumni.Clear();

		if ( dto is null )
		{
			_firstInternHired = false;
			PostingTier       = JobPostingTier.BulletinBoard;
			_applicantTimer   = 0f;
			return;
		}

		_firstInternHired = dto.FirstInternHired;
		PostingTier       = dto.PostingTier;
		_applicantTimer   = dto.ApplicantTimer;

		foreach ( var save in dto.Applicants )
			_applicants.Add( ApplicantFromSave( save ) );

		foreach ( var save in dto.Alumni )
		{
			_alumni.Add( new InternAlumnus
			{
				Name              = save.Name,
				Role              = save.Role,
				MonthsUntilReturn = save.MonthsUntilReturn,
				GuaranteedReturn  = save.GuaranteedReturn,
			} );
		}

		foreach ( var save in dto.Staff )
			RehireFromSave( save );
	}

	static EmployeeSave SaveEmployee( EmployeeNPC npc ) => new()
	{
		Name                    = npc.EmployeeName,
		Age                     = npc.Age,
		Role                    = npc.Role,
		Kind                    = npc.Kind,
		Salary                  = npc.Salary,
		HireDay                 = npc.HireDay,
		SkipFirstSalary         = npc.SkipFirstSalary,
		DeskSlotId              = npc.DeskSlotId,
		MonthsRemaining         = npc.MonthsRemaining,
		WasReturningIntern      = npc.WasReturningIntern,
		Ability1                = npc.Ability1,
		Ability2                = npc.Ability2,
		Stats                   = npc.Stats,
		Morale                  = npc.Morale,
		ActiveTraining          = npc.ActiveTraining,
		ResearchTopicId         = npc.ResearchTopicId,
		DaysIntoCurrentResearch = npc.DaysIntoCurrentResearch,
		BadMoodImmuneUntilTotalDay = npc.BadMoodImmuneUntilTotalDay,
		EnergyDrinkUntilTotalDay   = npc.EnergyDrinkUntilTotalDay,
		AppearanceSeed          = npc.AppearanceSeed,
		SavedClothing           = new List<Sandbox.ClothingContainer.ClothingEntry>( npc.SavedClothing ?? new List<Sandbox.ClothingContainer.ClothingEntry>() ),
	};

	static ApplicantSave SaveApplicant( Employee a ) => new()
	{
		Name              = a.Name,
		Age               = a.Age,
		Role              = a.Role,
		Kind              = a.Kind,
		Tier              = a.Tier,
		Salary            = a.Salary,
		SalaryBias        = a.SalaryBias,
		Abilities         = a.Abilities is null ? new() : new List<EmployeeAbility>( a.Abilities ),
		RevealedStats     = new List<MainStat>( a.RevealedStats ),
		Stats             = a.Stats,
		IsReturningIntern = a.IsReturningIntern,
		IsTutorialHire    = a.IsTutorialHire,
		AppearanceSeed    = a.AppearanceSeed,
	};

	static Employee ApplicantFromSave( ApplicantSave s )
	{
		var e = new Employee
		{
			Name              = s.Name,
			Age               = s.Age,
			Role              = s.Role,
			Kind              = s.Kind,
			Tier              = s.Tier,
			Stats             = s.Stats ?? new EmployeeStats(),
			Abilities         = (s.Abilities ?? new List<EmployeeAbility>()).AsReadOnly(),
			Salary            = s.Salary,
			SalaryBias        = s.SalaryBias,
			AppearanceSeed    = s.AppearanceSeed,
			IsReturningIntern = s.IsReturningIntern,
			IsTutorialHire    = s.IsTutorialHire,
		};
		foreach ( var st in s.RevealedStats ) e.RevealedStats.Add( st );
		return e;
	}

	/// Spawn an NPC from a save DTO and add to <see cref="Staff"/>. Bypasses
	/// the interview / energy / posting flow and the achievement record —
	/// achievements are restored separately, and rehiring during a load isn't
	/// a "new hire" event.
	void RehireFromSave( EmployeeSave save )
	{
		if ( EmployeeNpcPrefab is null )
		{
			Log.Warning( "[HR] Can't rehire from save: no citizen prefab assigned." );
			return;
		}

		// Resolve desk pose if the saved hire had a desk. Non-desk kinds
		// (Mentor / MarketingAgent / RemoteWorker) fall back to HRManager's
		// transform same as fresh hires.
		PlacementSlot deskSlot = null;
		if ( save.DeskSlotId is { } id )
			deskSlot = InventoryManager.Instance?.SlotById( id );

		Vector3  deskPos = deskSlot?.SitSpot?.WorldPosition
		                 ?? deskSlot?.WorldPosition
		                 ?? WorldPosition;
		Rotation deskRot = deskSlot?.SitSpot?.WorldRotation
		                 ?? deskSlot?.WorldRotation
		                 ?? WorldRotation;

		// Skip the spawn-point hop. The fresh-hire flow uses EmployeeSpawnPoint
		// as a "walk-in" animation; on save load we drop the NPC straight at
		// their desk because they were already there.
		var go  = EmployeeNpcPrefab.Clone( deskPos, deskRot );
		var npc = go.Components.Get<EmployeeNPC>();
		if ( npc is null )
		{
			go.Destroy();
			Log.Warning( $"[HR] Can't rehire {save.Name}: prefab missing EmployeeNPC component." );
			return;
		}

		// Build a synthetic Employee so we can reuse Assign() — the existing
		// path already wires up name / stats / abilities / desk pose.
		var ability1 = save.Ability1;
		var ability2 = save.Ability2;
		var abilities = new List<EmployeeAbility>();
		if ( ability1 is { } a1 ) abilities.Add( a1 );
		if ( ability2 is { } a2 ) abilities.Add( a2 );

		var synth = new Employee
		{
			Name           = save.Name,
			Age            = save.Age,
			Role           = save.Role,
			Tier           = 1,                              // tier is generation-only metadata
			Kind           = save.Kind,
			Stats          = save.Stats ?? new EmployeeStats(),
			Abilities      = abilities.AsReadOnly(),
			Salary         = save.Salary,
			SalaryBias     = 1f,
			AppearanceSeed = save.AppearanceSeed,
			DeskSlotId     = save.DeskSlotId,
		};

		// Pre-populate SavedClothing BEFORE Assign so the Dresser is
		// restored from the saved outfit instead of rolling new clothes.
		// (Assign → ApplyAppearance reads SavedClothing to decide whether
		// to deferred-Randomize or replay the captured list.)
		npc.SavedClothing = new List<Sandbox.ClothingContainer.ClothingEntry>(
			save.SavedClothing ?? new List<Sandbox.ClothingContainer.ClothingEntry>() );

		npc.Assign( synth, deskPos, deskRot );

		// Assign() resets Morale to 1 and clears CurrentTask; restore the
		// runtime fields it doesn't know about.
		npc.Morale                  = save.Morale;
		// Legacy saves (pre-anniversary rework) wrote HireDay = 0. Backfill
		// to 1 so they preserve the old "everyone paid on day 1" cadence.
		// New saves carry the actual day-of-hire.
		npc.HireDay                 = save.HireDay > 0 ? save.HireDay : 1;
		npc.SkipFirstSalary         = save.SkipFirstSalary;
		npc.MonthsRemaining         = save.MonthsRemaining;
		npc.WasReturningIntern      = save.WasReturningIntern;
		npc.ActiveTraining          = save.ActiveTraining;
		npc.ResearchTopicId         = save.ResearchTopicId ?? "";
		npc.DaysIntoCurrentResearch = save.DaysIntoCurrentResearch;
		npc.BadMoodImmuneUntilTotalDay = save.BadMoodImmuneUntilTotalDay;
		npc.EnergyDrinkUntilTotalDay   = save.EnergyDrinkUntilTotalDay;

		_staff.Add( npc );
	}
}
