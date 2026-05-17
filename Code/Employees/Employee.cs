/// <summary>
/// Which department an employee primarily belongs to.
/// Values 0–5 map directly to the EmployeeStats primary stat index
/// (0 = Programming … 5 = Focus). Founder=6 is outside that range and
/// must never be passed to <see cref="EmployeeStats.Generate"/> — it's
/// reserved for the player's own avatar (<see cref="PlayerStats"/>),
/// which seeds its stats via <see cref="EmployeeStats.Flat"/> instead.
/// </summary>
public enum EmployeeRole
{
	Programmer    = 0,
	Designer      = 1,
	Creative      = 2,   // Writer / game designer with high Creativity
	Artist        = 3,
	SoundDesigner = 4,
	Researcher    = 5,   // High Focus; speeds up the research tree
	Founder       = 6,   // Player-only — never assigned to a generated hire.
}

/// <summary>
/// A single employee in the studio.
/// Stats are mutable so they can grow through training and experience.
/// Morale is a 0–1 float multiplier applied to all stat outputs at runtime
/// (e.g. an unmotivated employee with 800 Programming effectively contributes
/// as though they have 400 Programming when Morale = 0.5).
/// </summary>
public sealed class Employee
{
	// ── Identity ─────────────────────────────────────────────────────────────
	public string              Name      { get; init; }
	public int                 Age       { get; init; }
	public EmployeeRole        Role      { get; init; }
	public long                Salary    { get; init; }  // per in-game month

	/// Quality bracket: 0 = Junior, 1 = Mid, 2 = Senior.
	/// Set during generation; controls stat ceiling and interview energy cost.
	public int                 Tier      { get; init; }

	/// Which "shape" of hire this is — Regular, Mentor, MarketingAgent,
	/// RemoteWorker, or Intern. Drives desk allocation, salary scaling, and
	/// the colour of the chip in the HR menu.
	public EmployeeKind        Kind      { get; init; } = EmployeeKind.Regular;

	/// True when this applicant is a previously-employed intern returning
	/// to the inbox with stronger stats. Cosmetic-only (the badge says
	/// "RETURNING" instead of "INTERN") — the rest of the lifecycle is the
	/// same as a fresh intern hire.
	public bool                IsReturningIntern { get; init; }

	/// True only for the very first applicant the player ever sees — the
	/// scripted tutorial hire created by <see cref="GenerateTutorialApplicant"/>.
	/// The InterviewPanel hides the Pass button on this candidate, and
	/// HRManager.PassOnInterviewSubject refuses to discard them. Frames the
	/// onboarding as "here is your first teammate, no choice" instead of
	/// letting a brand-new player accidentally pass on them.
	public bool                IsTutorialHire    { get; init; }

	// ── Stats & abilities ─────────────────────────────────────────────────────
	public EmployeeStats                 Stats     { get; set; }
	public IReadOnlyList<EmployeeAbility> Abilities { get; init; }

	/// Which of the six main stats have been revealed to the player during
	/// interviewing. The InterviewPanel renders revealed stats as numbers
	/// and hidden stats as "?". Populated by HRManager.TryStartInterview
	/// (random 2-of-6 today; future questionnaire will fill in more over
	/// the course of an interview). Cleared after hire/pass — the player
	/// never sees this set on hired staff.
	public HashSet<MainStat> RevealedStats { get; } = new();

	/// Salary "value" multiplier rolled at generation. < 1 = the candidate
	/// is asking below market for their stats (a good deal); > 1 = they're
	/// overpriced. Combined with the 2-of-6 stat reveal, this is the core
	/// of the hire-or-pass skill check: a candidate whose visible stats
	/// look strong AND whose salary is low is probably a bargain; one
	/// whose visible stats look mediocre AND who wants top-of-market money
	/// is almost certainly a trap. Stored for UI / debug; salary is already
	/// baked when this Employee was generated.
	public float SalaryBias { get; init; } = 1f;

	// ── Appearance ───────────────────────────────────────────────────────────

	/// Deterministic seed for hair / skin / clothing variation. Same seed →
	/// same look every time the citizen prefab is configured. Lets the resume
	/// portrait (later) and the in-world NPC pull from one source of truth.
	public int AppearanceSeed { get; init; }

	// ── Desk & workplace ─────────────────────────────────────────────────────

	/// Which physical desk this employee occupies, identified by the
	/// <see cref="PlacementSlot.Id"/> of their placed Desk. Null = applicant
	/// (not yet hired) OR a non-desk-taking kind (Mentor / MarketingAgent /
	/// RemoteWorker). HRManager assigns this on hire.
	public System.Guid? DeskSlotId { get; set; } = null;

