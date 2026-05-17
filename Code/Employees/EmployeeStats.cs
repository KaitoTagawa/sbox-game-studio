// ─────────────────────────────────────────────────────────────────────────────
// StatBlock — shared base for every three-sub-stat group.
// Each subclass below gives the three slots real names; the base handles
// Average, growth, and clamping so that logic never duplicates.
// ─────────────────────────────────────────────────────────────────────────────

public class StatBlock
{
	// Internal storage — use the named properties on each subclass instead.
	protected int _a, _b, _c;

	/// Displayed main-stat value (typical range 1–1000, uncapped above):
	/// average of the three sub-stats.
	public int Average => (_a + _b + _c) / 3;

	/// Floor at 1, no upper cap. Training, project XP, and furniture boosts
	/// can push individual sub-stats above 1000 — only applicant generation
	/// (see Generate below) still enforces the 1000 ceiling on roll-time stats.
	public static int Clamp( int v ) => Math.Max( 1, v );

	/// Grow sub-stat 0, 1, or 2 by <paramref name="amount"/>, floored at 1
	/// (no upper cap — sub-stats can exceed 1000).
	public void GrowSub( int subIndex, int amount )
	{
		switch ( subIndex )
		{
			case 0: _a = Clamp( _a + amount ); break;
			case 1: _b = Clamp( _b + amount ); break;
			case 2: _c = Clamp( _c + amount ); break;
		}
	}
}

// ── Per-category stat blocks ──────────────────────────────────────────────────

/// Programming: how an employee writes, reviews, and ships code.
public sealed class ProgrammingStats : StatBlock
{
	/// Reduces bug count in shipped games.
	public int CodeQuality          { get => _a; set => _a = value; }

	/// Catches bugs during development before they ship.
	public int BugResistance        { get => _b; set => _b = value; }

	/// Raw feature-coding throughput — how fast they build things.
	public int ImplementationSpeed  { get => _c; set => _c = value; }
}

/// Design: how well an employee crafts the game's feel and systems.
public sealed class DesignStats : StatBlock
{
	/// Quality of core gameplay mechanics and systems.
	public int MechanicDesign       { get => _a; set => _a = value; }

	/// How rewarding and satisfying the game feels to players.
	public int PlayerSatisfaction   { get => _b; set => _b = value; }

	/// Responsiveness, "juice", and tactile polish.
	public int GameFeel             { get => _c; set => _c = value; }
}

/// Creativity: originality, research speed, and genre breadth.
public sealed class CreativityStats : StatBlock
{
	/// Novelty score contributed to shipped games.
	public int Innovation           { get => _a; set => _a = value; }

	/// Unlocks higher-tier genres faster during training.
	public int ResearchAptitude     { get => _b; set => _b = value; }

	/// Reduces output penalty when working on unfamiliar genres.
	public int GenreVersatility     { get => _c; set => _c = value; }
}

/// Artistry: visual quality and aesthetic direction.
public sealed class ArtistryStats : StatBlock
{
	/// Overall visual quality rating on shipped games.
	public int VisualPolish         { get => _a; set => _a = value; }

	/// Art direction coherence and style consistency.
	public int AestheticSense       { get => _b; set => _b = value; }

	/// Fidelity and detail level of individual art assets.
	public int AssetQuality         { get => _c; set => _c = value; }
}

/// Sound: audio production quality across effects, music, and mixing.
public sealed class SoundStats : StatBlock
{
	/// Quality and appropriateness of sound effects.
	public int SoundEffects         { get => _a; set => _a = value; }

	/// Original soundtrack score and composition quality.
	public int MusicComposition     { get => _b; set => _b = value; }

	/// Mixing quality, atmosphere, and overall audio immersion.
	public int SoundAesthetics      { get => _c; set => _c = value; }
}

/// Focus: stamina, research throughput, and precision.
public sealed class FocusStats : StatBlock
{
	/// How quickly this employee completes research tasks.
	public int ResearchSpeed        { get => _a; set => _a = value; }

