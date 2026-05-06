/// <summary>
/// Marks a position in the office for employees to wander to when they're
/// not at their desk.
///
/// Drop one of these on any GameObject (a bookshelf, whiteboard, water-cooler,
/// couch, …) and pick a <see cref="Kind"/>:
///
///   Training — bookshelves / learning area. Employees in the Training state
///              walk here to "study". One of these per few desks is plenty.
///   Break    — couches / water-coolers / coffee machines. Employees in the
///              Distracted state pick one of these as their wander target.
///
/// HRManager / EmployeeNPC discover anchors at runtime via
/// <c>Scene.GetAllComponents&lt;WorkplaceAnchor&gt;()</c> — no manual wiring.
/// </summary>
public enum WorkplaceAnchorKind
{
	Training,
	Break,
}

public sealed class WorkplaceAnchor : Component
{
	[Property] public WorkplaceAnchorKind Kind { get; set; } = WorkplaceAnchorKind.Training;

	/// World position the NPC will walk to. Reads the GameObject's transform
	/// so designers can just move the marker around in the editor.
	public Vector3 Position => WorldPosition;
}
