/// <summary>
/// Where the studio is currently advertising open positions.
/// Higher tiers cost more per month but bring in better candidates.
/// They do NOT change the rate at which applicants arrive — only the
/// quality bracket distribution they roll under.
/// None = no active posting → no new applicants at all.
/// </summary>
public enum JobPostingTier
{
	None          = 0,
	BulletinBoard = 1,
	CareerSite    = 2,
	Headhunter    = 3,
}

/// <summary>
/// Metadata for each posting tier — display name, monthly cost, the
/// achievement that unlocks it (null = available from the start), and a
/// one-line blurb shown in the HR posting picker.
/// </summary>
public static class JobPosting
{
	public sealed class Info
	{
		public JobPostingTier  Tier                { get; init; }
		public string          Name                { get; init; } = "";
		public long            MonthlyCost         { get; init; }
		/// <summary>Null = always unlocked. Otherwise gated on this achievement.</summary>
		public AchievementId?  RequiredAchievement { get; init; }
		public string          Description         { get; init; } = "";
	}

	public static readonly IReadOnlyList<Info> All = new Info[]
	{
		new() {
			Tier                = JobPostingTier.None,
			Name                = "Off",
			MonthlyCost         = 0,
			RequiredAchievement = null,
			Description         = "Not advertising. No new applicants will arrive.",
		},
		new() {
			Tier                = JobPostingTier.BulletinBoard,
			Name                = "Bulletin Board",
			MonthlyCost         = 0,
			RequiredAchievement = null,
			Description         = "Free local listing — word-of-mouth juniors. Lowest applicant quality.",
		},
		new() {
			Tier                = JobPostingTier.CareerSite,
			Name                = "Career Site",
			MonthlyCost         = 3_000,
			RequiredAchievement = AchievementId.Ship3Games,
			Description         = "Reaches working professionals. Balanced quality.",
		},
		new() {
			Tier                = JobPostingTier.Headhunter,
			Name                = "Headhunter Agency",
			MonthlyCost         = 12_000,
			RequiredAchievement = AchievementId.Earn100k,
			Description         = "Targets senior talent. Most applicants will be experienced.",
		},
	};

	public static Info Get( JobPostingTier tier )
	{
		foreach ( var i in All )
			if ( i.Tier == tier ) return i;
		return All[0];
	}

	/// True if the posting tier has no requirement, or its required achievement
	/// has been unlocked.
	public static bool IsUnlocked( JobPostingTier tier )
	{
		var req = Get( tier ).RequiredAchievement;
		return req is null || Achievements.IsUnlocked( req.Value );
	}

	/// Friendly "Unlocked by …" string for the HR posting picker. Returns an
	/// empty string for tiers that have no requirement.
	public static string UnlockHint( JobPostingTier tier )
	{
		var req = Get( tier ).RequiredAchievement;
		if ( req is null ) return "";
		var ach = Achievements.Get( req.Value );
		return ach is null
			? $"Unlocked by achievement."
			: $"Unlocks: {ach.Name} — {ach.Description}";
	}
}
