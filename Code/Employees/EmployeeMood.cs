/// <summary>
/// Per-employee mood state. Set during a project's Production phase (see
/// <see cref="EmployeeNPC.TickMood"/>) and cleared back to <see cref="Neutral"/>
/// the moment the project leaves Production.
///
/// Effects:
///   • <see cref="Bad"/>  — pillar-contribution × 0.5 in <c>GameProjectManager.PillarContribution</c>.
///                          Cleared the moment the player opens a chat with the NPC.
///   • <see cref="Good"/> — NPC has a non-null <see cref="EmployeeNPC.PendingSuggestion"/>.
///                          The chat panel shows it as the greeting and replaces the
///                          default reply set with Accept / Decline.
/// </summary>
public enum EmployeeMood
{
	Neutral,
	Good,
	Bad,
}

/// <summary>
/// A feature idea an NPC pitches while in <see cref="EmployeeMood.Good"/>.
/// <para><see cref="Cost"/> = 0 means the player accepts for free with a
/// 50 % chance the bonus lands. <see cref="Cost"/> &gt; 0 means accepting
/// deducts the money and the bonus is guaranteed.</para>
/// <para>Plain class with public auto-properties + parameterless ctor —
/// this type is exposed on <see cref="EmployeeNPC.PendingSuggestion"/> as
/// a <c>[Property]</c>, and s&amp;box's inspector / scene serialiser
/// instantiates it via <c>Activator.CreateInstance</c>. A <c>record</c>
/// (init-only, positional ctor only) hits
/// <c>MissingMethodException: No parameterless constructor defined</c>
/// when the editor tries to render the widget. Same rule as the save-
/// system ADR.</para>
/// </summary>
public sealed class EmployeeSuggestion
{
	public string Text { get; set; } = "";
	public long   Cost { get; set; }

	public EmployeeSuggestion() { }

	public EmployeeSuggestion( string text, long cost = 0 )
	{
		Text = text;
		Cost = cost;
	}
}

/// <summary>
/// Which project pillar an accepted suggestion buffs. Mapped from the
/// suggesting NPC's role in <see cref="EmployeeSuggestions.PillarForRole"/>.
/// </summary>
public enum ProjectPillar { Design, Sound, Graphics }

/// <summary>
/// Suggestion text pools per role. Each role has a mix of free ideas (cost = 0)
/// and paid asks (cost &gt; 0) — paid asks pay out reliably, free ideas are
/// a 50/50 gamble. Implemented as a method (not a static-readonly array)
/// because s&amp;box's hot-reload doesn't re-run static initializers — same
/// reasoning as <c>InventoryCatalogue.All</c>.
/// </summary>
public static class EmployeeSuggestions
{
	public static EmployeeSuggestion Random( EmployeeRole role, System.Random rng )
	{
		var pool = PoolFor( role );
		return pool[rng.Next( pool.Length )];
	}

	/// Which pillar an accepted idea from this role buffs. Researcher rolls
	/// random because their work spans pillars.
	public static ProjectPillar PillarForRole( EmployeeRole role, System.Random rng ) => role switch
	{
		EmployeeRole.Programmer
		 or EmployeeRole.Designer
		 or EmployeeRole.Creative      => ProjectPillar.Design,
		EmployeeRole.Artist            => ProjectPillar.Graphics,
		EmployeeRole.SoundDesigner     => ProjectPillar.Sound,
		EmployeeRole.Researcher        => RandomPillar( rng ),
		_                              => ProjectPillar.Design,
	};

	/// Which sub-stat's average drives the +10 % bonus when an idea lands.
	/// Mirrors the <see cref="EmployeeRole"/> → primary-stat mapping used at
	/// generation time (<see cref="EmployeeStats.Generate"/>).
	public static int PrimaryStatValue( EmployeeRole role, EmployeeStats stats ) => role switch
	{
		EmployeeRole.Programmer    => stats.Programming.Average,
		EmployeeRole.Designer      => stats.Design.Average,
		EmployeeRole.Creative      => stats.Creativity.Average,
		EmployeeRole.Artist        => stats.Artistry.Average,
		EmployeeRole.SoundDesigner => stats.Sound.Average,
		EmployeeRole.Researcher    => stats.Focus.Average,
		_                          => stats.Overall,
	};

