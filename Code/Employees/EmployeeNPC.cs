/// <summary>
/// The source of truth for an employee's data — lives on the citizen prefab.
/// Every stat, salary, ability, and morale value is a [Property] field, so
/// they're inspectable on the spawned NPC at runtime.
///
/// Setup: build a single citizen prefab with a SkinnedModelRenderer + this
/// component, drop <see cref="PlacementSlot"/> markers (Kind = Desk) in the
/// scene at every desk position, and assign the prefab to
/// <c>HRManager.EmployeeNpcPrefab</c>. On hire
/// the prefab is cloned at the chosen desk's transform and <see cref="Assign"/>
/// fills in the data.
///
/// Behaviour: a small state machine cycles between Working / Training /
/// Distracted, picks a target position (desk, training anchor, break anchor),
/// and walks the NPC there. Higher Focus stat reduces the chance of going
/// Distracted.
/// </summary>
public sealed class EmployeeNPC : Component, IDevWorker
{
	// ── IDevWorker ────────────────────────────────────────────────────────────
	// EmployeeNPC's data model already matches the interface for everything
	// except <see cref="EmployeeName"/> (mapped to Name) and <see cref="IsPlayer"/>
	// (always false — there's a separate PlayerStats type for the founder).

	string IDevWorker.Name => EmployeeName;
	public bool       IsPlayer => false;


	// ── Identity ──────────────────────────────────────────────────────────────
	// IsAssigned = EmployeeName is not empty.

	[Property] public string       EmployeeName { get; set; } = "";
	[Property] public int          Age          { get; set; }
	[Property] public EmployeeRole Role         { get; set; }

	/// What "shape" of hire this NPC is — carried across from the
	/// <see cref="Employee"/> applicant. Most are <see cref="EmployeeKind.Regular"/>.
	[Property] public EmployeeKind Kind         { get; set; } = EmployeeKind.Regular;

	// ── Employment ────────────────────────────────────────────────────────────

	/// Monthly wage deducted on this NPC's <see cref="HireDay"/> each month.
	[Property] public long Salary   { get; set; }

	/// Day-of-month (1..30) this NPC was hired. HRManager pays their salary
	/// on this same day every in-game month, so each employee has their own
	/// payday rather than everyone draining the bank on Day 1. Legacy saves
	/// (pre-anniversary rework) load with <c>0</c> and HRManager backfills
	/// to <c>1</c> on first load — preserving the old "all salaries on day 1"
	/// behavior for already-hired staff.
	[Property] public int HireDay { get; set; }

	/// Set to true on hire and cleared the first time HRManager.PaySalaries
	/// runs for this NPC. Gives every new hire one free month-tick before
	/// they cost the studio anything — "salary paid the month after
	/// they're hired", on their <see cref="HireDay"/> anniversary.
	[Property] public bool SkipFirstSalary { get; set; }

	/// Which desk this NPC occupies, identified by the
	/// <see cref="PlacementSlot.Id"/> of their placed Desk. Null = not yet
	/// seated, or the kind doesn't take a desk (Mentor / MarketingAgent /
	/// RemoteWorker).
	[Property] public System.Guid? DeskSlotId { get; set; } = null;

	/// Months left on the contract before HRManager auto-fires this NPC.
	/// -1 = permanent hire (the default — every <see cref="EmployeeKind.Regular"/>
	/// employee). Currently used only for <see cref="EmployeeKind.Intern"/>
	/// (2-month contract, set by <see cref="HRManager.HireInterviewSubject"/>).
	[Property] public int  MonthsRemaining { get; set; } = -1;

	/// True when this hire was a returning intern (alumni). Cosmetic-only
	/// today — lets the HR Stats UI show a "RETURNED" badge instead of
	/// "INTERN" so the player remembers who's who.
	[Property] public bool WasReturningIntern { get; set; }

	// ── Abilities (max 2) ─────────────────────────────────────────────────────
	// Null = slot is empty. Inspectable as a dropdown in the editor.

	[Property] public EmployeeAbility? Ability1 { get; set; }
	[Property] public EmployeeAbility? Ability2 { get; set; }

	/// Deterministic seed driving this NPC's body sliders — same seed →
	/// same age / height / tint every time <see cref="ApplyAppearance"/>
	/// runs. Captured from the source <see cref="Employee"/> at hire time
	/// so save/load can reapply the exact same body shape.
	[Property] public int AppearanceSeed { get; set; }

	/// Snapshot of the <c>Sandbox.Dresser.Clothing</c> list, captured the
	/// first frame after Randomize runs. <c>Dresser.Randomize()</c> is
	/// unseeded so we can't reproduce the same outfit by replaying the
	/// seed; instead we save the resulting clothing list and restore it
	/// directly on rehire. Empty list = "this NPC hasn't been dressed yet"
	/// (fresh hire on the next frame); non-empty = "restore from this
	/// snapshot instead of rolling new clothes".
	[Property] public List<Sandbox.ClothingContainer.ClothingEntry> SavedClothing { get; set; } = new();

	// ── Stats (FIFA-style: each main stat = avg of 3 sub-stats, all 1–1000) ───

	[Property] public EmployeeStats Stats { get; set; } = new();

	// ── Runtime state ────────────────────────────────────────────────────────

	/// 0–1 multiplier on all effective stat contributions.
	/// Drops when salary is missed; recovers on payment.
	[Property] public float         Morale      { get; set; } = 1f;

	// ── Training state (IDevWorker) ──────────────────────────────────────────
	// `[Property]` so the values survive a scene save and can be inspected.

	/// Off-site session or null. While non-null, <see cref="TrainingManager"/>
	/// hides the GameObject and the worker contributes 0 to projects.
	[Property] public TrainingSession ActiveTraining { get; set; }

