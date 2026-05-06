/// <summary>
/// Stable identifier for each achievement. Other systems gate content by
/// calling <c>Achievements.IsUnlocked( AchievementId.Foo )</c>, so add new
/// values rather than reordering existing ones (the underlying int order
/// will eventually matter for save data).
/// </summary>
public enum AchievementId
{
	// ── Money milestones (lifetime earnings) ────────────────────────────────
	Earn1k,
	Earn10k,
	Earn100k,
	Earn1M,

	// ── Hiring milestones ───────────────────────────────────────────────────
	FirstHire,
	Hire5,
	Hire8,
	Hire10,

	// ── Player counts (monthly peak across all released games) ──────────────
	Players100,
	Players10k,
	Players1M,

	// ── Ability discovery (each ability seen on a hired employee) ───────────
	DiscoverFirstAbility,
	DiscoverAllAbilities,

	// ── Shipping milestones ─────────────────────────────────────────────────
	ShipFirstGame,
	Ship3Games,
	Ship10Games,
	Ship5Games,

	// ── Onboarding ─────────────────────────────────────────────────────────
	TutorialComplete,
}

/// <summary>
/// One achievement definition: the id used in lookups, the name shown in the
/// notification toast and lock-hints, and a predicate that's evaluated against
/// the achievement-tracker's stats whenever a record method runs.
/// </summary>
public sealed class Achievement
{
	public AchievementId                  Id          { get; init; }
	public string                         Name        { get; init; } = "";
	public string                         Description { get; init; } = "";
	public Func<bool>                     Evaluate    { get; init; }

	/// True for "spoiler" achievements that should appear as "???" in the
	/// Gallery's Unlocks list until the player earns them. Default false —
	/// most milestone-style achievements (money, hires, players) read fine
	/// pre-unlock and act as clear progression goals. Mark only the ones
	/// where reveal-on-unlock is part of the fun.
	public bool                           IsHidden    { get; init; } = false;

	/// Optional one-time cash bonus deposited into <c>GameManager.Money</c>
	/// when this achievement unlocks. Narrative framing: investor
	/// confidence, milestone funding, first-sale royalties — anything that
	/// reads as "the world rewards the studio for this". Default 0 (most
	/// achievements are pure tracker badges).
	///
	/// Note: bonus money is added directly to <c>Money</c> and does NOT
	/// pass through <c>RecordMoneyEarned</c>, so it can't cascade further
	/// money-tier achievements. "Funding" ≠ "earnings" by design.
	public long                           MoneyBonus  { get; init; } = 0;
}
