/// <summary>
/// Per-kind metadata for the four special-hire pipelines. Mirrors the shape
/// of <see cref="JobPosting"/> / <see cref="GameSpeed"/> / <see cref="GameGenres"/>
/// so the rest of the codebase can reach for the same patterns:
///
///   • <c>SpecialHires.Get( kind )</c>           — info lookup.
///   • <c>SpecialHires.IsUnlocked( kind )</c>    — has the gate been satisfied?
///   • <c>SpecialHires.UnlockedKinds()</c>       — currently rollable specials.
///
/// Anything kind-related that the rest of the game needs to know lives here:
/// salary scaling, desk requirement, ability-slot guarantees, and the
/// achievement that unlocks the kind. Generation rules live in
/// <see cref="Employee"/>; lifecycle rules (intern contract, alumni return)
/// live in <see cref="HRManager"/>.
/// </summary>
public static class SpecialHires
{
	public sealed class Info
	{
		public EmployeeKind     Kind                { get; init; }
		public string           Name                { get; init; } = "";
		public string           Description         { get; init; } = "";

		/// True if this kind needs a free desk to be hired.
		public bool             TakesDesk           { get; init; } = true;

		/// Multiplier on the applicant's auto-computed salary. Specials are
		/// usually pricier than the same-tier regular hire to offset their
		/// passive perks.
		public float            SalaryMultiplier    { get; init; } = 1f;

		/// 0–2 abilities normally; 1 = always exactly one; 2 = always two.
		/// Mentors/marketing/remote get a guaranteed ability so their hire
		/// feels distinct; interns have an empty pool (their value comes
		/// from contract length + alumni return).
		public int              GuaranteedAbilities { get; init; } = 0;

		/// Null = always unlocked. Otherwise gated on this achievement.
		public AchievementId?   RequiredAchievement { get; init; }
	}

	public static readonly IReadOnlyList<Info> All = new Info[]
	{
		new() {
			Kind                = EmployeeKind.Regular,
			Name                = "Hire",
			Description         = "Standard employee. Takes a desk; works on projects.",
			TakesDesk           = true,
			SalaryMultiplier    = 1f,
			GuaranteedAbilities = 0,
			RequiredAchievement = null,
		},

		// Mentor — the first special unlocked. Boosts training stat-gain rate.
		new() {
			Kind                = EmployeeKind.Mentor,
			Name                = "Mentor",
			Description         = "Speeds up your team's training. No desk needed.",
			TakesDesk           = false,
			SalaryMultiplier    = 1.4f,
			GuaranteedAbilities = 1,
			RequiredAchievement = AchievementId.Hire5,
		},

		// Marketing Agent — second unlock. Boosts marketing on game release.
		new() {
			Kind                = EmployeeKind.MarketingAgent,
			Name                = "Marketing Agent",
			Description         = "Boosts marketing on every game you ship.",
			TakesDesk           = false,
			SalaryMultiplier    = 1.5f,
			GuaranteedAbilities = 1,
			RequiredAchievement = AchievementId.Hire10,
		},

		// Remote Worker — third unlock. Standard role contribution, no desk.
		new() {
			Kind                = EmployeeKind.RemoteWorker,
			Name                = "Remote Worker",
			Description         = "Contributes to projects from home — no desk needed.",
			TakesDesk           = false,
			SalaryMultiplier    = 1.3f,
			GuaranteedAbilities = 0,
			RequiredAchievement = AchievementId.Earn100k,
		},

		// Intern — last unlock. Cheap junior on a 2-month contract, may
		// return later with much stronger stats. See HRManager for the
		// contract / alumni mechanics.
		new() {
			Kind                = EmployeeKind.Intern,
			Name                = "Intern",
			Description         = "Two-month contract. Takes a desk. May return.",
			TakesDesk           = true,
			SalaryMultiplier    = 0.4f,
			GuaranteedAbilities = 0,
			RequiredAchievement = AchievementId.Earn1M,
		},
	};

	public static Info Get( EmployeeKind kind )
	{
		foreach ( var i in All )
			if ( i.Kind == kind ) return i;
		return All[0];
	}

	public static bool IsUnlocked( EmployeeKind kind )
	{
		var req = Get( kind ).RequiredAchievement;
		return req is null || Achievements.IsUnlocked( req.Value );
	}

	public static bool TakesDesk( EmployeeKind kind ) => Get( kind ).TakesDesk;

	/// All special kinds (excluding <see cref="EmployeeKind.Regular"/>) that
	/// the player has unlocked. Used by HRManager when rolling for the next
	/// applicant — only unlocked specials are candidates.
	public static IEnumerable<EmployeeKind> UnlockedSpecials()
	{
		foreach ( var i in All )
		{
			if ( i.Kind == EmployeeKind.Regular ) continue;
			if ( IsUnlocked( i.Kind ) ) yield return i.Kind;
		}
	}
}
