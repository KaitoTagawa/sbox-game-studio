/// <summary>
/// What stage a <see cref="GameProject"/> is in.
///
///   • <see cref="Setup"/>      — player is moving through the three setup
///                                pages (title/genre, employee allocation,
///                                time allocation). Production hasn't started.
///   • <see cref="Production"/> — the project is ticking. Progress / points
///                                accumulate over real seconds.
///   • <see cref="Finished"/>   — production hit 100 % on every pillar; the
///                                game is ready to ship and post results.
/// </summary>
public enum GameProjectPhase
{
	Setup,
	Production,
	Finished,
}

/// <summary>
/// One in-progress game.
///
/// Mutable, lives on <see cref="GameProjectManager.Current"/>. The setup pages
/// fill in <see cref="Title"/>, <see cref="Genres"/>, <see cref="Assignments"/>,
/// and the three time-allocation fields. Once the player commits, the manager
/// flips <see cref="Phase"/> to <see cref="GameProjectPhase.Production"/> and
/// starts ticking the progress / points fields.
/// </summary>
public sealed class GameProject
{
	// ── Page 1 — identity ───────────────────────────────────────────────────

	public string Title { get; set; } = "";

	/// Selected genre tags. Capped by <see cref="GameProjectManager.GenreSlots"/>.
	public List<GameGenre> Genres { get; } = new();

	// ── Page 2 — worker allocation ──────────────────────────────────────────
	// The same worker can appear in multiple roles; their effective contribution
	// halves with each extra role they cover. Lookup of "how many roles is this
	// worker in" is computed on demand from this dictionary, so no separate
	// counter to keep in sync.
	//
	// Stored as <see cref="IDevWorker"/> so both hired NPCs and the studio
	// founder (PlayerStats) can be slotted into roles through one list.

	public Dictionary<GameDevRole, List<IDevWorker>> Assignments { get; } = new()
	{
		[GameDevRole.ProjectManager] = new(),
		[GameDevRole.Producer]       = new(),
		[GameDevRole.GameDirector]   = new(),
		[GameDevRole.SoundDirector]  = new(),
		[GameDevRole.ArtDirector]    = new(),
	};

	// ── Page 3 — time allocation ────────────────────────────────────────────
	// 0–1 fractions. 0.6 is the recommended default; the curve is logarithmic
	// so going from 0.6 → 1.0 costs disproportionately more time for marginal
	// quality gain (computed by GameProjectManager).

	public float DesignTime    { get; set; } = 0.6f;
	public float SoundTime     { get; set; } = 0.6f;
	public float GraphicsTime  { get; set; } = 0.6f;

	// ── Production state (Step 5 fills these in) ────────────────────────────
	// 0–1 progress per pillar; reaching 1 freezes that pillar's points total.

	public float DesignProgress    { get; set; }
	public float SoundProgress     { get; set; }
	public float GraphicsProgress  { get; set; }

	/// 1–1000 quality points per pillar — accumulated over production ticks.
	public int   DesignPoints      { get; set; }
	public int   SoundPoints       { get; set; }
	public int   GraphicsPoints    { get; set; }

	/// Persistent pillar-points bonuses earned mid-production from accepted
	/// employee suggestions. <see cref="GameProjectManager.AdvancePillar"/>
	/// re-derives <see cref="DesignPoints"/> from progress × target each
	/// tick, so without these tracker fields the +N from <see cref="GameProjectManager.AddPillarBonus"/>
	/// would get wiped on the next tick. Added on top of the computed
	/// points every tick AND at ship-time Snap.
	public int   DesignBonusPoints   { get; set; }
	public int   SoundBonusPoints    { get; set; }
	public int   GraphicsBonusPoints { get; set; }

	/// Persistent pillar-points penalties accumulated during ticks where
	/// any contributing NPC is in <see cref="EmployeeMood.Bad"/>. Mirror
	/// of the bonus tracker — needed because the live-ceiling model
	/// (<c>points = progress × target + bonus</c>) silently recovers
	/// missed ground the moment mood heals. Accumulating the per-tick
	/// "would-have-earned at full mood" delta makes the time spent at
	/// half strength a permanent score tax.
	public int   DesignPenaltyPoints   { get; set; }
	public int   SoundPenaltyPoints    { get; set; }
	public int   GraphicsPenaltyPoints { get; set; }

	/// First-time-per-production gates for the mood-event toasts. The
	/// player gets ONE warning toast for the first frustrated NPC and ONE
	/// info toast for the first NPC with an idea — every subsequent mood
	/// roll in that production is silent (just the in-world FX), so the
	/// notification stack doesn't get spammed at busy late-game studios.
	/// Reset when a new project enters Production.
	public bool  BadMoodToastShown    { get; set; }
	public bool  GoodMoodToastShown   { get; set; }

