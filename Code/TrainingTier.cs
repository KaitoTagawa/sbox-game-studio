/// <summary>
/// One of three off-site training tiers. Each tier has its own cost,
/// duration, and reward magnitude — picked per training session by the
/// player. Group sessions multiply cost by <see cref="TrainingManager.GroupCostMultiplier"/>
/// times the number of participants.
/// </summary>
public enum TrainingTier
{
	/// On-site, immediate gain. No off-site travel, no session — the
	/// reward applies the moment the player clicks. Smaller gain per
	/// dollar than the off-site tiers but pays for it with an energy
	/// hit to the player. Founder can train on Instant.
	Instant,

	Basic,
	Standard,
	Premium,
}

/// <summary>
/// Static catalogue of training-tier metadata. Mirrors the shape of
/// <see cref="JobPosting"/> / <see cref="GameSpeed"/> so the rest of the
/// codebase can reach for the same patterns:
///
///   • <c>TrainingTiers.Get( tier )</c> — info lookup.
///   • <c>TrainingTiers.All</c>         — full ordered list.
/// </summary>
public static class TrainingTiers
{
	public sealed class Info
	{
		public TrainingTier Tier            { get; init; }
		public string       Name            { get; init; } = "";
		public string       Description     { get; init; } = "";

		/// Per-person cost for an Individual session. Group cost multiplies
		/// this by <see cref="TrainingManager.GroupCostMultiplier"/> × headcount.
		public long         IndividualCost  { get; init; }

		/// Per-person energy cost the *player* spends when scheduling a
		/// session. Even the cheap Basic tier costs a bit so spamming
		/// trainings has a real ceiling.
		public int          IndividualEnergyCost { get; init; }

		/// Length of the off-site session, in in-game days. 0 = Instant —
		/// no session is created and the reward applies immediately;
		/// the worker keeps working at their desk.
		public int          DurationDays    { get; init; }

		/// Visual: how many green ✓ marks to render on the tier card.
		/// Communicates reward magnitude at a glance.
		public int          RewardTicks     { get; init; }

		/// Sub-stat points granted to all three sub-stats of the chosen
		/// main stat when the session completes. Displayed main-stat value
		/// moves up by the same amount.
		public int          RewardStatGain  { get; init; }
	}

	public static readonly IReadOnlyList<Info> All = new Info[]
	{
		// Stat-gain values 3× the original baseline (2026-05-02). Reasoning:
		// training is *consumed* — when a hire leaves, all the XP they
		// accumulated walks out the door, unlike furniture which keeps
		// inspiring the next person to sit at that desk. Training also
		// costs both money AND energy, while items only cost money. The 3×
		// uplift makes a single training session economically meaningful
		// against the risk of the trained worker quitting.
		new()
		{
			Tier                 = TrainingTier.Instant,
			Name                 = "Instant",
			Description          = "On-site quick drill. Worker stays at their desk; small immediate gain.",
			IndividualCost       = 400,
			IndividualEnergyCost = 5,
			DurationDays         = 0,
			RewardTicks          = 1,
			RewardStatGain       = 6,
		},
		new()
		{
			Tier                 = TrainingTier.Basic,
			Name                 = "Basic",
			Description          = "A short workshop. Cheap, fast, modest gain.",
			IndividualCost       = 500,
			IndividualEnergyCost = 3,
			DurationDays         = 3,
			RewardTicks          = 1,
			RewardStatGain       = 15,
		},
		new()
		{
			Tier                 = TrainingTier.Standard,
			Name                 = "Standard",
			Description          = "Week-long focused course. Solid mid-tier choice.",
			IndividualCost       = 2_000,
			IndividualEnergyCost = 6,
			DurationDays         = 7,
			RewardTicks          = 2,
			RewardStatGain       = 30,
		},
		new()
		{
			Tier                 = TrainingTier.Premium,
			Name                 = "Premium",
			Description          = "Full conference + retreat. Expensive, slow, big gain.",
			IndividualCost       = 8_000,
			IndividualEnergyCost = 10,
			DurationDays         = 14,
			RewardTicks          = 4,
			RewardStatGain       = 60,
		},
	};

	public static Info Get( TrainingTier tier )
	{
		foreach ( var i in All )
			if ( i.Tier == tier ) return i;
		return All[0];
	}
}

