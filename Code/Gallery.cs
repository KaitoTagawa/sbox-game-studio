/// <summary>
/// Persistent record of everything the studio has ever shown off:
///   • <see cref="ShippedGames"/>     — every game that's reached "Finished".
///   • <see cref="Trophies"/>         — annual Game-of-the-Year awards across
///                                      multiple divisions.
///   • Unlocks (achievements, genres, hires, speed, postings, abilities) are
///     pulled live from their respective catalogs by the panel — no need to
///     mirror that state here.
///
/// Behaves like <see cref="Shop"/> / <see cref="Settings"/> — singleton
/// component, modal popup, mutually exclusive with the other in-game modals.
/// </summary>
public sealed class Gallery : Component
{
	public static Gallery Instance { get; private set; }

	// ── Open state ──────────────────────────────────────────────────────────

	public bool IsOpen { get; private set; }

	/// Top-level section the player is currently viewing.
	public GallerySection ActiveSection { get; set; } = GallerySection.Unlocks;

	/// Filter inside the Unlocks section.
	public UnlocksFilter ActiveUnlocksFilter { get; set; } = UnlocksFilter.All;

	// ── Records ─────────────────────────────────────────────────────────────

	readonly List<ShippedGame> _games    = new();
	readonly List<Trophy>      _trophies = new();

	public IReadOnlyList<ShippedGame> ShippedGames => _games;
	public IReadOnlyList<Trophy>      Trophies     => _trophies;

	/// Which past-game card currently has its player-review tile expanded.
	/// Null = nothing expanded. Only one card can be open at a time so the
	/// scroll position stays predictable. Reference equality by game ref —
	/// fine because the panel reads from <see cref="_games"/> directly.
	public ShippedGame ExpandedReviewGame { get; private set; }

	/// Toggle the player-review tile open/closed for the given past-game
	/// card. Clicking the same card again closes it; clicking a different
	/// card switches to that one. Player reviews are available the moment
	/// a game ships — only the aggregate critic score is gated behind the
	/// 2-week <see cref="ReviewDelayDays"/> window.
	public void ToggleReviews( ShippedGame game )
	{
		if ( game is null ) return;
		ExpandedReviewGame = ReferenceEquals( ExpandedReviewGame, game ) ? null : game;
	}

	/// Fires once per <see cref="RecordShippedGame"/> call — i.e. every
	/// time a project finishes production and lands in the gallery.
	/// Used by <see cref="GameSaveManager"/> to autosave at the moment
	/// of release (a high-stakes event the player would not want to
	/// repeat). Static so subscribers don't have to live in the scene
	/// alongside Gallery.
	public static event Action OnGameShipped;

	// ── Review schedule + sales window ──────────────────────────────────────
	// Reviews don't land at ship time — they trickle in <see cref="ReviewDelayMonths"/>
	// in-game months later. The same OnMonthStart tick that releases reviews
	// also drives the post-review sales window: <see cref="ReleaseWindowMonths"/>
	// monthly slices distributed on a bell curve mimicking algorithmic discovery
	// (slow burn → peak → decline).

	[Property, Description( "How many in-game days after ship a review score lands. 14 days = 2 in-game weeks." )]
	public int ReviewDelayDays { get; set; } = 14;

	[Property, Description( "How many in-game months a shipped game keeps generating sales after reviews drop." )]
	public int ReleaseWindowMonths { get; set; } = 6;

	// ── Lifetime metric tunables ────────────────────────────────────────────
	// Calibrated against real Steam-indie data:
	//   • D7 retention 3-8 % (Solsten); D30 ~15 % for the average indie
	//   • Median session 14 min overall; diamond-tier 65 min (HowToMarketAGame)
	//   • Most players: 1-5 lifetime sessions. Hit games: engaged fans skew the
	//     average up to 20-30, which is also our hard ceiling for a 6-month tail
	//     at "≤ 1 session/day for a really committed fan".
	// Pillar / review modifiers multiply UP from these floors, so a maxed game
	// ships orders of magnitude more players × longer sessions × more returns.

	[Property, Description( "First-game baseline lifetime player count (multiplier 1.0x)." )]
	public int BasePlayers { get; set; } = 100;

	[Property, Description( "Zero-pillar baseline of average sessions per player. Multipliers scale this up; final value is hard-capped at MaxSessionsPerPlayer." )]
	public float BaseSessions { get; set; } = 2f;

	[Property, Description( "Hard ceiling on average sessions per player over the 6-month tail. <= 1 session/day for a fully engaged fan -> 30 is the upper bound the average can realistically reach for a hit game." )]
	public float MaxSessionsPerPlayer { get; set; } = 30f;

	[Property, Description( "First-game baseline average session length, in minutes. Calibrated to the 14-min median play time benchmark for Steam indie demos." )]
	public float BaseMinutes { get; set; } = 10f;

	[Property, Description( "Revenue per minute of total play time. $0.018/min — 0.016 → 0.018 on 2026-05-11 for a clean uniform +12.5% revenue lift. Prior step: 0.02 → 0.016 on 2026-05-09 (uniform −20%, paired with salary +3% lift and the buff stack that lets mid-tier teams hit revenue saturation). Pre-calibration history: 0.10 → 0.05 → 0.02 → 0.016 → 0.018." )]
	public float PricePerMinute { get; set; } = 0.018f;

