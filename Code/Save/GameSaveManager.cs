/// <summary>
/// Central save / load orchestrator. One per game; lives on the
/// GameManager GameObject. Owns the on-disk format, dispatches
/// per-system <c>ToSave()</c> / <c>LoadFrom()</c> calls, and gates the
/// migration chain on load.
///
/// Authoritative shape: ADR-0001 (docs/architecture/adr-0001-save-system.md).
///
/// **Spike status (2026-04-30)**: per the ADR's Migration Plan day-1
/// spike, this implementation persists ONLY <see cref="CalendarSave"/>
/// and <see cref="EconomySave"/>, written through
/// <c>FileSystem.Data.WriteJson</c>. The slot-aware
/// <c>Sandbox.Storage</c> backend that the ADR commits to layers in
/// once this round-trip is confirmed in-editor.
/// </summary>
public sealed class GameSaveManager : Component
{
	public static GameSaveManager Instance { get; private set; }

	/// Bumped on every breaking schema change. Old files load through
	/// <see cref="GameSaveMigrations"/> migrators; newer files refuse to
	/// load (no downgrade path).
	///
	/// History:
	///   v1 — initial format (ADR-0001).
	///   v2 — added <c>AchievementsSave.PeakBalance</c> (ADR-0002, leaderboards).
	///   v3 — added <c>LetterboxSave</c> (ADR-0003, fan letters + quests).
	///        v3-bump in Phase 1 (slot count change) so Phase 2 can land
	///        the data changes without a second schema increment.
	public const int SchemaVersion = 3;

	/// Reserved slot id for the autosave path. Doubles as the player-facing
	/// "Slot 1" — autosave triggers overwrite slot 1 directly, and the Save
	/// menu disables manual SAVE on this slot so the player can't fight the
	/// autosave for ownership of the file. LOAD stays enabled so the autosave
	/// is recoverable.
	public const string AutosaveSlotId = "slot-1";

	/// Player-facing manual save slots. Surfaced in the in-game Save menu
	/// (one card per slot). Slot 1 is the autosave slot (read-only from the
	/// player's POV); slot 2 is the free manual slot.
	/// Display order in the SavePanel matches this array's order. Autosave
	/// sits at the BOTTOM (index 2 → labelled "SLOT 3 (AUTO-SAVE)") so the
	/// player can't accidentally LOAD it when they meant to LOAD a manual
	/// save. The on-disk file id <c>"slot-1"</c> is unchanged — autosave
	/// triggers (<see cref="AutosaveSlotId"/>) still write there; the
	/// rearrangement is purely UX.
	public static readonly string[] ManualSlotIds = new[]
	{
		"slot-2",   // manual #1   → labelled SLOT 1 in the UI
		"slot-3",   // manual #2   → labelled SLOT 2 in the UI
		"slot-1",   // autosave    → labelled SLOT 3 (AUTO-SAVE) in the UI
	};

	/// Safe spot the player is teleported to before any wholesale slot
	/// activation runs (load, or buying an item that enables a slot the
	/// player is currently standing in). Drag a clear-floor anchor here
	/// in the inspector — typically a doorway or open hallway, well away
	/// from any desk / decoration / lounge slots. Without this set, both
	/// flows fall back to leaving the player where they are, which can
	/// cause physics interpenetration when the slot's collider enables.
	[Property] public GameObject PlayerRespawnPoint { get; set; }

	/// Distance below which the player counts as "standing in" a slot
	/// for the buy-time overlap check. 100u ≈ desk-sized footprint —
	/// generous enough that a player adjacent to a slot triggers the
	/// teleport, conservative enough that buying a coffee machine in
	/// the corner of the office doesn't snap the player around.
	const float SlotOverlapRadius = 100f;

