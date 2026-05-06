/// <summary>
/// Anyone who can be slotted into a <see cref="GameDevRole"/> on a project.
///
/// <see cref="EmployeeNPC"/> implements this for hired staff; <see cref="PlayerStats"/>
/// implements it for the studio founder (the player's own avatar). The Create
/// Game panel and the production tick both iterate the same <see cref="IDevWorker"/>
/// list so the player can be assigned to a role exactly like a hire.
///
/// Why an interface and not a shared base class? <see cref="EmployeeNPC"/> and
/// <see cref="PlayerStats"/> are both Components with very different lifecycles
/// — the NPC walks to a desk and ticks a state machine, the founder is the
/// player's own GameObject. Each owns its own data; the interface just unifies
/// the read-side view that the dev-game systems need.
/// </summary>
public interface IDevWorker
{
	/// Display name. For NPCs, the hire's name; for the player, "Founder" (or
	/// whatever <see cref="PlayerStats.Name"/> has been customised to).
	string Name { get; }

	/// Primary department. Drives which pillar the worker contributes to most.
	EmployeeRole Role { get; }

	/// Six-stat profile. Same shape across NPCs and the player so the
	/// production tick can sum contributions without branching on type.
	EmployeeStats Stats { get; }

	/// Personality / work-style traits. The player has founder-only
	/// <see cref="PlayerAbility"/> perks instead of <see cref="EmployeeAbility"/>,
	/// so for the player both slots return null.
	EmployeeAbility? Ability1 { get; }
	EmployeeAbility? Ability2 { get; }

	/// True when this worker is the player avatar — the UI uses this to render
	/// a "FOUNDER" pill instead of the regular employee ability chips.
	bool IsPlayer { get; }

	/// Per-month wage. Founder always returns 0 (the player isn't on
	/// payroll). Hires return their negotiated rate from the interview.
	/// Read-only across the interface — salary changes are managed by the
	/// owning component (PlayerStats / EmployeeNPC) on its own terms.
	long Salary { get; }

	/// Convenience query — true if either ability slot matches.
	bool HasAbility( EmployeeAbility a );

	// ── Training state ───────────────────────────────────────────────────────
	// Both kinds of worker (founder and hires) own their own training state so
	// it serializes naturally on save and so PillarContribution / production
	// systems can branch on `worker.ActiveTraining` without type-checking.

	/// Current off-site session, or null if available. Workers with a non-null
	/// session do not appear at their desk and do not contribute to projects
	/// until <see cref="TrainingManager"/> clears it on completion.
	TrainingSession ActiveTraining { get; set; }

	/// Currently-assigned research topic id (see <see cref="ResearchTopics"/>),
	/// or null if not researching. One topic per worker at a time. Researching
	/// workers stay on-desk and contribute to projects at
	/// <see cref="TrainingManager.ResearchProductivityFactor"/> (the
	/// productivity tax that pays for the topic's drip toward completion).
	string ResearchTopicId { get; set; }

	/// Days the worker has accumulated toward the current research topic.
	/// Total time = <see cref="ResearchTopic.BaseDays"/> × (100 / max(20, primary-stat))
	/// — see `TrainingManager.ResearchTotalDays`. When this hits the total,
	/// the topic completes and the genre unlocks.
	float DaysIntoCurrentResearch { get; set; }
}