	// Cumulative release-curve, precomputed once at startup. Index 0..100
	// covers fraction-of-window in 1% increments; values are the cumulative
	// share of TotalSales paid out by that point. cum[0] = 0, cum[100] = 1.
	//
	// Shape: linear ramp from 0 → peak over the first PeakFractionOfWindow,
	// then exponential decay with time constant DecayFractionOfWindow.
	// Tuned for a 6-month window (180 in-game days) so:
	//   • Peak rate hits at ~day 7 (week-1 launch spike)
	//   • ~10% of TotalSales delivered by week 1 (the launch surge)
	//   • ~60% by month 1, ~85% by month 2, ~95% by month 3
	//   • ~100% by month 6
	// Replaces the prior Gaussian (μ = 0.5, σ = 0.20) which peaked in
	// months 3–4 and made early sales feel anaemic.
	const float PeakFractionOfWindow  = 7f / 180f;     // ≈ 0.039
	const float DecayFractionOfWindow = 30f / 180f;    // ≈ 0.167 — exp τ in window-fraction units

	static readonly float[] CumulativeBell = BuildCumulativeRampDecay( steps: 100,
		peakFrac: PeakFractionOfWindow,
		decayTauFrac: DecayFractionOfWindow );

	static float[] BuildCumulativeRampDecay( int steps, float peakFrac, float decayTauFrac )
	{
		// Build the per-bucket PDF (linear ramp then exponential decay), then
		// normalize and accumulate. Final array has cum[0]=0 and cum[steps]=1
		// guaranteed so callers can scan it without edge-case branching.
		var pdf       = new float[steps + 1];
		float pdfSum  = 0f;
		for ( int i = 0; i <= steps; i++ )
		{
			float t = i / (float)steps;
			float p;
			if ( t <= peakFrac )
			{
				// Ramp up: 0 at t=0, 1 at t=peakFrac.
				p = peakFrac > 0f ? t / peakFrac : 1f;
			}
			else
			{
				// Decay from peak with exponential time constant tau.
				float dt = t - peakFrac;
				p = MathF.Exp( -dt / decayTauFrac );
			}
			pdf[i]  = p;
			pdfSum += p;
		}

		var cum   = new float[steps + 1];
		float run = 0f;
		for ( int i = 0; i <= steps; i++ )
		{
			run    += pdf[i] / pdfSum;
			cum[i]  = run;
		}
		cum[0]     = 0f;
		cum[steps] = 1f;
		return cum;
	}

	/// Lookup the cumulative-paid-out fraction at `t ∈ [0, 1]` window progress.
	/// Linearly interpolates between the 1%-resolution buckets.
	static float CumulativeFraction( float t )
	{
		if ( t <= 0f ) return 0f;
		if ( t >= 1f ) return 1f;
		float idx = t * (CumulativeBell.Length - 1);
		int   i   = (int)idx;
		if ( i >= CumulativeBell.Length - 1 ) return 1f;
		float frac = idx - i;
		return CumulativeBell[i] + (CumulativeBell[i + 1] - CumulativeBell[i]) * frac;
	}

	/// Real-time seconds for the full release window at 1× game speed.
	/// 6 months × 30 days × <see cref="GameManager.SecondsPerDay"/>.
	float ReleaseWindowSeconds =>
		ReleaseWindowMonths * 30f * GameManager.SecondsPerDay;

