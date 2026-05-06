/// <summary>
/// What "shape" of hire an employee is. Drives the inbox roll, the desk-take
/// rule, the salary multiplier, and the colour of the chip shown next to the
/// name in the HR menu.
///
/// Most hires are <see cref="Regular"/> — the existing role-shaped employees
/// from the studio's day-one applicant pool. Other kinds are special hires
/// gated behind achievements and unlocked in the order listed here:
///   1. <see cref="Mentor"/>           — boosts training stat-gains; no desk.
///   2. <see cref="MarketingAgent"/>   — boosts marketing on game release; no desk.
///   3. <see cref="RemoteWorker"/>     — works on projects from home; no desk.
///   4. <see cref="Intern"/>           — 2-month contract, takes a desk, may
///                                       return later as a star applicant.
/// </summary>
public enum EmployeeKind
{
	Regular        = 0,
	Mentor         = 1,
	MarketingAgent = 2,
	RemoteWorker   = 3,
	Intern         = 4,
}

/// <summary>
/// Display + theming helpers so UI code never has to switch on the enum
/// directly. The CSS-class strings here mirror the colour rules in
/// <c>GameMenuPanel.razor.scss</c> — keep them in sync if you rename one.
/// </summary>
public static class EmployeeKindExtensions
{
	public static string DisplayName( this EmployeeKind k ) => k switch
	{
		EmployeeKind.Regular        => "Hire",
		EmployeeKind.Mentor         => "Mentor",
		EmployeeKind.MarketingAgent => "Marketing Agent",
		EmployeeKind.RemoteWorker   => "Remote Worker",
		EmployeeKind.Intern         => "Intern",
		_                           => k.ToString(),
	};

	/// One-liner shown under the name in the applicant card.
	public static string Description( this EmployeeKind k ) => k switch
	{
		EmployeeKind.Mentor         => "Speeds up your team's training. No desk needed.",
		EmployeeKind.MarketingAgent => "Boosts marketing on every game release. No desk needed.",
		EmployeeKind.RemoteWorker   => "Works from home — contributes to projects without a desk.",
		EmployeeKind.Intern         => "Two-month contract. Takes a desk. May return as a star.",
		_                           => "",
	};

	/// CSS modifier appended to applicant rows / interview heads / staff chips
	/// so each kind has its own accent colour.
	public static string BadgeClass( this EmployeeKind k ) => k switch
	{
		EmployeeKind.Mentor         => "kind-mentor",
		EmployeeKind.MarketingAgent => "kind-marketing",
		EmployeeKind.RemoteWorker   => "kind-remote",
		EmployeeKind.Intern         => "kind-intern",
		_                           => "",
	};

	/// Short uppercase pill text shown next to the applicant's name.
	public static string PillLabel( this EmployeeKind k ) => k switch
	{
		EmployeeKind.Mentor         => "MENTOR",
		EmployeeKind.MarketingAgent => "MARKETING",
		EmployeeKind.RemoteWorker   => "REMOTE",
		EmployeeKind.Intern         => "INTERN",
		_                           => "",
	};

	public static bool IsSpecial( this EmployeeKind k ) => k != EmployeeKind.Regular;
}
