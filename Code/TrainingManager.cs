/// <summary>
/// Studio-wide training pipeline. Replaces the original "instant +12" model
/// (2026-04-29 v1) with a tier+duration+research system (v2):
///
///   • <see cref="TrainIndividual"/> — pick one tier, one stat, one worker.
///     Worker goes off-site for the tier's duration; reward applies on
///     completion. Founder cannot go off-site.
///   • <see cref="TrainGroup"/>      — pick one tier + one stat. Every
///     eligible hire (not the founder) goes off-site simultaneously.
///     Total cost = `tier × <see cref="GroupCostMultiplier"/> × headcount`.
///   • <see cref="ToggleResearch"/>  — flip a per-worker toggle. While set,
///     the worker stays at their desk but contributes to projects at a
///     reduced rate, gaining one sub-stat point every ~ResearchAvgIntervalDays
///     days. Always-on slow drip.
///
/// Off-site sessions tick on <see cref="GameManager.OnDayStart"/>; research
/// drips also tick there (paused while off-site). Per-frame work is limited
/// to a visibility toggle — hiding the GameObject of any off-site NPC.
/// </summary>
public sealed class TrainingManager : Component
{
	public static TrainingManager Instance { get; private set; }

	// ── Modal state (mirrors Shop / Settings / Gallery) ──────────────────────