	// ── Lifecycle ───────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;
		// Review reveal lives on OnDayStart so the 14-day delay is exact
		// (and so that calendar fast-forwards like Smoke Break — which
		// fire OnDayStart per advanced day — release reviews mid-month
		// instead of waiting for the next monthly tick).
		GameManager.OnDayStart += CheckReviewReveals;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		GameManager.OnDayStart -= CheckReviewReveals;
	}

	float _tipReinforceAccum;
	const float TipReinforceInterval = 2f;

	protected override void OnUpdate()
	{
		// Self-heal the "check your game" sticky toast: as long as we have
		// at least one shipped game and the player hasn't yet opened the
		// Gallery, keep a tip pinned in the corner. Re-pushing every 2s
		// covers session restarts, hot-reload, and Notifications.Clear.
		_tipReinforceAccum += Time.Delta;
		if ( _tipReinforceAccum >= TipReinforceInterval )
		{
			_tipReinforceAccum = 0f;
			if ( !EverOpened && _games.Count > 0 && !Notifications.HasTag( GalleryTipTag ) )
			{
				Notifications.Push( "How's your game doing?",
					"Press TAB → open Gallery to see revenue and reviews.",
					"info", duration: 0f, tag: GalleryTipTag );
			}
		}

		// Continuous sales accrual — runs every frame for any game in its
		// release window. `OnMonthStart` still handles the discrete review
		// release; everything money-related is now smooth.
		if ( _games.Count == 0 ) return;
		// Tutorial freeze: keep the in-flight sales tail paused while the
		// first-launch flow is up. (Tutorial finishes before anything ships
		// in practice, but defensive against save-loaded edge cases.)
		if ( TutorialManager.Instance is { IsBlockingTime: true } ) return;

		// Modal / Escape-menu pause: no sales accrual while the player is
		// in a configuration UI or the engine is paused.
		if ( GameManager.Instance is { IsTimePaused: true } )       return;

		float dt = Time.Delta * (GameManager.Instance?.TimeMultiplier ?? 1f);
		if ( dt <= 0f )                return;
		if ( ReleaseWindowSeconds <= 0f ) return;

		float fractionStep = dt / ReleaseWindowSeconds;

		foreach ( var game in _games )
		{
			if ( game.ReleaseActive )
				AccrueContinuousSales( game, fractionStep );
		}
	}

	/// Fires every in-game day. Walks the shipped-game list and reveals any
	/// game whose <see cref="ReviewDelayDays"/> threshold has elapsed since
	/// ship — including reveals triggered mid-month by Smoke Break or any
	/// other calendar fast-forward path.
	void CheckReviewReveals()
	{
		if ( GameManager.Instance is not { } gm ) return;

		foreach ( var game in _games )
		{
			if ( !game.ReviewReleased && IsReviewDue( game, gm ) )
				RevealReview( game );
		}
	}

	/// True once <see cref="ReviewDelayDays"/> in-game days have elapsed
	/// since the game shipped. Uses a flat day count instead of month
	/// arithmetic so the delay is exactly N days regardless of which day
	/// of the month the game launched on.
	bool IsReviewDue( ShippedGame game, GameManager gm )
	{
		int shipAbs = AbsoluteDay( game.ShipYear, game.ShipMonth, game.ShipDay );
		int nowAbs  = AbsoluteDay( gm.Year, gm.Month, gm.Day );
		return nowAbs - shipAbs >= ReviewDelayDays;
	}

	/// Year/Month/Day → strictly increasing day index. Uses the calendar's
	/// 30-day months × 12-month years so 1 in-game year = 360 days.
	static int AbsoluteDay( int year, int month, int day ) =>
		year * 360 + (month - 1) * 30 + day;

	/// Reveal the (already-computed) review score to the player. Reviews are
	/// purely an indicator of game quality — they don't drive sales. The
	/// score was set the moment the game shipped (see <see cref="ComputeScoreAndMetrics"/>);
	/// this just flips <see cref="ShippedGame.ReviewReleased"/> and pushes
	/// the toast so the player sees the public reveal a couple in-game
	/// months after launch.
	void RevealReview( ShippedGame game )
	{
		game.ReviewReleased = true;

		Notifications.Push( "Reviews are in!",
			$"\"{game.Title}\" — {game.ReviewScore}/100",
			"info", duration: 8f );
	}

	/// Compute the eventual review score AND the lifetime metrics (player
	/// count, sessions, session minutes, total sales). Called from
	/// <see cref="RecordShippedGame"/> at ship time so the public can play
	/// the game immediately — the review score is set but kept hidden behind
	/// <see cref="ShippedGame.ReviewReleased"/> until the 2-month review
	/// window elapses. Reviews don't drive sales mathematically; the metrics
	/// formulas use the score as a quality multiplier, but since the score
	/// is a deterministic function of pillar points anyway, computing it at
	/// ship vs. at reveal yields the same numbers — it's a UX delay, not a
	/// gameplay one.
	void ComputeScoreAndMetrics( ShippedGame game )
	{
		// ── Review score ────────────────────────────────────────────────────
		// Non-linear curve: score = 100 × sqrt(avgPillar / 1000). Easier to
		// climb to mid-tier (avg 250 → 50), hard to crack 100 (still need
		// avg 1000 across all three pillars). Concave so reviewers reward
		// "competent across the board" but only a near-perfect game gets a
		// near-perfect score.
		float avg        = (game.DesignPoints + game.SoundPoints + game.GraphicsPoints) / 3f;
		float normalized = MathF.Max( 0f, avg / 1000f );
		int   baseScore  = (int)MathF.Round( MathF.Sqrt( normalized ) * 100f );

		int sacrificed = 0;
		if ( game.DesignPoints   == 0 ) sacrificed++;
		if ( game.SoundPoints    == 0 ) sacrificed++;
		if ( game.GraphicsPoints == 0 ) sacrificed++;

		game.ReviewScore    = Math.Clamp( baseScore - sacrificed * 15, 0, 100 );
		// game.ReviewReleased stays false — kept hidden until OnMonthStart's
		// reveal logic fires the public notification.

		// ── Lifetime metrics ────────────────────────────────────────────────
		// Each metric weights one pillar at 50% and the other two at 25% each.
		// Sound dominates session length, Design dominates session count
		// (replayability), Graphics dominates raw player count (visual pull).

		float D = game.DesignPoints   / 1000f;
		float S = game.SoundPoints    / 1000f;
		float G = game.GraphicsPoints / 1000f;
		float R = game.ReviewScore    / 100f;

		float playerWeight  = 0.50f * G + 0.25f * D + 0.25f * S;
		float sessionWeight = 0.50f * D + 0.25f * G + 0.25f * S;
		float minuteWeight  = 0.50f * S + 0.25f * G + 0.25f * D;

		// Pillar multipliers anchor at 1.0 (so the BasePlayers / BaseSessions /
		// BaseMinutes literally describe a zero-pillar baseline) and scale up
		// to 4× when a pillar weight maxes at 1.0. Coefficient was previously 5
		// (1..6×) — flattened to keep great games from snowballing too hard
		// when all three mults compound through players × sessions × minutes.
		float playerMult  = 1f + 3f * playerWeight;
		float sessionMult = 1f + 3f * sessionWeight;
		float minuteMult  = 1f + 3f * minuteWeight;

		// Review modifiers: PlayerCount gets a concave (sqrt) curve so great
		// reviews still meaningfully outsell mid ones, but a 100-score game
		// doesn't end the run. Was previously (5R)² → 1..26; sqrt → 1..10.
		// SessionsPerPlayer + AvgSessionMinutes get a gentler linear bump
		// (good games are slightly stickier per player, not dramatically more).
		float revPlayerBoost = 1f + 9f * MathF.Sqrt( R );   // 1..10
		float revQualityMod  = 1f + 0.5f * R;              // 1..1.5

		float genreRev  = GameGenres.TotalRevenue( game.Genres );
		int   gmYear    = GameManager.Instance?.Year ?? 2026;
		float yearScale = 1f + (gmYear - 2026) * 0.20f;

		// Quest fulfillment (ADR-0003 Phase 3, stacked 2026-05-10):
		// every open fan-letter quest whose genre matches one of this
		// game's tags fulfills together. Each match adds +20% to
		// lifetimePlayers (max +60% since a game carries up to 3
		// genres and the dedup rule guarantees 1 quest per genre).
		// Multi-quest ships also schedule a press follow-up letter
		// 14 in-game days out — see Letterbox.TryFulfillQuests.
		int   fulfilledCount = Letterbox.Instance?.TryFulfillQuests( game.Genres, game.Title ) ?? 0;
		float questBonus     = 1f + 0.20f * fulfilledCount;

		long  lifetimePlayers   = (long)MathF.Round( BasePlayers * playerMult * revPlayerBoost * genreRev * yearScale * questBonus );
		// Average sessions per player — multiplier-scaled then capped at
		// MaxSessionsPerPlayer. The ceiling represents "≤ 1 session/day for
		// a fully engaged fan over a 6-month tail" — even the best games can't
		// realistically average more than ~30 sessions per buyer, since the
		// engaged-fan tail averages with the long flat tail of "bought it,
		// played once".
		float sessionsPerPlayer = MathF.Min( MaxSessionsPerPlayer,
			BaseSessions * sessionMult * revQualityMod );
		float avgSessionMinutes = BaseMinutes * minuteMult * revQualityMod;
		long  lifetimeSessions  = (long)Math.Round( (double)lifetimePlayers * sessionsPerPlayer );

		// ── Targets vs. progressive counts ──────────────────────────────────
		// Lifetime* are the FINAL values the game will reach by end of window.
		// PlayerCount / TotalSessions / RevenueEarned ramp from 0 → target via
		// CumulativeFraction in AccrueContinuousSales, so all three displays
		// move in lockstep on the gallery card. AvgSessionMinutes is a true
		// average — it doesn't ramp, it's just the per-session length.

		game.LifetimePlayers   = lifetimePlayers;
		game.LifetimeSessions  = lifetimeSessions;
		game.SessionsPerPlayer = sessionsPerPlayer;
		game.AvgSessionMinutes = avgSessionMinutes;

		game.PlayerCount   = 0;
		game.TotalSessions = 0;

		// Lifetime sales target — the bell curve in AccrueContinuousSales
		// pays this out smoothly across the release window.
		double totalPlayMinutes = (double)lifetimePlayers * sessionsPerPlayer * avgSessionMinutes;
		game.TotalSales = (long)Math.Round( totalPlayMinutes * PricePerMinute );
	}

	/// Per-frame metrics tick. Walks the cumulative bell curve and ramps
	/// PlayerCount, TotalSessions, and RevenueEarned together toward their
	/// LifetimePlayers / LifetimeSessions / TotalSales targets. No floating-
	/// point drift — each progressive value is recomputed from the curve
	/// every frame, so all three land exactly on their targets when the
	/// window closes.
	void AccrueContinuousSales( ShippedGame game, float fractionStep )
	{
		game.WindowElapsedFraction = MathF.Min( 1f, game.WindowElapsedFraction + fractionStep );

		float frac = CumulativeFraction( game.WindowElapsedFraction );

		// Money — pay only the delta this frame; AddMoney is the side-effect.
		// Game sales are the ONLY money source that counts toward Earn-N
		// achievements (per user direction — refunds, achievement bonuses,
		// etc. shouldn't inflate lifetime-earnings tracking), so the
		// RecordMoneyEarned call lives here at the sales path explicitly.
		long expectedRevenue = (long)Math.Round( game.TotalSales * (double)frac );
		long delta           = expectedRevenue - game.RevenueEarned;
		if ( delta > 0 )
		{
			GameManager.Instance?.AddMoney( delta );
			Achievements.RecordMoneyEarned( delta );
			game.RevenueEarned = expectedRevenue;
		}

		// Players + sessions — pure display ramp, no side-effects. Recompute
		// from the curve every frame so we never drift out of sync with money.
		game.PlayerCount   = (long)Math.Round( game.LifetimePlayers  * (double)frac );
		game.TotalSessions = (long)Math.Round( game.LifetimeSessions * (double)frac );

		// Wrap when the curve hits 1.0 — guaranteed because we clamped above.
		if ( game.WindowElapsedFraction >= 1f )
		{
			// Pay out any sub-cent rounding remainder so the books balance.
			// Counts toward Earn-N achievements (it's the last slice of the
			// sales tail), same as the per-frame delta above.
			long remainder = game.TotalSales - game.RevenueEarned;
			if ( remainder > 0 )
			{
				GameManager.Instance?.AddMoney( remainder );
				Achievements.RecordMoneyEarned( remainder );
				game.RevenueEarned = game.TotalSales;
			}

			// Snap displays to their targets so the final card reads exactly
			// the lifetime numbers (no rounding fuzz from the per-frame curve).
			game.PlayerCount   = game.LifetimePlayers;
			game.TotalSessions = game.LifetimeSessions;
			game.ReleaseActive = false;

			Notifications.Push( "Release wrapped",
				$"\"{game.Title}\" total earnings: ${game.RevenueEarned:N0}",
				"success", duration: 10f );
		}
	}

	// ── Open / close ────────────────────────────────────────────────────────

	/// True once the player has opened the Gallery in this run. Drives the
	/// post-first-ship "check your game" tip — sticky toast that nags the
	/// player until they actually come look. Persisted via GalleryShipsSave.
	[Property] public bool EverOpened { get; set; }

	/// Tag for the sticky tip toast so we can clear it the moment the
	/// player opens the Gallery for the first time.
	const string GalleryTipTag = "gallery-tip";

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			Modals.CloseAllExcept( this );

			// First-ever open: jump straight to the Past Games tab so the
			// player sees their freshly-shipped game (which is why the
			// "check your game" tip pointed them here in the first place),
			// clear the standing tip, and never re-arm it.
			if ( !EverOpened )
			{
				EverOpened    = true;
				ActiveSection = GallerySection.PastGames;
				Notifications.RemoveByTag( GalleryTipTag );
			}
		}
		GameManager.RefreshPlayerLock();
	}

	public void SetSection( GallerySection s )      => ActiveSection       = s;
	public void SetUnlocksFilter( UnlocksFilter f ) => ActiveUnlocksFilter = f;

	// ── Recording APIs ──────────────────────────────────────────────────────
	// Step 5d will call <see cref="RecordShippedGame"/> when a project hits
	// Finished and the player ships it. The annual award ceremony (future)
	// will call <see cref="AwardTrophy"/> with the winning game's title.

	public void RecordShippedGame( ShippedGame game )
	{
		if ( game is null ) return;

		// Compute the (deterministic) review score and lifetime metrics now,
		// then open the sales window immediately. Reviews land later as a
		// pure UI reveal — they don't recompute anything. See §2026-04-29
		// rework: "the game should be available to the public before the
		// review; review is just an indicator for how good the game was."
		ComputeScoreAndMetrics( game );
		game.ReleaseActive         = true;
		game.WindowElapsedFraction = 0f;

		// Stamp the per-game medal based on the highest pillar score. The
		// cash bonus + notification + achievement record fire here so the
		// player sees the reward exactly at ship time.
		//
		// One-shot per tier per run: if this tier has ALREADY been awarded
		// in the current run, downgrade the stamp to None so the game card
		// doesn't display a duplicate trophy. The first Bronze ship keeps
		// its Bronze badge; subsequent Bronze-qualifying ships render with
		// no medal at all. Same rule for Silver and Gold. Counters reset
		// on new-game / restart so each fresh run can claim each tier once.
		var tentativeMedal = GameMedalExtensions.MedalFor(
			game.DesignPoints, game.SoundPoints, game.GraphicsPoints );
		game.Medal = IsMedalTierAlreadyAwardedThisRun( tentativeMedal )
			? GameMedal.None
			: tentativeMedal;
		AwardMedal( game );

		_games.Add( game );

		// Drive the Ship*Games achievements (which in turn unlock the higher
		// JobPosting tiers — Career Site behind 3 ships, etc.).
		Achievements.RecordShippedGame();

		// Submit the four ship-time leaderboard stats (ADR-0002). Wrapped
		// internally in try/catch so a platform failure can't break ship.
		Leaderboards.SubmitOnShip( game );

		OnGameShipped?.Invoke();
	}

	public void AwardTrophy( Trophy trophy )
	{
		if ( trophy is null ) return;
		_trophies.Add( trophy );
	}

	/// Pay out the medal cash bonus + push the celebratory notification +
	/// hand off to <see cref="Achievements"/> so the FirstBronzeMedal /
	/// FirstSilverMedal / FirstGoldMedal entries unlock on first sight of
	/// each tier. No-op for <see cref="GameMedal.None"/> (game didn't hit
	/// even the Bronze threshold).
	///
	/// One-shot per tier per run: the FIRST Bronze ship in a run fires the
	/// full ceremony (cash + popup + bonus drop); subsequent Bronze ships
	/// in the same run silent-return. Same rule for Silver and Gold. Each
	/// shipped game's medal is still recorded against the game itself
	/// (via the caller's <see cref="ShippedGame.Medal"/>), so the Trophies
	/// tab keeps showing every medaled ship — only the per-tier celebration
	/// is one-shot. Counters reset on new-game / restart.
	void AwardMedal( ShippedGame game )
	{
		if ( game is null )                     return;
		if ( game.Medal == GameMedal.None )     return;
		if ( IsMedalTierAlreadyAwardedThisRun( game.Medal ) ) return;

		long cash = game.Medal.CashReward();
		long before = GameManager.Instance?.Money ?? -1;
		GameManager.Instance?.AddMoney( cash );
		long after  = GameManager.Instance?.Money ?? -1;
		// DEBUG: pin down the "medal cash didn't arrive" mystery. Drop
		// once confirmed working.
		Log.Info( $"[Medal] {game.Medal} on \"{game.Title}\" → AddMoney({cash}); wallet {before} → {after}" );

		// Tier-specific bonus inventory drops. Bronze ships a small
		// consolation pack of Chewing Gum (the budget Good-mood booster)
		// so a fresh studio can prime a few buffs without dipping into
		// cash. Silver / Gold reserved for richer drops once balance
		// shakes out.
		string bonusBlurb = "";
		if ( game.Medal == GameMedal.Bronze )
		{
			InventoryManager.Instance?.GrantConsumable( ItemKind.ChewingGum, 3 );
			bonusBlurb = " · +3 Chewing Gum";
		}

		Notifications.Push(
			$"{game.Medal.Icon()} {game.Medal.DisplayName()} awarded",
			$"\"{game.Title}\" earned a {game.Medal.DisplayName().ToLower()} — +${cash:N0}{bonusBlurb}.",
			"success", duration: 10f );

		Achievements.RecordMedalAwarded( game.Medal );
	}

	/// True if a Bronze / Silver / Gold ceremony has already fired in the
	/// current run — used by <see cref="AwardMedal"/> to keep each tier's
	/// celebration a one-shot. Reads the Achievements counters directly
	/// (they're bumped inside AwardMedal itself, so 1+ means "we already
	/// fired the full block once this run"). Resets on
	/// <see cref="Achievements.ResetForNewRun"/>.
	static bool IsMedalTierAlreadyAwardedThisRun( GameMedal m ) => m switch
	{
		GameMedal.Bronze => Achievements.BronzeMedalsAwarded >= 1,
		GameMedal.Silver => Achievements.SilverMedalsAwarded >= 1,
		GameMedal.Gold   => Achievements.GoldMedalsAwarded   >= 1,
		_                => false,
	};

	/// Counts of medals awarded across this run, by tier. Drives the
	/// Trophies tab summary header. Recomputed on the fly from the live
	/// shipped-games list so save/load round-trips for free.
	public int MedalCount( GameMedal tier )
	{
		int n = 0;
		foreach ( var g in _games )
			if ( g.Medal == tier ) n++;
		return n;
	}

	/// Wipe shipped games + trophies (new save / debug). Named
	/// ResetProgress so it doesn't shadow <c>Component.Reset()</c>, which
	/// the engine calls on its own schedule.
	public void ResetProgress()
	{
		_games.Clear();
		_trophies.Clear();

		// Re-arm the "check your game" sticky tip for the new run — clear
		// the flag so OnUpdate self-heal will push it again after the
		// player ships their first game in this run, and drop any stale
		// tip that was already on screen from the previous playthrough.
		EverOpened = false;
		Notifications.RemoveByTag( GalleryTipTag );
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public GalleryShipsSave Save() => new()
	{
		ShippedGames = new List<ShippedGame>( _games ),
		Trophies     = new List<Trophy>( _trophies ),
		EverOpened   = EverOpened,
	};

	public void Load( GalleryShipsSave dto )
	{
		_games.Clear();
		_trophies.Clear();
		EverOpened = false;
		if ( dto is null ) return;
		foreach ( var g in dto.ShippedGames )
		{
			MigrateLegacyShippedGame( g );
			_games.Add( g );
		}
		foreach ( var t in dto.Trophies ) _trophies.Add( t );
		EverOpened = dto.EverOpened;
	}

	/// Pre-rework saves wrote PlayerCount / TotalSessions as final lifetime
	/// values directly (no LifetimePlayers field). Detect that shape and
	/// backfill: treat the existing counts as both the target AND the final
	/// state, and mark the release inactive so AccrueContinuousSales doesn't
	/// rewind them to 0 next frame.
	static void MigrateLegacyShippedGame( ShippedGame g )
	{
		if ( g is null ) return;
		if ( g.LifetimePlayers > 0 ) return;          // already migrated
		if ( g.PlayerCount    <= 0 ) return;          // never had metrics

		g.LifetimePlayers  = g.PlayerCount;
		g.LifetimeSessions = g.TotalSessions;
		g.ReleaseActive    = false;
	}
}

