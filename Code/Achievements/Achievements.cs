/// <summary>
/// Central achievement tracker. Static so any system can call it without
/// scene wiring — call <c>Achievements.RecordX(...)</c> when something
/// happens, and gate content with <c>Achievements.IsUnlocked( id )</c>.
///
/// Stats live here for now (in-memory only). When save/load lands, persist
/// _unlocked + the four counters + the abilities set.
/// </summary>
public static class Achievements
{
	// ── Tracked stats ───────────────────────────────────────────────────────

	public static long LifetimeEarned     { get; private set; }
	public static int  TotalHires         { get; private set; }
	public static long PeakMonthlyPlayers { get; private set; }
	public static int  GamesShipped       { get; private set; }

	/// Distinct abilities seen on at least one hired employee.
	public static HashSet<EmployeeAbility> AbilitiesDiscovered { get; } = new();

	/// Genres unlocked by completing a <see cref="ResearchTopic"/>. Parallel
	/// path to the existing achievement-gated genre unlocks — `GameGenres.IsUnlocked`
	/// returns true if EITHER the achievement OR the research is satisfied.
	public static HashSet<GameGenre> ResearchedGenres { get; } = new();

	/// Live snapshot of staff size — pulled from HRManager when needed
	/// rather than tracked here, so fires/quits stay in sync automatically.
	public static int StaffSize => HRManager.Instance?.Staff.Count ?? 0;

	// ── Unlock state ────────────────────────────────────────────────────────

	static readonly HashSet<AchievementId> _unlocked = new();

	public static IReadOnlyCollection<AchievementId> Unlocked => _unlocked;

	public static event Action<Achievement> OnUnlocked;

	public static bool IsUnlocked( AchievementId id ) => _unlocked.Contains( id );

	public static Achievement Get( AchievementId id )
	{
		foreach ( var a in Registry )
			if ( a.Id == id ) return a;
		return null;
	}

	public static IReadOnlyList<Achievement> All => Registry;

	// ── Recording APIs ──────────────────────────────────────────────────────
	// Called by other systems whenever a tracked event occurs. Each one runs
	// the full evaluation pass — cheap with ~12 achievements; revisit if the
	// list grows large.

	public static void RecordMoneyEarned( long amount )
	{
		if ( amount <= 0 ) return;
		LifetimeEarned += amount;
		EvaluateAll();
	}

	public static void RecordHire( EmployeeNPC npc )
	{
		if ( npc is null ) return;
		TotalHires++;
		if ( npc.Ability1 is { } a1 ) AbilitiesDiscovered.Add( a1 );
		if ( npc.Ability2 is { } a2 ) AbilitiesDiscovered.Add( a2 );
		EvaluateAll();
	}

	public static void RecordMonthlyPlayers( long peakPlayers )
	{
		if ( peakPlayers > PeakMonthlyPlayers )
			PeakMonthlyPlayers = peakPlayers;
		EvaluateAll();
	}

	/// Called by <see cref="Gallery.RecordShippedGame"/> every time a project
	/// publishes. Drives the Ship*Games achievement gates and (via those
	/// gates) the higher job-posting tiers. The first five ships also each
	/// auto-unlock a foundation genre (see <see cref="ShipMilestoneGenres"/>),
	/// graduating the player out of "default 5 tags only" without making them
	/// research the basics.
	public static void RecordShippedGame()
	{
		GamesShipped++;
		AwardShipMilestoneGenre( GamesShipped );
		EvaluateAll();
	}

	/// Genre awarded on each of the first five ships. Index 0 = ship #1.
	/// Each entry corresponds to a research-pool genre — the in-flight
	/// research topic, if any, still completes cleanly because
	/// <see cref="RecordGenreResearched"/> no-ops on a second add. Ship 5's
	/// reward (Mystery) was chosen as a deliberately neutral 1.0×/1.0× capper
	/// so the milestone payoff doesn't run away with a high-revenue tag.
	static readonly GameGenre[] ShipMilestoneGenres =
	{
		GameGenre.Puzzle,        // ship 1 — Cognitive Design topic
		GameGenre.Racing,        // ship 2 — Car Mechanics topic
		GameGenre.Horror,        // ship 3 — Horror Psychology topic
		GameGenre.PackOpening,   // ship 4 — Gambling Psychology topic
		GameGenre.Mystery,       // ship 5 — Forensic Studies topic
	};