	protected override void OnAwake()
	{
		Instance = this;

		// Autosave subscriptions. Both events fire well after the systems
		// they describe have committed their state, so the snapshot is
		// always consistent. Day-tick is the high-cadence beat (every
		// in-game day); project-shipped is the rare big-event beat. The
		// old month-start hook was redundant once day-tick landed — daily
		// covers monthly N-times-over.
		GameManager.OnDayStart   += AutosaveOnDayStart;
		Gallery.OnGameShipped    += AutosaveOnGameShipped;
	}

	/// One-shot guard for the boot auto-load. Set true after the first
	/// successful (or attempted) load on the very first OnUpdate frame, so
	/// the loader can't run twice in a session.
	bool _bootLoadDone;

	protected override void OnStart()
	{
		// Cross-run files (settings + meta-progression) load once on boot,
		// after every system's OnAwake has run so target singletons are
		// live. The per-run autosave (slot 1) is loaded one tick later in
		// OnUpdate — we defer it because Apply() needs InventoryManager's
		// OnStart (DiscoverSlots, BuildVariantCache) to have completed
		// first, otherwise the scene's slots aren't queryable yet.
		LoadSettings();
		LoadMeta();
	}

	protected override void OnUpdate()
	{
		if ( _bootLoadDone ) return;
		_bootLoadDone = true;
		BootAutoLoad();
	}

	/// First-frame auto-load: read the autosave slot (slot 1) and apply it,
	/// so the player resumes exactly where they left off and never
	/// accidentally overwrites progress by closing/reopening the game.
	/// Silent on success — the world simply appears restored. On failure
	/// we log a warning and stay on the seeded fresh state.
	void BootAutoLoad()
	{
		if ( !HasRun( AutosaveSlotId ) )
		{
			Log.Info( "[Save] No autosave on disk — starting fresh." );
			return;
		}

		if ( TryLoadRun( AutosaveSlotId, out var err ) )
		{
			Log.Info( "[Save] Boot auto-load: restored from autosave." );
		}
		else
		{
			Log.Warning( $"[Save] Boot auto-load failed: {err}. Continuing with fresh state." );
			Notifications.Push( "Save load failed",
				"Couldn't restore your last autosave — starting a fresh studio.",
				"warning", duration: 8f );
		}
	}

	protected override void OnDestroy()
	{
		GameManager.OnDayStart   -= AutosaveOnDayStart;
		Gallery.OnGameShipped    -= AutosaveOnGameShipped;

		// Cross-run state writes on exit too. Settings might have changed
		// during the session; meta-progression won't have unless the run
		// ended via Restart, but writing it is cheap.
		SaveSettings();
		SaveMeta();

		// Final per-run autosave on scene exit. Guarded by a singleton
		// check — component destruction order is undefined, so if
		// PlayerStats / HRManager are already gone the snapshot would land
		// empty and stomp the autosave with garbage. Better to skip than
		// corrupt.
		if ( PlayerStats.Instance is not null && HRManager.Instance is not null )
			Autosave( "scene exit" );

		if ( Instance == this ) Instance = null;
	}

	void AutosaveOnDayStart()    => Autosave( "day tick" );
	void AutosaveOnGameShipped() => Autosave( "game shipped" );

	/// Wrapper around <see cref="TrySaveRun"/> for autosave triggers.
	/// Logs the trigger reason in the success path's existing line and
	/// surfaces failures as a warning rather than a notification — these
	/// fire from background events the player isn't directly driving.
	void Autosave( string reason )
	{
		if ( !TrySaveRun( AutosaveSlotId, out var err ) )
			Log.Warning( $"[Save] autosave on {reason} failed: {err}" );
		else
			Log.Info( $"[Save] autosave triggered by {reason}." );
	}

	// ── Public API ────────────────────────────────────────────────────────

