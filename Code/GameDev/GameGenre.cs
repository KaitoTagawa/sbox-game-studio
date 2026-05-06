/// <summary>
/// A genre tag a player can attach to a game during the Create-Game flow.
/// Genres are free-form: every project picks 1–N tags from any category. The
/// chosen set drives revenue (per-tag multiplier), effort (per-tag multiplier),
/// and synergy (per-pair bonus/penalty — see <see cref="GameGenres.Synergy"/>).
///
/// Conceptually the values fall into <see cref="GameGenreCategory"/> buckets,
/// purely for UI grouping. There's no enforcement that you pick one from each
/// bucket — a "2D Multiplayer Roguelike Mobile Story" is a perfectly valid
/// (if chaotic) project.
/// </summary>
public enum GameGenre
{
	// ── Defaults (always unlocked from day one) ─────────────────────────────
	Fishing,        // theme
	TwoD,           // format
	Action,         // gameplay
	Story,          // gameplay
	SinglePlayer,   // audience

	// ── Unlockable ──────────────────────────────────────────────────────────
	Soccer,
	Gambling,
	ThreeD,
	Idle,
	Roguelike,
	FPS,
	TPS,
	MOBA,
	Multiplayer,
	Mobile,
	Gacha,
	PackOpening,

	// ── Research-unlockable (added via ResearchTopic completion) ────────────
	Racing,
	Horror,
	Puzzle,
	Simulation,
	RPG,
	Survival,
	LocalCoop,

	// ── Second wave of research-unlockable genres ───────────────────────────
	Stealth,
	Platformer,
	Fighting,
	RTS,
	TBS,
	CardGame,
	RhythmGame,
	Tycoon,
	CityBuilder,
	Crafting,
	SpaceOpera,
	Cyberpunk,
	Steampunk,
	PostApocalyptic,
	Fantasy,
	Mystery,
	Western,
	VR,
	Educational,
	Casual,

	// ── Third wave: research-gated additions ───────────────────────────────
	Sandbox,
	OpenWorld,
	BattleRoyale,
	TowerDefense,
	AutoBattler,
}

/// <summary>
/// Coarse grouping for the genre picker UI. Has no gameplay effect on its own.
/// </summary>
public enum GameGenreCategory
{
	Theme,
	Format,
	Gameplay,
	Audience,
	Monetization,
}

/// <summary>
/// Static metadata for one genre. Includes display strings, the bucket the
/// picker UI groups it into, the per-tag effort and revenue scalars applied
/// during production / payoff, and an optional achievement gate that hides
/// the tag from the picker until the player earns it.
/// </summary>
public sealed class GameGenreInfo
{
	public GameGenre          Genre               { get; init; }
	public GameGenreCategory  Category            { get; init; }
	public string             Name                { get; init; } = "";
	public string             Description         { get; init; } = "";

	/// 1.0 = baseline. >1 makes the project take longer to build; <1 speeds it up.
	public float              EffortMultiplier    { get; init; } = 1f;

	/// 1.0 = baseline. Multiplies post-launch revenue. Risky tags (gambling,
	/// gacha) pay more in exchange for higher effort and audience cost.
	public float              RevenueMultiplier   { get; init; } = 1f;

	/// Null = unlocked from the start. Otherwise the tag stays hidden until
	/// the named achievement fires.
	public AchievementId?     RequiredAchievement { get; init; }

	/// True if this genre is unreachable except by completing a research
	/// topic that targets it (or by some explicit one-off unlock the
	/// research path can satisfy via <see cref="Achievements.ResearchedGenres"/>).
	/// Without this, genres with no <see cref="RequiredAchievement"/> would
	/// silently default to "always unlocked" — which is the opposite of
	/// intent for the research wave.
	public bool               RequiresResearch    { get; init; }

	/// Optional inventory-item gate. When set, the genre stays locked until
	/// the player owns at least one of <paramref name="RequiredItem"/>
	/// (placed or stashed). Drives the Server → Multiplayer wiring: buying
	/// the Server item unlocks Multiplayer projects studio-wide.
	public ItemKind?          RequiredItem        { get; init; }
}

