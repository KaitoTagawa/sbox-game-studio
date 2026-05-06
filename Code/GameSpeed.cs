/// <summary>
/// Selectable game-flow speeds. Drives <see cref="GameManager.TimeMultiplier"/>:
/// the calendar advances and energy regenerates that many times faster.
///
/// Mirrors <see cref="JobPosting"/>'s shape — each tier carries display
/// metadata and an optional achievement gate. Achievement-gated tiers stay
/// invisible (well, locked) until the player earns the requirement, so the
/// "skip ahead" buttons unlock as a reward for progress instead of being
/// available from the first minute.
/// </summary>
public enum GameSpeedTier
{
	Normal   = 0,
	Fast     = 1,
	VeryFast = 2,
	Insane   = 3,
}

public static class GameSpeed
{
	public sealed class Info
	{
		public GameSpeedTier   Tier                { get; init; }
		public string          Name                { get; init; } = "";
		public float           Multiplier          { get; init; } = 1f;
		/// <summary>Null = always available. Otherwise gated on this achievement.</summary>
		public AchievementId?  RequiredAchievement { get; init; }
		public string          Description         { get; init; } = "";
	}

	public static readonly IReadOnlyList<Info> All = new Info[]
	{
		new() {
			Tier                = GameSpeedTier.Normal,
			Name                = "Normal",
			Multiplier          = 1f,
			RequiredAchievement = null,
			Description         = "Real time. The default cadence — 1 real minute per in-game day.",
		},
		new() {
			Tier                = GameSpeedTier.Fast,
			Name                = "Fast",
			Multiplier          = 2f,
			RequiredAchievement = AchievementId.Hire5,
			Description         = "2× speed. Days fly by — good once you've got a few employees humming.",
		},
		new() {
			Tier                = GameSpeedTier.VeryFast,
			Name                = "Very Fast",
			Multiplier          = 4f,
			RequiredAchievement = AchievementId.Earn100k,
			Description         = "4× speed. Months in minutes; for waiting out big projects.",
		},
		new() {
			Tier                = GameSpeedTier.Insane,
			Name                = "Insane",
			Multiplier          = 8f,
			RequiredAchievement = AchievementId.Earn1M,
			Description         = "8× speed. For the impatient mogul.",
		},
	};

	public static Info Get( GameSpeedTier tier )
	{
		foreach ( var i in All )
			if ( i.Tier == tier ) return i;
		return All[0];
	}

	public static bool IsUnlocked( GameSpeedTier tier )
	{
		var req = Get( tier ).RequiredAchievement;
		return req is null || Achievements.IsUnlocked( req.Value );
	}
}