	/// Currently-assigned <see cref="ResearchTopic.Id"/>, or empty/null when
	/// the worker isn't researching. While set (and not off-site), the
	/// worker contributes to projects at
	/// <see cref="TrainingManager.ResearchProductivityFactor"/>.
	[Property] public string          ResearchTopicId { get; set; } = "";

	[Property] public float           DaysIntoCurrentResearch { get; set; }

	/// Current behaviour state (Working / Training / Distracted / Idle).
	/// Driven by the state machine; assignable in the inspector for debugging.
	[Property] public EmployeeState State       { get; set; } = EmployeeState.Idle;

	/// Human-readable label mirrored from <see cref="State"/>; useful for HUDs.
	[Property] public string        CurrentTask { get; set; } = "";

	// ── Mood (per-project, drives chat flow + pillar contribution) ───────────

	/// Current mood. Only changes during a project's Production phase; resets
	/// to <see cref="EmployeeMood.Neutral"/> the moment the project leaves
	/// Production (or ships). See <see cref="TickMood"/> for the roll cadence.
	[Property] public EmployeeMood Mood { get; set; } = EmployeeMood.Neutral;

	/// Idea this NPC is currently pitching while in <see cref="EmployeeMood.Good"/>.
	/// Null in any other mood. Captured by <see cref="EmployeeInteractor"/> at
	/// chat-open time so accept/decline still works even if mood resets mid-chat.
	[Property] public EmployeeSuggestion PendingSuggestion { get; set; }

	/// Absolute "total day" index past which this NPC's Energy Drink boost
	/// expires. While active, P(Good) per tick is 50% higher (e.g. 3.5% →
	/// 5.25% at 5 hires). 0 = no boost ever granted — safe sentinel since
	/// real game time always lives at year 2026+ which puts
	/// <see cref="TotalDayNow"/> in the hundreds of thousands.
	[Property] public int EnergyDrinkUntilTotalDay { get; set; }

	/// Optional child GameObject hosting a "lightbulb / sparkle" particle.
	/// Toggled on while in <see cref="EmployeeMood.Good"/>, off otherwise.
	/// Wire via the bob.prefab inspector — disable the child by default so
	/// it doesn't emit on spawn.
	[Property] public GameObject GoodMoodFx { get; set; }

	float _moodTickTimer;

	/// Number of remaining mood ticks during which this NPC is exempt
	/// from new mood rolls. Set to 2 in <see cref="SetMood"/> whenever
	/// mood transitions from Good back to Neutral (player accepted /
	/// declined the suggestion, OR production ended). Decremented per
	/// <c>MoodTickInterval</c> tick inside <see cref="TickMood"/>.
	/// Ephemeral — not persisted in the save (mood resets to Neutral on
	/// load anyway when production isn't active).
	int _moodResolutionCooldown;

	/// True only while the NPC is actively at their desk producing work.
	public bool IsWorking => State == EmployeeState.Working;

	// ── State-machine tuning ──────────────────────────────────────────────────

	/// Units per second when walking between desk / anchors.
	[Property] public float WalkSpeed        { get; set; } = 80f;

	/// Vertical translate applied to the NPC's transform on arrival
	/// when seated, so the citizen sits flush on the chair seat instead
	/// of with feet through it. Hardcoded (not [Property]) because
	/// s&box prefab serialisation does not auto-add new fields to
	/// existing saved prefabs. Edit this value to tune; +20 to +35 is
	/// typical for citizen.vmdl at scale 1.5×.
	const float SitVerticalLift = 10f;

	/// How long the NPC stays in any single state (random in this range).
	[Property] public float MinStateDuration { get; set; } = 12f;
	[Property] public float MaxStateDuration { get; set; } = 30f;

	// ── Derived (read-only, not inspectable) ─────────────────────────────────

	public bool IsAssigned => !string.IsNullOrEmpty( EmployeeName );

	public bool HasAbility( EmployeeAbility a ) => Ability1 == a || Ability2 == a;

	/// Applies Morale as a multiplier to a stat block's average, after a flat
	/// add from workstation gear (chair / computer / monitor at this hire's
	/// desk) and studio-wide furniture (lounge stuff, books, etc.). Clamped
	/// 1–1000. Boost numbers come from <see cref="InventoryCatalogue.Entry.StatBoost"/>;
	/// they are intentionally hidden from the UI — the player just feels the
	/// difference as their team performs better.
	public int EffectiveStat( StatBlock block )
	{
		int boost = WorkstationStatBoost
		          + (InventoryManager.Instance?.StudioWideStatBoost ?? 0);
		return StatBlock.Clamp( (int)((block.Average + boost) * Morale) );
	}

	/// Sum of <see cref="InventoryCatalogue.Entry.StatBoost"/> across the
	/// chair / computer / monitor currently mounted on this hire's desk.
	/// Returns 0 for a remote (deskless) hire. Live — reads the current
	/// items at <see cref="DeskSlotId"/> every call, so reassignment
	/// (fire → hire-to-same-desk) automatically inherits the upgrades.
	public int WorkstationStatBoost
	{
		get
		{
			if ( DeskSlotId is not System.Guid id ) return 0;
			var inv = InventoryManager.Instance;
			if ( inv is null ) return 0;

			int total = 0;
			total += BoostAt( inv, id, SlotKind.Chair    );
			total += BoostAt( inv, id, SlotKind.Computer );
			total += BoostAt( inv, id, SlotKind.Monitor  );
			return total;

			static int BoostAt( InventoryManager inv, System.Guid deskId, SlotKind mount )
			{
				var item = inv.ItemAtDeskMount( deskId, mount );
				if ( item is null ) return 0;
				var entry = InventoryCatalogue.Get( item.Kind );
				return entry?.StatBoost ?? 0;
			}
		}
	}