// ── Section enums ──────────────────────────────────────────────────────────
// Defined alongside Gallery so the panel and any future ceremony system can
// reference them without hunting through other files.

/// Top-level page inside the Gallery popup.
public enum GallerySection
{
	Unlocks   = 0,
	PastGames = 1,
	Trophies  = 2,
}

/// Filter applied inside the Unlocks section. Each value maps to one source
/// catalog (achievements, genres, special hires, speed tiers, job postings,
/// hire-trait abilities). <see cref="All"/> shows everything together.
public enum UnlocksFilter
{
	All          = 0,
	Achievements = 1,
	Genres       = 2,
	Hires        = 3,
	Speed        = 4,
	Postings     = 5,
	Abilities    = 6,
}

// ── Records ────────────────────────────────────────────────────────────────

/// <summary>
/// One released game. Recorded by the Step 5d ship flow; read by the Past
/// Games view in the Gallery popup, plus future systems (review aggregator,
/// award ceremony, marketing comparisons).
/// </summary>
public sealed record ShippedGame
{
	public string                Title          { get; init; } = "";
	public IReadOnlyList<GameGenre> Genres      { get; init; } = System.Array.Empty<GameGenre>();

	/// Calendar position when the game shipped. ShipDay defaults to 1 so
	/// legacy saves that pre-date day-precision ship stamps still load and
	/// resolve to a sensible review date (off by ≤ 30 days at worst).
	public int                   ShipDay        { get; init; } = 1;
	public int                   ShipMonth      { get; init; }
	public int                   ShipYear       { get; init; }

