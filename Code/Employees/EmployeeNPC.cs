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
			d.Randomize();
			SavedClothing = new List<Sandbox.ClothingContainer.ClothingEntry>( d.Clothing ?? new List<Sandbox.ClothingContainer.ClothingEntry>() );
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

		// Off-site training intercept: if a session is active, the NPC walks
		// to the studio's spawn point and then hides its renderer (anim
		// graph stays warm — disabling the whole GameObject was wiping the
		// SkinnedModelRenderer's locomotion + sit-blend state, which is why
		// returning hires used to slide and not sit).
		if ( ActiveTraining is not null )
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
			// Arrived (XY-wise). Snap to the desk's exact facing when working
			// so they don't end up rotated awkwardly toward whatever they came
			// from. When seated, also lift the transform so the citizen sits
			// ON the chair seat instead of with their feet through it.
			if ( State == EmployeeState.Working )
			{
				WorldRotation = _deskRot;
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
}