	static void AwardShipMilestoneGenre( int shipNumber )
	{
		if ( shipNumber < 1 ) return;
		if ( shipNumber > ShipMilestoneGenres.Length ) return;
		RecordGenreResearched( ShipMilestoneGenres[shipNumber - 1] );
	}

	/// Called by <see cref="TrainingManager.CompleteResearch"/> when a worker
	/// finishes a topic. Adds the genre to <see cref="ResearchedGenres"/> so
	/// `GameGenres.IsUnlocked` reports it open, and pushes a notification.
	public static void RecordGenreResearched( GameGenre g )
	{
		if ( !ResearchedGenres.Add( g ) ) return;
		var info = GameGenres.Get( g );
		Notifications.Push( "Genre Unlocked",
			info is not null
				? $"{info.Name} is now available for new projects."
				: $"{g} is now available.",
			"success", duration: 8f );
	}

	/// Manual unlock — useful for cheats / debug menu.
	public static void ForceUnlock( AchievementId id )
	{
		var ach = Get( id );
		if ( ach != null && !_unlocked.Contains( id ) )
			Unlock( ach );
	}

	/// Wipe everything (new save, debug reset).
	public static void Reset()
	{
		_unlocked.Clear();
		AbilitiesDiscovered.Clear();
		ResearchedGenres.Clear();
		LifetimeEarned = 0;
		TotalHires = 0;
		PeakMonthlyPlayers = 0;
		GamesShipped = 0;
	}

	// ── Save / Load (per ADR-0001) ──────────────────────────────────────────

	public static AchievementsSave Save() => new()
	{
		Unlocked            = new List<AchievementId>( _unlocked ),
		AbilitiesDiscovered = new List<EmployeeAbility>( AbilitiesDiscovered ),
		ResearchedGenres    = new List<GameGenre>( ResearchedGenres ),
		LifetimeEarned      = LifetimeEarned,
		TotalHires          = TotalHires,
		PeakMonthlyPlayers  = PeakMonthlyPlayers,
		GamesShipped        = GamesShipped,
	};

	public static void Load( AchievementsSave dto )
	{
		Reset();
		if ( dto is null ) return;
		foreach ( var id in dto.Unlocked            ) _unlocked.Add( id );
		foreach ( var a  in dto.AbilitiesDiscovered ) AbilitiesDiscovered.Add( a );
		foreach ( var g  in dto.ResearchedGenres    ) ResearchedGenres.Add( g );
		LifetimeEarned     = dto.LifetimeEarned;
		TotalHires         = dto.TotalHires;
		PeakMonthlyPlayers = dto.PeakMonthlyPlayers;
		GamesShipped       = dto.GamesShipped;
	}

	// ── Evaluation ──────────────────────────────────────────────────────────

	static void EvaluateAll()
	{
		foreach ( var ach in Registry )
		{
			if ( _unlocked.Contains( ach.Id ) ) continue;
			if ( ach.Evaluate?.Invoke() == true )
				Unlock( ach );
		}
	}

	static void Unlock( Achievement ach )
	{
		_unlocked.Add( ach.Id );

		// Cash bonus, if any. Funding ≠ earnings — investors paying for a
		// milestone shouldn't count toward Earn-N achievements. AddMoney no
		// longer auto-tracks earnings (Gallery sales paths call
		// RecordMoneyEarned explicitly), so this credit is naturally
		// excluded from lifetime-earnings tracking.
		var gm = GameManager.Instance;
		if ( ach.MoneyBonus > 0 && gm is not null )
			gm.AddMoney( ach.MoneyBonus );

		string body = ach.MoneyBonus > 0
			? $"{ach.Name} — funding +${ach.MoneyBonus:N0}"
			: ach.Name;
		Notifications.Push( "Achievement Unlocked", body, "success", duration: 8f );

		Log.Info( $"[Achievement] {ach.Name} — {ach.Description}"
			+ (ach.MoneyBonus > 0 ? $" (funding +${ach.MoneyBonus:N0})" : "") );
		OnUnlocked?.Invoke( ach );

		// Bridge to the sbox.game platform's achievement service so the
		// player's account-level achievement list mirrors the in-game one.
		// Only fires on first-time unlocks (Load() refills _unlocked directly,
		// it doesn't re-enter this method) — matches what the platform expects.
		// Wrapped in try/catch because Sandbox.Services.Achievements may throw
		// when running outside a published-game context (e.g. local editor
		// session without an sbox.game project linked); we don't want a
		// missing platform to spoil the in-game unlock notification.
		BridgeUnlockToPlatform( ach );
	}