	// ── Runtime state ────────────────────────────────────────────────────────

	/// 0.0 – 1.0. Multiplies all effective stat contributions.
	/// Drops when salary is missed; recovers on payment.
	public float  Morale      { get; set; } = 1f;
	public bool   IsWorking   { get; set; }
	public string CurrentTask { get; set; } = string.Empty;

	// ── Helpers ──────────────────────────────────────────────────────────────

	public bool HasAbility( EmployeeAbility a ) => Abilities.Contains( a );

	/// Effective value of a stat block after applying Morale.
	public int EffectiveStat( StatBlock block ) =>
		StatBlock.Clamp( (int)(block.Average * Morale) );

	// ── Name banks ───────────────────────────────────────────────────────────

	static readonly string[] FirstNames =
	{
		// Western / gender-neutral
		"Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Avery", "Quinn",
		"Dakota", "Sage", "Drew", "Blair", "Cameron", "Reese", "Emery", "Finley",
		"Hayden", "Rowan", "Skylar", "Peyton",
		// East Asian
		"Kai", "Wei", "Jing", "Lin", "Haru", "Ren", "Yuki", "Jae", "Soo", "Hyun",
		"Min", "Bo", "Saki", "Xiu",
		// South Asian
		"Arya", "Arjun", "Priya", "Dev", "Ananya", "Kabir", "Riya", "Rohan", "Anika", "Vikram",
		// African / Afro-Caribbean
		"Amara", "Kofi", "Zara", "Jabari", "Nia", "Kwame", "Yara", "Ade",
		// Latin American
		"Camila", "Diego", "Valentina", "Mateo", "Luna", "Santiago", "Valeria", "Rafael",
	};

	static readonly string[] LastNames =
	{
		// East Asian
		"Kim", "Park", "Chen", "Zhang", "Wang", "Nakamura", "Tanaka", "Sato", "Lee", "Liu",
		// South Asian
		"Singh", "Patel", "Sharma", "Kumar", "Gupta", "Nair", "Rao",
		// African / Middle Eastern
		"Okafor", "Osei", "Diallo", "Mensah", "Ibrahim", "Hassan", "Khalil",
		// Latin American
		"Rivera", "Santos", "Rodrigues", "Morales", "Torres", "Vargas", "Gomez",
		// Western European
		"Müller", "Schmidt", "Dupont", "Dubois", "Rossi", "Ferrari", "García", "López",
		// Eastern European
		"Petrov", "Kowalski", "Novak", "Popov", "Ivanova", "Szabo",
		// English / Irish / common
		"Smith", "Johnson", "Williams", "Brown", "Davies", "Wilson",
		"Murphy", "O'Brien", "Walsh", "Anderson",
	};

	// ── Salary helpers ────────────────────────────────────────────────────────

	/// Per-role salary multiplier. Programmer / SoundDesigner premiums
	/// temporarily flattened — both used to tax the player for mechanics
	/// that aren't wired yet (Producer-Programming routing per gamedev
	/// GDD §2.1.14, ability multipliers per §2.1.13). Restore the
	/// premiums once those wires land.
	static float RoleMultiplier( EmployeeRole role ) => role switch
	{
		EmployeeRole.Programmer    => 1.00f,
		EmployeeRole.SoundDesigner => 1.00f,
		EmployeeRole.Designer      => 1.00f,
		EmployeeRole.Artist        => 1.00f,
		EmployeeRole.Creative      => 0.95f,
		EmployeeRole.Researcher    => 0.90f,
		_                          => 1.00f,
	};