	/// How long they sustain peak output before needing a break.
	public int WorkConsistency      { get => _b; set => _b = value; }

	/// Cross-discipline error-catching — QA-like precision.
	public int AttentionToDetail    { get => _c; set => _c = value; }
}

// ─────────────────────────────────────────────────────────────────────────────
// EmployeeStats — the full six-stat profile, FIFA-style.
//
// Each main stat (1–1000) is the average of its three named sub-stats.
// Productivity is NOT here — it is an office-level multiplier (see GameManager).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class EmployeeStats
{
	public ProgrammingStats Programming { get; set; } = new();
	public DesignStats      Design      { get; set; } = new();
	public CreativityStats  Creativity  { get; set; } = new();
	public ArtistryStats    Artistry    { get; set; } = new();
	public SoundStats       Sound       { get; set; } = new();
	public FocusStats       Focus       { get; set; } = new();

	/// Overall rating: unweighted average of all six main stats (1–1000).
	public int Overall => ( Programming.Average + Design.Average + Creativity.Average +
	                         Artistry.Average   + Sound.Average  + Focus.Average ) / 6;

	/// Bump every sub-stat across all six categories by <paramref name="amount"/>,
	/// clamped to 1–1000. Used by the tutorial's Start Up Training to give
	/// the founder a flat +10 across the board.
	public void BoostAll( int amount )
	{
		StatBlock[] blocks = { Programming, Design, Creativity, Artistry, Sound, Focus };
		foreach ( var block in blocks )
		{
			block.GrowSub( 0, amount );
			block.GrowSub( 1, amount );
			block.GrowSub( 2, amount );
		}
	}

	/// Bump every sub-stat of one main-stat block by <paramref name="amount"/>,
	/// clamped to 1–1000. The displayed Average rises by exactly
	/// <paramref name="amount"/>. Used by training rewards and project XP.
	public void BoostMainStat( MainStat stat, int amount )
	{
		StatBlock block = stat switch
		{
			MainStat.Programming => Programming,
			MainStat.Design      => Design,
			MainStat.Creativity  => Creativity,
			MainStat.Artistry    => Artistry,
			MainStat.Sound       => Sound,
			MainStat.Focus       => Focus,
			_                    => null,
		};
		if ( block is null ) return;
		block.GrowSub( 0, amount );
		block.GrowSub( 1, amount );
		block.GrowSub( 2, amount );
	}

	// ── Generation ───────────────────────────────────────────────────────────

	/// <summary>
	/// Build a stat block with every sub-stat set to the same value. Used for
	/// the studio founder's day-one starter sheet (flat 30 across the board)
	/// and for deterministic test fixtures. Value is clamped 1–1000.
	/// </summary>
	public static EmployeeStats Flat( int value )
	{
		int v = StatBlock.Clamp( value );
		return new EmployeeStats
		{
			Programming = new ProgrammingStats {
				CodeQuality = v, BugResistance = v, ImplementationSpeed = v,
			},
			Design = new DesignStats {
				MechanicDesign = v, PlayerSatisfaction = v, GameFeel = v,
			},
			Creativity = new CreativityStats {
				Innovation = v, ResearchAptitude = v, GenreVersatility = v,
			},
			Artistry = new ArtistryStats {
				VisualPolish = v, AestheticSense = v, AssetQuality = v,
			},
			Sound = new SoundStats {
				SoundEffects = v, MusicComposition = v, SoundAesthetics = v,
			},
			Focus = new FocusStats {
				ResearchSpeed = v, WorkConsistency = v, AttentionToDetail = v,
			},
		};
	}

	/// <summary>
	/// Generate a randomised stat block for a new applicant.
	/// </summary>
	/// <param name="rng">Shared RNG instance.</param>
	/// <param name="tier">0 = junior, 1 = mid, 2 = senior.</param>
	/// <param name="primaryStatIndex">
	///   Which of the six stats (0 = Programming … 5 = Focus) gets the boosted
	///   range. Pass <c>(int)employee.Role</c> — the enum values are aligned.
	/// </param>
	/// <param name="jobQuality">0–1. Better postings raise the stat floor (filter out weak candidates).</param>
	/// <param name="progressionFactor">0–1. Later game raises both floor and ceiling (studio reputation).</param>
	public static EmployeeStats Generate( Random rng, int tier, int primaryStatIndex,
	                                       float jobQuality = 0f, float progressionFactor = 0f )
	{
		// Tier base ranges retuned 2026-05-02 (Bulletin 200 / Career 400 /
		// Headhunter 800), then senior secondary lifted twice 2026-05-08 —
		// first (600, 950) → (700, 1000) for reachability, then → (750, 1000)
		// so a Year 2026 Headhunter applicant has ~30 % chance of OVR ≥ 900.
		// Hi is hard-capped at 1000 by ApplicantClamp; raising lo is the only
		// way to push elite-tier mean upward. Junior is intentionally weak
		// (overall ~108 at q=0); senior is the "ringer" tier.
		(int lo, int hi) primaryBase = tier switch
		{
			0 => (50,  250),    // junior:  primary avg 150
			1 => (300, 600),    // mid:     primary avg 450
			2 => (700, 1000),   // senior:  primary avg 850
			_ => (50,  250),
		};
		(int lo, int hi) secondaryBase = tier switch
		{
			0 => (40,  160),    // junior:  secondary avg 100
			1 => (180, 460),    // mid:     secondary avg 320
			2 => (750, 1000),   // senior:  secondary avg 875 — elite-tier knob; do not drop below 720 without recalc
			_ => (40,  160),
		};

		// jobQuality raises the floor  — better posting = fewer weak applicants slip through.
		// progressionFactor lifts both — late-game studio reputation attracts stronger talent.
		// Applicant rolls stay capped at 1000 even though grown stats (training,
		// project XP, furniture) can push past — fresh hires never spawn above the cap.
		static int ApplicantClamp( int v ) => Math.Clamp( v, 1, 1000 );
		(int lo, int hi) primary = (
			ApplicantClamp( primaryBase.lo + (int)(jobQuality * 60) + (int)(progressionFactor * 80) ),
			ApplicantClamp( primaryBase.hi + (int)(jobQuality * 30) + (int)(progressionFactor * 60) )
		);
		(int lo, int hi) secondary = (
			ApplicantClamp( secondaryBase.lo + (int)(jobQuality * 40) + (int)(progressionFactor * 50) ),
			ApplicantClamp( secondaryBase.hi + (int)(jobQuality * 20) + (int)(progressionFactor * 40) )
		);

		int Sub( int statIndex )
		{
			var r  = statIndex == primaryStatIndex ? primary : secondary;
			int lo = r.lo;
			int hi = Math.Max( lo + 1, r.hi ); // guarantee a valid rng range
			return ApplicantClamp( rng.Next( lo, hi + 1 ) );
		}

		return new EmployeeStats
		{
			Programming = new ProgrammingStats
			{
				CodeQuality         = Sub( 0 ),
				BugResistance       = Sub( 0 ),
				ImplementationSpeed = Sub( 0 ),
			},
			Design = new DesignStats
			{
				MechanicDesign      = Sub( 1 ),
				PlayerSatisfaction  = Sub( 1 ),
				GameFeel            = Sub( 1 ),
			},
			Creativity = new CreativityStats
			{
				Innovation          = Sub( 2 ),
				ResearchAptitude    = Sub( 2 ),
				GenreVersatility    = Sub( 2 ),
			},
			Artistry = new ArtistryStats
			{
				VisualPolish        = Sub( 3 ),
				AestheticSense      = Sub( 3 ),
				AssetQuality        = Sub( 3 ),
			},
			Sound = new SoundStats
			{
				SoundEffects        = Sub( 4 ),
				MusicComposition    = Sub( 4 ),
				SoundAesthetics     = Sub( 4 ),
			},
			Focus = new FocusStats
			{
				ResearchSpeed       = Sub( 5 ),
				WorkConsistency     = Sub( 5 ),
				AttentionToDetail   = Sub( 5 ),
			},
		};
	}
}