	/// Pillar points captured at ship time (1–1000 each).
	public int                   DesignPoints   { get; init; }
	public int                   SoundPoints    { get; init; }
	public int                   GraphicsPoints { get; init; }

	/// Aggregate review score 0–100. Stays at 0 while <see cref="ReviewReleased"/>
	/// is false (the game has shipped but critics haven't weighed in yet —
	/// reviews land <see cref="Gallery.ReviewDelayMonths"/> in-game months
	/// after ship). Settable so the Gallery's monthly tick can backfill it.
	public int                   ReviewScore    { get; set; }

	/// True once the review window has elapsed and a score has been written.
	/// The Gallery card / past-games view can branch on this to show
	/// "Reviews pending" until it flips.
	public bool                  ReviewReleased { get; set; }

	/// Lifetime metric TARGETS — written once at ship time by
	/// <see cref="Gallery.ComputeScoreAndMetrics"/>. The visible
	/// <see cref="PlayerCount"/> / <see cref="TotalSessions"/> /
	/// <see cref="RevenueEarned"/> ramp up from 0 toward these targets
	/// over the 6-month release window.
	public long                  LifetimePlayers     { get; set; }
	public long                  LifetimeSessions    { get; set; }

	/// Lifetime per-player averages — true averages, not progressive. Used
	/// for the "AVG SESSION" gallery cell and for downstream systems that
	/// want the original disaggregation.
	public float                 SessionsPerPlayer   { get; set; }
	public float                 AvgSessionMinutes   { get; set; }