	/// Set true at Production-entry on the player's 2nd game so the next
	/// eligible NPC mood-tick is forced to fire (Good and Bad respectively).
	/// Guarantees the player sees a Good and a Bad mood event during their
	/// second project so they learn the mood / chat mechanic exists.
	/// Cleared by EmployeeNPC.TickMood once consumed.
	public bool  GuaranteeGoodMoodPending { get; set; }
	public bool  GuaranteeBadMoodPending  { get; set; }

	// ── Lifecycle ───────────────────────────────────────────────────────────

	public GameProjectPhase Phase { get; set; } = GameProjectPhase.Setup;

	/// True once every pillar has hit 100 % progress.
	public bool IsProductionComplete =>
		DesignProgress >= 1f && SoundProgress >= 1f && GraphicsProgress >= 1f;

	/// True when the project was fast-finished via a Smoke Break consumable.
	/// Suppresses the post-publish XP award (you don't learn from skipped
	/// work) and may gate other "did you actually develop this game?" checks.
	public bool WasSmokeBroken { get; set; }

	// ── Derived helpers ─────────────────────────────────────────────────────

	/// Sum of pair-wise synergy across the chosen tag set (see GameGenres).
	public float Synergy => GameGenres.TotalSynergy( Genres );

	/// Combined per-tag effort multiplier (product). Drives production duration.
	public float EffortMultiplier => GameGenres.TotalEffort( Genres );

	/// Combined per-tag revenue multiplier (product). Drives ship-time payoff.
	public float RevenueMultiplier => GameGenres.TotalRevenue( Genres );

	/// How many distinct roles a given worker currently covers on this project.
	/// Returns 0 if they're not assigned anywhere.
	public int RoleCountFor( IDevWorker worker )
	{
		if ( worker is null ) return 0;
		int n = 0;
		foreach ( var kv in Assignments )
			if ( kv.Value.Contains( worker ) ) n++;
		return n;
	}

	/// Workers not slotted into any role on this project. Includes both hired
	/// staff and the founder. Useful for "lone wolf" abilities like
	/// <see cref="EmployeeAbility.SuperHacker"/> that only pay off when the
	/// holder is left alone — Step 5's tick reads from here to apply their
	/// hidden bonus.
	public IEnumerable<IDevWorker> UnassignedStaff
	{
		get
		{
			// Founder first — they show up at the top of the assignment grid
			// too, so this keeps the iteration order consistent.
			var player = PlayerStats.Instance;
			if ( player is not null && RoleCountFor( player ) == 0 )
				yield return player;

			var staff = HRManager.Instance?.Staff;
			if ( staff is null ) yield break;
			foreach ( var npc in staff )
			{
				if ( RoleCountFor( npc ) == 0 ) yield return npc;
			}
		}
	}

	/// Efficiency multiplier applied to an employee's contribution given how
	/// many roles they're already covering. 1 role → 100 %, 2 → 50 %, 3 → 25 %, …
	/// Intended to be called *with the role being evaluated already counted*
	/// in <see cref="RoleCountFor"/>; halving starts at the second slot.
	public static float EfficiencyForRoleCount( int roleCount )
	{
		if ( roleCount <= 0 ) return 0f;
		return 1f / (1 << (roleCount - 1));   // 1, 0.5, 0.25, 0.125, …
	}

	/// Average <c>Focus.Average</c> across every uniquely-assigned worker on
	/// this project (counted once even if covering multiple roles). Returns
	/// 0 when no one is assigned. Kept for any UI / metrics that want a
	/// quality readout independent of headcount.
	public float AvgTeamFocus
	{
		get
		{
			var seen = new HashSet<IDevWorker>();
			int sum = 0;
			foreach ( var kv in Assignments )
			{
				foreach ( var w in kv.Value )
				{
					if ( w is null )    continue;
					if ( !seen.Add(w) ) continue;
					sum += w.Stats?.Focus?.Average ?? 0;
				}
			}
			return seen.Count == 0 ? 0f : (float)sum / seen.Count;
		}
	}

	/// Sum of <c>Focus.Average</c> across every uniquely-assigned worker
	/// (counted once even if covering multiple roles). Drives the project-
	/// driven energy regen on <c>GameManager</c> — both *quality* (per-
	/// worker Focus) and *headcount* (more workers) lift the rate, so a
	/// larger studio refills the founder's energy noticeably faster than a
	/// solo developer does.
	public float TotalTeamFocus
	{
		get
		{
			var seen = new HashSet<IDevWorker>();
			int sum = 0;
			foreach ( var kv in Assignments )
			{
				foreach ( var w in kv.Value )
				{
					if ( w is null )    continue;
					if ( !seen.Add(w) ) continue;
					sum += w.Stats?.Focus?.Average ?? 0;
				}
			}
			return sum;
		}
	}
}
