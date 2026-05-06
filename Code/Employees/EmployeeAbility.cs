/// <summary>
/// Special passive ability an employee can have (1–2 per employee).
/// Abilities are revealed during the interview process, not through stats.
/// They represent personality traits and work-style quirks.
/// </summary>
public enum EmployeeAbility
{
	/// <summary>
	/// Nearby employees gain stats 25 % faster during training sessions.
	/// Covers the "Leadership" role from the original 10-stat design.
	/// </summary>
	Leader,

	/// <summary>
	/// Adjacent employees produce 10 % more output while this employee is working.
	/// Covers the "Teamwork" role from the original 10-stat design.
	/// </summary>
	TeamPlayer,

	/// <summary>
	/// No stat decay during crunch periods or overtime shifts.
	/// Great for deadline-heavy projects.
	/// </summary>
	NightOwl,

	/// <summary>
	/// Gains stats 40 % faster from all training activities.
	/// A diamond-in-the-rough with low current stats but huge growth ceiling.
	/// </summary>
	QuickLearner,

	/// <summary>
	/// 15 % chance of an innovation breakthrough on each project milestone,
	/// adding a bonus to the game's Innovation score.
	/// </summary>
	CreativeSpark,

	/// <summary>
	/// No output penalty when assigned outside their primary role.
	/// Useful for small teams that need everyone to wear multiple hats.
	/// </summary>
	Polymath,

	/// <summary>
	/// Bug rate cut by 50 %, but Implementation Speed reduced by 20 %.
	/// Slows the team down but ships cleaner code.
	/// </summary>
	Perfectionist,

	/// <summary>
	/// Passively boosts the Creativity sub-stat (Innovation) of every employee
	/// working in the same room by a flat amount.
	/// </summary>
	Inspiration,

	/// <summary>
	/// Lone-wolf coder. Contributes a hidden bonus to a game in production
	/// *only when left unassigned* on the project — sneaking off and shipping
	/// quietly at 3 AM. Putting them on any role disables the bonus, so the
	/// player has to choose: predictable role output, or unpredictable shadow
	/// output.
	/// </summary>
	SuperHacker,
}

/// <summary>
/// Helper extensions for ability descriptions shown in interview/HR panels.
/// </summary>
public static class EmployeeAbilityExtensions
{
	public static string DisplayName( this EmployeeAbility a ) => a switch
	{
		EmployeeAbility.Leader        => "Leader",
		EmployeeAbility.TeamPlayer    => "Team Player",
		EmployeeAbility.NightOwl      => "Night Owl",
		EmployeeAbility.QuickLearner  => "Quick Learner",
		EmployeeAbility.CreativeSpark => "Creative Spark",
		EmployeeAbility.Polymath      => "Polymath",
		EmployeeAbility.Perfectionist => "Perfectionist",
		EmployeeAbility.Inspiration   => "Inspiration",
		EmployeeAbility.SuperHacker   => "Super Hacker",
		_                             => a.ToString(),
	};

	public static string Description( this EmployeeAbility a ) => a switch
	{
		EmployeeAbility.Leader        => "Nearby employees train 25% faster.",
		EmployeeAbility.TeamPlayer    => "Adjacent workers produce 10% more output.",
		EmployeeAbility.NightOwl      => "No stat decay during crunch or overtime.",
		EmployeeAbility.QuickLearner  => "Gains stats 40% faster from training.",
		EmployeeAbility.CreativeSpark => "15% chance of innovation breakthrough per milestone.",
		EmployeeAbility.Polymath      => "No penalty when working outside primary role.",
		EmployeeAbility.Perfectionist => "50% fewer bugs, but 20% slower implementation.",
		EmployeeAbility.Inspiration   => "Boosts everyone's Innovation stat in the same room.",
		EmployeeAbility.SuperHacker   => "Hidden bonus to a game in production — but only when left unassigned.",
		_                             => string.Empty,
	};
}
