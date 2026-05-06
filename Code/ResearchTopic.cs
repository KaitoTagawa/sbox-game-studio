/// <summary>
/// One research topic a worker can be assigned to. Replaces the per-stat
/// drip model — each topic has a defined endpoint, a target genre to unlock,
/// a primary stat that drives speed, and an optional gating predicate that
/// hides the topic until a triggering event fires (e.g., "5 games shipped"
/// for Friendship Dynamics → Local Co-op).
///
/// Lifecycle:
///   1. Topic appears in the Research tab when <see cref="Available"/> is true.
///   2. Player assigns one worker → <see cref="TrainingManager.StartResearch"/>.
///   3. <see cref="TrainingManager.OnDayStart"/> ticks `DaysIntoCurrentResearch`
///      every in-game day; total days = `BaseDays × (100 / max(20, primaryStat))`.
///   4. On completion, the unlocked genre is added to
///      <see cref="Achievements.ResearchedGenres"/> and `GameGenres.IsUnlocked`
///      starts returning true for it.
///
/// One topic per worker at a time. Cancelling refunds nothing — the energy
/// + time are sunk. Founder is eligible (research is on-desk, not off-site).
/// </summary>
public sealed class ResearchTopic
{
	public string  Id          { get; init; } = "";
	public string  Name        { get; init; } = "";
	public string  Description { get; init; } = "";

	/// Genre this topic unlocks on completion. Null = the topic is purely
	/// flavor / pre-requisite chain (none today, but the field exists for
	/// later "research foundations" topics that gate other research).
	public GameGenre? UnlocksGenre { get; init; }

	/// Which main stat the assigned worker uses to drive speed. Higher
	/// stat → faster completion (see <see cref="TrainingManager.ResearchTotalDays"/>).
	public MainStat PrimaryStat { get; init; }

	/// Days the topic takes when the worker's <see cref="PrimaryStat"/>
	/// average is exactly 100. Scales 1/x with stat — a 300-stat worker
	/// finishes a 60-day topic in 20 days; a flat-30 founder takes ~200.
	public int BaseDays { get; init; } = 60;

	/// Predicate that hides the topic from the player until the gate is
	/// satisfied. Default: always available. Override for event-triggered
	/// topics (e.g., `() => Achievements.GamesShipped >= 5`).
	public System.Func<bool> Available { get; init; } = () => true;
}

