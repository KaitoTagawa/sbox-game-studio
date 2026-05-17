/// <summary>
/// First-launch tutorial state machine. Walks the player through every
/// piece of the core loop one screen at a time. Phase progression:
///   • <see cref="TutorialPhase.Welcome"/>          — modal up, time paused, all menus blocked.
///   • <see cref="TutorialPhase.HireFirst"/>        — only HR + Hire tab clickable.
///   • <see cref="TutorialPhase.CreateGameOnly"/>   — only Create Game clickable.
///   • <see cref="TutorialPhase.BuyBinOnly"/>       — only Shop clickable (must buy a Bin).
///   • <see cref="TutorialPhase.StartupTraining"/>  — only Training clickable; the Start Up Training CTA boosts founder +10 to all stats.
///   • <see cref="TutorialPhase.PublishReady"/>     — fast-completed project's Publish UI auto-opens; modal "good job, ready to publish".
///   • <see cref="TutorialPhase.BuyChair"/>         — game is shipped, money flows. Only Shop clickable; must buy a Standard Chair.
///   • <see cref="TutorialPhase.AssignChair"/>      — only Edit Desks clickable; player assigns the chair to a hire.
///   • <see cref="TutorialPhase.Complete"/>         — final congrats modal.
///
/// Time stays paused through <see cref="PublishReady"/> (the first project
/// fast-completes via <see cref="GameProjectManager.CompleteImmediately"/>
/// rather than waiting on the production timer). From <see cref="BuyChair"/>
/// onward, time runs — the just-shipped game's revenue starts flowing.
/// Persisted per-run via <see cref="TutorialSave"/>.
/// </summary>
public sealed class TutorialManager : Component
{
	public static TutorialManager Instance { get; private set; }

	[Property] public TutorialPhase Phase { get; set; } = TutorialPhase.Welcome;

	/// Highest phase the player has clicked through the modal for. Null = no
	/// phase dismissed yet (Welcome modal shows on a fresh boot).
	[Property] public TutorialPhase? LastAcknowledgedPhase { get; set; }

	/// Set true the first time the player buys a Bin during BuyBinOnly.
	[Property] public bool BinBought { get; set; }

	/// Set true the first time the player runs Start Up Training during
	/// StartupTraining. Triggers project fast-complete + PublishReady phase.
	[Property] public bool StartupTrainingDone { get; set; }

	/// Set true the first time the player buys a Standard Chair during
	/// BuyChair. Advances to AssignChair.
	[Property] public bool ChairBought { get; set; }

	/// Set true the first time the player assigns a chair to a desk during
	/// AssignChair. Advances to Complete.
	[Property] public bool ChairAssigned { get; set; }

	/// Set true once the player has opened chat with the NPC named in the
	/// first-ever Good-mood toast. While false, the first Good-mood toast
	/// pushes sticky (duration = 0) so the player can't miss it; once set,
	/// future Good-mood toasts use the default 5s auto-expire.
	[Property] public bool FirstGoodMoodToastSeen { get; set; }

	// ── Visibility helpers ──────────────────────────────────────────────────

	/// True while the tutorial wants the calendar / project / applicant /
	/// sales ticks frozen. Time stays paused right up through PublishReady
	/// (we fast-complete the first project to avoid making the player wait
	/// on the production timer); BuyChair / AssignChair / Complete let
	/// time run so the just-shipped game's revenue flows in.
	public bool IsBlockingTime => Phase == TutorialPhase.Welcome
	                            || Phase == TutorialPhase.HireFirst
	                            || Phase == TutorialPhase.CreateGameOnly
	                            || Phase == TutorialPhase.EnergyIntro
	                            || Phase == TutorialPhase.BuyBinOnly
	                            || Phase == TutorialPhase.StartupTraining
	                            || Phase == TutorialPhase.PublishReady;