/// <summary>
/// The genre catalogue + per-pair synergy table.
///
/// Synergy is symmetric and pair-additive: the score for a project is the
/// sum of <see cref="Synergy"/> across every unordered pair of selected
/// tags. Listed pairs reward natural fits (RPG+Fantasy, MP+MOBA, Mobile+
/// Gacha) and penalise designed clashes (2D+3D, SinglePlayer+Multiplayer,
/// Educational+Gambling). Pairs absent from the table return 0.
///
/// The total flows through <c>GameProjectManager.synergy</c> as a
/// multiplier on pillar points, so picking compatible tags is a real lever
/// for the player's review score and revenue.
/// </summary>
public static class GameGenres
{
	public static readonly IReadOnlyList<GameGenreInfo> All = new GameGenreInfo[]
	{
		// ── Theme ────────────────────────────────────────────────────────────
		new() { Genre = GameGenre.Fishing,  Category = GameGenreCategory.Theme,
			Name = "Fishing",   Description = "Cast, wait, reel. Cozy and timeless.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f },

		new() { Genre = GameGenre.Soccer,   Category = GameGenreCategory.Theme,
			Name = "Soccer",    Description = "The world's game. Mass appeal, demands polish.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.2f,
			RequiredAchievement = AchievementId.FirstHire },

		new() { Genre = GameGenre.Gambling, Category = GameGenreCategory.Theme,
			Name = "Gambling",  Description = "Higher stakes, higher revenue, narrower audience.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.5f,
			RequiredAchievement = AchievementId.Earn100k },

		// ── Format ───────────────────────────────────────────────────────────
		new() { Genre = GameGenre.TwoD,  Category = GameGenreCategory.Format,
			Name = "2D",   Description = "Pixel-perfect, fast to ship.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f },

		new() { Genre = GameGenre.ThreeD, Category = GameGenreCategory.Format,
			Name = "3D",   Description = "Bigger pipeline, more visual punch.",
			EffortMultiplier = 1.4f, RevenueMultiplier = 1.2f,
			RequiredAchievement = AchievementId.Earn10k },

		// ── Gameplay ─────────────────────────────────────────────────────────
		new() { Genre = GameGenre.Action,    Category = GameGenreCategory.Gameplay,
			Name = "Action",    Description = "Twitch combat, moment-to-moment fun.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f },

		new() { Genre = GameGenre.Story,     Category = GameGenreCategory.Gameplay,
			Name = "Story",     Description = "Writing-first. Narrative carries the experience.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.0f },

		new() { Genre = GameGenre.Idle,      Category = GameGenreCategory.Gameplay,
			Name = "Idle",      Description = "Plays itself. Cheap to make, sticky audience.",
			EffortMultiplier = 0.7f, RevenueMultiplier = 0.8f,
			RequiredAchievement = AchievementId.Earn1k },

		new() { Genre = GameGenre.Roguelike, Category = GameGenreCategory.Gameplay,
			Name = "Roguelike", Description = "Permadeath, run-based replayability.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.1f,
			RequiredAchievement = AchievementId.Hire5 },

		new() { Genre = GameGenre.FPS,       Category = GameGenreCategory.Gameplay,
			Name = "FPS",       Description = "First-person shooter. Pricey to build, big audience.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 1.3f,
			RequiredAchievement = AchievementId.Earn10k },

		new() { Genre = GameGenre.TPS,       Category = GameGenreCategory.Gameplay,
			Name = "TPS",       Description = "Third-person shooter. Cinematic, action-heavy.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 1.2f,
			RequiredAchievement = AchievementId.Earn10k },

		new() { Genre = GameGenre.MOBA,      Category = GameGenreCategory.Gameplay,
			Name = "MOBA",      Description = "Five-versus-five with a lane meta. Risky, lucrative.",
			EffortMultiplier = 1.6f, RevenueMultiplier = 1.8f,
			RequiredAchievement = AchievementId.Hire10 },

		// ── Audience ─────────────────────────────────────────────────────────
		new() { Genre = GameGenre.SinglePlayer, Category = GameGenreCategory.Audience,
			Name = "Single-Player", Description = "One player, one story. Predictable scope.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f },

		new() { Genre = GameGenre.Multiplayer,  Category = GameGenreCategory.Audience,
			Name = "Multiplayer",   Description = "Servers, matchmaking, ongoing service. Hard.",
			EffortMultiplier = 1.5f, RevenueMultiplier = 1.4f,
			RequiredItem = ItemKind.Server },     // unlocks once a Server is bought

		new() { Genre = GameGenre.Mobile,       Category = GameGenreCategory.Audience,
			Name = "Mobile",        Description = "Touch controls, smaller scope, huge reach.",
			EffortMultiplier = 0.9f, RevenueMultiplier = 1.1f,
			RequiredAchievement = AchievementId.Players100 },

		// ── Monetization ─────────────────────────────────────────────────────
		new() { Genre = GameGenre.Gacha,       Category = GameGenreCategory.Monetization,
			Name = "Gacha",        Description = "Random pulls, whales, daily login loops.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 2.0f,
			RequiredAchievement = AchievementId.Earn100k },

		new() { Genre = GameGenre.PackOpening, Category = GameGenreCategory.Monetization,
			Name = "Pack Opening", Description = "Booster-pack dopamine. Steady spenders.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.6f,
			RequiredAchievement = AchievementId.Earn10k },

		// ── Research-unlockable ─────────────────────────────────────────────
		// Locked from the start — exposed by completing the matching
		// ResearchTopic (see Code/ResearchTopic.cs). Some are also event-gated
		// at the topic level (e.g., Friendship Dynamics needs 5 ships first).
		// `RequiredAchievement` left null because the gating lives on the
		// research topic, not on the genre itself.

		new() { Genre = GameGenre.Racing,    Category = GameGenreCategory.Theme,
			Name = "Racing",     Description = "Wheels, lines, lap times. Big arcade audience.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.Horror,    Category = GameGenreCategory.Theme,
			Name = "Horror",     Description = "Tension, dread, jump scares. Niche but loud.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.2f, RequiresResearch = true },

		new() { Genre = GameGenre.Puzzle,    Category = GameGenreCategory.Gameplay,
			Name = "Puzzle",     Description = "Mind-benders. Cheap to ship, evergreen audience.",
			EffortMultiplier = 0.9f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.Simulation, Category = GameGenreCategory.Gameplay,
			Name = "Simulation", Description = "Systems on systems on systems. Audience pays for depth.",
			EffortMultiplier = 1.4f, RevenueMultiplier = 1.5f, RequiresResearch = true },

		new() { Genre = GameGenre.RPG,       Category = GameGenreCategory.Gameplay,
			Name = "RPG",        Description = "Stats, classes, side quests. Long projects, devoted fans.",
			EffortMultiplier = 1.5f, RevenueMultiplier = 1.6f, RequiresResearch = true },

		new() { Genre = GameGenre.Survival,  Category = GameGenreCategory.Gameplay,
			Name = "Survival",   Description = "Hunger, weather, crafting. Streamer-friendly.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.LocalCoop, Category = GameGenreCategory.Audience,
			Name = "Local Co-op",Description = "Couch friends, party energy. Hard to balance, easy to love.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.2f, RequiresResearch = true },

		// ── Second wave of research-unlockable genres ─────────────────────
		// All gated on a corresponding ResearchTopic completion. The topics
		// roll into the player's research inbox over time (one per in-game
		// month) so most of these stay invisible at game start.

		new() { Genre = GameGenre.Stealth,         Category = GameGenreCategory.Gameplay,
			Name = "Stealth",         Description = "Hide, sneak, eliminate. Patient players, tight scope.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		new() { Genre = GameGenre.Platformer,      Category = GameGenreCategory.Gameplay,
			Name = "Platformer",      Description = "Jump, run, time it perfectly. Precision feel.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.Fighting,        Category = GameGenreCategory.Gameplay,
			Name = "Fighting",        Description = "Frame data, combos, tournament minds. Brutal to balance.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 1.2f, RequiresResearch = true },

		new() { Genre = GameGenre.RTS,             Category = GameGenreCategory.Gameplay,
			Name = "RTS",             Description = "Bases, units, APM. Hard genre, devoted audience.",
			EffortMultiplier = 1.4f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.TBS,             Category = GameGenreCategory.Gameplay,
			Name = "Turn-Based",      Description = "Move-counter-move strategy. Cheaper to ship than RTS.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		new() { Genre = GameGenre.CardGame,        Category = GameGenreCategory.Gameplay,
			Name = "Card Game",       Description = "Deckbuilders, TCGs, the whole bit. Pairs great with monetisation.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.RhythmGame,      Category = GameGenreCategory.Gameplay,
			Name = "Rhythm",          Description = "Hit the note. Soundtrack carries the experience.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.Tycoon,          Category = GameGenreCategory.Gameplay,
			Name = "Tycoon",          Description = "Build a business, watch numbers go up. Sticky audience.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.CityBuilder,     Category = GameGenreCategory.Gameplay,
			Name = "City Builder",    Description = "Zoning, infrastructure, traffic flow. Large scope, evergreen.",
			EffortMultiplier = 1.4f, RevenueMultiplier = 1.4f, RequiresResearch = true },

		new() { Genre = GameGenre.Crafting,        Category = GameGenreCategory.Gameplay,
			Name = "Crafting",        Description = "Combine, refine, build. Loops well with Survival / Sandbox.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		new() { Genre = GameGenre.SpaceOpera,      Category = GameGenreCategory.Theme,
			Name = "Space Opera",     Description = "Big ships, big stakes, big galaxies.",
			EffortMultiplier = 1.3f, RevenueMultiplier = 1.3f, RequiresResearch = true },

		new() { Genre = GameGenre.Cyberpunk,       Category = GameGenreCategory.Theme,
			Name = "Cyberpunk",       Description = "Neon, hacking, megacorps. Aesthetic-first audience.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.4f, RequiresResearch = true },

		new() { Genre = GameGenre.Steampunk,       Category = GameGenreCategory.Theme,
			Name = "Steampunk",       Description = "Brass, gears, alt-history Victorian. Niche but loud.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		new() { Genre = GameGenre.PostApocalyptic, Category = GameGenreCategory.Theme,
			Name = "Post-Apocalyptic",Description = "Wasteland, scarcity, scavenging. Streamer-friendly.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.2f, RequiresResearch = true },

		new() { Genre = GameGenre.Fantasy,         Category = GameGenreCategory.Theme,
			Name = "Fantasy",         Description = "Magic, dragons, the classics. Foundational audience.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.4f, RequiresResearch = true },

		new() { Genre = GameGenre.Mystery,         Category = GameGenreCategory.Theme,
			Name = "Mystery",         Description = "Detective work, twists, evidence. Reads great.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.Western,         Category = GameGenreCategory.Theme,
			Name = "Western",         Description = "Frontier, gunslinging, dust. Cinematic energy.",
			EffortMultiplier = 1.1f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.VR,              Category = GameGenreCategory.Format,
			Name = "VR",              Description = "Headset audience. Premium pricing, niche reach.",
			EffortMultiplier = 1.6f, RevenueMultiplier = 1.5f, RequiresResearch = true },

		new() { Genre = GameGenre.Educational,     Category = GameGenreCategory.Audience,
			Name = "Educational",     Description = "Teaches as it plays. Steady B2B + parent revenue.",
			EffortMultiplier = 0.9f, RevenueMultiplier = 0.8f, RequiresResearch = true },

		new() { Genre = GameGenre.Casual,          Category = GameGenreCategory.Audience,
			Name = "Casual",          Description = "Pick up, put down. Cheap to ship, massive reach.",
			EffortMultiplier = 0.8f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		// ── Third wave: extra genres to round the catalogue out to 50 ─────
		// Five new research-gated genres + one currently-locked online tag
		// (no research path yet — see comment).
		new() { Genre = GameGenre.Sandbox,         Category = GameGenreCategory.Gameplay,
			Name = "Sandbox",         Description = "No goals, just systems. Player makes their own fun.",
			EffortMultiplier = 1.2f, RevenueMultiplier = 1.1f, RequiresResearch = true },

		new() { Genre = GameGenre.OpenWorld,       Category = GameGenreCategory.Format,
			Name = "Open World",      Description = "One huge connected map. Massive scope, massive payoff.",
			EffortMultiplier = 1.7f, RevenueMultiplier = 1.6f, RequiresResearch = true },

		new() { Genre = GameGenre.BattleRoyale,    Category = GameGenreCategory.Gameplay,
			Name = "Battle Royale",   Description = "Last-player-standing at scale. Ongoing service genre.",
			EffortMultiplier = 1.6f, RevenueMultiplier = 1.7f, RequiresResearch = true },

		new() { Genre = GameGenre.TowerDefense,    Category = GameGenreCategory.Gameplay,
			Name = "Tower Defense",   Description = "Place towers, watch waves break. Tight scope, evergreen.",
			EffortMultiplier = 0.9f, RevenueMultiplier = 1.0f, RequiresResearch = true },

		new() { Genre = GameGenre.AutoBattler,     Category = GameGenreCategory.Gameplay,
			Name = "Auto-Battler",    Description = "Build the squad, watch them fight. Sticky and addictive.",
			EffortMultiplier = 1.0f, RevenueMultiplier = 1.2f, RequiresResearch = true },
	};

	public static GameGenreInfo Get( GameGenre g )
	{
		foreach ( var info in All )
			if ( info.Genre == g ) return info;
		return null;
	}

	/// True if the player can pick this genre right now.
	///
	/// Order of evaluation:
	///   1. <see cref="Achievements.ResearchedGenres"/> — research completed
	///      always unlocks, no matter what other gates say.
	///   2. <see cref="GameGenreInfo.RequiresResearch"/> — when true and step 1
	///      didn't fire, the genre is locked. Belt-and-braces against the
	///      old "no achievement = always unlocked" footgun.
	///   3. <see cref="GameGenreInfo.RequiredAchievement"/> — must be unlocked
	///      if set.
	///   4. <see cref="GameGenreInfo.RequiredItem"/> — must be owned (placed
	///      or stashed) if set. Drives Server → Multiplayer.
	///   5. No gates at all → unlocked from day one (true defaults like 2D,
	///      Action, Story, SinglePlayer, Fishing).
	public static bool IsUnlocked( GameGenre g )
	{
		var info = Get( g );
		if ( info is null ) return false;

		// Research path overrides everything else.
		if ( Achievements.ResearchedGenres.Contains( g ) ) return true;

		// Research-only genres stay locked until step 1 fires.
		if ( info.RequiresResearch ) return false;

		// Achievement gate.
		if ( info.RequiredAchievement is { } id && !Achievements.IsUnlocked( id ) )
			return false;

		// Item gate (e.g., owning a Server unlocks Multiplayer).
		if ( info.RequiredItem is { } item )
		{
			var inv = InventoryManager.Instance;
			if ( inv is null || !inv.OwnsAny( item ) ) return false;
		}

		return true;
	}

	// ── Synergy table ───────────────────────────────────────────────────────
	// Pair-additive synergy: project score = sum over every unordered pair.
	// Order doesn't matter — Synergy(a,b) checks both lookups before falling
	// back to 0. Positive = natural fit; negative = clash.

	static readonly Dictionary<(GameGenre, GameGenre), float> _synergies = new()
	{
		// ── Format: 2D / 3D / Mobile / VR ────────────────────────────────────
		// 2D anchors small/stylised genres; 3D anchors immersive shooters/RPGs.
		// Mobile pulls toward bite-sized and monetised. VR rewards immersion.
		[(GameGenre.TwoD,         GameGenre.ThreeD)]       = -0.30f,
		[(GameGenre.TwoD,         GameGenre.VR)]           = -0.20f,
		[(GameGenre.TwoD,         GameGenre.Roguelike)]    = +0.30f,
		[(GameGenre.TwoD,         GameGenre.Platformer)]   = +0.30f,
		[(GameGenre.TwoD,         GameGenre.Puzzle)]       = +0.30f,
		[(GameGenre.TwoD,         GameGenre.RhythmGame)]   = +0.30f,
		[(GameGenre.TwoD,         GameGenre.CardGame)]     = +0.20f,
		[(GameGenre.TwoD,         GameGenre.Action)]       = +0.10f,
		[(GameGenre.TwoD,         GameGenre.Idle)]         = +0.20f,
		[(GameGenre.TwoD,         GameGenre.Tycoon)]       = +0.20f,
		[(GameGenre.TwoD,         GameGenre.TowerDefense)] = +0.20f,
		[(GameGenre.TwoD,         GameGenre.AutoBattler)]  = +0.20f,
		[(GameGenre.TwoD,         GameGenre.Casual)]       = +0.20f,

		[(GameGenre.ThreeD,       GameGenre.FPS)]          = +0.30f,
		[(GameGenre.ThreeD,       GameGenre.TPS)]          = +0.30f,
		[(GameGenre.ThreeD,       GameGenre.Action)]       = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.Racing)]       = +0.30f,
		[(GameGenre.ThreeD,       GameGenre.RPG)]          = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.OpenWorld)]    = +0.30f,
		[(GameGenre.ThreeD,       GameGenre.Survival)]     = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.Horror)]       = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.Sandbox)]      = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.SpaceOpera)]   = +0.20f,
		[(GameGenre.ThreeD,       GameGenre.VR)]           = +0.30f,

		[(GameGenre.Mobile,       GameGenre.Idle)]         = +0.40f,
		[(GameGenre.Mobile,       GameGenre.Casual)]       = +0.40f,
		[(GameGenre.Mobile,       GameGenre.Gacha)]        = +0.40f,
		[(GameGenre.Mobile,       GameGenre.PackOpening)]  = +0.30f,
		[(GameGenre.Mobile,       GameGenre.Puzzle)]       = +0.30f,
		[(GameGenre.Mobile,       GameGenre.RhythmGame)]   = +0.30f,
		[(GameGenre.Mobile,       GameGenre.CardGame)]     = +0.30f,
		[(GameGenre.Mobile,       GameGenre.Tycoon)]       = +0.20f,
		[(GameGenre.Mobile,       GameGenre.TowerDefense)] = +0.20f,
		[(GameGenre.Mobile,       GameGenre.AutoBattler)]  = +0.20f,
		[(GameGenre.Mobile,       GameGenre.Educational)]  = +0.20f,
		[(GameGenre.Mobile,       GameGenre.Soccer)]       = +0.20f,
		[(GameGenre.Mobile,       GameGenre.Racing)]       = +0.20f,
		[(GameGenre.Mobile,       GameGenre.Fishing)]      = +0.20f,
		[(GameGenre.Mobile,       GameGenre.FPS)]          = -0.10f,
		[(GameGenre.Mobile,       GameGenre.RTS)]          = -0.20f,
		[(GameGenre.Mobile,       GameGenre.OpenWorld)]    = -0.20f,
		[(GameGenre.Mobile,       GameGenre.VR)]           = -0.30f,

		[(GameGenre.VR,           GameGenre.FPS)]          = +0.30f,
		[(GameGenre.VR,           GameGenre.Action)]       = +0.30f,
		[(GameGenre.VR,           GameGenre.Horror)]       = +0.40f,
		[(GameGenre.VR,           GameGenre.Survival)]     = +0.30f,
		[(GameGenre.VR,           GameGenre.Sandbox)]      = +0.30f,
		[(GameGenre.VR,           GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.VR,           GameGenre.Simulation)]   = +0.30f,
		[(GameGenre.VR,           GameGenre.Educational)]  = +0.20f,
		[(GameGenre.VR,           GameGenre.Idle)]         = -0.20f,

		// ── Combat / shooter cores ──────────────────────────────────────────
		[(GameGenre.FPS,          GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.FPS,          GameGenre.BattleRoyale)] = +0.40f,
		[(GameGenre.FPS,          GameGenre.Action)]       = +0.20f,
		[(GameGenre.FPS,          GameGenre.Horror)]       = +0.20f,
		[(GameGenre.FPS,          GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.FPS,          GameGenre.Cyberpunk)]    = +0.20f,
		[(GameGenre.FPS,          GameGenre.PostApocalyptic)] = +0.20f,
		[(GameGenre.FPS,          GameGenre.SpaceOpera)]   = +0.20f,
		[(GameGenre.FPS,          GameGenre.Story)]        = +0.10f,

		[(GameGenre.TPS,          GameGenre.Action)]       = +0.30f,
		[(GameGenre.TPS,          GameGenre.Story)]        = +0.20f,
		[(GameGenre.TPS,          GameGenre.Cyberpunk)]    = +0.20f,
		[(GameGenre.TPS,          GameGenre.PostApocalyptic)] = +0.20f,
		[(GameGenre.TPS,          GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.TPS,          GameGenre.Multiplayer)]  = +0.20f,

		[(GameGenre.Fighting,     GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.Fighting,     GameGenre.LocalCoop)]    = +0.30f,
		[(GameGenre.Fighting,     GameGenre.TwoD)]         = +0.20f,
		[(GameGenre.Fighting,     GameGenre.ThreeD)]       = +0.20f,
		[(GameGenre.Fighting,     GameGenre.SinglePlayer)] = -0.10f,

		[(GameGenre.MOBA,         GameGenre.Multiplayer)]  = +0.40f,
		[(GameGenre.MOBA,         GameGenre.RPG)]          = +0.10f,
		[(GameGenre.MOBA,         GameGenre.Fantasy)]      = +0.10f,
		[(GameGenre.MOBA,         GameGenre.SinglePlayer)] = -0.30f,

		[(GameGenre.BattleRoyale, GameGenre.Multiplayer)]  = +0.40f,
		[(GameGenre.BattleRoyale, GameGenre.Action)]       = +0.20f,
		[(GameGenre.BattleRoyale, GameGenre.Survival)]     = +0.20f,
		[(GameGenre.BattleRoyale, GameGenre.SinglePlayer)] = -0.30f,

		// ── Strategy / sim ───────────────────────────────────────────────────
		[(GameGenre.RTS,          GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.RTS,          GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.RTS,          GameGenre.Fantasy)]      = +0.10f,
		[(GameGenre.RTS,          GameGenre.SpaceOpera)]   = +0.20f,

		[(GameGenre.TBS,          GameGenre.SinglePlayer)] = +0.30f,
		[(GameGenre.TBS,          GameGenre.Roguelike)]    = +0.20f,
		[(GameGenre.TBS,          GameGenre.Fantasy)]      = +0.20f,
		[(GameGenre.TBS,          GameGenre.Story)]        = +0.10f,
		[(GameGenre.TBS,          GameGenre.RPG)]          = +0.10f,
		[(GameGenre.TBS,          GameGenre.CardGame)]     = +0.20f,

		[(GameGenre.Tycoon,       GameGenre.Simulation)]   = +0.40f,
		[(GameGenre.Tycoon,       GameGenre.CityBuilder)]  = +0.20f,
		[(GameGenre.Tycoon,       GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.Tycoon,       GameGenre.Casual)]       = +0.20f,
		[(GameGenre.Tycoon,       GameGenre.Idle)]         = +0.20f,
		[(GameGenre.Tycoon,       GameGenre.Crafting)]     = +0.10f,

		[(GameGenre.CityBuilder,  GameGenre.Simulation)]   = +0.30f,
		[(GameGenre.CityBuilder,  GameGenre.Sandbox)]      = +0.20f,
		[(GameGenre.CityBuilder,  GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.CityBuilder,  GameGenre.Survival)]     = +0.10f,
		[(GameGenre.CityBuilder,  GameGenre.Crafting)]     = +0.10f,

		[(GameGenre.Simulation,   GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Simulation,   GameGenre.Educational)]  = +0.20f,
		[(GameGenre.Simulation,   GameGenre.Sandbox)]      = +0.20f,
		[(GameGenre.Simulation,   GameGenre.Crafting)]     = +0.10f,
		[(GameGenre.Simulation,   GameGenre.Casual)]       = +0.10f,

		// ── Story / RPG / setting clusters ───────────────────────────────────
		[(GameGenre.Story,        GameGenre.SinglePlayer)] = +0.30f,
		[(GameGenre.Story,        GameGenre.RPG)]          = +0.30f,
		[(GameGenre.Story,        GameGenre.Mystery)]      = +0.30f,
		[(GameGenre.Story,        GameGenre.Horror)]       = +0.20f,
		[(GameGenre.Story,        GameGenre.OpenWorld)]    = +0.30f,
		[(GameGenre.Story,        GameGenre.Fantasy)]      = +0.20f,
		[(GameGenre.Story,        GameGenre.SpaceOpera)]   = +0.30f,
		[(GameGenre.Story,        GameGenre.Cyberpunk)]    = +0.20f,
		[(GameGenre.Story,        GameGenre.Steampunk)]    = +0.20f,
		[(GameGenre.Story,        GameGenre.PostApocalyptic)] = +0.20f,
		[(GameGenre.Story,        GameGenre.Western)]      = +0.20f,
		[(GameGenre.Story,        GameGenre.Multiplayer)]  = -0.10f,
		[(GameGenre.Story,        GameGenre.Idle)]         = -0.20f,
		[(GameGenre.Story,        GameGenre.Casual)]       = -0.10f,

		[(GameGenre.RPG,          GameGenre.OpenWorld)]    = +0.30f,
		[(GameGenre.RPG,          GameGenre.Fantasy)]      = +0.40f,
		[(GameGenre.RPG,          GameGenre.SpaceOpera)]   = +0.20f,
		[(GameGenre.RPG,          GameGenre.Cyberpunk)]    = +0.20f,
		[(GameGenre.RPG,          GameGenre.Steampunk)]    = +0.20f,
		[(GameGenre.RPG,          GameGenre.PostApocalyptic)] = +0.30f,
		[(GameGenre.RPG,          GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.RPG,          GameGenre.Roguelike)]    = +0.20f,
		[(GameGenre.RPG,          GameGenre.Crafting)]     = +0.10f,
		[(GameGenre.RPG,          GameGenre.Idle)]         = -0.10f,
		[(GameGenre.RPG,          GameGenre.Casual)]       = -0.10f,

		// ── Roguelike core ──────────────────────────────────────────────────
		[(GameGenre.Roguelike,    GameGenre.Action)]       = +0.20f,
		[(GameGenre.Roguelike,    GameGenre.Crafting)]     = +0.20f,
		[(GameGenre.Roguelike,    GameGenre.Survival)]     = +0.20f,
		[(GameGenre.Roguelike,    GameGenre.CardGame)]     = +0.20f,
		[(GameGenre.Roguelike,    GameGenre.SinglePlayer)] = +0.30f,

		// ── Survival / sandbox / open-world cluster ─────────────────────────
		[(GameGenre.Survival,     GameGenre.Crafting)]     = +0.30f,
		[(GameGenre.Survival,     GameGenre.OpenWorld)]    = +0.30f,
		[(GameGenre.Survival,     GameGenre.Sandbox)]      = +0.30f,
		[(GameGenre.Survival,     GameGenre.PostApocalyptic)] = +0.30f,
		[(GameGenre.Survival,     GameGenre.Horror)]       = +0.30f,
		[(GameGenre.Survival,     GameGenre.Multiplayer)]  = +0.20f,
		[(GameGenre.Survival,     GameGenre.LocalCoop)]    = +0.20f,
		[(GameGenre.Survival,     GameGenre.Casual)]       = -0.20f,

		[(GameGenre.Sandbox,      GameGenre.Crafting)]     = +0.30f,
		[(GameGenre.Sandbox,      GameGenre.Multiplayer)]  = +0.20f,
		[(GameGenre.Sandbox,      GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Sandbox,      GameGenre.OpenWorld)]    = +0.20f,
		[(GameGenre.Sandbox,      GameGenre.Educational)]  = +0.20f,
		[(GameGenre.Sandbox,      GameGenre.Casual)]       = +0.10f,

		[(GameGenre.OpenWorld,    GameGenre.Multiplayer)]  = +0.10f,
		[(GameGenre.OpenWorld,    GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.OpenWorld,    GameGenre.Action)]       = +0.20f,
		[(GameGenre.OpenWorld,    GameGenre.Western)]      = +0.20f,
		[(GameGenre.OpenWorld,    GameGenre.PostApocalyptic)] = +0.20f,
		[(GameGenre.OpenWorld,    GameGenre.Fantasy)]      = +0.20f,
		[(GameGenre.OpenWorld,    GameGenre.Cyberpunk)]    = +0.20f,

		[(GameGenre.Crafting,     GameGenre.LocalCoop)]    = +0.20f,
		[(GameGenre.Crafting,     GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Crafting,     GameGenre.Casual)]       = +0.10f,

		// ── Theme atmospheres ───────────────────────────────────────────────
		[(GameGenre.Horror,       GameGenre.Mystery)]      = +0.30f,
		[(GameGenre.Horror,       GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.Horror,       GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.Horror,       GameGenre.PostApocalyptic)] = +0.20f,
		[(GameGenre.Horror,       GameGenre.Casual)]       = -0.20f,
		[(GameGenre.Horror,       GameGenre.Mobile)]       = -0.10f,

		[(GameGenre.Mystery,      GameGenre.Puzzle)]       = +0.20f,
		[(GameGenre.Mystery,      GameGenre.Stealth)]      = +0.10f,
		[(GameGenre.Mystery,      GameGenre.Western)]      = +0.10f,
		[(GameGenre.Mystery,      GameGenre.Cyberpunk)]    = +0.20f,
		[(GameGenre.Mystery,      GameGenre.SinglePlayer)] = +0.20f,

		[(GameGenre.Cyberpunk,    GameGenre.Stealth)]      = +0.20f,
		[(GameGenre.Cyberpunk,    GameGenre.Action)]       = +0.20f,
		[(GameGenre.Cyberpunk,    GameGenre.SpaceOpera)]   = +0.10f,

		[(GameGenre.Steampunk,    GameGenre.Fantasy)]      = +0.20f,
		[(GameGenre.Steampunk,    GameGenre.Mystery)]      = +0.20f,
		[(GameGenre.Steampunk,    GameGenre.Western)]      = +0.10f,

		[(GameGenre.SpaceOpera,   GameGenre.Action)]       = +0.20f,
		[(GameGenre.SpaceOpera,   GameGenre.Multiplayer)]  = +0.10f,
		[(GameGenre.SpaceOpera,   GameGenre.Fantasy)]      = +0.10f,

		[(GameGenre.PostApocalyptic, GameGenre.Western)]   = +0.10f,

		[(GameGenre.Fantasy,      GameGenre.Western)]      = -0.10f,

		[(GameGenre.Western,      GameGenre.Action)]       = +0.20f,

		// ── Audience tags ───────────────────────────────────────────────────
		[(GameGenre.SinglePlayer, GameGenre.Multiplayer)]  = -0.30f,
		[(GameGenre.SinglePlayer, GameGenre.LocalCoop)]    = -0.10f,
		[(GameGenre.LocalCoop,    GameGenre.Multiplayer)]  = +0.10f,
		[(GameGenre.LocalCoop,    GameGenre.Action)]       = +0.30f,
		[(GameGenre.LocalCoop,    GameGenre.Platformer)]   = +0.30f,
		[(GameGenre.LocalCoop,    GameGenre.Casual)]       = +0.20f,
		[(GameGenre.LocalCoop,    GameGenre.Story)]        = -0.10f,

		[(GameGenre.Casual,       GameGenre.Idle)]         = +0.30f,
		[(GameGenre.Casual,       GameGenre.Puzzle)]       = +0.30f,
		[(GameGenre.Casual,       GameGenre.RhythmGame)]   = +0.30f,
		[(GameGenre.Casual,       GameGenre.Soccer)]       = +0.20f,
		[(GameGenre.Casual,       GameGenre.Racing)]       = +0.20f,
		[(GameGenre.Casual,       GameGenre.AutoBattler)]  = +0.20f,
		[(GameGenre.Casual,       GameGenre.CardGame)]     = +0.20f,

		// ── Idle ────────────────────────────────────────────────────────────
		[(GameGenre.Idle,         GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Idle,         GameGenre.Crafting)]     = +0.10f,
		[(GameGenre.Idle,         GameGenre.Action)]       = -0.30f,
		[(GameGenre.Idle,         GameGenre.FPS)]          = -0.30f,
		[(GameGenre.Idle,         GameGenre.Multiplayer)]  = -0.20f,

		// ── Monetization clusters ───────────────────────────────────────────
		[(GameGenre.Gacha,        GameGenre.PackOpening)]  = +0.40f,
		[(GameGenre.Gambling,     GameGenre.Gacha)]        = +0.30f,
		[(GameGenre.Gambling,     GameGenre.PackOpening)]  = +0.30f,
		[(GameGenre.Gambling,     GameGenre.Casual)]       = +0.20f,
		[(GameGenre.Gambling,     GameGenre.Mobile)]       = +0.20f,
		[(GameGenre.Gacha,        GameGenre.RPG)]          = +0.20f,
		[(GameGenre.Gacha,        GameGenre.AutoBattler)]  = +0.20f,
		[(GameGenre.Gacha,        GameGenre.Casual)]       = +0.20f,
		[(GameGenre.PackOpening,  GameGenre.CardGame)]     = +0.40f,
		[(GameGenre.PackOpening,  GameGenre.Casual)]       = +0.20f,

		// ── Educational (incompatible with predatory monetization) ──────────
		[(GameGenre.Educational,  GameGenre.Puzzle)]       = +0.30f,
		[(GameGenre.Educational,  GameGenre.Casual)]       = +0.20f,
		[(GameGenre.Educational,  GameGenre.Story)]        = +0.10f,
		[(GameGenre.Educational,  GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Educational,  GameGenre.Idle)]         = -0.10f,
		[(GameGenre.Educational,  GameGenre.Horror)]       = -0.30f,
		[(GameGenre.Educational,  GameGenre.Gambling)]     = -0.40f,
		[(GameGenre.Educational,  GameGenre.Gacha)]        = -0.40f,
		[(GameGenre.Educational,  GameGenre.PackOpening)]  = -0.30f,

		// ── Sport / racing ──────────────────────────────────────────────────
		[(GameGenre.Soccer,       GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.Soccer,       GameGenre.LocalCoop)]    = +0.20f,
		[(GameGenre.Soccer,       GameGenre.SinglePlayer)] = -0.10f,
		[(GameGenre.Racing,       GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.Racing,       GameGenre.LocalCoop)]    = +0.20f,
		[(GameGenre.Racing,       GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Racing,       GameGenre.Action)]       = +0.10f,

		// ── Niche fits ──────────────────────────────────────────────────────
		[(GameGenre.Platformer,   GameGenre.Action)]       = +0.20f,
		[(GameGenre.Platformer,   GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Platformer,   GameGenre.Story)]        = +0.10f,
		[(GameGenre.Platformer,   GameGenre.Fighting)]     = -0.10f,

		[(GameGenre.Stealth,      GameGenre.Action)]       = +0.10f,
		[(GameGenre.Stealth,      GameGenre.Story)]        = +0.10f,
		[(GameGenre.Stealth,      GameGenre.RPG)]          = +0.10f,
		[(GameGenre.Stealth,      GameGenre.Multiplayer)]  = -0.10f,

		[(GameGenre.TowerDefense, GameGenre.TBS)]          = +0.20f,
		[(GameGenre.TowerDefense, GameGenre.RTS)]          = +0.20f,
		[(GameGenre.TowerDefense, GameGenre.Casual)]       = +0.20f,
		[(GameGenre.TowerDefense, GameGenre.AutoBattler)]  = +0.20f,
		[(GameGenre.TowerDefense, GameGenre.Fantasy)]      = +0.10f,
		[(GameGenre.TowerDefense, GameGenre.SinglePlayer)] = +0.20f,

		[(GameGenre.AutoBattler,  GameGenre.RPG)]          = +0.20f,
		[(GameGenre.AutoBattler,  GameGenre.Fantasy)]      = +0.10f,
		[(GameGenre.AutoBattler,  GameGenre.Multiplayer)]  = +0.20f,
		[(GameGenre.AutoBattler,  GameGenre.CardGame)]     = +0.20f,

		[(GameGenre.CardGame,     GameGenre.Multiplayer)]  = +0.30f,
		[(GameGenre.CardGame,     GameGenre.Fantasy)]      = +0.20f,

		[(GameGenre.RhythmGame,   GameGenre.Multiplayer)]  = +0.10f,

		[(GameGenre.Puzzle,       GameGenre.SinglePlayer)] = +0.20f,
		[(GameGenre.Puzzle,       GameGenre.Multiplayer)]  = +0.10f,

		[(GameGenre.Fishing,      GameGenre.Casual)]       = +0.30f,
		[(GameGenre.Fishing,      GameGenre.SinglePlayer)] = +0.10f,
		[(GameGenre.Fishing,      GameGenre.Story)]        = +0.10f,
		[(GameGenre.Fishing,      GameGenre.Sandbox)]      = +0.10f,
		[(GameGenre.Fishing,      GameGenre.Multiplayer)]  = -0.10f,

		// ── Thematic / tonal clashes ────────────────────────────────────────
		// Cozy doesn't blend with high-energy or grim; sport doesn't blend
		// with story-first or cerebral; educational won't survive next to
		// mature themes; casual doesn't fit competitive cores.
		[(GameGenre.Fishing,      GameGenre.Soccer)]       = -0.30f,
		[(GameGenre.Fishing,      GameGenre.Action)]       = -0.20f,
		[(GameGenre.Fishing,      GameGenre.FPS)]          = -0.30f,
		[(GameGenre.Fishing,      GameGenre.Horror)]       = -0.20f,
		[(GameGenre.Fishing,      GameGenre.Fighting)]     = -0.20f,
		[(GameGenre.Fishing,      GameGenre.BattleRoyale)] = -0.30f,
		[(GameGenre.Fishing,      GameGenre.MOBA)]         = -0.20f,

		[(GameGenre.Soccer,       GameGenre.Story)]        = -0.20f,
		[(GameGenre.Soccer,       GameGenre.RPG)]          = -0.20f,
		[(GameGenre.Soccer,       GameGenre.Stealth)]      = -0.20f,
		[(GameGenre.Soccer,       GameGenre.Survival)]     = -0.20f,
		[(GameGenre.Soccer,       GameGenre.Horror)]       = -0.30f,
		[(GameGenre.Soccer,       GameGenre.Idle)]         = -0.20f,

		[(GameGenre.Racing,       GameGenre.Story)]        = -0.10f,
		[(GameGenre.Racing,       GameGenre.Stealth)]      = -0.20f,
		[(GameGenre.Racing,       GameGenre.Survival)]     = -0.20f,
		[(GameGenre.Racing,       GameGenre.RPG)]          = -0.10f,

		[(GameGenre.Idle,         GameGenre.Survival)]     = -0.20f,
		[(GameGenre.Idle,         GameGenre.Horror)]       = -0.20f,
		[(GameGenre.Idle,         GameGenre.RTS)]          = -0.20f,
		[(GameGenre.Idle,         GameGenre.Stealth)]      = -0.10f,

		[(GameGenre.Casual,       GameGenre.RTS)]          = -0.20f,
		[(GameGenre.Casual,       GameGenre.MOBA)]         = -0.30f,
		[(GameGenre.Casual,       GameGenre.BattleRoyale)] = -0.20f,
		[(GameGenre.Casual,       GameGenre.Stealth)]      = -0.20f,
		[(GameGenre.Casual,       GameGenre.FPS)]          = -0.10f,

		[(GameGenre.Educational,  GameGenre.Fighting)]     = -0.20f,
		[(GameGenre.Educational,  GameGenre.FPS)]          = -0.20f,
		[(GameGenre.Educational,  GameGenre.BattleRoyale)] = -0.30f,
		[(GameGenre.Educational,  GameGenre.Cyberpunk)]    = -0.10f,
		[(GameGenre.Educational,  GameGenre.PostApocalyptic)] = -0.20f,
		[(GameGenre.Educational,  GameGenre.Western)]      = -0.10f,

		[(GameGenre.Mystery,      GameGenre.FPS)]          = -0.10f,
		[(GameGenre.Mystery,      GameGenre.BattleRoyale)] = -0.30f,
		[(GameGenre.Mystery,      GameGenre.Multiplayer)]  = -0.10f,

		[(GameGenre.BattleRoyale, GameGenre.Story)]        = -0.20f,
		[(GameGenre.BattleRoyale, GameGenre.RPG)]          = -0.10f,
		[(GameGenre.BattleRoyale, GameGenre.Mobile)]       = -0.10f,

		[(GameGenre.Sandbox,      GameGenre.Horror)]       = -0.10f,
		[(GameGenre.Sandbox,      GameGenre.Story)]        = -0.10f,

		// ── Setting-vs-setting clashes ──────────────────────────────────────
		// Different aesthetic worlds rarely fuse cleanly.
		[(GameGenre.Western,      GameGenre.SpaceOpera)]   = -0.20f,
		[(GameGenre.Western,      GameGenre.Cyberpunk)]    = -0.20f,
		[(GameGenre.Cyberpunk,    GameGenre.Fantasy)]      = -0.10f,
		[(GameGenre.Steampunk,    GameGenre.Cyberpunk)]    = -0.10f,
		[(GameGenre.Steampunk,    GameGenre.SpaceOpera)]   = -0.10f,
		[(GameGenre.Steampunk,    GameGenre.PostApocalyptic)] = -0.10f,
		[(GameGenre.Fantasy,      GameGenre.PostApocalyptic)] = -0.10f,
	};

	/// Synergy score between two specific tags. Symmetric, returns 0 for any
	/// pair that isn't in the table or for the same-tag case.
	public static float Synergy( GameGenre a, GameGenre b )
	{
		if ( a == b ) return 0f;
		if ( _synergies.TryGetValue( (a, b), out var v ) ) return v;
		if ( _synergies.TryGetValue( (b, a), out var w ) ) return w;
		return 0f;
	}

	/// Total synergy of a chosen tag set: sum over every unordered pair.
	public static float TotalSynergy( IReadOnlyList<GameGenre> genres )
	{
		float total = 0f;
		for ( int i = 0; i < genres.Count; i++ )
			for ( int j = i + 1; j < genres.Count; j++ )
				total += Synergy( genres[i], genres[j] );
		return total;
	}

	/// Total effort multiplier across the set (product of per-tag values).
	/// Empty set returns 1.
	public static float TotalEffort( IReadOnlyList<GameGenre> genres )
	{
		float m = 1f;
		foreach ( var g in genres )
		{
			var info = Get( g );
			if ( info is not null ) m *= info.EffortMultiplier;
		}
		return m;
	}

	/// Total revenue multiplier across the set (product of per-tag values).
	/// Empty set returns 1.
	public static float TotalRevenue( IReadOnlyList<GameGenre> genres )
	{
		float m = 1f;
		foreach ( var g in genres )
		{
			var info = Get( g );
			if ( info is not null ) m *= info.RevenueMultiplier;
		}
		return m;
	}
}
