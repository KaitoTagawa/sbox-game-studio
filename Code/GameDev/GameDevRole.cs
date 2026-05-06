/// <summary>
/// The five lead positions on a game project. Each role weights one pillar of
/// the final game's quality:
///   • <see cref="ProjectManager"/> — productivity / how fast it ships
///   • <see cref="Producer"/>       — bug rate (lower = cleaner release)
///   • <see cref="GameDirector"/>   — fun / game-design points
///   • <see cref="SoundDirector"/>  — audio points
///   • <see cref="ArtDirector"/>    — graphics points
///
/// An employee can be slotted into any number of roles on a single project,
/// but each extra role halves their effective contribution
/// (1 role → 100 %, 2 → 50 %, 3 → 25 %, …).
/// </summary>
public enum GameDevRole
{
	ProjectManager,
	Producer,
	GameDirector,
	SoundDirector,
	ArtDirector,
}

/// <summary>
/// The five quality bands a <see cref="GameDevAbility"/> can land in.
/// Tiers are universal; the *flavour* of each ability — the name — is bespoke
/// per role. A "Legendary" Project Manager is "Time Itself" while a Legendary
/// Art Director is "Descendant of Van Gogh".
/// </summary>
public enum GameDevAbilityTier
{
	Novice,
	Skilled,
	Renowned,
	Worldly,
	Legendary,
}

/// <summary>
/// One role-tier ability slot. The runtime contribution multiplier is applied
/// to whichever pillar the role weights when the ability-holder is assigned
/// to that role on a project.
/// </summary>
public sealed class GameDevAbility
{
	public GameDevRole         Role        { get; init; }
	public GameDevAbilityTier  Tier        { get; init; }
	public string              Name        { get; init; } = "";
	public string              Description { get; init; } = "";
	public float               Multiplier  { get; init; } = 1f;
}

/// <summary>
/// 25-entry table — five tiers per role. Lookups go through <see cref="Get"/>
/// or <see cref="ForRole"/>; consumers usually want the latter to render an
/// employee's role-specific perk options.
/// </summary>
public static class GameDevAbilities
{
	// Universal multiplier curve — same across every role.
	// Tune freely; legendary should feel meaningfully better than novice
	// without dwarfing employee stats outright.
	public const float NoviceMultiplier    = 1.05f;
	public const float SkilledMultiplier   = 1.15f;
	public const float RenownedMultiplier  = 1.30f;
	public const float WorldlyMultiplier   = 1.50f;
	public const float LegendaryMultiplier = 1.80f;

	public static float MultiplierFor( GameDevAbilityTier tier ) => tier switch
	{
		GameDevAbilityTier.Novice    => NoviceMultiplier,
		GameDevAbilityTier.Skilled   => SkilledMultiplier,
		GameDevAbilityTier.Renowned  => RenownedMultiplier,
		GameDevAbilityTier.Worldly   => WorldlyMultiplier,
		GameDevAbilityTier.Legendary => LegendaryMultiplier,
		_                            => 1f,
	};