	// Modal visibility — the prominent intro shown at each phase's first
	// entry. Hidden once the player clicks the Begin/Got it button, which
	// stamps LastAcknowledgedPhase. The persistent corner toast (see
	// PushPhaseObjective) takes over from there as the standing reminder.
	public bool IsWelcomeVisible          => Phase == TutorialPhase.Welcome          && LastAcknowledgedPhase != TutorialPhase.Welcome;
	public bool IsCreateGameModalVisible  => Phase == TutorialPhase.CreateGameOnly   && LastAcknowledgedPhase != TutorialPhase.CreateGameOnly;
	public bool IsEnergyIntroVisible      => Phase == TutorialPhase.EnergyIntro      && LastAcknowledgedPhase != TutorialPhase.EnergyIntro;
	public bool IsBuyBinModalVisible      => Phase == TutorialPhase.BuyBinOnly       && LastAcknowledgedPhase != TutorialPhase.BuyBinOnly;
	public bool IsTrainingModalVisible    => Phase == TutorialPhase.StartupTraining  && LastAcknowledgedPhase != TutorialPhase.StartupTraining;
	public bool IsPublishReadyVisible     => Phase == TutorialPhase.PublishReady     && LastAcknowledgedPhase != TutorialPhase.PublishReady;
	public bool IsBuyChairModalVisible    => Phase == TutorialPhase.BuyChair         && LastAcknowledgedPhase != TutorialPhase.BuyChair;
	public bool IsAssignChairModalVisible => Phase == TutorialPhase.AssignChair      && LastAcknowledgedPhase != TutorialPhase.AssignChair;
	public bool IsCompleteModalVisible    => Phase == TutorialPhase.Complete         && LastAcknowledgedPhase != TutorialPhase.Complete;

	public bool IsAnyModalVisible => IsWelcomeVisible
	                              || IsCreateGameModalVisible
	                              || IsEnergyIntroVisible
	                              || IsBuyBinModalVisible
	                              || IsTrainingModalVisible
	                              || IsPublishReadyVisible
	                              || IsBuyChairModalVisible
	                              || IsAssignChairModalVisible
	                              || IsCompleteModalVisible;

	/// True if the menu's tile gating should restrict to a phase-specific
	/// allowlist. False once the tutorial hits Complete.
	public bool IsMenuLocked => Phase != TutorialPhase.Complete
	                          && Phase != TutorialPhase.PublishReady;

	/// Drives the gold "Start Up Training" CTA in TrainingPanel.
	public bool IsStartupTrainingStepActive => Phase == TutorialPhase.StartupTraining;

	/// Tutorial wants to force a Bin purchase right now.
	public bool IsForcingBinPurchase => Phase == TutorialPhase.BuyBinOnly;

	/// Tutorial wants to force a Standard Chair purchase right now.
	public bool IsForcingChairPurchase => Phase == TutorialPhase.BuyChair;

	protected override void OnAwake()
	{
		Instance = this;
		Log.Info( $"[Tutorial] OnAwake — Phase={Phase}, BinBought={BinBought}, " +
		          $"StartupTrainingDone={StartupTrainingDone}, ChairBought={ChairBought}, " +
		          $"ChairAssigned={ChairAssigned}, " +
		          $"LastAck={LastAcknowledgedPhase?.ToString() ?? "null"}, " +
		          $"IsAnyModalVisible={IsAnyModalVisible}." );
	}

	protected override void OnStart()
	{
		// Re-push the current phase's objective toast on every load so the
		// player isn't dropped into a paused world without context. Sticky
		// toasts don't survive a session restart on their own. Welcome is
		// handled by its modal (the player clicks Begin), and Complete is
		// the one-shot celebration — neither needs the standing toast.
		if ( Phase != TutorialPhase.Welcome && Phase != TutorialPhase.Complete )
			PushPhaseObjective();

		Log.Info( $"[Tutorial] OnStart — Phase={Phase}, IsBlockingTime={IsBlockingTime}." );
	}

	/// Tag every tutorial toast with this so the next phase's Push can wipe
	/// the previous sticky one — without it, advancing phases would stack
	/// objective toasts on top of each other.
	const string TutorialToastTag = "tutorial";

