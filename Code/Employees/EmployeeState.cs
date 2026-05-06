/// <summary>
/// What an employee is currently doing on the office floor.
/// Drives the simple state machine in <see cref="EmployeeNPC"/> — picks a
/// destination (desk, training anchor, wander spot) and gates productivity:
///
///   Working    — sitting at their assigned desk; full stat output.
///   Training   — at a bookshelf / learning area; no immediate output, but
///                a slow long-term stat gain (added later).
///   Distracted — wandering the office; produces nothing this tick.
///   Idle       — default for unassigned NPCs / freshly hired before they've
///                walked to their desk for the first time.
/// </summary>
public enum EmployeeState
{
	Idle,
	Working,
	Training,
	Distracted,
}