	/// Progressive player count — ramps 0 → <see cref="LifetimePlayers"/>
	/// over the release window in lockstep with <see cref="RevenueEarned"/>.
	public long                  PlayerCount         { get; set; }

	/// Progressive session count — ramps 0 → <see cref="LifetimeSessions"/>
	/// over the release window in lockstep with <see cref="RevenueEarned"/>.
	public long                  TotalSessions       { get; set; }

	/// Total lifetime sales target — what the game is *expected* to earn over
	/// the full release window. The actual money paid out so far lives on
	/// <see cref="RevenueEarned"/>.
	public long                  TotalSales          { get; set; }

	/// Running total of the revenue actually paid out so far. Continuously
	/// updated by <see cref="Gallery.AccrueContinuousSales"/> as the window
	/// elapses; lands exactly on <see cref="TotalSales"/> when the window
	/// closes.
	public long                  RevenueEarned       { get; set; }

	/// True between review release and the end of the sales window. While
	/// active, <see cref="Gallery.AccrueContinuousSales"/> ticks the player
	/// money every frame following the cumulative bell curve.
	public bool                  ReleaseActive       { get; set; }

	/// Window progress in [0, 1]. 0 at review release, 1 at window close.
	/// Drives the cumulative-bell lookup that determines how much of
	/// <see cref="TotalSales"/> has been paid out so far.
	public float                 WindowElapsedFraction { get; set; }