	/// Salary derived from actual stat quality on a piecewise exponential
	/// curve (rebalanced 2026-05-08 — was linear ×$2.50/stat).
	/// Formula:
	///   • OVR ≤ 700: <see cref="SalaryAnchorWage"/> × <see cref="SalaryGrowthBase"/>^((overall − <see cref="SalaryAnchorStats"/>) / <see cref="SalaryGrowthStatStep"/>)
	///   • OVR > 700: <c>kneeWage</c> × <see cref="SalaryGrowthLateBase"/>^((overall − <see cref="SalaryGrowthKneeStats"/>) / <see cref="SalaryGrowthStatStep"/>)
	/// Then × role multiplier × ±12 % noise × <paramref name="salaryBias"/>, floored at $50.
	///
	/// The curve is anchored so a 300-OVR hire at neutral bias still costs
	/// ~$750/mo (matches the hard-pinned tutorial wage in
	/// <see cref="GenerateTutorialApplicant"/>, which stays untouched). The
	/// rate is steep (×1.8 per +100 OVR) up to OVR 700, then knees down to
	/// ×1.6 per +100 OVR — so the mid-→-senior climb is punchy, but elite
	/// teams don't price themselves entirely out of profitability.
	///
	/// Anchor table (neutral bias / role / variance):
	///   300 → $750     400 → $1,350     500 → $2,430     600 → $4,374
	///   700 → $7,873   800 → $12,124    900 → $18,672   1000 → $28,752
	///
	/// Late-base (1.54) was reverse-engineered from the 8-employee revenue
	/// model: an all-OVR-1000 studio (3/3/2 pillar split, neutral bias /
	/// role / quality) ships ~$329K/mo steady-state; an 8× $28,752 payroll
	/// nets ~$100K/mo profit. Mid-tier (≤ OVR 700) ROI is unaffected.
	///
	/// SalaryBias still rolls multiplicatively on top — a 0.6× great-deal
	/// elite is 60 % of the curve, a 1.45× overpriced one is 145 %. The
	/// hidden-stats interview minigame's skill check stays intact.
	const double SalaryAnchorStats    = 300.0;   // OVR at which the curve passes through anchor wage
	const double SalaryAnchorWage     = 774.0;   // monthly $ at the anchor — bumped 750→774 on 2026-05-09 so an 8× OVR-700 team lands at ~$65K/mo payroll (target hit: 774 × 1.8⁴ × 8 = $65,028)
	const double SalaryGrowthBase     = 1.8;     // wage multiplier per StatStep BELOW the knee
	const double SalaryGrowthLateBase = 1.40;    // wage multiplier per StatStep ABOVE the knee — softened 1.54→1.40 on 2026-05-09 so OVR-1000 profit grows monotonically instead of collapsing under salary at the saturated revenue ceiling
	const double SalaryGrowthKneeStats = 700.0;  // OVR at which growth slope kinks down
	const double SalaryGrowthStatStep = 100.0;   // OVR per growth-base step

	static long CalculateSalary( Random rng, EmployeeStats stats, EmployeeRole role, float salaryBias = 1f )
	{
		double variance = 0.88 + rng.NextDouble() * 0.24;            // 0.88–1.12
		double overall  = stats.Overall;

		double curve;
		if ( overall <= SalaryGrowthKneeStats )
		{
			double exponent = (overall - SalaryAnchorStats) / SalaryGrowthStatStep;
			curve = SalaryAnchorWage * Math.Pow( SalaryGrowthBase, exponent );
		}
		else
		{
			// Continuous join: evaluate the lower branch at the knee, then
			// continue with the gentler late-game growth from that anchor.
			double kneeExponent = (SalaryGrowthKneeStats - SalaryAnchorStats) / SalaryGrowthStatStep;
			double kneeWage     = SalaryAnchorWage * Math.Pow( SalaryGrowthBase, kneeExponent );
			double exponent     = (overall - SalaryGrowthKneeStats) / SalaryGrowthStatStep;
			curve = kneeWage * Math.Pow( SalaryGrowthLateBase, exponent );
		}

		double raw = curve * RoleMultiplier( role ) * variance * salaryBias;
		return Math.Max( 50L, (long)raw );
	}

	/// Roll a salary bias for a fresh applicant: ~25 % "great deal" (0.6×),
	/// ~50 % market-rate (1.0×), ~25 % "overpriced" (1.45×). The two extremes
	/// are what makes the interview minigame interesting — a great-stat
	/// candidate at 0.6× is the kind of hire the player should grab, and a
	/// mediocre candidate at 1.45× is the kind they should pass on. Most of
	/// the time the bias is neutral so neither signal dominates.
	static float RollSalaryBias( Random rng )
	{
		int roll = rng.Next( 100 );
		if ( roll < 25 ) return 0.55f + (float)rng.NextDouble() * 0.15f;  // 0.55–0.70 great deal
		if ( roll < 75 ) return 0.92f + (float)rng.NextDouble() * 0.16f;  // 0.92–1.08 normal
		return            1.30f + (float)rng.NextDouble() * 0.30f;         // 1.30–1.60 overpriced
	}

	// ── Tier weighting ────────────────────────────────────────────────────────