	// ── Movement bookkeeping ─────────────────────────────────────────────────

	Vector3         _deskPos;
	Rotation        _deskRot;
	Vector3         _targetPos;
	float           _stateTimer;
	float           _stateDuration;
	readonly Random _rng = new();

	/// Cached at OnAwake — the citizen's animated mesh. Velocity and
	/// sit state are pushed to it every frame in <see cref="UpdateAnimation"/>.
	SkinnedModelRenderer _renderer;

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		_renderer = GameObject.Components.Get<SkinnedModelRenderer>();

		// Force the citizen renderer on at spawn — bob.prefab has occasionally
		// been saved with it disabled (easy slip while authoring child FX
		// nodes). The training-hide flow (HideForTraining / FinishReturnFromTraining)
		// still toggles it correctly later because that path explicitly sets
		// Enabled rather than relying on the saved state.
		if ( _renderer is not null )
			_renderer.Enabled = true;

		// Force the mood-FX children to match the spawned Mood value (which
		// defaults to Neutral → both off). Without this, whatever Enabled
		// state the children were saved with in bob.prefab leaks into runtime,
		// so an author leaving them visible while wiring causes both effects
		// to play on every fresh hire.
		ApplyMoodFx();
	}

	protected override void OnUpdate()
	{
		// Deferred clothing randomisation from ApplyAppearance — runs one
		// frame after spawn to keep the hire-click frame snappy. Snapshot
		// the resulting Dresser.Clothing list into SavedClothing so save/
		// load can restore the same outfit (Dresser.Randomize itself is
		// unseeded — replaying AppearanceSeed alone won't reproduce it).
		if ( _pendingDresserRandomize is { } d )
		{
			_pendingDresserRandomize = null;
			var rt0 = RealTime.Now;
			d.Randomize();
			SavedClothing = new List<Sandbox.ClothingContainer.ClothingEntry>( d.Clothing ?? new List<Sandbox.ClothingContainer.ClothingEntry>() );
			var randomizeMs = (RealTime.Now - rt0) * 1000f;
			if ( _spawnDebugStartTime > 0f )
			{
				var totalMs = (RealTime.Now - _spawnDebugStartTime) * 1000f;
				Log.Info( $"[HR-DEBUG] {EmployeeName}: Randomize {randomizeMs:F1}ms · Total spawn-to-ready {totalMs:F1}ms" );
				_spawnDebugStartTime = 0f;
			}
		}

		// Spawned-but-unconfigured NPCs (mid-frame between Clone and Assign)
		// shouldn't tick. Once Assign runs, IsAssigned becomes true forever
		// — Fire destroys the GameObject rather than clearing fields.
		if ( !IsAssigned ) return;

		// Non-desk hires (Mentor / MarketingAgent / RemoteWorker) live in
		// the data layer only — no walk-around behaviour. They sit wherever
		// HRManager spawned them and don't drift between desk / training /
		// break anchors.
		if ( !SpecialHires.TakesDesk( Kind ) ) return;

		// Modal pause: freeze the NPC entirely while the player is in a
		// configuration UI. No mood rolls, no state-machine transitions,
		// no movement updates. Animation poses linger at whatever blend
		// they were last set to — s&box's animation graph is engine-driven
		// and continues without OnUpdate. If poses look frozen mid-stride
		// during pause, that's the trade-off; the alternative (forcing
		// idle on every NPC) is visible UI churn at pause/unpause.
		if ( GameManager.Instance is { IsTimePaused: true } ) return;

		// Mood ticking — only meaningful during Production; cheap to call.
		TickMood();

		// Off-site training intercept: if a session is active, the NPC walks
		// to the studio's spawn point and then hides its renderer (anim
		// graph stays warm — disabling the whole GameObject was wiping the
		// SkinnedModelRenderer's locomotion + sit-blend state, which is why
		// returning hires used to slide and not sit).
		//
		// IsRealTrainingSession guards against s&box's editor auto-creating
		// a zeroed default `TrainingSession` when the prefab is re-saved —
		// previously that ghost session was triggering HideForTraining on
		// every fresh hire and disabling the renderer at spawn.
		if ( IsRealTrainingSession( ActiveTraining ) )
		{
			WalkToSpawnAndVanish();
			return;
		}

		// Returned this frame — teleport back to the spawn point and reveal
		// the renderer before the regular state machine resumes. Doing this
		// inside EmployeeNPC keeps TrainingManager out of visibility-fix
		// business entirely.
		if ( _hiddenForTraining )
			FinishReturnFromTraining();

		TickStateMachine();
		TickMovement();
	}

	/// True between arrival at the spawn point and the next return. Drives
	/// the renderer-visibility toggle (instead of disabling the whole
	/// GameObject) so the SkinnedModelRenderer's animation graph never
	/// stops ticking.
	bool _hiddenForTraining;

	/// Walks the NPC to <see cref="HRManager.EmployeeSpawnPoint"/>; once
	/// within 2 units (planar) the renderer hides and the flag flips. While
	/// hidden, this method is a no-op so the NPC stays put.
	void WalkToSpawnAndVanish()
	{
		if ( _hiddenForTraining ) return;

		var spawn = HRManager.Instance?.EmployeeSpawnPoint;
		if ( spawn is null )
		{
			// No spawn point — hide in place as a fallback.
			HideForTraining();
			return;
		}

		var spawnPos = spawn.WorldPosition;
		var current  = WorldPosition;
		var delta    = (spawnPos - current).WithZ( 0 );
		var dist     = delta.Length;

		if ( dist < 2f )
		{
			HideForTraining();
			UpdateAnimation( 0f );
			return;
		}

		var step = MathF.Min( WalkSpeed * Time.Delta, dist );
		var dir  = delta.Normal;
		WorldPosition = current + dir * step;
		if ( dir.LengthSquared > 0.0001f )
			WorldRotation = Rotation.LookAt( dir );

		UpdateAnimation( step / Time.Delta );
	}

	void HideForTraining()
	{
		_hiddenForTraining = true;
		if ( _renderer is not null )
			_renderer.Enabled = false;
	}

	/// Called the first frame after <see cref="ActiveTraining"/> clears.
	/// Re-shows the renderer and teleports the NPC back to the spawn point
	/// so the existing Working state machine walks them to their desk.
	void FinishReturnFromTraining()
	{
		_hiddenForTraining = false;
		if ( _renderer is not null )
			_renderer.Enabled = true;

		var spawn = HRManager.Instance?.EmployeeSpawnPoint;
		if ( spawn is not null )
		{
			WorldPosition = spawn.WorldPosition;
			WorldRotation = spawn.WorldRotation;
		}
	}

	// ── State machine ─────────────────────────────────────────────────────────

	void TickStateMachine()
	{
		_stateTimer += Time.Delta;
		if ( _stateTimer < _stateDuration ) return;

		_stateTimer = 0f;
		PickNextState();
	}

	void PickNextState()
	{
		// TEMPORARY (Phase 0 of design/gdd/employee-behavior.md): only
		// Working + Idle are active. Training and Distracted rolls are
		// disabled until the new sub-mode / object / chat / helping
		// behaviors are wired up. Re-enable per employee-behavior.md §3.1
		// when ready — the focus / distract-chance math previously here
		// is preserved in the GDD's F-DIST formula.
		SetState( EmployeeState.Working );
	}

	void SetState( EmployeeState s )
	{
		State          = s;
		_stateTimer    = 0f;
		_stateDuration = MathX.Lerp( MinStateDuration, MaxStateDuration,
			(float)_rng.NextDouble() );
		_targetPos     = ResolveTargetPosition( s );
		CurrentTask    = s switch
		{
			EmployeeState.Working    => "Working",
			EmployeeState.Training   => "Training",
			EmployeeState.Distracted => "Slacking off",
			_                        => "",
		};
	}

	Vector3 ResolveTargetPosition( EmployeeState s ) => s switch
	{
		EmployeeState.Training   => RandomAnchor( WorkplaceAnchorKind.Training ),
		EmployeeState.Distracted => RandomAnchor( WorkplaceAnchorKind.Break ),
		_                        => _deskPos,
	};

	Vector3 RandomAnchor( WorkplaceAnchorKind kind )
	{
		var anchors = Scene
			.GetAllComponents<WorkplaceAnchor>()
			.Where( a => a.Kind == kind )
			.ToList();

		// No marker placed → wander a short distance from the desk so the
		// state still has visible behaviour out-of-the-box.
		if ( anchors.Count == 0 )
			return _deskPos + RandomXyOffset( 96f );

		return anchors[_rng.Next( anchors.Count )].Position;
	}

	Vector3 RandomXyOffset( float radius )
	{
		var x = ((float)_rng.NextDouble() * 2 - 1) * radius;
		var y = ((float)_rng.NextDouble() * 2 - 1) * radius;
		return new Vector3( x, y, 0 );
	}

	// ── Movement ──────────────────────────────────────────────────────────────

	void TickMovement()
	{
		var current      = WorldPosition;
		var deltaPlanar  = (_targetPos - current).WithZ( 0 );
		var distPlanar   = deltaPlanar.Length;

		if ( distPlanar < 2f )
		{
			// Arrived (XY-wise). When seated, lerp toward the desk's facing
			// (or toward the player if this NPC is the active chat partner)
			// so swiveling looks like a swivel chair, not a teleport. Also
			// lift the transform so the citizen sits ON the chair seat
			// instead of with their feet through it.
			if ( State == EmployeeState.Working )
			{
				var target = ResolveSeatedRotation();
				WorldRotation = Rotation.Slerp( WorldRotation, target, Time.Delta * SeatedTurnSpeed );
				WorldPosition = _deskPos + Vector3.Up * SitVerticalLift;
			}
			UpdateAnimation( 0f );
			return;
		}

		var step = MathF.Min( WalkSpeed * Time.Delta, distPlanar );
		var dir  = deltaPlanar.Normal;
		WorldPosition = current + dir * step;

		// Face the planar motion direction (don't tilt up/down on stairs).
		if ( dir.LengthSquared > 0.0001f )
			WorldRotation = Rotation.LookAt( dir );

		UpdateAnimation( step / Time.Delta );
	}

	/// Yaw lerp speed (radians-equivalent units; passed to Rotation.Slerp
	/// as `Time.Delta * SeatedTurnSpeed`). 8 = full 180° in ~0.4s — quick
	/// enough to feel responsive when the player walks around the desk
	/// during chat, slow enough to read as a deliberate turn.
	const float SeatedTurnSpeed = 8f;

	/// Pick the rotation a seated NPC should be aiming for this frame.
	/// While this NPC is the active chat target, look at the player so it
	/// reads as eye contact; otherwise fall back to the desk's authored
	/// facing. Yaw-only — Z component is dropped so the citizen doesn't
	/// tilt up/down when the player is on a different floor height.
	Rotation ResolveSeatedRotation()
	{
		var ix = EmployeeInteractor.Instance;
		if ( ix is not null && ix.ChatTarget == this )
		{
			var player = Scene.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
			if ( player is not null )
			{
				var toPlayer = (player.WorldPosition - WorldPosition).WithZ( 0 );
				if ( toPlayer.LengthSquared > 0.0001f )
					return Rotation.LookAt( toPlayer.Normal );
			}
		}
		return _deskRot;
	}

	/// Push movement + sit state into the citizen anim graph. The
	/// citizen.vanmgrph blends locomotion from local-space velocity
	/// (`move_x` = forward, `move_y` = strafe) and `move_speed` /
	/// `move_groundspeed`. Since the NPC always rotates to face its
	/// movement direction (in TickMovement), local velocity is
	/// effectively (groundSpeed, 0, 0). `sit` (0 = not_sitting,
	/// 1 = sitting) toggles the sit pose.
	void UpdateAnimation( float groundSpeed )
	{
		if ( _renderer is null ) return;

		_renderer.Set( "move_groundspeed", groundSpeed );
		_renderer.Set( "move_speed",       groundSpeed );
		_renderer.Set( "move_x",           groundSpeed );
		_renderer.Set( "move_y",           0f );

		// Sit when arrived (speed near zero) AND in Working state.
		// If sitting plays at the wrong time or never plays, swap the
		// 0/1 indices — the graph's enum order is the source of truth.
		// Vertical alignment is handled by SitVerticalLift in
		// TickMovement, not by sit_offset_height (which only knee-bends).
		bool seated = State == EmployeeState.Working && groundSpeed < 0.1f;
		_renderer.Set( "sit", seated ? 1 : 0 );
	}

	/// True iff <paramref name="s"/> describes a session that's actually been
	/// scheduled — has an end-date set. A null reference, or a default-zeroed
	/// instance (which the s&amp;box editor occasionally serialises into the
	/// prefab when the type is auto-instantiated for the inspector widget),
	/// returns false. Used to guard the off-site-training branch in
	/// <see cref="OnUpdate"/> so a ghost session can't hide fresh hires.
	static bool IsRealTrainingSession( TrainingSession s ) =>
		s is not null && s.EndYear > 0;

	// ── Mood transitions ──────────────────────────────────────────────────────

	/// Seconds (in-game time, ie scaled by GameSpeed) between mood-change
	/// rolls. Coupled with <see cref="MoodChangeChanceForStaff"/> below.
	/// TEMP: 2.5s for playtest — bump back to ~25f once mood pacing is dialed.
	const float MoodTickInterval = 2.5f;

	/// Per-tick chance an NPC who is currently Neutral flips to Good.
	/// Scales DOWN with studio size so big offices don't drown the player —
	/// but the curve is tuned so the STUDIO-wide event rate (per-NPC chance
	/// × staff count) strictly grows with each new hire (no tier-boundary
	/// dips — adding a 5th hire used to lower studio activity). Returned
	/// number is the FULL per-tick P(Good) baseline; Energy Drink and the
	/// Letterbox motivation buff scale on top of it at the call site.
	///   • 1–4 hires : 0.040  (4.0 %)
	///   •   5 hires : 0.035  (3.5 %)
	///   •   6 hires : 0.0325 (3.25 %)
	///   •   7 hires : 0.030  (3.0 %)
	///   • 8+ hires  : 0.0275 (2.75 %)
	static float MoodChangeChanceForStaff()
	{
		int n = HRManager.Instance?.Staff.Count ?? 0;
		if ( n >= 8 ) return 0.0275f;
		if ( n == 7 ) return 0.030f;
		if ( n == 6 ) return 0.0325f;
		if ( n == 5 ) return 0.035f;
		return 0.040f;
	}

	/// Single absolute-day index (Year × 360 + Month × 30 + Day) used to
	/// compare against <see cref="EnergyDrinkUntilTotalDay"/>. Calendar
	/// uses fixed 30-day months / 12-month years so a single int is enough
	/// — no DateTime gymnastics needed. Returns 0 if the GameManager isn't
	/// up yet (very early in scene boot), which keeps buffs inactive.
	static int TotalDayNow()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return 0;
		return gm.Year * 360 + (gm.Month - 1) * 30 + (gm.Day - 1);
	}

	/// True while the Energy Drink buff window is active. Read by
	/// <see cref="TickMood"/> to scale P(Good) per tick.
	public bool IsEnergyBoosted => TotalDayNow() < EnergyDrinkUntilTotalDay;

	/// Grant N in-game days of Energy Drink boost (P(Good) × 1.5 per tick).
	/// Stacks by extension: re-granting takes the later of the two expiry
	/// days, never the earlier — players can't accidentally shorten an
	/// active buff by re-using one. Caller is responsible for decrementing
	/// stash + showing the toast.
	public void GrantEnergyBoost( int days )
	{
		if ( days <= 0 ) return;
		var until = TotalDayNow() + days;
		if ( until > EnergyDrinkUntilTotalDay )
			EnergyDrinkUntilTotalDay = until;
	}

	/// Roll mood transitions while in Production. Outside Production we
	/// idempotently reset mood to Neutral so a project that ends with
	/// pending suggestions cleans up automatically.
	void TickMood()
	{
		// Mood system stays dormant for the entire tutorial. The Production-
		// phase gate below would otherwise let moods roll between Begin
		// Production and the Start Up Training CTA on StartupTraining — a
		// sticky toast landing on top of the tutorial UI would confuse the
		// player before they understand the chat mechanic.
		if ( TutorialManager.Instance is { Phase: not TutorialPhase.Complete } )
		{
			if ( Mood != EmployeeMood.Neutral )
				SetMood( EmployeeMood.Neutral, silent: true );
			_moodTickTimer = 0f;
			return;
		}

		var phase = GameProjectManager.Instance?.Current?.Phase;

		if ( phase != GameProjectPhase.Production )
		{
			if ( Mood != EmployeeMood.Neutral )
				SetMood( EmployeeMood.Neutral, silent: true );
			_moodTickTimer = 0f;
			return;
		}

		// Modal / Escape-menu pause: same gate the rest of the time-based
		// systems use. Mood roll cadence freezes while the player is in
		// a configuration UI or the engine is paused.
		if ( GameManager.Instance is { IsTimePaused: true } ) return;

		_moodTickTimer += Time.Delta * (GameManager.Instance?.TimeMultiplier ?? 1f);
		if ( _moodTickTimer < MoodTickInterval ) return;
		_moodTickTimer = 0f;

		// Don't double-roll an NPC who's already in Good — they stay there
		// until the player chats with them or production ends.
		if ( Mood != EmployeeMood.Neutral ) return;

		// Just-resolved cooldown: skip rolls for the first 2 ticks after
		// a Good mood was cleared (see SetMood). Counts down at the
		// MoodTickInterval cadence, not per-frame.
		if ( _moodResolutionCooldown > 0 )
		{
			_moodResolutionCooldown--;
			return;
		}

		// Second-game tutorial nudge: GameProjectManager flags the Production
		// of the player's 2nd ever game with GuaranteeGoodMoodPending, so the
		// next eligible (Neutral) NPC tick force-fires Good. The flag clears
		// on consumption — after that moods revert to the regular
		// probabilistic roll.
		var project = GameProjectManager.Instance?.Current;
		if ( project is { GuaranteeGoodMoodPending: true } )
		{
			project.GuaranteeGoodMoodPending = false;
			SetMood( EmployeeMood.Good );
			return;
		}

		// Per-tick Good chance. Baseline is MoodChangeChanceForStaff (e.g.
		// 3.5% at 5 hires). Energy Drink / Chewing Gum scale it × 1.5, and
		// the fan-letter motivation buff adds +2% absolute.
		float goodChance = MoodChangeChanceForStaff();
		if ( IsEnergyBoosted ) goodChance *= 1.5f;

		// Fan-letter motivation (ADR-0003): reading a non-quest letter
		// grants the studio +2% absolute Good-mood chance for 14 in-game
		// days. Stacks additively on top of Energy Drink. Studio-wide
		// (not per-employee), so every NPC's roll picks it up uniformly.
		if ( Letterbox.Instance?.IsMotivated ?? false ) goodChance += 0.02f;

		if ( _rng.NextDouble() < goodChance )
			SetMood( EmployeeMood.Good );
	}

	/// Apply a mood transition. Toggles the Good-mood particle child so the
	/// player can spot the NPC across the office. Pushes a one-shot Idea
	/// toast the FIRST time Good fires per production; subsequent rolls are
	/// silent so a busy studio doesn't stack notifications.
	/// <paramref name="silent"/> overrides the toast for transitions that
	/// aren't player-relevant (e.g. accept/decline resolution, production-end
	/// reset).
	public void SetMood( EmployeeMood mood, bool silent = false )
	{
		var previous = Mood;
		Mood = mood;

		// Resolution cooldown: when an NPC's mood transitions OUT of Good
		// back to Neutral, grant a 2-tick grace period before they can roll
		// again. Prevents "Good → accept → Good again next tick" ping-pong.
		// Applies whether the resolution was player-driven (silent: true
		// via EmployeeInteractor) or system-driven (production end).
		if ( previous != EmployeeMood.Neutral && mood == EmployeeMood.Neutral )
			_moodResolutionCooldown = 2;

		switch ( mood )
		{
			case EmployeeMood.Good:
				PendingSuggestion = EmployeeSuggestions.Random( Role, _rng );
				if ( !silent && TryConsumeGoodMoodToastSlot() )
				{
					// First-ever Good toast in the playthrough is sticky
					// (duration = 0) and tagged so EmployeeInteractor can
					// dismiss it when the player opens chat. Subsequent
					// toasts use the default 5s auto-expire.
					bool firstEver = !(TutorialManager.Instance?.FirstGoodMoodToastSeen ?? true);
					Notifications.Push(
						"Idea!",
						$"{EmployeeName} has a suggestion.",
						"info",
						duration: firstEver ? 0f : 5f,
						tag:      firstEver ? "first-good-mood" : "" );
				}
				break;
			default:
				PendingSuggestion = null;
				break;
		}
		ApplyMoodFx();
	}

	static bool TryConsumeGoodMoodToastSlot()
	{
		var p = GameProjectManager.Instance?.Current;
		if ( p is null ) return false;
		if ( p.GoodMoodToastShown ) return false;
		p.GoodMoodToastShown = true;
		return true;
	}

	/// Mirror <see cref="Mood"/> onto the Good-mood particle child. Safe
	/// when the inspector slot is unbound; just no-ops.
	void ApplyMoodFx()
	{
		if ( GoodMoodFx is not null ) GoodMoodFx.Enabled = Mood == EmployeeMood.Good;
	}

	// ── Player chat ───────────────────────────────────────────────────────────

	/// Pick a flavored greeting line. Called by <see cref="EmployeeInteractor"/>
	/// when the chat panel opens. Kind-specific lines (Mentor / Intern /
	/// Marketing / Remote) override Role lines — "Mentor" is a stronger voice
	/// signal than the underlying department.
	public string PickGreeting()
	{
		var kindLines = KindGreetings( Kind );
		var pool      = kindLines.Length > 0 ? kindLines : RoleGreetings( Role );
		return pool[_rng.Next( pool.Length )];
	}

	/// Pick a flavored thanks line for a gift handoff. Same Role-flavored
	/// pool style as the rest of the chat; called by EmployeeInteractor
	/// after a successful TryGiveConsumable / TryAssignItemToDesk so the
	/// chat panel reads as a real exchange. Toast notifications already
	/// describe the specific item / effect — this line is just the human
	/// reaction.
	public string PickThanksLine()
	{
		var pool = ThanksLines( Role );
		return pool[_rng.Next( pool.Length )];
	}

	static string[] ThanksLines( EmployeeRole role ) => role switch
	{
		EmployeeRole.Programmer    => new[] { "Oh, nice. Thanks!",          "Cheers — appreciate it.",       "That's thoughtful, thanks." },
		EmployeeRole.Designer      => new[] { "Aw, thank you!",             "You didn't have to.",            "Sweet — thanks!" },
		EmployeeRole.Creative      => new[] { "How kind. Thank you.",       "That means something. Thanks.",  "Genuinely appreciated." },
		EmployeeRole.Artist        => new[] { "Aww, thanks!",                "You're the best!",               "That's so nice — thank you!" },
		EmployeeRole.SoundDesigner => new[] { "Cheers, boss!",               "Appreciate that.",               "Nice — thanks!" },
		EmployeeRole.Researcher    => new[] { "Mm — thank you, really.",    "Thoughtful. Appreciated.",       "Thank you." },
		_                          => new[] { "Thanks!" },
	};

	/// Pick a flavored response to a player reply, again with Kind-overrides-
	/// Role precedence. Lines are short — single-sentence reactions that read
	/// like a passing exchange, not a long branching dialogue.
	public string RespondTo( ChatReplyKind reply )
	{
		var kindLines = KindResponses( Kind, reply );
		var pool      = kindLines.Length > 0 ? kindLines : RoleResponses( Role, reply );
		return pool[_rng.Next( pool.Length )];
	}

	static string[] KindGreetings( EmployeeKind kind ) => kind switch
	{
		EmployeeKind.Mentor         => new[] { "Good to see you.", "Take it easy out there.", "Question for you when you have a sec." },
		EmployeeKind.Intern         => new[] { "Oh — hi!", "Uh, hello!", "Hey! Did I do something wrong?" },
		EmployeeKind.MarketingAgent => new[] { "Got a sec? We should talk reach.", "Numbers are looking up.", "Hey hey." },
		EmployeeKind.RemoteWorker   => new[] { "Working from home today.", "Connection's solid, thanks.", "Ping me on Slack any time." },
		_                           => System.Array.Empty<string>(),
	};

	static string[] RoleGreetings( EmployeeRole role ) => role switch
	{
		EmployeeRole.Programmer    => new[] { "Hey.", "Almost done with this bug.", "What's up?", "Compiling — give me a sec." },
		EmployeeRole.Designer      => new[] { "Hi!", "Got an idea I want to run by you.", "Let me show you the latest mockup." },
		EmployeeRole.Creative      => new[] { "Greetings.", "Mid-thought, but hi.", "I had a dream about this game last night." },
		EmployeeRole.Artist        => new[] { "Oh, hi!", "Look at this — what do you think?", "Five more minutes on this asset." },
		EmployeeRole.SoundDesigner => new[] { "Hey.", "Listen to this real quick.", "Hi! New layer just landed." },
		EmployeeRole.Researcher    => new[] { "Mmm? Oh, hello.", "Found something interesting.", "One sec — finishing this paper." },
		_                          => new[] { "Hi." },
	};

	static string[] KindResponses( EmployeeKind kind, ChatReplyKind reply ) => (kind, reply) switch
	{
		(EmployeeKind.Mentor,         ChatReplyKind.AskWork)    => new[] { "Steady. Mentoring keeps me sharp.", "Helping the juniors find their feet." },
		(EmployeeKind.Mentor,         ChatReplyKind.Compliment) => new[] { "That means a great deal.", "I appreciate it." },

		(EmployeeKind.Intern,         ChatReplyKind.AskWork)    => new[] { "I'm trying my best!", "Slow but I'm getting there." },
		(EmployeeKind.Intern,         ChatReplyKind.Compliment) => new[] { "Really?! Thank you!", "That's so nice to hear!" },

		(EmployeeKind.MarketingAgent, ChatReplyKind.AskWork)    => new[] { "Engagement's up — let me show you the numbers.", "Building the funnel." },
		(EmployeeKind.MarketingAgent, ChatReplyKind.Compliment) => new[] { "Appreciate that.", "Glad you think so." },

		(EmployeeKind.RemoteWorker,   ChatReplyKind.AskWork)    => new[] { "All good — Slack's quiet today.", "Heads-down, no meetings." },
		(EmployeeKind.RemoteWorker,   ChatReplyKind.Compliment) => new[] { "Means a lot from across the wifi.", "Thanks!" },

		_                                                       => System.Array.Empty<string>(),
	};

	static string[] RoleResponses( EmployeeRole role, ChatReplyKind reply ) => (role, reply) switch
	{
		(EmployeeRole.Programmer,    ChatReplyKind.AskWork)    => new[] { "Just hunting bugs.", "Compiles, somehow.", "Refactoring this old mess." },
		(EmployeeRole.Programmer,    ChatReplyKind.Compliment) => new[] { "Means a lot, thanks.", "Aw, cheers." },

		(EmployeeRole.Designer,      ChatReplyKind.AskWork)    => new[] { "Iterating on layouts.", "Cleaning up the wireframes." },
		(EmployeeRole.Designer,      ChatReplyKind.Compliment) => new[] { "Thanks!", "I really needed to hear that." },

		(EmployeeRole.Creative,      ChatReplyKind.AskWork)    => new[] { "Story's coming together.", "Working on a twist." },
		(EmployeeRole.Creative,      ChatReplyKind.Compliment) => new[] { "Glad you noticed.", "That means something, coming from you." },

		(EmployeeRole.Artist,        ChatReplyKind.AskWork)    => new[] { "Painting something cool.", "Just nailed a tricky shader." },
		(EmployeeRole.Artist,        ChatReplyKind.Compliment) => new[] { "Thanks! Means a lot.", "Awww!" },

		(EmployeeRole.SoundDesigner, ChatReplyKind.AskWork)    => new[] { "New track shaping up.", "Layering some ambience." },
		(EmployeeRole.SoundDesigner, ChatReplyKind.Compliment) => new[] { "Thank you!", "Cheers, boss." },

		(EmployeeRole.Researcher,    ChatReplyKind.AskWork)    => new[] { "Reading up on something.", "Got a paper open you'd like." },
		(EmployeeRole.Researcher,    ChatReplyKind.Compliment) => new[] { "Appreciate it.", "Glad I could help." },

		_                                                      => new[] { "Sure." },
	};

	// ── Public API (called by HRManager) ──────────────────────────────────────

	/// Configure a freshly-cloned prefab from an applicant's data and seat it
	/// at the given desk pose. After this call, the original Employee object
	/// is discarded — EmployeeNPC is the sole owner of the data.
	public void Assign( Employee e, Vector3 deskPos, Rotation deskRot )
	{
		// Cache the desk pose first; the state machine reads _deskPos when
		// resolving the Working target, and Assign() ends in Working state.
		_deskPos   = deskPos;
		_deskRot   = deskRot;
		_targetPos = deskPos;

		EmployeeName       = e.Name;
		Age                = e.Age;
		Role               = e.Role;
		Salary             = e.Salary;
		DeskSlotId         = e.DeskSlotId;
		Stats              = e.Stats;           // EmployeeNPC takes ownership
		Morale             = 1f;
		CurrentTask        = "";
		Kind               = e.Kind;
		WasReturningIntern = e.IsReturningIntern;

		// Abilities — max 2, stored as two nullable slots.
		Ability1 = e.Abilities.Count > 0 ? e.Abilities[0] : null;
		Ability2 = e.Abilities.Count > 1 ? e.Abilities[1] : null;

		// Don't snap to the desk — leave the NPC at its clone position
		// (HRManager.EmployeeSpawnPoint if set, otherwise already at the
		// desk). SetState(Working) below sets _targetPos = _deskPos, so
		// TickMovement walks them from spawn to desk on the next frame.
		AppearanceSeed = e.AppearanceSeed;
		ApplyAppearance( AppearanceSeed );
		SetState( EmployeeState.Working );

		Log.Info( $"[NPC] {EmployeeName} ({Role}) seated at desk slot {DeskSlotId}." );
	}

	// ── Appearance ────────────────────────────────────────────────────────────

	/// Apply per-hire visual variation by feeding the applicant's
	/// <see cref="Employee.AppearanceSeed"/> into the citizen prefab's
	/// <c>Sandbox.Dresser</c> component. Same seed → same look every time
	/// (deterministic), so the future resume-preview portrait will match
	/// the spawned NPC at the desk.
	///
	/// The Dresser drives age / height / tint via its Manual* sliders and
	/// drives clothing via its Clothing list. We seed-randomise both:
	///   • Manual sliders set deterministically from the seed.
	///   • Randomise() refreshes the clothing pool from the citizen's
	///     built-in wardrobe.
	void ApplyAppearance( int seed )
	{
		var dresser = GameObject.Components.Get<Sandbox.Dresser>();
		if ( dresser is null )
		{
			Log.Warning( $"[NPC] {EmployeeName}: no Sandbox.Dresser component on the citizen prefab — appearance won't vary." );
			return;
		}

		var rng = new System.Random( seed );

		// Deterministic manual sliders (0..1). Same seed → same body.
		// These are cheap float assignments — apply immediately.
		dresser.ManualAge    = (float)rng.NextDouble();
		dresser.ManualHeight = (float)rng.NextDouble();
		dresser.ManualTint   = (float)rng.NextDouble();

		if ( SavedClothing is { Count: > 0 } restore )
		{
			// Reload path: restore the exact outfit captured on first hire.
			// Copy the list to detach from any shared reference, then call
			// Apply() to rebuild the clothing meshes on the model.
			dresser.Clothing = new List<Sandbox.ClothingContainer.ClothingEntry>( restore );
			dresser.Apply();
			return;
		}

		// Fresh-hire path: defer Randomize to the next OnUpdate (Randomize
		// loads clothing meshes + textures synchronously — running a frame
		// later hides the spike behind the just-shown notification +
		// state-machine startup). The post-Randomize tick snapshots the
		// outcome into SavedClothing.
		_pendingDresserRandomize = dresser;
	}

	Sandbox.Dresser _pendingDresserRandomize;

	/// Debug-only stopwatch start (RealTime.Now seconds) stamped by HRManager
	/// at hire-button time. The deferred-Randomize block in OnUpdate logs the
	/// final spawn-to-ready elapsed and clears this back to 0. 0 = not tracked.
	public float _spawnDebugStartTime;
}

/// <summary>
/// Player reply options that produce an NPC response. Each value maps to a
/// per-Kind / per-Role response row in <see cref="EmployeeNPC.RespondTo"/>.
/// Adding a new kind: extend this enum, add the button in Hud.razor's
/// chat-replies block, and add response rows for every Role (and any Kind
/// that should override the role flavor). Note "Catch you later" is NOT
/// here — it closes the panel directly without going through RespondTo.
/// </summary>
public enum ChatReplyKind
{
	AskWork,     // "How's work going?"
	Compliment,  // "You're doing great work."
}