/// <summary>
/// Active off-site training session attached to an <see cref="IDevWorker"/>.
/// Plain serializable class — survives save/load once persistence lands.
/// Null on the worker = available; non-null = currently away.
///
/// Research is NOT a session — it's a separate <c>ResearchTarget</c> flag on
/// the worker because research happens at the desk and never expires.
/// </summary>
public sealed class TrainingSession
{
	/// Off-site session type. Always <see cref="TrainingMode.Individual"/> or
	/// <see cref="TrainingMode.Group"/> — research never lives in a session.
	public TrainingMode Mode { get; set; }

	public TrainingTier Tier { get; set; }
	public MainStat     Stat { get; set; }

	/// Calendar position when the session started (informational; the
	/// completion check uses End*).
	public int StartDay   { get; set; }
	public int StartMonth { get; set; }
	public int StartYear  { get; set; }

	/// Calendar position the session resolves at. <see cref="TrainingManager.OnDayStart"/>
	/// applies the reward and clears the session the first day on or after this.
	public int EndDay     { get; set; }
	public int EndMonth   { get; set; }
	public int EndYear    { get; set; }

	/// Sub-stat points to grant on completion (clamped to 1–1000 by
	/// <see cref="StatBlock.GrowSub"/>).
	public int RewardSubstatGain { get; set; }

	/// Display name carried over from the consumed offer ("Tennis Class",
	/// "Tokyo Tour", etc.). Read by the HR Stats badge and the completion
	/// notification so the player sees the same flavor that lured them in.
	public string OfferName { get; set; } = "";
}

/// <summary>
/// Snapshot of one completed off-site training session — read by the
/// <c>TrainingResultPanel.razor</c> modal so the player sees concrete
/// stat movement on completion instead of just a one-line toast.
///
/// Plain serializable class; <see cref="TrainingManager._pendingResults"/>
/// holds them in a list and the panel dequeues as the player clicks
/// Continue on each.
/// </summary>
public sealed class TrainingResult
{
	public string   WorkerName { get; set; } = "";

	/// Display label of the consumed offer, e.g. "Tokyo Tour" / "Tennis Class".
	/// Falls back to the tier name when the session predates the offer system.
	public string   OfferName  { get; set; } = "";

	public MainStat Stat       { get; set; }

	/// Main-stat average just before <see cref="ApplyGain"/> ran.
	public int      StatBefore { get; set; }

	/// Main-stat average right after <see cref="ApplyGain"/>. Difference
	/// (After − Before) is the visible gain shown on the modal — note this
	/// can be smaller than the tier's `RewardStatGain` when the worker was
	/// already near the 1000 sub-stat cap.
	public int      StatAfter  { get; set; }
}

/// <summary>
/// Which training path a session belongs to. Individual / Group are both
/// off-site (worker disappears for the duration) — except Instant tiers,
/// which apply on-desk. Research is now topic-based (see <see cref="ResearchTopic"/>);
/// it never appears in a <see cref="TrainingSession"/> or in the offer queue.
/// </summary>
public enum TrainingMode
{
	Individual,
	Group,
	Research,
}

/// <summary>
/// One named training opportunity sitting in <see cref="TrainingManager"/>'s
/// rolling queue, mirroring the applicant inbox in <see cref="HRManager"/>.
/// New offers spawn on a timer; the player consumes one to start a training.
/// Research has no offers — see <see cref="TrainingVariants.RollResearchTopic"/>
/// for its flavor-name path.
///
/// Plain serializable class so the queue survives save/load alongside the
/// rest of the training state.
/// </summary>
public sealed class TrainingOffer
{
	public TrainingMode Mode { get; set; }
	public TrainingTier Tier { get; set; }
	public string       Name { get; set; } = "";

	/// Which main stat this offer trains. Decided at offer-spawn time (see
	/// <see cref="TrainingManager.RollOffer"/>) and surfaced as an icon on
	/// the offer card — the player picks the offer they want, the offer
	/// dictates the stat (rather than the player picking both).
	public MainStat     Stat { get; set; } = MainStat.Design;

	/// Lookup helper — convenience for the UI so it doesn't have to hit
	/// <see cref="TrainingTiers.Get"/> separately.
	public TrainingTiers.Info TierInfo => TrainingTiers.Get( Tier );
}