/// <summary>
/// Static catalogue of every research topic. Order is the default UI order
/// in the Research tab. Adding a new topic: append an entry below + (if it
/// unlocks a new genre) add a `GameGenre` value and a matching catalogue
/// entry in <see cref="GameGenres"/>.
/// </summary>
public static class ResearchTopics
{
	public static readonly IReadOnlyList<ResearchTopic> All = new ResearchTopic[]
	{
		// ── Always-available topics (foundation set) ────────────────────────
		new()
		{
			Id           = "car_mechanics",
			Name         = "Car Mechanics",
			Description  = "Drivetrains, tyre physics, racing lines. Unlocks Racing.",
			UnlocksGenre = GameGenre.Racing,
			PrimaryStat  = MainStat.Programming,
			BaseDays     = 60,
		},
		new()
		{
			Id           = "horror_psychology",
			Name         = "Horror Psychology",
			Description  = "What scares people, and why. Unlocks Horror.",
			UnlocksGenre = GameGenre.Horror,
			PrimaryStat  = MainStat.Creativity,
			BaseDays     = 60,
		},
		new()
		{
			Id           = "cognitive_design",
			Name         = "Cognitive Design",
			Description  = "Patterns, problem-solving, the satisfaction loop. Unlocks Puzzle.",
			UnlocksGenre = GameGenre.Puzzle,
			PrimaryStat  = MainStat.Design,
			BaseDays     = 50,
		},
		new()
		{
			Id           = "narrative_systems",
			Name         = "Narrative Systems",
			Description  = "Branching dialogue, quest graphs, character arcs. Unlocks RPG.",
			UnlocksGenre = GameGenre.RPG,
			PrimaryStat  = MainStat.Creativity,
			BaseDays     = 90,
		},
		new()
		{
			Id           = "gambling_psychology",
			Name         = "Gambling Psychology",
			Description  = "Variable rewards, near-misses, the dopamine math. Unlocks Pack Opening.",
			UnlocksGenre = GameGenre.PackOpening,
			PrimaryStat  = MainStat.Creativity,
			BaseDays     = 70,
		},

		// ── Event-gated topics ──────────────────────────────────────────────
		new()
		{
			Id           = "simulation_math",
			Name         = "Simulation Math",
			Description  = "Differential equations, agent systems, emergent rules. Unlocks Simulation.",
			UnlocksGenre = GameGenre.Simulation,
			PrimaryStat  = MainStat.Programming,
			BaseDays     = 90,
			Available    = () => Achievements.GamesShipped >= 3,
		},
		new()
		{
			Id           = "wilderness_studies",
			Name         = "Wilderness Studies",
			Description  = "Hunger curves, shelter, weather. Unlocks Survival.",
			UnlocksGenre = GameGenre.Survival,
			PrimaryStat  = MainStat.Design,
			BaseDays     = 80,
			Available    = () => Achievements.IsUnlocked( AchievementId.Earn10k ),
		},
		new()
		{
			Id           = "friendship_dynamics",
			Name         = "Friendship Dynamics",
			Description  = "Couch-co-op pacing, shared-screen energy. Unlocks Local Co-op.",
			UnlocksGenre = GameGenre.LocalCoop,
			PrimaryStat  = MainStat.Creativity,
			BaseDays     = 90,
			Available    = () => Achievements.GamesShipped >= 5,
		},

		// ── Second wave (rolled into the inbox over time) ──────────────────

		new() { Id = "espionage_tactics",   Name = "Espionage Tactics",
			Description = "Vision cones, alert states, the patience of a shadow. Unlocks Stealth.",
			UnlocksGenre = GameGenre.Stealth, PrimaryStat = MainStat.Design, BaseDays = 70 },

		new() { Id = "movement_mechanics",  Name = "Movement Mechanics",
			Description = "Coyote time, double-jumps, air control. Unlocks Platformer.",
			UnlocksGenre = GameGenre.Platformer, PrimaryStat = MainStat.Programming, BaseDays = 50 },

		new() { Id = "combat_choreography", Name = "Combat Choreography",
			Description = "Frames, hitboxes, the dance of duels. Unlocks Fighting.",
			UnlocksGenre = GameGenre.Fighting, PrimaryStat = MainStat.Design, BaseDays = 70 },

		new() { Id = "battlefield_tactics", Name = "Battlefield Tactics",
			Description = "Resources, build orders, lane control. Unlocks RTS.",
			UnlocksGenre = GameGenre.RTS, PrimaryStat = MainStat.Programming, BaseDays = 90 },

		new() { Id = "strategic_planning",  Name = "Strategic Planning",
			Description = "Branching options, hidden info, long horizons. Unlocks Turn-Based.",
			UnlocksGenre = GameGenre.TBS, PrimaryStat = MainStat.Focus, BaseDays = 80 },

		new() { Id = "deckbuilding_theory", Name = "Deckbuilding Theory",
			Description = "Curves, archetypes, the math of variance. Unlocks Card Game.",
			UnlocksGenre = GameGenre.CardGame, PrimaryStat = MainStat.Design, BaseDays = 60 },

		new() { Id = "audio_synchronization", Name = "Audio Synchronization",
			Description = "Beat detection, latency budgets, hit windows. Unlocks Rhythm.",
			UnlocksGenre = GameGenre.RhythmGame, PrimaryStat = MainStat.Sound, BaseDays = 60 },

		new() { Id = "business_theory",     Name = "Business Theory",
			Description = "Profit margins, growth loops, the art of the line graph. Unlocks Tycoon.",
			UnlocksGenre = GameGenre.Tycoon, PrimaryStat = MainStat.Focus, BaseDays = 70,
			Available    = () => Achievements.GamesShipped >= 3 },

		new() { Id = "urban_planning",      Name = "Urban Planning",
			Description = "Zoning, traffic flow, civic happiness. Unlocks City Builder.",
			UnlocksGenre = GameGenre.CityBuilder, PrimaryStat = MainStat.Design, BaseDays = 90,
			Available    = () => Achievements.GamesShipped >= 7 },

		new() { Id = "materials_science",   Name = "Materials Science",
			Description = "Recipes, durability, refinement chains. Unlocks Crafting.",
			UnlocksGenre = GameGenre.Crafting, PrimaryStat = MainStat.Programming, BaseDays = 60 },

		new() { Id = "astronomy",           Name = "Astronomy",
			Description = "Star systems, FTL hand-waves, scale. Unlocks Space Opera.",
			UnlocksGenre = GameGenre.SpaceOpera, PrimaryStat = MainStat.Creativity, BaseDays = 80,
			Available    = () => Achievements.GamesShipped >= 4 },

		new() { Id = "futurism",            Name = "Futurism",
			Description = "Neon, augmentations, megacorps. Unlocks Cyberpunk.",
			UnlocksGenre = GameGenre.Cyberpunk, PrimaryStat = MainStat.Creativity, BaseDays = 70 },

		new() { Id = "industrial_history",  Name = "Industrial History",
			Description = "Steam, brass, anachronistic tech. Unlocks Steampunk.",
			UnlocksGenre = GameGenre.Steampunk, PrimaryStat = MainStat.Creativity, BaseDays = 70 },

		new() { Id = "apocalyptic_studies", Name = "Apocalyptic Studies",
			Description = "Wasteland economies, scavenging, hope-and-rust. Unlocks Post-Apocalyptic.",
			UnlocksGenre = GameGenre.PostApocalyptic, PrimaryStat = MainStat.Creativity, BaseDays = 70 },

		new() { Id = "mythology",           Name = "Mythology",
			Description = "Pantheons, hero journeys, the bones of fantasy. Unlocks Fantasy.",
			UnlocksGenre = GameGenre.Fantasy, PrimaryStat = MainStat.Creativity, BaseDays = 70,
			Available    = () => Achievements.GamesShipped >= 2 },

		new() { Id = "forensic_studies",    Name = "Forensic Studies",
			Description = "Clues, alibis, the satisfying click of solving. Unlocks Mystery.",
			UnlocksGenre = GameGenre.Mystery, PrimaryStat = MainStat.Focus, BaseDays = 60 },

		new() { Id = "frontier_history",    Name = "Frontier History",
			Description = "Saloons, showdowns, dust on boots. Unlocks Western.",
			UnlocksGenre = GameGenre.Western, PrimaryStat = MainStat.Creativity, BaseDays = 60 },

		new() { Id = "spatial_computing",   Name = "Spatial Computing",
			Description = "Headset interaction, motion sickness budgets, 6DOF. Unlocks VR.",
			UnlocksGenre = GameGenre.VR, PrimaryStat = MainStat.Programming, BaseDays = 120,
			Available    = () => Achievements.IsUnlocked( AchievementId.Earn100k ) },

		new() { Id = "pedagogy",            Name = "Pedagogy",
			Description = "How people learn, retain, and re-engage. Unlocks Educational.",
			UnlocksGenre = GameGenre.Educational, PrimaryStat = MainStat.Design, BaseDays = 60 },

		new() { Id = "audience_research",   Name = "Audience Research",
			Description = "Onboarding curves, attention budgets, the snack-game shape. Unlocks Casual.",
			UnlocksGenre = GameGenre.Casual, PrimaryStat = MainStat.Creativity, BaseDays = 50 },

		// ── Third wave: matches the 5 new research-gated genres added
		//    alongside this file in GameGenre.cs. ──────────────────────
		new() { Id = "emergent_systems",    Name = "Emergent Systems",
			Description = "Rules that play out beyond their author. Unlocks Sandbox.",
			UnlocksGenre = GameGenre.Sandbox, PrimaryStat = MainStat.Programming, BaseDays = 70 },

		new() { Id = "world_streaming",     Name = "World Streaming",
			Description = "Endless terrain, seamless transitions, level-of-detail pipelines. Unlocks Open World.",
			UnlocksGenre = GameGenre.OpenWorld, PrimaryStat = MainStat.Programming, BaseDays = 110,
			Available    = () => Achievements.GamesShipped >= 6 },

		new() { Id = "lobby_economics",     Name = "Lobby Economics",
			Description = "Match shape, drop curves, last-player-standing math. Unlocks Battle Royale.",
			UnlocksGenre = GameGenre.BattleRoyale, PrimaryStat = MainStat.Design, BaseDays = 90,
			Available    = () => Achievements.GamesShipped >= 5 },

		new() { Id = "wave_pacing",         Name = "Wave Pacing",
			Description = "Spawn timings, lane geometry, the satisfaction of a perfect choke. Unlocks Tower Defense.",
			UnlocksGenre = GameGenre.TowerDefense, PrimaryStat = MainStat.Design, BaseDays = 60 },

		new() { Id = "synergy_theory",      Name = "Synergy Theory",
			Description = "Build crafting at runtime, archetype clusters, randomised draft loops. Unlocks Auto-Battler.",
			UnlocksGenre = GameGenre.AutoBattler, PrimaryStat = MainStat.Design, BaseDays = 70 },
	};

	public static ResearchTopic Get( string id )
	{
		if ( string.IsNullOrEmpty( id ) ) return null;
		foreach ( var t in All )
			if ( t.Id == id ) return t;
		return null;
	}

	/// All topics whose <see cref="ResearchTopic.Available"/> predicate is
	/// true right now AND whose target genre isn't already unlocked. Drives
	/// the Research tab's visible list.
	public static IEnumerable<ResearchTopic> Listable()
	{
		foreach ( var t in All )
		{
			if ( !t.Available() ) continue;
			if ( t.UnlocksGenre is { } g && Achievements.ResearchedGenres.Contains( g ) ) continue;
			yield return t;
		}
	}
}
