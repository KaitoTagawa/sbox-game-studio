/// <summary>
/// Founder-only perks. These are deliberately separate from
/// <see cref="EmployeeAbility"/> — regular hires can never roll any of these,
/// so the studio head is always meaningfully different from a senior employee.
///
/// Pick a small set at character creation and unlock more as the player levels
/// up. Most are passive multipliers / cost reductions on systems the player
/// already interacts with (HR, marketing, energy).
/// </summary>
public enum PlayerAbility
{
	/// <summary>Interview energy cost reduced by 50 %.</summary>
	ChiefRecruiter,

	/// <summary>Monthly posting fees reduced by 30 %.</summary>
	PennyPincher,

	/// <summary>New hires accept 10 % lower starting salaries.</summary>
	DealCloser,

	/// <summary>Player energy regenerates 50 % faster.</summary>
	CrunchEvangelist,

	/// <summary>All employee morale recovers 1 % more per paid month.</summary>
	VisionaryFounder,

	/// <summary>Released games gain a 30 % marketing-attention bonus.</summary>
	HypeMachine,

	/// <summary>Genre / market demand visible one in-game month early.</summary>
	TrendForecaster,

	/// <summary>The player's own contributions count for 50 % more output.</summary>
	MidnightOilCoder,

	/// <summary>Stats grow 25 % faster from any training session the player attends.</summary>
	SelfTaught,

	/// <summary>One free re-roll on the applicant inbox per in-game month.</summary>
	NetworkedFounder,
}

/// <summary>
/// Display / description metadata for <see cref="PlayerAbility"/>. Mirrors the
/// <see cref="EmployeeAbilityExtensions"/> shape so the same UI patterns can
/// render either set.
/// </summary>
public static class PlayerAbilityExtensions
{
	public static string DisplayName( this PlayerAbility a ) => a switch
	{
		PlayerAbility.ChiefRecruiter   => "Chief Recruiter",
		PlayerAbility.PennyPincher     => "Penny Pincher",
		PlayerAbility.DealCloser       => "Deal Closer",
		PlayerAbility.CrunchEvangelist => "Crunch Evangelist",
		PlayerAbility.VisionaryFounder => "Visionary Founder",
		PlayerAbility.HypeMachine      => "Hype Machine",
		PlayerAbility.TrendForecaster  => "Trend Forecaster",
		PlayerAbility.MidnightOilCoder => "Midnight-Oil Coder",
		PlayerAbility.SelfTaught       => "Self-Taught",
		PlayerAbility.NetworkedFounder => "Networked Founder",
		_                              => a.ToString(),
	};

	public static string Description( this PlayerAbility a ) => a switch
	{
		PlayerAbility.ChiefRecruiter   => "Interviews cost 50% less energy.",
		PlayerAbility.PennyPincher     => "Job posting fees reduced by 30%.",
		PlayerAbility.DealCloser       => "New hires accept 10% lower starting salaries.",
		PlayerAbility.CrunchEvangelist => "Your energy regenerates 50% faster.",
		PlayerAbility.VisionaryFounder => "Employee morale recovers 1% extra each paid month.",
		PlayerAbility.HypeMachine      => "Marketing campaigns are 30% more effective.",
		PlayerAbility.TrendForecaster  => "See genre demand one in-game month in advance.",
		PlayerAbility.MidnightOilCoder => "Your own work contributes 50% more output.",
		PlayerAbility.SelfTaught       => "You gain 25% more from any training session.",
		PlayerAbility.NetworkedFounder => "One free applicant re-roll per in-game month.",
		_                              => string.Empty,
	};
}