	/// <summary>
	/// Serialise the live game state to <paramref name="slotId"/>.
	/// Returns false on failure with a player-presentable reason in
	/// <paramref name="error"/>.
	/// </summary>
	public bool TrySaveRun( string slotId, out string error )
	{
		error = null;
		try
		{
			var dto = Capture();
			FileSystem.Data.CreateDirectory( "saves" );
			FileSystem.Data.WriteJson( PathFor( slotId ), dto );
			Log.Info( $"[Save] wrote slot '{slotId}' (v{dto.SchemaVersion}) " +
				$"day={dto.Calendar?.Day}/{dto.Calendar?.Month}/{dto.Calendar?.Year} " +
				$"money=${dto.Economy?.Money:N0} " +
				$"hires={dto.HR?.Staff?.Count ?? 0} " +
				$"applicants={dto.HR?.Applicants?.Count ?? 0} " +
				$"items={dto.Inventory?.Items?.Count ?? 0} " +
				$"unlocked={dto.Achievements?.Unlocked?.Count ?? 0} " +
				$"shipped={dto.Gallery?.ShippedGames?.Count ?? 0} " +
				$"project={(dto.ActiveProject is null ? "none" : dto.ActiveProject.Phase.ToString())}" );

			// Submit the two save-time leaderboard stats (ADR-0002).
			// Wrapped internally so a platform failure can't fail the save.
			Leaderboards.SubmitOnSave();

			return true;
		}
		catch ( System.Exception ex )
		{
			error = ex.Message;
			Log.Warning( $"[Save] failed to write slot '{slotId}': {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Read <paramref name="slotId"/> from disk, run any pending
	/// migrators, and apply the result to live game state. Returns false
	/// if the file is missing, corrupt, or from a future SchemaVersion.
	/// </summary>
	public bool TryLoadRun( string slotId, out string error )
	{
		error = null;
		try
		{
			string path = PathFor( slotId );
			if ( !FileSystem.Data.FileExists( path ) )
			{
				error = $"No save in slot '{slotId}'.";
				return false;
			}

			var raw = FileSystem.Data.ReadJson<GameSave>( path );
			if ( raw is null )
			{
				error = "Save file is empty or unreadable.";
				return false;
			}

			var dto = GameSaveMigrations.Migrate( raw );
			if ( dto is null )
			{
				error = $"Save '{slotId}' is from a newer build (v{raw.SchemaVersion}); cannot downgrade.";
				return false;
			}

			Log.Info( $"[Save] read slot '{slotId}' (v{dto.SchemaVersion}) " +
				$"day={dto.Calendar?.Day}/{dto.Calendar?.Month}/{dto.Calendar?.Year} " +
				$"money=${dto.Economy?.Money:N0} " +
				$"hires={dto.HR?.Staff?.Count ?? 0} " +
				$"items={dto.Inventory?.Items?.Count ?? 0} " +
				$"unlocked={dto.Achievements?.Unlocked?.Count ?? 0}" );
			Apply( dto );
			Log.Info( $"[Save] applied slot '{slotId}' → live state: " +
				$"day={GameManager.Instance?.Day}/{GameManager.Instance?.Month}/{GameManager.Instance?.Year} " +
				$"money=${GameManager.Instance?.Money:N0} " +
				$"hires={HRManager.Instance?.Staff?.Count ?? 0} " +
				$"items={InventoryManager.Instance?.Owned?.Count ?? 0}" );
			return true;
		}
		catch ( System.Exception ex )
		{
			error = ex.Message;
			Log.Warning( $"[Save] failed to load slot '{slotId}': {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Wipe per-run state and start a fresh studio. Triggered by the
	/// "Start New Game" button under the Save menu's slot list. Cross-run
	/// state (settings + meta-progression) is preserved. The next autosave
	/// will overwrite slot 1 with this fresh state, so manual slot 2 is
	/// the only safety net for the previous run — by design, since the
	/// confirmation modal warns the player.
	/// </summary>
	public void RestartRun()
	{
		Log.Info( "[Save] RestartRun: wiping per-run state." );

		// Same close-all-modals trick Apply() uses, so panels don't hold
		// stale references to about-to-be-cleared state.
		CloseAllUi();
		MovePlayerToSafeAnchor();

		Achievements.Reset();
		PlayerStats.Instance?.ResetProgress();
		InventoryManager.Instance?.ResetProgress(); // re-seeds starter Desk
		Gallery.Instance?.ResetProgress();
		Letterbox.Instance?.ResetProgress();

		// Systems without an explicit Reset method clear via Load(null) —
		// they treat null DTO as "no save state, start empty".
		HRManager.Instance?.Load( null );
		TrainingManager.Instance?.Load( null );
		GameProjectManager.Instance?.Load( null );

		// Calendar + economy bypass Load(null) (which is a no-op for
		// those) and use the dedicated reset path.
		GameManager.Instance?.RestartRun();

		// Drop in-flight notifications from the previous run (the
		// "Released!" toasts, achievement popups, etc.) so the fresh
		// studio doesn't inherit ghosts of a finished playthrough.
		Notifications.Clear();

		// Re-seed the day-1 tutorial applicant so the new run has the
		// same on-ramp as a fresh boot — the player wakes up with one
		// guaranteed hire in the HR inbox.
		HRManager.Instance?.SeedTutorialApplicant();

		// Reset the first-launch tutorial so RestartRun re-shows the
		// welcome modal + relocks menus until the player makes their
		// first hire of the new run.
		TutorialManager.Instance?.ResetProgress();

		Notifications.Push( "New Game",
			"Studio reset to a fresh start.",
			"info", duration: 5f );
	}

	/// <summary>Remove <paramref name="slotId"/> from disk.</summary>
	public void DeleteRun( string slotId )
	{
		string path = PathFor( slotId );
		if ( FileSystem.Data.FileExists( path ) )
			FileSystem.Data.DeleteFile( path );
	}

	/// <summary>True if <paramref name="slotId"/> has a save on disk.</summary>
	public bool HasRun( string slotId )
		=> FileSystem.Data.FileExists( PathFor( slotId ) );

	/// <summary>
	/// Read just the metadata (SavedAt + GameVersion + day) from a slot
	/// without applying it to live state. Drives the Save menu's "Last
	/// saved: …" display. Returns null if the file is missing or
	/// corrupt — caller renders "No save yet" in that case.
	/// </summary>
	public GameSave PeekRun( string slotId )
	{
		string path = PathFor( slotId );
		if ( !FileSystem.Data.FileExists( path ) ) return null;
		try
		{
			return FileSystem.Data.ReadJson<GameSave>( path );
		}
		catch
		{
			return null;
		}
	}

	// ── Capture / Apply ───────────────────────────────────────────────────
	// New systems opt in by adding their sub-DTO here. Each Capture call
	// pulls from the live system; each Apply call routes back to it.

	GameSave Capture()
	{
		var dto = new GameSave
		{
			SchemaVersion = SchemaVersion,
			SavedAt       = DateTimeOffset.UtcNow,
			GameVersion   = BuildIdentifier,
		};

		// Order doesn't matter on save — every system snapshots independently.
		var gm = GameManager.Instance;
		if ( gm is not null )
		{
			dto.Calendar = gm.SaveCalendar();
			dto.Economy  = gm.SaveEconomy();
		}

		dto.Achievements = Achievements.Save();

		var founder = PlayerStats.Instance;
		if ( founder is not null )
			dto.Founder = founder.Save();

		var hr = HRManager.Instance;
		if ( hr is not null )
			dto.HR = hr.Save();

		var inv = InventoryManager.Instance;
		if ( inv is not null )
			dto.Inventory = inv.Save();

		var tm = TrainingManager.Instance;
		if ( tm is not null )
			dto.Training = tm.Save();

		var gallery = Gallery.Instance;
		if ( gallery is not null )
			dto.Gallery = gallery.Save();

		var pm = GameProjectManager.Instance;
		if ( pm is not null )
			dto.ActiveProject = pm.Save();

		var tut = TutorialManager.Instance;
		if ( tut is not null )
			dto.Tutorial = tut.Save();

		var letterbox = Letterbox.Instance;
		if ( letterbox is not null )
			dto.Letterbox = letterbox.Save();

		return dto;
	}

	void Apply( GameSave dto )
	{
		// Apply order matters — later systems may reference earlier state:
		//   1. Achievements  — gates everything else (item/genre/posting unlocks)
		//   2. Calendar/Economy — month/money state other systems read at OnDayStart
		//   3. Founder       — establishes PlayerStats.Instance for project assignments
		//   4. Inventory     — must precede HR (HR resolves desk slots via inventory)
		//   5. HR            — staff list ready before project assignments dereference it
		//   6. Training      — research inbox / offer queue
		//   7. Gallery       — shipped games (read-only at load)
		//   8. ActiveProject — last; depends on Founder + HR being live for worker refs

		// Close every modal and clear any UI subject pointers BEFORE the
		// per-system loads run. Otherwise an open InterviewPanel or
		// StaffStatsPanel still references an Employee/EmployeeNPC that's
		// about to be destroyed and re-cloned, leaving the panel bound to
		// a dangling object — visible as ghost text, frozen modals, or
		// "missing component" warnings during the load tick.
		CloseAllUi();

		// Teleport the player to a known-clear anchor before slot
		// GameObjects re-enable. The save's slot occupancy can put solid
		// geometry where the player is currently standing — interpenetrating
		// with the player's collider on enable causes the physics solver
		// to pop them out unpredictably (or trap them inside).
		MovePlayerToSafeAnchor();

		Achievements.Load( dto.Achievements );

		var gm = GameManager.Instance;
		if ( gm is not null )
		{
			gm.LoadCalendar( dto.Calendar );
			gm.LoadEconomy( dto.Economy );
		}

		PlayerStats.Instance?.Load( dto.Founder );
		InventoryManager.Instance?.Load( dto.Inventory );
		HRManager.Instance?.Load( dto.HR );
		TrainingManager.Instance?.Load( dto.Training );
		Gallery.Instance?.Load( dto.Gallery );
		GameProjectManager.Instance?.Load( dto.ActiveProject );
		TutorialManager.Instance?.Load( dto.Tutorial );
		Letterbox.Instance?.Load( dto.Letterbox );
	}

	// ── Cross-run files (settings + meta-progression) ────────────────────
	// Each lives in its own FileSystem.Data file, separate from the
	// per-run save slots. Loaded once at boot, written on demand and on
	// scene exit. A corrupt per-run save can't take settings or
	// meta-progression with it.

	const string SettingsPath = "settings.json";
	const string MetaPath     = "meta.json";

	public void SaveSettings()
	{
		var settings = Settings.Instance;
		if ( settings is null ) return;
		try
		{
			FileSystem.Data.WriteJson( SettingsPath, settings.Save() );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Save] failed to write settings: {ex.Message}" );
		}
	}

	public void LoadSettings()
	{
		var settings = Settings.Instance;
		if ( settings is null ) return;
		if ( !FileSystem.Data.FileExists( SettingsPath ) ) return;
		try
		{
			var dto = FileSystem.Data.ReadJson<SettingsSave>( SettingsPath );
			settings.Load( dto );
			Log.Info( "[Save] settings loaded." );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Save] failed to read settings: {ex.Message}" );
		}
	}

	/// Meta-progression file (Hall of Fame, persistent unlocks across
	/// Restart). The <c>MetaProgression</c> singleton that produces /
	/// consumes this DTO doesn't exist yet — Save/Load are stubs that
	/// keep the file plumbing alive so it doesn't have to land twice.
	public void SaveMeta()
	{
		// TODO: read from MetaProgression.Instance.Save() when that
		//       singleton exists (planned in step-5-payoff.md).
		var dto = new MetaSave { SavedAt = System.DateTimeOffset.UtcNow };
		try
		{
			FileSystem.Data.WriteJson( MetaPath, dto );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Save] failed to write meta: {ex.Message}" );
		}
	}

	public void LoadMeta()
	{
		if ( !FileSystem.Data.FileExists( MetaPath ) ) return;
		try
		{
			var dto = FileSystem.Data.ReadJson<MetaSave>( MetaPath );
			// TODO: dispatch to MetaProgression.Instance.Load(dto) when
			//       that singleton exists. For now the read just confirms
			//       the file is well-formed.
			if ( dto is not null )
				Log.Info( $"[Save] meta loaded ({dto.HallOfFame?.Count ?? 0} runs, {dto.CarryoverUnlocks?.Count ?? 0} unlocks)." );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Save] failed to read meta: {ex.Message}" );
		}
	}

	// ── Player teleport (load + slot-overlap-on-buy) ─────────────────────

	/// Teleport the player to <see cref="PlayerRespawnPoint"/>. No-op if
	/// the anchor isn't assigned in the inspector or the player can't be
	/// found in the scene. Public so <see cref="InventoryManager"/> can
	/// call it from the buy path when a fresh slot would land on top of
	/// the player.
	public void MovePlayerToSafeAnchor()
	{
		if ( PlayerRespawnPoint is null ) return;

		var player = Scene.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
		if ( player is null ) return;

		player.WorldPosition = PlayerRespawnPoint.WorldPosition;
		player.WorldRotation = PlayerRespawnPoint.WorldRotation;
	}

	/// True if the player's current position is within
	/// <see cref="SlotOverlapRadius"/> of <paramref name="slotGo"/>'s
	/// position — close enough that enabling the slot's collider will
	/// interpenetrate. Distance-based heuristic; doesn't need an exact
	/// AABB intersection because the radius is sized for desk-class
	/// footprints.
	public static bool PlayerWouldOverlap( GameObject slotGo )
	{
		if ( slotGo is null )                           return false;
		var player = Sandbox.Game.ActiveScene?.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
		if ( player is null )                           return false;
		return Vector3.DistanceBetween( player.WorldPosition, slotGo.WorldPosition ) < SlotOverlapRadius;
	}

	// ── Internals ─────────────────────────────────────────────────────────

	/// Force every modal closed and clear UI "subject" pointers before a
	/// load swaps the underlying state out from under them. Mirrors the
	/// mutual-exclusion pattern each modal already uses in its own
	/// <c>SetOpen(true)</c> path — but called centrally here so a load
	/// from any modal context always lands at a clean UI state.
	static void CloseAllUi()
	{
		Modals.CloseAllExcept();

		// HR has two non-modal subjects (interview / staff-view) and the
		// inventory desk-assign picker — clear them so panels bound to
		// these properties don't dereference stale references. Interview
		// subject is cleared inside HRManager.Load (PassOnInterviewSubject
		// refuses to drop the tutorial hire, so we can't use it here).
		HRManager.Instance?.CloseWorkerView();
		InventoryManager.Instance?.CancelAssign();

		GameManager.RefreshPlayerLock();
	}

	static string PathFor( string slotId )
	{
		// Hyphens / lowercase only — keeps the file name shell-safe across
		// platforms. Caller is responsible for sanitising player-typed
		// slot names before they reach here (manual-save UI's job).
		return $"saves/run-{slotId}.json";
	}

	/// Build identifier baked into every save's <see cref="GameSave.GameVersion"/>
	/// for diagnostic purposes (which dev build wrote this file?). Spike
	/// uses a hardcoded marker; production reads from <c>Game.Build</c> or
	/// equivalent once verified against current s&amp;box 1.0 docs.
	const string BuildIdentifier = "dev-2026-04-30";
}