	public string ShipDateLabel
	{
		get
		{
			string[] m =
			{
				"Jan", "Feb", "Mar", "Apr", "May", "Jun",
				"Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
			};
			int idx = System.Math.Clamp( ShipMonth - 1, 0, 11 );
			return $"{m[idx]} Y{ShipYear}";
		}
	}

	/// Medal awarded at ship time, based on the highest of
	/// <see cref="DesignPoints"/> / <see cref="SoundPoints"/> /
	/// <see cref="GraphicsPoints"/>. Defaults to <see cref="GameMedal.None"/>
	/// for legacy ships that pre-date the medal system; live ships set this
	/// in <see cref="Gallery.RecordShippedGame"/>. Set (not init) so save
	/// deserialisation can write it directly per ADR-0001.
	public GameMedal             Medal               { get; set; } = GameMedal.None;
}

/// Per-game medal tier awarded at ship time. Highest pillar score crosses
/// a threshold → the matching tier wins (no stacking — Gold supersedes
/// Silver/Bronze for the same game).
///   Bronze: any pillar ≥ 200,  $500 cash bonus
///   Silver: any pillar ≥ 500,  $2,000 cash bonus
///   Gold:   any pillar ≥ 1000, $10,000 cash bonus
public enum GameMedal
{
	None   = 0,
	Bronze = 1,
	Silver = 2,
	Gold   = 3,
}