	public bool IsOpen { get; private set; }

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			GameMenu.Instance?.SetOpen( false );
			Shop.Instance?.SetOpen( false );
			Settings.Instance?.SetOpen( false );
			Gallery.Instance?.SetOpen( false );
			GameProjectManager.Instance?.SetOpen( false );
		}
		GameManager.RefreshPlayerLock();
	}

	// ── Tunables ─────────────────────────────────────────────────────────────

	[Property, Description( "Group cost = tier x this x headcount. 1.5 = 50% premium per head over Individual." )]
	public float GroupCostMultiplier { get; set; } = 1.5f;

	[Property, Description( "Researching workers contribute at this fraction of their normal pillar contribution. 0.8 = 20% productivity tax." )]
	public float ResearchProductivityFactor { get; set; } = 0.8f;

	[Property, Description( "Base energy cost to start a research topic. Final cost = base x max(1, target genre RevenueMultiplier) - better genres cost more to research." )]
	public int BaseResearchStartEnergy { get; set; } = 3;

	[Property, Description( "Maximum research topics queued in the discovery inbox at once. Player won't see more than this until they consume some by assigning workers / completing them." )]
	public int MaxResearchInboxSize { get; set; } = 8;

	// ── Research discovery inbox ─────────────────────────────────────────────
	// Topics aren't all visible at the start — exactly one rolls into this
	// list on each in-game month boundary (and one at game start to seed),
	// drawn from the pool of currently-`Available()` topics whose target
	// genre isn't already unlocked. Topics stay in the inbox while a worker
	// researches them; CompleteResearch removes them when the genre lands.

	readonly List<string> _researchInbox = new();
	public IReadOnlyList<string> ResearchInbox => _researchInbox;

	// ── Offer queue (mirrors HRManager's applicant pump) ────────────────────

	[Property, Description( "Real seconds between rolls of a new training offer. 15s ~ 1.5 in-game days at 10s/day - halved again from 30s so offers post twice as often (4x cadence vs the original 60s)." )]
	public float SecondsBetweenOffers { get; set; } = 15f;

	[Property, Description( "Maximum offers in the inbox at once. New rolls are skipped while the queue is full." )]
	public int MaxOffersInQueue { get; set; } = 6;

	[Property, Description( "Per-tier roll weights - Instant / Basic / Standard / Premium. Higher = more frequent." )]
	public int WeightInstant  { get; set; } = 40;
	[Property] public int WeightBasic    { get; set; } = 30;
	[Property] public int WeightStandard { get; set; } = 20;
	[Property] public int WeightPremium  { get; set; } = 10;

	readonly List<TrainingOffer> _offers = new();
	public IReadOnlyList<TrainingOffer> Offers => _offers;

	float _offerTimer;

	// ── Internal RNG (used for offer roll variants) ──────────────────────────

	readonly System.Random _rng = new();

	// ── Lifecycle ────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;
		GameManager.OnDayStart += OnDayStart;
		GameManager.OnMonthStart += OnMonthStartTick;

		// Seed one topic immediately so the player has something visible the
		// moment the Research tab opens. Subsequent topics roll in monthly.
		TryRollResearchTopic();
	}

	protected override void OnDestroy()
	{
		GameManager.OnDayStart -= OnDayStart;
		GameManager.OnMonthStart -= OnMonthStartTick;
		if ( Instance == this ) Instance = null;
	}

	/// One new research topic per in-game month — slow drip, mirrors the
	/// applicant pump but at calendar-tick cadence. Skipped silently when
	/// the inbox is full or no eligible candidates remain.
	void OnMonthStartTick()
	{
		TryRollResearchTopic();
	}

	void TryRollResearchTopic()
	{
		if ( _researchInbox.Count >= MaxResearchInboxSize ) return;

		var candidates = new List<ResearchTopic>();
		foreach ( var t in ResearchTopics.Listable() )
		{
			if ( _researchInbox.Contains( t.Id ) )                         continue;
			// Already being worked on (in-progress) → still in inbox if it
			// was rolled. If it's NOT in inbox yet (edge: assigned via cheat),
			// don't double-discover.
			if ( WorkerOnTopic( t.Id ) is not null )                        continue;
			candidates.Add( t );
		}

		if ( candidates.Count == 0 ) return;

		var pick = candidates[_rng.Next( candidates.Count )];
		_researchInbox.Add( pick.Id );

		string genreLabel = pick.UnlocksGenre is { } g
			? GameGenres.Get( g )?.Name ?? g.ToString()
			: "research";
		Notifications.Push( "New research available!",
			$"{pick.Name} → unlocks {genreLabel}",
			"info", duration: 8f );
	}

	IDevWorker WorkerOnTopic( string topicId )
	{
		foreach ( var w in AllWorkers() )
			if ( w.ResearchTopicId == topicId ) return w;
		return null;
	}

	protected override void OnUpdate()
	{
		// Visibility / off-site choreography is fully owned by EmployeeNPC —
		// see WalkToSpawnAndVanish + FinishReturnFromTraining. We used to
		// toggle GameObject.Enabled here, but disabling the whole GameObject
		// wiped the SkinnedModelRenderer's animation graph state on re-enable
		// (returning hires would slide and never sit). Hiding just the
		// renderer in EmployeeNPC keeps the graph warm.

		// Offer pump — same shape as HRManager.TickApplicantTimer. Roll a new
		// offer onto the inbox every SecondsBetweenOffers (real-time, not
		// in-game-day-tied so the cadence doesn't compound with calendar speed).
		// Gated behind the player's first extra-desk purchase so training
		// doesn't compete with the early-game shop progression — see
		// `UnlockTrainingOffers` for the burst-release on desk-buy.
		if ( !TrainingUnlocked ) return;

		_offerTimer += Time.Delta;
		if ( _offerTimer < SecondsBetweenOffers ) return;
		_offerTimer = 0f;

		if ( _offers.Count >= MaxOffersInQueue ) return;

		var offer = RollOffer();
		_offers.Add( offer );

		Notifications.Push( "New training offer",
			$"{offer.Name} ({offer.TierInfo.Name} {offer.Mode})",
			"info", duration: 5f );
	}

	/// True once the player has bought their first extra desk past the
	/// starter — gates the training offer pump and the burst-release.
	/// Self-derived from inventory so no separate save flag is needed.
	bool TrainingUnlocked => (InventoryManager.Instance?.OwnedCount( ItemKind.Desk ) ?? 1) > 1;

	/// Burst-release N training offers onto the inbox immediately. Called
	/// from InventoryManager when the player buys their first extra desk —
	/// the early-game pacing means they may have waited a long while for
	/// any training, so we kick-start with a small batch to make the
	/// system's significance immediately legible.
	public void ReleaseInitialOffers( int count = 3 )
	{
		int slots = MaxOffersInQueue - _offers.Count;
		int toAdd = System.Math.Min( count, slots );
		for ( int i = 0; i < toAdd; i++ )
		{
			var offer = RollOffer();
			_offers.Add( offer );
		}
		if ( toAdd > 0 )
		{
			Notifications.Push( "Training unlocked",
				$"{toAdd} new training offer{(toAdd == 1 ? "" : "s")} just landed in the inbox.",
				"info", duration: 10f );
		}
	}

	TrainingOffer RollOffer()
	{
		// Group training is deferred to a future release — only Individual
		// offers cycle through the queue today. The Group code paths
		// (`TrainGroup`, `GroupCostMultiplier`, the Group variants in
		// `TrainingVariants`, the GroupCard styling) all stay in place,
		// dormant, so re-enabling is a one-line flip back to a 50/50 mode roll.
		var mode = TrainingMode.Individual;
		var tier = RollTier();
		var pool = TrainingVariants.For( mode, tier );
		var name = pool.Count == 0 ? "Unknown Training" : pool[_rng.Next( pool.Count )];

		// Each offer carries its own MainStat so the player decides by
		// picking an offer rather than picking offer + stat separately.
		// Picked uniformly at random across the six main stats.
		var stat = (MainStat)_rng.Next( 6 );

		return new TrainingOffer { Mode = mode, Tier = tier, Name = name, Stat = stat };
	}

	TrainingTier RollTier()
	{
		int wI = Math.Max( 0, WeightInstant  );
		int wB = Math.Max( 0, WeightBasic    );
		int wS = Math.Max( 0, WeightStandard );
		int wP = Math.Max( 0, WeightPremium  );
		int total = wI + wB + wS + wP;
		if ( total <= 0 ) return TrainingTier.Basic;

		int roll = _rng.Next( total );
		if ( (roll -= wI) < 0 ) return TrainingTier.Instant;
		if ( (roll -= wB) < 0 ) return TrainingTier.Basic;
		if ( (roll -= wS) < 0 ) return TrainingTier.Standard;
		return TrainingTier.Premium;
	}

	/// Player-facing — drop an offer from the queue without consuming it.
	/// Called by the panel's "Reject" / × on a training-offer card. New
	/// offers can re-fill the slot on the next pump tick.
	public void RejectOffer( TrainingOffer offer )
	{
		if ( offer is null ) return;
		_offers.Remove( offer );
	}

	// ── Public API: Individual ───────────────────────────────────────────────

	public bool TrainIndividual( IDevWorker worker, TrainingOffer offer, MainStat stat )
	{
		if ( worker is null ) return false;
		if ( offer  is null ) return false;
		if ( offer.Mode != TrainingMode.Individual ) return false;
		if ( !_offers.Contains( offer ) ) return false;

		var  info     = offer.TierInfo;
		bool isInstant = info.DurationDays <= 0;

		// Founder can train Instant (stays at desk) but never goes off-site.
		if ( worker.IsPlayer && !isInstant )
		{
			Notifications.Push( "Founder unavailable",
				"Founder can't go off-site. Use Instant or Research.",
				"warning" );
			return false;
		}

		// Block any training (off-site OR Instant) on a worker who's already
		// in an active session. Instant used to slip past this check on the
		// theory that the worker is "at their desk" — but if ActiveTraining
		// is set they're literally off-site, so an Instant on top would
		// double-dip XP onto someone the player can't see.
		if ( worker.ActiveTraining is not null )
		{
			Notifications.Push( "Already training",
				$"{worker.Name} is already off-site — wait for them to return.",
				"warning" );
			return false;
		}

		if ( !TrySpend( info.IndividualCost, info.IndividualEnergyCost, worker.Name ) ) return false;

		// Consume the offer the moment the spend clears. Even if some later
		// path bails, the offer's gone — same way HR's interview flow
		// burns the applicant on start.
		_offers.Remove( offer );

		if ( isInstant )
		{
			int before = Average( worker.Stats, stat );
			ApplyGain( worker.Stats, stat, info.RewardStatGain );
			int after  = Average( worker.Stats, stat );

			Notifications.Push( "Trained",
				$"{worker.Name} +{after - before} {stat} ({offer.Name})",
				"success", duration: 5f );

			// Same result-modal path as off-site completions — players want
			// to see the before/after numbers regardless of duration.
			_pendingResults.Add( new TrainingResult
			{
				WorkerName = worker.Name,
				OfferName  = offer.Name,
				Stat       = stat,
				StatBefore = before,
				StatAfter  = after,
			} );
			GameManager.RefreshPlayerLock();   // free cursor instantly so Continue is clickable
			return true;
		}

		worker.ActiveTraining = BuildSession( TrainingMode.Individual, offer.Tier, stat, info, offer.Name );
		Notifications.Push( "Training started",
			$"{worker.Name} → {offer.Name} ({info.DurationDays}d)",
			"info", duration: 6f );
		return true;
	}

	// ── Public API: Group ────────────────────────────────────────────────────

	public bool TrainGroup( TrainingOffer offer, MainStat stat )
	{
		var hr = HRManager.Instance;
		if ( hr is null )                                return false;
		if ( offer is null )                              return false;
		if ( offer.Mode != TrainingMode.Group )           return false;
		if ( !_offers.Contains( offer ) )                 return false;

		var  info     = offer.TierInfo;
		bool isInstant = info.DurationDays <= 0;

		// Skip anyone already in an active training session — applies to
		// both off-site and Instant Group runs. Letting Instant double-dip
		// onto an off-site worker stacks XP onto someone the player can't
		// see and was the source of the overlap exploit.
		var eligible = new List<EmployeeNPC>();
		foreach ( var npc in hr.Staff )
		{
			if ( npc.ActiveTraining is not null ) continue;
			eligible.Add( npc );
		}

		if ( eligible.Count == 0 )
		{
			Notifications.Push( "No one to train",
				"Every employee is busy or off-site already.",
				"warning" );
			return false;
		}

		long  totalCost   = (long)MathF.Round( info.IndividualCost       * GroupCostMultiplier * eligible.Count );
		int   totalEnergy = Math.Max( 1, (int)MathF.Round( info.IndividualEnergyCost * GroupCostMultiplier * eligible.Count ) );

		if ( !TrySpend( totalCost, totalEnergy, "the studio" ) ) return false;

		_offers.Remove( offer );

		if ( isInstant )
		{
			foreach ( var npc in eligible )
				ApplyGain( npc.Stats, stat, info.RewardStatGain );

			Notifications.Push( "Group training",
				$"{eligible.Count} staff +{info.RewardStatGain} {stat} ({offer.Name})",
				"success", duration: 6f );
			return true;
		}

		foreach ( var npc in eligible )
			npc.ActiveTraining = BuildSession( TrainingMode.Group, offer.Tier, stat, info, offer.Name );

		Notifications.Push( "Group training",
			$"{eligible.Count} sent to {offer.Name} ({info.DurationDays}d) for ${totalCost:N0}",
			"info", duration: 8f );
		return true;
	}

	// ── Public API: Research ─────────────────────────────────────────────────

	/// Energy the player spends to start `topic`. Scales with the unlocked
	/// genre's RevenueMultiplier — better genres cost more research effort.
	public int ResearchEnergyCostFor( ResearchTopic topic )
	{
		float mult = 1f;
		if ( topic?.UnlocksGenre is { } g && GameGenres.Get( g ) is { } info )
			mult = MathF.Max( 1f, info.RevenueMultiplier );
		return Math.Max( 1, (int)MathF.Round( BaseResearchStartEnergy * mult ) );
	}

	/// Total in-game days a topic takes for the given worker. Stat scales
	/// 1/x with a min of 20 (so a flat-30 founder still finishes eventually).
	public static float ResearchTotalDays( ResearchTopic topic, IDevWorker w )
	{
		if ( topic is null || w?.Stats is null ) return 0f;
		int   stat      = Average( w.Stats, topic.PrimaryStat );
		float effective = MathF.Max( 20f, stat );
		return topic.BaseDays * (100f / effective);
	}

	/// Assign a worker to a topic. One topic per worker at a time. Founder
	/// is eligible (research is on-desk, no off-site travel).
	public bool StartResearch( IDevWorker worker, string topicId )
	{
		if ( worker is null ) return false;

		var topic = ResearchTopics.Get( topicId );
		if ( topic is null )
		{
			Notifications.Push( "Research failed",
				$"Unknown topic: {topicId}", "warning" );
			return false;
		}

		if ( worker.ActiveTraining is not null )
		{
			Notifications.Push( "Off-site",
				$"{worker.Name} is away — can't start research now.",
				"warning" );
			return false;
		}

		if ( !string.IsNullOrEmpty( worker.ResearchTopicId ) )
		{
			Notifications.Push( "Already researching",
				$"{worker.Name} is busy with another topic — cancel that first.",
				"warning" );
			return false;
		}

		if ( !topic.Available() )
		{
			Notifications.Push( "Locked",
				$"{topic.Name} isn't available yet.", "warning" );
			return false;
		}

		int energy = ResearchEnergyCostFor( topic );
		var gm     = GameManager.Instance;
		if ( gm is null || gm.Energy < energy )
		{
			Notifications.Push( "Not enough energy",
				$"Need {energy} ⚡ to start {topic.Name}.", "warning" );
			return false;
		}
		gm.TrySpendEnergy( energy );

		worker.ResearchTopicId         = topic.Id;
		worker.DaysIntoCurrentResearch = 0f;

		Notifications.Push( "Research started",
			$"{worker.Name} → {topic.Name}", "info", duration: 6f );
		return true;
	}

	/// Cancel the worker's research without applying any partial reward —
	/// the days are sunk. Energy isn't refunded either.
	public void CancelResearch( IDevWorker worker )
	{
		if ( worker is null ) return;
		if ( string.IsNullOrEmpty( worker.ResearchTopicId ) ) return;

		Notifications.Push( "Research cancelled",
			$"{worker.Name} stopped researching.", "info" );

		worker.ResearchTopicId         = null;
		worker.DaysIntoCurrentResearch = 0f;
	}

	// ── Daily tick (sessions + research) ─────────────────────────────────────

	void OnDayStart()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return;

		// Off-site session completion first — clearing ActiveTraining lets
		// the same-day research tick below resume for that worker.
		//
		// Defensive guard: a session with all-zero EndDay/EndMonth/EndYear
		// is treated as invalid (it would otherwise satisfy IsOnOrAfter for
		// every real in-game date and auto-complete on day 1). This protects
		// against scene-serialised default sessions slipping through —
		// happened once with PlayerStats, see the 2026-04-30 fix.
		foreach ( var w in AllWorkers() )
		{
			if ( w.ActiveTraining is not { } s ) continue;
			if ( s.EndYear == 0 && s.EndMonth == 0 && s.EndDay == 0 )
			{
				w.ActiveTraining = null;
				continue;
			}
			if ( IsOnOrAfter( gm, s.EndDay, s.EndMonth, s.EndYear ) )
				CompleteSession( w, s );
		}

		// Research progress tick. One day per OnDayStart per worker, paused
		// while off-site. When elapsed days hit the topic total, the topic
		// completes and the genre unlocks.
		foreach ( var w in AllWorkers() )
		{
			if ( string.IsNullOrEmpty( w.ResearchTopicId ) ) continue;
			if ( w.ActiveTraining is not null )              continue;

			var topic = ResearchTopics.Get( w.ResearchTopicId );
			if ( topic is null )
			{
				// Stale topic id (catalogue entry removed?). Clear and bail.
				w.ResearchTopicId         = null;
				w.DaysIntoCurrentResearch = 0f;
				continue;
			}

			w.DaysIntoCurrentResearch += 1f;

			if ( w.DaysIntoCurrentResearch >= ResearchTotalDays( topic, w ) )
				CompleteResearch( w, topic );
		}
	}

	void CompleteResearch( IDevWorker worker, ResearchTopic topic )
	{
		if ( topic.UnlocksGenre is { } g )
			Achievements.RecordGenreResearched( g );

		string genreLabel = topic.UnlocksGenre is { } gen
			? $" — {GameGenres.Get( gen )?.Name ?? gen.ToString()} unlocked"
			: "";

		Notifications.Push( "Research complete!",
			$"{worker.Name} finished {topic.Name}{genreLabel}",
			"success", duration: 10f );

		worker.ResearchTopicId         = null;
		worker.DaysIntoCurrentResearch = 0f;

		// Topic exits the inbox — it's done. New topics will roll in via
		// OnMonthStart to refill the slot.
		_researchInbox.Remove( topic.Id );
	}

	void CompleteSession( IDevWorker worker, TrainingSession s )
	{
		// Capture before/after main-stat averages so the result modal can
		// show the "before → after, +N" line. Sub-stats are clamped at
		// 1000 by GrowSub so the actual main-average gain may be smaller
		// than RewardSubstatGain when the worker was already near cap.
		int before = Average( worker.Stats, s.Stat );
		ApplyGain( worker.Stats, s.Stat, s.RewardSubstatGain );
		int after  = Average( worker.Stats, s.Stat );

		worker.ActiveTraining = null;

		string label = string.IsNullOrEmpty( s.OfferName ) ? s.Tier.ToString() : s.OfferName;

		Notifications.Push( "Training complete",
			$"{worker.Name} returned from {label}: +{after - before} {s.Stat}",
			"success", duration: 8f );

		// Queue for the dedicated result modal (TrainingResultPanel.razor).
		_pendingResults.Add( new TrainingResult
		{
			WorkerName = worker.Name,
			OfferName  = label,
			Stat       = s.Stat,
			StatBefore = before,
			StatAfter  = after,
		} );

		// Off-site session completion fires from OnUpdate, not user input —
		// the player could be deep in any modal when this lands. Force-close
		// the conflicting ones so the result modal sits on a clean screen.
		// Publish modal (GameProjectManager.IsOpen) is deliberately untouched:
		// TrainingResultPanel.HasResult defers to it and reveals once ship is
		// dealt with.
		GameMenu.Instance?.SetOpen( false );
		Shop.Instance?.SetOpen( false );
		Gallery.Instance?.SetOpen( false );
		Settings.Instance?.SetOpen( false );

		GameManager.RefreshPlayerLock();   // free cursor instantly so Continue is clickable
	}

	// ── Training-completion result queue ─────────────────────────────────────
	// `TrainingResultPanel.razor` reads from this list and dequeues entries as
	// the player clicks Continue on each. Lets the player see exactly which
	// stats moved and by how much instead of just a one-line toast.

	readonly List<TrainingResult> _pendingResults = new();
	public IReadOnlyList<TrainingResult> PendingResults => _pendingResults;

	public void DequeuePendingResult()
	{
		if ( _pendingResults.Count == 0 ) return;
		_pendingResults.RemoveAt( 0 );
		// When the last result clears, re-evaluate the lock so the cursor
		// hides again if no other modal is still open.
		GameManager.RefreshPlayerLock();
	}

	// ── Public read-side helpers ─────────────────────────────────────────────

	/// Per-frame contribution multiplier the production tick should apply for
	/// this worker. 0 if off-site, 0.8× if researching a topic, 1.0× otherwise.
	public static float ProductivityFactor( IDevWorker worker )
	{
		if ( worker is null )                          return 1f;
		if ( worker.ActiveTraining is not null )       return 0f;
		if ( string.IsNullOrEmpty( worker.ResearchTopicId ) ) return 1f;
		return Instance?.ResearchProductivityFactor ?? 0.8f;
	}

	/// Look up the displayed (1–1000) value of a main stat. Used by the
	/// training panel to preview "current → projected" before committing.
	public static int Average( EmployeeStats stats, MainStat stat )
	{
		var block = BlockFor( stats, stat );
		return block?.Average ?? 0;
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// Combined money + energy spend. Both must clear or nothing is charged.
	/// Notification is specific to whichever resource came up short, so the
	/// player knows exactly which gate they hit.
	bool TrySpend( long money, int energy, string subject )
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return false;

		if ( gm.Energy < energy )
		{
			Notifications.Push( "Not enough energy",
				$"Need {energy} ⚡ to train {subject}.",
				"warning" );
			return false;
		}
		if ( gm.Money < money )
		{
			Notifications.Push( "Training failed",
				$"Need ${money:N0} to train {subject}.",
				"warning" );
			return false;
		}

		// Both clear — charge each in turn. The per-resource Try* on
		// GameManager re-checks the threshold (defensive against another
		// system spending in between), but at this point both must succeed.
		if ( energy > 0 ) gm.TrySpendEnergy( energy );
		if ( money  > 0 ) gm.TrySpend( money );
		return true;
	}

	TrainingSession BuildSession( TrainingMode mode, TrainingTier tier, MainStat stat, TrainingTiers.Info info, string offerName )
	{
		var gm = GameManager.Instance;
		int sd  = gm?.Day   ?? 1;
		int sm  = gm?.Month ?? 1;
		int sy  = gm?.Year  ?? 2026;
		var (ed, em, ey) = AddDays( sd, sm, sy, info.DurationDays );
		return new TrainingSession
		{
			Mode              = mode,
			Tier              = tier,
			Stat              = stat,
			StartDay          = sd, StartMonth = sm, StartYear = sy,
			EndDay            = ed, EndMonth   = em, EndYear   = ey,
			RewardSubstatGain = info.RewardStatGain,
			OfferName         = offerName ?? "",
		};
	}

	static (int day, int month, int year) AddDays( int day, int month, int year, int days )
	{
		day += days;
		while ( day > 30 )  { day -= 30; month++; }
		while ( month > 12 ) { month -= 12; year++; }
		return (day, month, year);
	}

	static bool IsOnOrAfter( GameManager gm, int day, int month, int year )
	{
		if ( gm.Year  > year  ) return true;
		if ( gm.Year  < year  ) return false;
		if ( gm.Month > month ) return true;
		if ( gm.Month < month ) return false;
		return gm.Day >= day;
	}

	static IEnumerable<IDevWorker> AllWorkers()
	{
		if ( PlayerStats.Instance is { } me ) yield return me;
		var hr = HRManager.Instance;
		if ( hr is null ) yield break;
		foreach ( var npc in hr.Staff ) yield return npc;
	}

	static void ApplyGain( EmployeeStats stats, MainStat stat, int amount )
	{
		if ( stats is null ) return;
		var block = BlockFor( stats, stat );
		if ( block is null ) return;
		block.GrowSub( 0, amount );
		block.GrowSub( 1, amount );
		block.GrowSub( 2, amount );
	}

	static StatBlock BlockFor( EmployeeStats stats, MainStat stat ) => stat switch
	{
		MainStat.Programming => stats.Programming,
		MainStat.Design      => stats.Design,
		MainStat.Creativity  => stats.Creativity,
		MainStat.Artistry    => stats.Artistry,
		MainStat.Sound       => stats.Sound,
		MainStat.Focus       => stats.Focus,
		_                    => null,
	};

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────
	// Per-worker active training state is persisted on the worker (PlayerStats
	// for the founder, EmployeeNPC for hires) — only the studio-wide queues
	// (research-discovery inbox + offer queue) live here.

	public TrainingSave Save() => new()
	{
		ResearchInbox = new List<string>( _researchInbox ),
		Offers        = new List<TrainingOffer>( _offers ),
		OfferTimer    = _offerTimer,
	};

	public void Load( TrainingSave dto )
	{
		_researchInbox.Clear();
		_offers.Clear();

		if ( dto is null )
		{
			_offerTimer = 0f;
			return;
		}

		foreach ( var id    in dto.ResearchInbox ) _researchInbox.Add( id );
		foreach ( var offer in dto.Offers        ) _offers.Add( offer );
		_offerTimer = dto.OfferTimer;
	}
}

