/// <summary>
/// The studio founder — drop this on the player GameObject. The player carries
/// the same six-stat block as any employee (<see cref="EmployeeStats"/>), so
/// every system that reads an employee's stats can read the player's too.
///
/// Differences from a normal hire:
///   • <see cref="Salary"/> is always 0 — the founder doesn't pay themselves.
///   • Has <see cref="Level"/> + <see cref="Xp"/>, gained from shipping games,
///     hiring, training, etc. Each level unlocks a slot in <see cref="Abilities"/>.
///   • Pulls from <see cref="PlayerAbility"/> (founder-only perks) — never the
///     regular <see cref="EmployeeAbility"/> pool.
/// </summary>
public sealed class PlayerStats : Component, IDevWorker
{
	public static PlayerStats Instance { get; private set; }

	// ── Identity ──────────────────────────────────────────────────────────────

	[Property] public string Name { get; set; } = "Founder";

	/// Founder is its own role — distinct from any of the six employee
	/// departments. The player isn't a programmer or a designer; they're
	/// the studio head, and UI surfaces them with a "FOUNDER" pill rather
	/// than a department label.
	[Property] public EmployeeRole Role { get; set; } = EmployeeRole.Founder;

	// ── IDevWorker ────────────────────────────────────────────────────────────
	// Founder-only PlayerAbility perks live in <see cref="Abilities"/>; they're
	// a different enum from <see cref="EmployeeAbility"/>, so the interface's
	// employee-ability slots are always null for the player and the dev-game
	// systems just treat the founder as having no employee-style traits.

	EmployeeAbility? IDevWorker.Ability1 => null;
	EmployeeAbility? IDevWorker.Ability2 => null;
	bool IDevWorker.HasAbility( EmployeeAbility a ) => false;
	public bool IsPlayer => true;

	// Training state. Founder can never go off-site (TrainingManager rejects
	// the request), so ActiveTraining is always null in practice — kept as a
	// settable auto-property to satisfy IDevWorker and to keep the door open
	// for future founder-specific training paths. Research is permitted, so
	// ResearchTarget + the tick counter do see real use.
	[Property] public TrainingSession ActiveTraining          { get; set; }
	[Property] public string          ResearchTopicId         { get; set; } = "";
	[Property] public float           DaysIntoCurrentResearch { get; set; }

	// ── Levelling ─────────────────────────────────────────────────────────────

	[Property] public int  Level { get; set; } = 1;
	[Property] public long Xp    { get; set; } = 0;

	/// XP required to reach the next level. Linear curve for now —
	/// 1 000 at L1 → 2 000 at L2 → 3 000 at L3 …
	public long XpForNextLevel => Level * 1_000L;

	/// Founder's salary is always free — that's the point of running the studio.
	public long Salary => 0;

	public static event Action<int>            OnLevelUp;
	public static event Action<PlayerAbility>  OnAbilityUnlocked;

	// ── Stats ─────────────────────────────────────────────────────────────────

	/// Same shape as <see cref="EmployeeNPC.Stats"/>. Persisted across saves.
	[Property] public EmployeeStats Stats { get; set; } = new();

	/// Treat the founder as always at full Morale (no salary missed → no decay).
	public float Morale => 1f;

	public int EffectiveStat( StatBlock block ) => StatBlock.Clamp( block.Average );

	// ── Abilities ─────────────────────────────────────────────────────────────

	/// Founder perks the player has unlocked. Populated by level-up rewards or
	/// achievement-gated grants — never appears on a regular employee.
	public HashSet<PlayerAbility> Abilities { get; private set; } = new();

	public bool HasAbility( PlayerAbility a ) => Abilities.Contains( a );

	/// Unlocked abilities so far this run. Each level grants one slot the
	/// player can spend in the Settings / Founder UI.
	public int AbilitySlotsTotal     => Math.Max( 1, Level );
	public int AbilitySlotsRemaining => Math.Max( 0, AbilitySlotsTotal - Abilities.Count );

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;

		// Backfill: if the founder's stats are all zero (fresh prefab, never
		// customised in the inspector, no save loaded), seed flat-300
		// starter stats across every sub-stat. Mid-tier baseline lets the
		// founder solo-ship the first project without grinding to mid-tier
		// hires first; growth from shipping / training still pushes them
		// further into senior territory.
		if ( Stats is null || Stats.Overall <= 0 )
		{
			Stats = EmployeeStats.Flat( 300 );
		}
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	// ── XP / Levelling API ────────────────────────────────────────────────────

	/// Award experience. Cascades through multiple level-ups if a single grant
	/// is huge (e.g. shipping a smash-hit game).
	public void AddXp( long amount )
	{
		if ( amount <= 0 ) return;
		Xp += amount;

		while ( Xp >= XpForNextLevel )
		{
			Xp -= XpForNextLevel;
			Level++;
			Notifications.Push( "Level Up!",
				$"You reached Level {Level}.", "success", duration: 6f );
			OnLevelUp?.Invoke( Level );
		}
	}

	// ── Ability API ───────────────────────────────────────────────────────────

	/// Spend a level-granted slot to unlock a founder perk.
	/// Returns false if no slots remain or the ability is already taken.
	public bool TryUnlockAbility( PlayerAbility ability )
	{
		if ( Abilities.Contains( ability ) ) return false;
		if ( AbilitySlotsRemaining <= 0 )
		{
			Notifications.Push( "No slots",
				$"Level up to unlock more founder abilities.", "warning" );
			return false;
		}

		Abilities.Add( ability );
		Notifications.Push( "Ability Unlocked",
			ability.DisplayName(), "success", duration: 6f );
		OnAbilityUnlocked?.Invoke( ability );
		return true;
	}

	/// Debug / save-loader entry-point: unlock without consuming a slot.
	public void ForceGrantAbility( PlayerAbility ability )
	{
		Abilities.Add( ability );
	}

	/// Wipe everything (new save, debug reset).
	/// Named ResetProgress instead of Reset so it doesn't shadow
	/// <c>Component.Reset()</c>, which the engine calls on its own schedule.
	public void ResetProgress()
	{
		Level     = 1;
		Xp        = 0;
		Abilities = new HashSet<PlayerAbility>();
		Stats     = EmployeeStats.Flat( 300 );
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public FounderSave Save() => new()
	{
		Name                    = Name,
		Role                    = Role,
		Level                   = Level,
		Xp                      = Xp,
		Stats                   = Stats,
		Abilities               = new List<PlayerAbility>( Abilities ),
		ActiveTraining          = ActiveTraining,
		ResearchTopicId         = ResearchTopicId,
		DaysIntoCurrentResearch = DaysIntoCurrentResearch,
	};

	public void Load( FounderSave dto )
	{
		if ( dto is null ) return;
		Name                    = dto.Name ?? "Founder";
		Role                    = dto.Role;
		Level                   = Math.Max( 1, dto.Level );
		Xp                      = dto.Xp;
		Stats                   = dto.Stats ?? EmployeeStats.Flat( 300 );
		Abilities               = new HashSet<PlayerAbility>( dto.Abilities ?? new List<PlayerAbility>() );
		ActiveTraining          = dto.ActiveTraining;
		ResearchTopicId         = dto.ResearchTopicId ?? "";
		DaysIntoCurrentResearch = dto.DaysIntoCurrentResearch;
	}
}