	/// Toast nudge for the current phase. Replaces the old per-phase modal —
	/// title is the objective, body is the explicit "Press TAB → ..." hint.
	/// All in-progress objectives are sticky (Duration = 0); only the final
	/// Complete celebration auto-expires. No-op once the tutorial is done.
	public void PushPhaseObjective()
	{
		// Wipe any prior tutorial toast (visible OR queued behind a menu)
		// before pushing the new one — sticky toasts would otherwise stack
		// across phase advances.
		Notifications.RemoveByTag( TutorialToastTag );

		switch ( Phase )
		{
			case TutorialPhase.HireFirst:
				Notifications.Push( "Hire your first employee",
					"Press TAB → open HR → hire someone.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.CreateGameOnly:
				Notifications.Push( "Design your first game",
					"Press TAB → open Create Game.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.BuyBinOnly:
				Notifications.Push( "Decorate the office",
					"Press TAB → open Shop → buy a Bin.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.StartupTraining:
				Notifications.Push( "Run startup training",
					"Press TAB → open Training → run Start Up Training (free).",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.PublishReady:
				Notifications.Push( "Ship your game",
					"Press TAB → open Create Game → click Publish.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.BuyChair:
				Notifications.Push( "Upgrade the office",
					"Press TAB → open Shop → buy a Standard Chair.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.AssignChair:
				Notifications.Push( "Assign the chair",
					$"Press TAB → open Edit Desks → give {FirstHireName} the chair.",
					"info", duration: 0f, tag: TutorialToastTag );
				break;
			case TutorialPhase.Complete:
				Notifications.Push( "Tutorial complete",
					"The studio is yours from here on. Good luck!",
					"success", duration: 6f, tag: TutorialToastTag );
				break;
		}
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	// ── Self-heal toast loop ─────────────────────────────────────────────────
	// OnStart only fires on a fresh component spawn — hot-reload preserves
	// component instances, so a recompile after we'd already entered the
	// scene would leave the player without their objective toast. This loop
	// re-pushes the current phase's sticky toast whenever it goes missing
	// (hot-reload, Notifications.Clear, manual dismiss, etc.) and handles
	// the Welcome → HireFirst auto-advance defensively too.

	float _toastReinforceAccum;
	const float ToastReinforceInterval = 2f;

	protected override void OnUpdate()
	{
		// Welcome is handled by its modal; toast doesn't apply yet.
		// Complete fires once and shouldn't re-arm.
		if ( Phase == TutorialPhase.Welcome )       return;
		if ( Phase == TutorialPhase.Complete )      return;

		_toastReinforceAccum += Time.Delta;
		if ( _toastReinforceAccum < ToastReinforceInterval ) return;
		_toastReinforceAccum = 0f;

		// Re-push only if the sticky toast is missing from both the visible
		// list and the pending queue. RemoveByTag inside PushPhaseObjective
		// makes the call idempotent if a tutorial toast does exist.
		if ( !Notifications.HasTag( TutorialToastTag ) )
			PushPhaseObjective();
	}

	// ── Phase advancement ─────────────────────────────────────────────────

	/// Player clicked the modal's primary button. Welcome additionally
	/// advances to HireFirst (the player committed to starting the
	/// tutorial). All other modal phases just dismiss — their advance
	/// happens on gameplay events.
	public void AcknowledgeCurrentPhase()
	{
		// Capture the phase we're acknowledging BEFORE we step the
		// machine into the next one. Otherwise stamping LastAck = Phase
		// after the advancement would mark the *new* phase as already
		// dismissed, suppressing its modal (e.g. after EnergyIntro →
		// BuyBinOnly, the BuyBin modal would never appear).
		var dismissed = Phase;

		if ( Phase == TutorialPhase.Welcome )
			Phase = TutorialPhase.HireFirst;

		// EnergyIntro is a pure read-the-modal interstitial — clicking
		// "Got it" advances to BuyBinOnly so the player heads to the shop
		// next.
		if ( Phase == TutorialPhase.EnergyIntro )
			Phase = TutorialPhase.BuyBinOnly;

		LastAcknowledgedPhase = dismissed;

		// Award the tutorial-complete achievement the moment the Complete
		// modal is dismissed (rather than at chair-assignment time) so the
		// "Achievement Unlocked — Studio Open — funding +$300" toast pops
		// AFTER the modal is gone — visible on the Hud layer instead of
		// hidden behind the celebration card. ForceUnlock is idempotent.
		if ( dismissed == TutorialPhase.Complete )
			Achievements.ForceUnlock( AchievementId.TutorialComplete );

		// Modal just dismissed — fire the persistent corner toast so the
		// player has a standing "Press TAB → ..." reminder underneath.
		// Skipped on Complete (one-shot celebration toast handled at the
		// transition into Complete itself).
		if ( Phase != TutorialPhase.Complete )
			PushPhaseObjective();

		Log.Info( $"[Tutorial] AcknowledgeCurrentPhase — now Phase={Phase}, " +
		          $"LastAck={LastAcknowledgedPhase}, " +
		          $"IsAnyModalVisible={IsAnyModalVisible}, IsMenuLocked={IsMenuLocked}." );
	}

	public void NotifyFirstHire()
	{
		if ( Phase != TutorialPhase.HireFirst ) return;
		// Stamp the OUTGOING phase as acknowledged before stepping forward
		// so the incoming phase's modal isn't preemptively suppressed by
		// `LastAcknowledgedPhase == newPhase`. Mirrors the same fix used
		// in AcknowledgeCurrentPhase for EnergyIntro → BuyBinOnly.
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.CreateGameOnly;
		PushPhaseObjective();
		Log.Info( "[Tutorial] First hire detected → CreateGameOnly." );
	}

	public void NotifyProductionBegun()
	{
		if ( Phase != TutorialPhase.CreateGameOnly ) return;
		LastAcknowledgedPhase = Phase;
		// Insert the EnergyIntro interstitial first — modal explains that
		// energy regenerates while a project is in production. Player
		// clicks "Got it" → AcknowledgeCurrentPhase advances to BuyBinOnly.
		Phase = TutorialPhase.EnergyIntro;
		Log.Info( "[Tutorial] Production begun → EnergyIntro." );
	}

	public void NotifyBinBought()
	{
		if ( BinBought ) return;
		if ( Phase != TutorialPhase.BuyBinOnly ) return;
		BinBought = true;
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.StartupTraining;
		PushPhaseObjective();

		// Force-close the shop so the player isn't tempted to keep
		// browsing / accidentally spending their starting cash on random
		// items — the next tutorial step is in Training, not the shop.
		Shop.Instance?.SetOpen( false );

		Log.Info( "[Tutorial] Bin bought → StartupTraining." );
	}

	public void NotifyStartupTrainingDone()
	{
		if ( StartupTrainingDone ) return;
		if ( Phase != TutorialPhase.StartupTraining ) return;
		StartupTrainingDone = true;
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.PublishReady;

		// Skip the production wait — player has already done the design
		// work and decorated the office, the 60s timer would just be empty
		// time on their first game.
		GameProjectManager.Instance?.CompleteImmediately();

		PushPhaseObjective();
		Log.Info( "[Tutorial] Startup Training done → PublishReady (project fast-completed)." );
	}

	public void NotifyGamePublished()
	{
		// PublishReady → BuyChair on first publish during the tutorial.
		// Any later publishes are no-ops (Phase already past).
		if ( Phase != TutorialPhase.PublishReady ) return;
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.BuyChair;
		PushPhaseObjective();
		Log.Info( "[Tutorial] First game published → BuyChair (time unblocks, money starts flowing)." );
	}

	public void NotifyChairBought()
	{
		if ( ChairBought ) return;
		if ( Phase != TutorialPhase.BuyChair ) return;
		ChairBought = true;
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.AssignChair;
		PushPhaseObjective();

		// Mirror the Bin-bought flow — kick the player out of the shop so
		// they don't keep browsing. Next step is in Edit Desks (assign
		// the chair to their hire), not the shop.
		Shop.Instance?.SetOpen( false );

		Log.Info( "[Tutorial] Standard Chair bought → AssignChair." );
	}

	public void NotifyChairAssigned()
	{
		if ( ChairAssigned ) return;
		if ( Phase != TutorialPhase.AssignChair ) return;
		ChairAssigned = true;
		LastAcknowledgedPhase = Phase;
		Phase = TutorialPhase.Complete;
		PushPhaseObjective();

		// Direct $300 deposit, gated only by `ChairAssigned` (which we
		// just set to true above) so it fires exactly once per tutorial
		// completion regardless of achievement state. Decoupled from the
		// achievement's MoneyBonus path because that uses
		// `_unlocked.Contains` for idempotency — if a previous playthrough
		// left TutorialComplete in `_unlocked` and the player loaded a
		// save instead of New-Game-restarting, the achievement-driven
		// payout would no-op and the player would never see their $300.
		GameManager.Instance?.AddMoney( 350 );

		// Achievement still fires for the unlock badge / sbox.game
		// platform sync, just without its own money deposit.
		Achievements.ForceUnlock( AchievementId.TutorialComplete );

		// High-priority confirmation toast (warning kind → routed through
		// NotificationsOverlay at z=1000, renders ABOVE the Complete
		// modal so the player can't miss the funding deposit).
		Notifications.Push( "Tutorial complete",
			"Studio Open unlocked — +$350 funding deposited.",
			"warning", duration: 6f );

		Log.Info( "[Tutorial] Chair assigned → Complete." );
	}

	/// Returns true if BeginProduction is allowed under current tutorial
	/// rules. Only allowed during CreateGameOnly (and outside the tutorial).
	public bool CanBeginProduction()
	{
		if ( Phase == TutorialPhase.Complete ) return true;
		return Phase == TutorialPhase.CreateGameOnly;
	}

	// ── Tile + sub-tab gating ─────────────────────────────────────────────

	public bool IsTileEnabled( string tileName )
	{
		if ( !IsMenuLocked ) return true;

		// Settings + Save are always allowed during the tutorial.
		if ( tileName is "Settings" or "Save" ) return true;

		return Phase switch
		{
			TutorialPhase.HireFirst        => tileName == "Human Resource",
			TutorialPhase.CreateGameOnly   => tileName == "Create Game",
			TutorialPhase.EnergyIntro      => false,   // modal-only beat; no tiles clickable
			TutorialPhase.BuyBinOnly       => tileName == "Shop",
			TutorialPhase.StartupTraining  => tileName == "Training",
			TutorialPhase.BuyChair         => tileName == "Shop",
			TutorialPhase.AssignChair      => tileName == "Edit Desks",
			_                              => true,
		};
	}

	public bool IsHRSubviewEnabled( HRSubview subview )
	{
		if ( Phase != TutorialPhase.HireFirst ) return true;
		return subview == HRSubview.Hire;
	}

	/// Display name of the first hire — used by the AssignChair modal copy
	/// ("give <name> a new chair"). Falls back to a generic label if the
	/// player somehow has no staff at this point.
	public string FirstHireName
	{
		get
		{
			var hr = HRManager.Instance;
			if ( hr is null || hr.Staff.Count == 0 ) return "your hire";
			return hr.Staff[0].EmployeeName;
		}
	}

	// ── Save / Load (per ADR-0001) ─────────────────────────────────────────

	public TutorialSave Save() => new()
	{
		Phase                  = Phase,
		BinBought              = BinBought,
		StartupTrainingDone    = StartupTrainingDone,
		ChairBought            = ChairBought,
		ChairAssigned          = ChairAssigned,
		LastAcknowledgedPhase  = LastAcknowledgedPhase,
		FirstGoodMoodToastSeen = FirstGoodMoodToastSeen,
	};

	public void Load( TutorialSave dto )
	{
		// Legacy save migration: pre-tutorial saves (no TutorialSave field)
		// → if there's already staff, fast-forward to Complete.
		if ( dto is null )
		{
			bool hasStaff = (HRManager.Instance?.Staff.Count ?? 0) > 0;
			Phase                  = hasStaff ? TutorialPhase.Complete : TutorialPhase.Welcome;
			BinBought              = false;
			StartupTrainingDone    = false;
			ChairBought            = false;
			ChairAssigned          = false;
			LastAcknowledgedPhase  = hasStaff ? TutorialPhase.Complete : (TutorialPhase?)null;
			FirstGoodMoodToastSeen = false;
			return;
		}

		Phase                  = dto.Phase;
		BinBought              = dto.BinBought;
		StartupTrainingDone    = dto.StartupTrainingDone;
		ChairBought            = dto.ChairBought;
		ChairAssigned          = dto.ChairAssigned;
		LastAcknowledgedPhase  = dto.LastAcknowledgedPhase;
		FirstGoodMoodToastSeen = dto.FirstGoodMoodToastSeen;
	}

	/// Renamed from Reset() so it doesn't shadow Component.Reset(), which
	/// the engine calls on its own schedule. Same pattern as Gallery.
	public void ResetProgress()
	{
		Phase                  = TutorialPhase.Welcome;
		BinBought              = false;
		StartupTrainingDone    = false;
		ChairBought            = false;
		ChairAssigned          = false;
		LastAcknowledgedPhase  = null;
		FirstGoodMoodToastSeen = false;
		Log.Info( $"[Tutorial] ResetProgress — Phase=Welcome. IsAnyModalVisible={IsAnyModalVisible}." );
	}
}

/// Coarse stages of the first-launch tutorial. Add a value here AND a
/// matching branch in TutorialManager / TutorialPanel when extending.
public enum TutorialPhase
{
	Welcome          = 0,
	HireFirst        = 1,
	CreateGameOnly   = 2,
	BuyBinOnly       = 3,
	StartupTraining  = 4,
	PublishReady     = 5,
	BuyChair         = 6,
	AssignChair      = 7,
	Complete         = 8,
	// Inserted between CreateGameOnly and BuyBinOnly. Appended at the end
	// of the enum so existing save files keep their integer values stable.
	EnergyIntro      = 9,
}