/// MainStat enum + display helpers — left here (where the rest of the
/// training data model lives) so the UI and TrainingManager pull them from
/// one place. EmployeeStats itself doesn't need an enum since each main
/// stat is exposed as a named property.
public enum MainStat
{
	Programming,
	Design,
	Creativity,
	Artistry,
	Sound,
	Focus,
}

public static class MainStatExtensions
{
	public static string DisplayName( this MainStat s ) => s switch
	{
		MainStat.Programming => "Programming",
		MainStat.Design      => "Design",
		MainStat.Creativity  => "Creativity",
		MainStat.Artistry    => "Artistry",
		MainStat.Sound       => "Sound",
		MainStat.Focus       => "Focus",
		_                    => s.ToString(),
	};

	public static string Icon( this MainStat s ) => s switch
	{
		MainStat.Programming => "💻",
		MainStat.Design      => "🎮",
		MainStat.Creativity  => "💡",
		MainStat.Artistry    => "🎨",
		MainStat.Sound       => "🎵",
		MainStat.Focus       => "🎯",
		_                    => "•",
	};

	/// Read the displayed (1–1000) average for one main stat off an
	/// <see cref="EmployeeStats"/> bundle. Mirrors the private
	/// <c>BlockFor</c> in <see cref="TrainingManager"/> but is the
	/// public-facing entry point — UI consumers (InterviewPanel,
	/// TrainingPanel) use this so they don't need their own switch.
	public static int AverageOf( this MainStat s, EmployeeStats stats ) => s switch
	{
		MainStat.Programming => stats.Programming.Average,
		MainStat.Design      => stats.Design.Average,
		MainStat.Creativity  => stats.Creativity.Average,
		MainStat.Artistry    => stats.Artistry.Average,
		MainStat.Sound       => stats.Sound.Average,
		MainStat.Focus       => stats.Focus.Average,
		_                    => 0,
	};
}