	public static readonly IReadOnlyList<GameDevAbility> All = new GameDevAbility[]
	{
		// ── Project Manager — productivity / schedule ───────────────────────
		new() { Role = GameDevRole.ProjectManager, Tier = GameDevAbilityTier.Novice,
			Name = "Punctual",
			Description = "Hits dates a hair sooner than the rest.",
			Multiplier = NoviceMultiplier },
		new() { Role = GameDevRole.ProjectManager, Tier = GameDevAbilityTier.Skilled,
			Name = "Sprint Veteran",
			Description = "Knows how to break a project into clean two-week chunks.",
			Multiplier = SkilledMultiplier },
		new() { Role = GameDevRole.ProjectManager, Tier = GameDevAbilityTier.Renowned,
			Name = "Scrum Master",
			Description = "The team stays in flow for hours at a time on her watch.",
			Multiplier = RenownedMultiplier },
		new() { Role = GameDevRole.ProjectManager, Tier = GameDevAbilityTier.Worldly,
			Name = "Schedule Sorcerer",
			Description = "Bends impossible deadlines into 'aggressive but doable.'",
			Multiplier = WorldlyMultiplier },
		new() { Role = GameDevRole.ProjectManager, Tier = GameDevAbilityTier.Legendary,
			Name = "Time Itself",
			Description = "Has personally negotiated terms with the calendar.",
			Multiplier = LegendaryMultiplier },

		// ── Producer — bug fighting ─────────────────────────────────────────
		new() { Role = GameDevRole.Producer, Tier = GameDevAbilityTier.Novice,
			Name = "Bug Catcher",
			Description = "Spots the obvious crashes before QA does.",
			Multiplier = NoviceMultiplier },
		new() { Role = GameDevRole.Producer, Tier = GameDevAbilityTier.Skilled,
			Name = "QA Veteran",
			Description = "Has shipped enough builds to know where to look.",
			Multiplier = SkilledMultiplier },
		new() { Role = GameDevRole.Producer, Tier = GameDevAbilityTier.Renowned,
			Name = "Bug Hunter",
			Description = "Reproduces edge cases the engineers swore were impossible.",
			Multiplier = RenownedMultiplier },
		new() { Role = GameDevRole.Producer, Tier = GameDevAbilityTier.Worldly,
			Name = "Quality Gatekeeper",
			Description = "Nothing ships under his name with a P0 open.",
			Multiplier = WorldlyMultiplier },
		new() { Role = GameDevRole.Producer, Tier = GameDevAbilityTier.Legendary,
			Name = "The Last Bug",
			Description = "Bugs flee the project at the mention of her name.",
			Multiplier = LegendaryMultiplier },

		// ── Game Director — fun / design ────────────────────────────────────
		new() { Role = GameDevRole.GameDirector, Tier = GameDevAbilityTier.Novice,
			Name = "Idea Pitcher",
			Description = "One in five of his pitches actually works.",
			Multiplier = NoviceMultiplier },
		new() { Role = GameDevRole.GameDirector, Tier = GameDevAbilityTier.Skilled,
			Name = "Game Whisperer",
			Description = "Turns dry mechanics into playable moments.",
			Multiplier = SkilledMultiplier },
		new() { Role = GameDevRole.GameDirector, Tier = GameDevAbilityTier.Renowned,
			Name = "Genre Visionary",
			Description = "Sees the next twist in the formula before anyone else does.",
			Multiplier = RenownedMultiplier },
		new() { Role = GameDevRole.GameDirector, Tier = GameDevAbilityTier.Worldly,
			Name = "Industry Auteur",
			Description = "Critics review her games with reverence.",
			Multiplier = WorldlyMultiplier },
		new() { Role = GameDevRole.GameDirector, Tier = GameDevAbilityTier.Legendary,
			Name = "The Next Miyamoto",
			Description = "Designs landmark experiences a generation studies for years.",
			Multiplier = LegendaryMultiplier },

		// ── Sound Director — audio ──────────────────────────────────────────
		new() { Role = GameDevRole.SoundDirector, Tier = GameDevAbilityTier.Novice,
			Name = "Beat Maker",
			Description = "Loops are clean and the kicks land.",
			Multiplier = NoviceMultiplier },
		new() { Role = GameDevRole.SoundDirector, Tier = GameDevAbilityTier.Skilled,
			Name = "Foley Artist",
			Description = "Records her own footsteps, leaves, and cloth in the studio basement.",
			Multiplier = SkilledMultiplier },
		new() { Role = GameDevRole.SoundDirector, Tier = GameDevAbilityTier.Renowned,
			Name = "Audio Wizard",
			Description = "Mixes for headphones and 5.1 in the same pass.",
			Multiplier = RenownedMultiplier },
		new() { Role = GameDevRole.SoundDirector, Tier = GameDevAbilityTier.Worldly,
			Name = "Composer Laureate",
			Description = "Has a Grammy. Won't talk about it.",
			Multiplier = WorldlyMultiplier },
		new() { Role = GameDevRole.SoundDirector, Tier = GameDevAbilityTier.Legendary,
			Name = "Heir of Mozart",
			Description = "Hears the score for your game before the design doc is written.",
			Multiplier = LegendaryMultiplier },

		// ── Art Director — graphics ─────────────────────────────────────────
		new() { Role = GameDevRole.ArtDirector, Tier = GameDevAbilityTier.Novice,
			Name = "Pixel Pusher",
			Description = "Every sprite reads on a single glance.",
			Multiplier = NoviceMultiplier },
		new() { Role = GameDevRole.ArtDirector, Tier = GameDevAbilityTier.Skilled,
			Name = "Concept Maestro",
			Description = "Paints worlds in a morning that take the team months to build.",
			Multiplier = SkilledMultiplier },
		new() { Role = GameDevRole.ArtDirector, Tier = GameDevAbilityTier.Renowned,
			Name = "Color Theorist",
			Description = "Holds a palette in his head the entire team adopts within a week.",
			Multiplier = RenownedMultiplier },
		new() { Role = GameDevRole.ArtDirector, Tier = GameDevAbilityTier.Worldly,
			Name = "Visionary Painter",
			Description = "Her style books sell better than most studios' games.",
			Multiplier = WorldlyMultiplier },
		new() { Role = GameDevRole.ArtDirector, Tier = GameDevAbilityTier.Legendary,
			Name = "Descendant of Van Gogh",
			Description = "Allegedly. Won't confirm. Look at the brushwork and decide for yourself.",
			Multiplier = LegendaryMultiplier },
	};

	public static GameDevAbility Get( GameDevRole role, GameDevAbilityTier tier )
	{
		foreach ( var a in All )
			if ( a.Role == role && a.Tier == tier ) return a;
		return null;
	}

	public static IEnumerable<GameDevAbility> ForRole( GameDevRole role )
	{
		foreach ( var a in All )
			if ( a.Role == role ) yield return a;
	}
}

/// <summary>
/// Display helpers for the dev-role enums — keeps Razor templates clean.
/// </summary>
public static class GameDevRoleExtensions
{
	public static string DisplayName( this GameDevRole r ) => r switch
	{
		GameDevRole.ProjectManager => "Project Manager",
		GameDevRole.Producer       => "Producer",
		GameDevRole.GameDirector   => "Game Design",
		GameDevRole.SoundDirector  => "Sound Design",
		GameDevRole.ArtDirector    => "Graphic Design",
		_                          => r.ToString(),
	};

	/// One-line description of the role's contribution to a project.
	public static string Description( this GameDevRole r ) => r switch
	{
		GameDevRole.ProjectManager => "Sets the pace. More throughput per tick.",
		GameDevRole.Producer       => "Cleans up bugs before launch.",
		GameDevRole.GameDirector   => "Owns the fun. Game-design points.",
		GameDevRole.SoundDirector  => "Owns the audio. Sound points.",
		GameDevRole.ArtDirector    => "Owns the look. Graphics points.",
		_                          => "",
	};

	public static string DisplayName( this GameDevAbilityTier t ) => t switch
	{
		GameDevAbilityTier.Novice    => "Novice",
		GameDevAbilityTier.Skilled   => "Skilled",
		GameDevAbilityTier.Renowned  => "Renowned",
		GameDevAbilityTier.Worldly   => "Worldly",
		GameDevAbilityTier.Legendary => "Legendary",
		_                            => t.ToString(),
	};
}