	/// Biases junior / mid / senior split based on job-posting quality (0 = cheapest, 1 = premium).
	/// Low quality → mostly juniors. High quality → mostly seniors.
	static int WeightedTier( Random rng, float jobQuality )
	{
		// Tier mix tuned 2026-05-02 so per-posting expected overall stat
		// lands near: Bulletin (q=0.2) → 200, Career (q=0.55) → 400,
		// Headhunter (q=0.9) → 800.
		//
		//   q=0.0 →  95 / 5  / 0   (no postings ever roll this low; safety floor)
		//   q=0.2 →  61 / 39 / 0   (Bulletin)
		//   q=0.55→  19 / 63 / 18  (Career)
		//   q=0.9 →   1 / 4  / 95  (Headhunter)
		//   q=1.0 →   0 / 5  / 95
		//
		// Curves: junior falls off as (1-q)², senior ramps as q⁴ × 200
		// (steep tail so seniors only flood the pool at premium postings).
		// The q⁴ curve keeps Career senior% modest (≈18 %) so it doesn't
		// drag Career's average above 400, while still sending Headhunter
		// to ~95 % senior.
		int juniorPct = (int)((1f - jobQuality) * (1f - jobQuality) * 95f);
		int seniorPct = Math.Min( 95, (int)(jobQuality * jobQuality * jobQuality * jobQuality * 200f) );
		int midPct    = Math.Max( 0, 100 - juniorPct - seniorPct );

		int roll = rng.Next( 100 );
		if ( roll < juniorPct )                return 0;
		if ( roll < juniorPct + midPct )       return 1;
		return 2;
	}

	// ── Factory ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Generate a randomised applicant ready to appear in the HR inbox.
	/// </summary>
	/// <param name="rng">Shared RNG (pass GameManager's instance for reproducibility).</param>
	/// <param name="jobQuality">
	///   0 = cheapest bulletin board; 1 = premium headhunter.
	///   Biases the tier distribution toward seniors and raises stat floors.
	/// </param>
	/// <param name="progressionFactor">
	///   0 = start of game (Year 2026); 1 = late game (~2031+).
	///   Raises the overall talent pool's stat ceiling as the studio's reputation grows.
	/// </param>
	/// <param name="forceTier">Pin tier for testing: 0 junior, 1 mid, 2 senior. Null = weighted random.</param>
	/// <param name="forceRole">Pin role for testing. Null = random.</param>
	public static Employee GenerateApplicant(
		Random        rng              = null,
		float         jobQuality       = 0.2f,
		float         progressionFactor = 0f,
		int?          forceTier        = null,
		EmployeeRole? forceRole        = null )
	{
		rng ??= new Random();

		var role = forceRole ?? (EmployeeRole)rng.Next( 6 );
		int tier = forceTier ?? WeightedTier( rng, jobQuality );

		var stats = EmployeeStats.Generate( rng, tier, (int)role, jobQuality, progressionFactor );

		// Pick 1 or 2 abilities at random — hard cap of 2, never duplicate.
		var pool      = Enum.GetValues<EmployeeAbility>().OrderBy( _ => rng.Next() ).ToList();
		int count     = rng.Next( 1, 3 ); // 1 or 2
		var abilities = pool.Take( count ).ToList().AsReadOnly();

		float bias = RollSalaryBias( rng );

		return new Employee
		{
			Name           = $"{FirstNames[rng.Next( FirstNames.Length )]} {LastNames[rng.Next( LastNames.Length )]}",
			Age            = rng.Next( 21, 52 ),
			Role           = role,
			Tier           = tier,
			Stats          = stats,
			Abilities      = abilities,
			Salary         = CalculateSalary( rng, stats, role, bias ),
			SalaryBias     = bias,
			AppearanceSeed = rng.Next(),
		};
	}

	/// <summary>
	/// The very first applicant the player ever sees: a flat 300-stat
	/// programmer at a fixed $750/mo salary (the anchor point of the
	/// exponential salary curve in <see cref="CalculateSalary"/>).
	/// Designed as a tutorial gimme —
	/// both revealed stats will be 300, the rest are guaranteed to also be
	/// 300, and the player CAN'T pass (Pass is hidden in the InterviewPanel
	/// and PassOnInterviewSubject refuses to discard them). Subsequent rolls
	/// use the regular random generator with full quality + price variance.
	/// </summary>
	public static Employee GenerateTutorialApplicant( Random rng = null )
	{
		rng ??= new Random();
		var role  = EmployeeRole.Programmer;
		var stats = EmployeeStats.Flat( 300 );

		// One ability; cosmetic — the tutorial applicant is meant to look
		// strong on paper without surprises.
		var pool      = Enum.GetValues<EmployeeAbility>().OrderBy( _ => rng.Next() ).ToList();
		var abilities = pool.Take( 1 ).ToList().AsReadOnly();

		return new Employee
		{
			Name           = $"{FirstNames[rng.Next( FirstNames.Length )]} {LastNames[rng.Next( LastNames.Length )]}",
			Age            = rng.Next( 24, 38 ),
			Role           = role,
			Tier           = 1, // mid — matches the 300-stat profile
			Stats          = stats,
			Abilities      = abilities,
			Salary         = 750L,      // fixed tutorial wage — also where the exponential salary curve passes through (300 OVR, see CalculateSalary)
			SalaryBias     = 1f,
			AppearanceSeed = rng.Next(),
			IsTutorialHire = true,
		};
	}