public static class GameMedalExtensions
{
	/// Pillar score required to qualify for each tier. Indices line up
	/// with <see cref="GameMedal"/> values; index 0 (None) is unused.
	public const int BronzeThreshold = 200;
	public const int SilverThreshold = 500;
	public const int GoldThreshold   = 1000;

	/// Cash bonus paid into <see cref="GameManager.Money"/> on award.
	public const long BronzeReward = 500;
	public const long SilverReward = 2_000;
	public const long GoldReward   = 10_000;

	/// Highest tier whose threshold is crossed by any of the three pillars.
	/// Returns <see cref="GameMedal.None"/> when no pillar ≥ Bronze.
	public static GameMedal MedalFor( int designPoints, int soundPoints, int graphicsPoints )
	{
		int max = System.Math.Max( designPoints, System.Math.Max( soundPoints, graphicsPoints ) );
		if ( max >= GoldThreshold )   return GameMedal.Gold;
		if ( max >= SilverThreshold ) return GameMedal.Silver;
		if ( max >= BronzeThreshold ) return GameMedal.Bronze;
		return GameMedal.None;
	}

	public static long CashReward( this GameMedal m ) => m switch
	{
		GameMedal.Bronze => BronzeReward,
		GameMedal.Silver => SilverReward,
		GameMedal.Gold   => GoldReward,
		_                => 0,
	};

	public static string DisplayName( this GameMedal m ) => m switch
	{
		GameMedal.Bronze => "Bronze Medal",
		GameMedal.Silver => "Silver Medal",
		GameMedal.Gold   => "Gold Medal",
		_                => "",
	};

	public static string Icon( this GameMedal m ) => m switch
	{
		GameMedal.Bronze => "🥉",
		GameMedal.Silver => "🥈",
		GameMedal.Gold   => "🥇",
		_                => "",
	};

	/// CSS modifier so cards / badges can pick the right accent colour.
	public static string BadgeClass( this GameMedal m ) => m switch
	{
		GameMedal.Bronze => "medal-bronze",
		GameMedal.Silver => "medal-silver",
		GameMedal.Gold   => "medal-gold",
		_                => "",
	};
}

/// <summary>
/// One Game-of-the-Year-style award. The annual ceremony (future feature)
/// emits one trophy per division per year. Trophies persist forever — the
/// Gallery's Trophies tab is a permanent shrine of past wins.
/// </summary>
public sealed record Trophy
{
	public TrophyDivision Division   { get; init; }
	public int            Year       { get; init; }
	public string         GameTitle  { get; init; } = "";
	public IReadOnlyList<GameGenre> GameGenres { get; init; } = System.Array.Empty<GameGenre>();

	/// Optional flavour line shown under the award. e.g. "A landmark
	/// release in the genre" or "Edged out the competition by a single point."
	public string         Citation   { get; init; } = "";
}

/// Categories the year-end award ceremony judges. Add a value here, and
/// the Gallery's Trophies tab picks it up automatically.
public enum TrophyDivision
{
	GameOfTheYear   = 0,
	BestDesign      = 1,
	BestSound       = 2,
	BestGraphics    = 3,
	BestIndie       = 4,
	BestNewcomer    = 5,
	CriticsChoice   = 6,
}

public static class TrophyDivisionExtensions
{
	public static string DisplayName( this TrophyDivision d ) => d switch
	{
		TrophyDivision.GameOfTheYear => "Game of the Year",
		TrophyDivision.BestDesign    => "Best Game Design",
		TrophyDivision.BestSound     => "Best Sound Design",
		TrophyDivision.BestGraphics  => "Best Visual Design",
		TrophyDivision.BestIndie     => "Best Indie",
		TrophyDivision.BestNewcomer  => "Best Newcomer",
		TrophyDivision.CriticsChoice => "Critics' Choice",
		_                            => d.ToString(),
	};

	/// Tier-coloured CSS modifier so the trophies cards can pick distinct
	/// accent colours (gold for GotY, etc.). Mirrors the BadgeClass pattern
	/// from <see cref="EmployeeKind"/>.
	public static string BadgeClass( this TrophyDivision d ) => d switch
	{
		TrophyDivision.GameOfTheYear => "div-goty",
		TrophyDivision.BestDesign    => "div-design",
		TrophyDivision.BestSound     => "div-sound",
		TrophyDivision.BestGraphics  => "div-graphics",
		TrophyDivision.BestIndie     => "div-indie",
		TrophyDivision.BestNewcomer  => "div-newcomer",
		TrophyDivision.CriticsChoice => "div-critics",
		_                            => "",
	};

	public static string Icon( this TrophyDivision d ) => d switch
	{
		TrophyDivision.GameOfTheYear => "🏆",
		TrophyDivision.BestDesign    => "🎮",
		TrophyDivision.BestSound     => "🎵",
		TrophyDivision.BestGraphics  => "🎨",
		TrophyDivision.BestIndie     => "💎",
		TrophyDivision.BestNewcomer  => "🌱",
		TrophyDivision.CriticsChoice => "📰",
		_                            => "🏅",
	};
}