	// ── Platform bridge (sbox.game achievements) ──────────────────────────────

	/// Master kill switch for the sbox.game platform achievement bridge.
	/// Set to <c>false</c> to keep all unlocks purely in-game (useful when
	/// playtesting before the sbox.game project page has any achievements
	/// registered, or if the platform service is misbehaving). `static
	/// readonly` instead of `const` so the early-return branch isn't dead
	/// code at compile time (CS0162).
	static readonly bool BridgeToPlatform = true;

	/// In-game <see cref="AchievementId"/> → sbox.game dashboard string ID.
	/// These IDs must match what's registered on the project's sbox.game
	/// page; the dashboard is the source of truth, this map just routes
	/// in-game unlocks to the right platform entry. Convention is
	/// lowercase snake_case so they read cleanly in the dashboard URL.
	///
	/// Adding a new <see cref="AchievementId"/>? Add an entry here AND
	/// register it on the dashboard. Missing entries are logged but don't
	/// throw — the in-game unlock still fires.
	static readonly Dictionary<AchievementId, string> PlatformIdMap = new()
	{
		{ AchievementId.Earn1k,               "earn_1k"               },
		{ AchievementId.Earn10k,              "earn_10k"              },
		{ AchievementId.Earn100k,             "earn_100k"             },
		{ AchievementId.Earn1M,               "earn_1m"               },
		{ AchievementId.FirstHire,            "first_hire"            },
		{ AchievementId.Hire5,                "hire_5"                },
		{ AchievementId.Hire8,                "hire_8"                },
		{ AchievementId.Hire10,               "hire_10"               },
		{ AchievementId.Players100,           "players_100"           },
		{ AchievementId.Players10k,           "players_10k"           },
		{ AchievementId.Players1M,            "players_1m"            },
		{ AchievementId.DiscoverFirstAbility, "discover_first_ability"},
		{ AchievementId.DiscoverAllAbilities, "discover_all_abilities"},
		{ AchievementId.ShipFirstGame,        "ship_first_game"       },
		{ AchievementId.Ship3Games,           "ship_3_games"          },
		{ AchievementId.Ship5Games,           "ship_5_games"          },
		{ AchievementId.Ship10Games,          "ship_10_games"         },
		{ AchievementId.TutorialComplete,     "tutorial_complete"     },
	};