	/// <summary>
	/// Generate a special-kind applicant. Mostly a wrapper around the regular
	/// generator with the kind-specific tweaks layered on:
	///   • Tier baseline shifts up — Mentors / Marketing / Remote workers come
	///     in at mid-or-better since they're "specialists you actively sought".
	///   • Salary multiplied by <see cref="SpecialHires.Info.SalaryMultiplier"/>.
	///   • Ability count clamped up to <see cref="SpecialHires.Info.GuaranteedAbilities"/>
	///     when the kind requires at least N. Interns (which guarantee 0)
	///     instead get a max-2 ability draw on their *return* visit.
	///
	/// The intern flow has its own special path:
	///   • Fresh interns ship at junior tier (cheap, weak, short contract).
	///   • Returning interns are forced to Senior, get max abilities (2), and
	///     receive a <see cref="SalaryMultiplier"/> override below the
	///     market rate for that tier.
	/// </summary>
	public static Employee GenerateSpecialApplicant(
		Random        rng,
		EmployeeKind  kind,
		float         jobQuality        = 0.4f,
		float         progressionFactor = 0f,
		bool          isReturningIntern = false,
		EmployeeRole? forceRole         = null )
	{
		rng ??= new Random();
		var info = SpecialHires.Get( kind );

		// Tier rules per kind. Anything but Intern lands at mid+; returning
		// interns are full Senior; fresh interns are Junior.
		int tier = kind switch
		{
			EmployeeKind.Intern         => isReturningIntern ? 2 : 0,
			EmployeeKind.Mentor         => 2,    // mentors are always senior
			EmployeeKind.MarketingAgent => rng.Next( 1, 3 ), // mid/senior
			EmployeeKind.RemoteWorker   => rng.Next( 1, 3 ), // mid/senior
			_                           => WeightedTier( rng, jobQuality ),
		};

		var role  = forceRole ?? (EmployeeRole)rng.Next( 6 );
		var stats = EmployeeStats.Generate( rng, tier, (int)role, jobQuality, progressionFactor );

		// Ability draw — the GuaranteedAbilities sets a floor (1 → exactly one
		// ability minimum), 0 leaves the regular 1–2 random draw intact.
		var pool      = Enum.GetValues<EmployeeAbility>().OrderBy( _ => rng.Next() ).ToList();
		int abilityCount = isReturningIntern
			? 2                                       // returners always max out
			: Math.Max( info.GuaranteedAbilities, rng.Next( 1, 3 ) );
		var abilities = pool.Take( abilityCount ).ToList().AsReadOnly();

		// Salary: regular formula × kind multiplier × salary bias. Specials
		// still roll a bias — a "great deal" Mentor is genuinely a great
		// deal, an overpriced Marketing Agent is a trap. Returning interns
		// get an extra flat 30 % discount on top — the whole point of the
		// alumni mechanic is that a returning intern is a *bargain* senior.
		float bias = RollSalaryBias( rng );
		long salary = CalculateSalary( rng, stats, role, bias );
		salary = (long)(salary * info.SalaryMultiplier);
		if ( isReturningIntern ) salary = (long)(salary * 0.7f);
		// Floor scales with the $2.50-per-stat anchor — was $1,200 when the
		// per-point rate was higher; $500 keeps specials feeling premium
		// without breaking the early-game budget.
		salary = Math.Max( 500L, salary );

		return new Employee
		{
			Name              = $"{FirstNames[rng.Next( FirstNames.Length )]} {LastNames[rng.Next( LastNames.Length )]}",
			Age               = kind == EmployeeKind.Intern && !isReturningIntern
				? rng.Next( 18, 24 )                  // interns are young
				: rng.Next( 25, 55 ),
			Role              = role,
			Tier              = tier,
			Stats             = stats,
			Abilities         = abilities,
			Salary            = salary,
			SalaryBias        = bias,
			AppearanceSeed    = rng.Next(),
			Kind              = kind,
			IsReturningIntern = isReturningIntern,
		};
	}
}