	static ProjectPillar RandomPillar( System.Random rng ) =>
		(ProjectPillar)rng.Next( 0, 3 );

	/// Inflate a suggestion's base cost by studio size, measured by owned
	/// Desk count. Linear ramp: 1 desk = 1×, 8 desks = 10×. Anchors mean
	/// a $300 base reads as $300 solo and $3,000 at the full 8-desk
	/// studio, with even steps in between (~$600 at 2 desks, ~$1,200 at
	/// 4, etc.). Clamps desks to 1..8 so buying past the 8-slot cap
	/// doesn't keep inflating costs forever.
	public static long ScaledCost( long baseCost )
	{
		if ( baseCost <= 0 ) return 0;

		const int   AnchorDesks  = 8;
		const float AnchorScale  = 10f;

		int desks = InventoryManager.Instance?.OwnedCount( ItemKind.Desk ) ?? 1;
		desks     = System.Math.Clamp( desks, 1, AnchorDesks );

		float scale = 1f + (desks - 1) * (AnchorScale - 1f) / (AnchorDesks - 1);
		long  raw   = (long)System.Math.Round( baseCost * scale );

		// Round to nearest $100 so the price tag reads clean ($700 / $1,100
		// / $2,600 / $3,000) instead of fractional ramp values like $686.
		return ((raw + 50) / 100) * 100;
	}

	static EmployeeSuggestion[] PoolFor( EmployeeRole role ) => role switch
	{
		EmployeeRole.Programmer => new[]
		{
			new EmployeeSuggestion( "I want to add ragdoll physics. It'd really sell the action.", 0 ),
			new EmployeeSuggestion( "Procedural levels could be killer here.",                       0 ),
			new EmployeeSuggestion( "Hear me out — co-op networking, just a prototype.",             0 ),
			new EmployeeSuggestion( "There's a great middleware library — can I license it?",      300 ),
			new EmployeeSuggestion( "An AI plugin would save us weeks.",                            300 ),
		},
		EmployeeRole.Designer => new[]
		{
			new EmployeeSuggestion( "What if we add a mid-run upgrade system?",                      0 ),
			new EmployeeSuggestion( "Player-driven story branches. Hear me out.",                    0 ),
			new EmployeeSuggestion( "I want to commission a level designer for a week. Doable?",    300 ),
		},
		EmployeeRole.Creative => new[]
		{
			new EmployeeSuggestion( "I have a story twist that'll change everything.",               0 ),
			new EmployeeSuggestion( "Let me write some flavor text for the items?",                  0 ),
			new EmployeeSuggestion( "I found a great VA — can we book one session?",               300 ),
		},
		EmployeeRole.Artist => new[]
		{
			new EmployeeSuggestion( "Stylized PBR pass would look amazing.",                         0 ),
			new EmployeeSuggestion( "Hand-painted UI? Just say the word.",                           0 ),
			new EmployeeSuggestion( "Can I buy this stylized asset pack? It'd save us days.",      300 ),
			new EmployeeSuggestion( "There's a gorgeous brush set on sale.",                       300 ),
		},
		EmployeeRole.SoundDesigner => new[]
		{
			new EmployeeSuggestion( "Adaptive music layers would slap. Let me try?",                 0 ),
			new EmployeeSuggestion( "I want to record some foley today.",                            0 ),
			new EmployeeSuggestion( "I have a cool idea — can I buy this sample pack?",            300 ),
			new EmployeeSuggestion( "A pro reverb plugin would polish the mix.",                   300 ),
		},
		EmployeeRole.Researcher => new[]
		{
			new EmployeeSuggestion( "I read about a new procedural technique. Worth trying.",        0 ),
			new EmployeeSuggestion( "This paper has applications here — let me explore.",            0 ),
			new EmployeeSuggestion( "A research subscription would help.",                          300 ),
		},
		_ => new[] { new EmployeeSuggestion( "I have an idea.", 0 ) },
	};
}