	static void BridgeUnlockToPlatform( Achievement ach )
	{
		if ( !BridgeToPlatform ) return;

		if ( !PlatformIdMap.TryGetValue( ach.Id, out var platformId ) )
		{
			Log.Warning( $"[Achievement] no platform mapping for {ach.Id} — " +
			             "add it to PlatformIdMap and register on sbox.game." );
			return;
		}

		try
		{
			Sandbox.Services.Achievements.Unlock( platformId );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Achievement] platform unlock failed for '{platformId}': {ex.Message}" );
		}
	}

	// ── Registry ────────────────────────────────────────────────────────────

	static readonly List<Achievement> Registry = new()
	{
		// Money — no bonus on Earn-N (would feel like "you earned $1k, here's
		// $1k more for earning $1k"; the achievement itself is the marker).
		new() { Id = AchievementId.Earn1k,   Name = "First Grand",
		        Description = "Earn $1,000 lifetime.",
		        Evaluate = () => LifetimeEarned >= 500 },
		new() { Id = AchievementId.Earn10k,  Name = "Five Figures",
		        Description = "Earn $10,000 lifetime.",
		        Evaluate = () => LifetimeEarned >= 2_000 },
		new() { Id = AchievementId.Earn100k, Name = "Six Figures",
		        Description = "Earn $100,000 lifetime.",
		        Evaluate = () => LifetimeEarned >= 100_000 },
		new() { Id = AchievementId.Earn1M,   Name = "Millionaire",
		        Description = "Earn $1,000,000 lifetime.",
		        Evaluate = () => LifetimeEarned >= 1_000_000 },

		// Hiring — small staffing-budget grants for milestone hires.
		new() { Id = AchievementId.FirstHire, Name = "First Recruit",
		        Description = "Hire your first employee.",
		        Evaluate = () => TotalHires >= 1,
		        MoneyBonus = 200 },
		new() { Id = AchievementId.Hire5,     Name = "Studio Built",
		        Description = "Hire 5 employees over your career.",
		        Evaluate = () => TotalHires >= 5,
		        MoneyBonus = 1_000 },
		new() { Id = AchievementId.Hire8,     Name = "Eight Strong",
		        Description = "Hire 8 employees over your career.",
		        Evaluate = () => TotalHires >= 8,
		        MoneyBonus = 2_500 },
		new() { Id = AchievementId.Hire10,    Name = "Studio Veteran",
		        Description = "Hire 10 employees over your career.",
		        Evaluate = () => TotalHires >= 10,
		        MoneyBonus = 5_000 },

		// Player counts — investor confidence rewards as the audience grows.
		new() { Id = AchievementId.Players100,  Name = "First Hundred Fans",
		        Description = "Reach 100 monthly players in any released game.",
		        Evaluate = () => PeakMonthlyPlayers >= 100,
		        MoneyBonus = 500 },
		new() { Id = AchievementId.Players10k,  Name = "Cult Hit",
		        Description = "Reach 10,000 monthly players.",
		        Evaluate = () => PeakMonthlyPlayers >= 10_000,
		        MoneyBonus = 5_000 },
		new() { Id = AchievementId.Players1M,   Name = "Mainstream",
		        Description = "Reach 1,000,000 monthly players.",
		        Evaluate = () => PeakMonthlyPlayers >= 1_000_000,
		        IsHidden = true,
		        MoneyBonus = 50_000 },

		// Ability discovery — small "talent agency referral" rewards.
		new() { Id = AchievementId.DiscoverFirstAbility, Name = "Hidden Talents",
		        Description = "Hire someone with at least one ability.",
		        Evaluate = () => AbilitiesDiscovered.Count >= 1,
		        MoneyBonus = 200 },
		new() { Id = AchievementId.DiscoverAllAbilities, Name = "Talent Spotter",
		        Description = "See every ability across your hires.",
		        Evaluate = () => AbilitiesDiscovered.Count >= Enum.GetValues<EmployeeAbility>().Length,
		        IsHidden = true,
		        MoneyBonus = 5_000 },

		// Shipping — the first-ship $300 is the "you have something to spend
		// on the next project" hook the player asked for.
		new() { Id = AchievementId.ShipFirstGame, Name = "Released",
		        Description = "Publish your first game.",
		        Evaluate = () => GamesShipped >= 1,
		        MoneyBonus = 100 },
		new() { Id = AchievementId.Ship3Games,   Name = "Indie Studio",
		        Description = "Publish 3 games. Unlocks the Career Site posting.",
		        Evaluate = () => GamesShipped >= 3,
		        MoneyBonus = 1_000 },
		new() { Id = AchievementId.Ship5Games,   Name = "Catalogue Builder",
		        Description = "Publish 5 games. Unlocks a 3rd genre slot per project.",
		        Evaluate = () => GamesShipped >= 5,
		        MoneyBonus = 1_000 },
		new() { Id = AchievementId.Ship10Games,  Name = "Veteran Studio",
		        Description = "Publish 100 games.",
		        Evaluate = () => GamesShipped >= 100,
		        MoneyBonus = 3_000 },

		// Onboarding — fired manually from TutorialManager.NotifyChairAssigned.
		// Evaluate is a stub (always false) because the trigger isn't a
		// tracked stat; the achievement is awarded via Achievements.ForceUnlock
		// when the player finishes the chair-assignment final step.
		// Money is deposited directly from TutorialManager.NotifyChairAssigned
		// (gated by the ChairAssigned flag, so it fires exactly once per
		// tutorial completion). Setting MoneyBonus here would double-pay
		// any new-game restart that re-unlocks the achievement.
		new() { Id = AchievementId.TutorialComplete, Name = "Studio Open",
		        Description = "Finished the new-studio tutorial.",
		        Evaluate = () => false,
		        MoneyBonus = 0 },
	};
}
