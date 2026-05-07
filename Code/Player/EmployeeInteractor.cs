using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Player-side proximity tracker + chat-state owner for employee interactions.
///
/// Each frame finds the closest assigned <see cref="EmployeeNPC"/> within
/// <see cref="InteractRange"/> of the player and exposes it as
/// <see cref="NearestEmployee"/>; the Hud reads that to show the centered
/// "Press E" prompt. On the "Use" key press, opens a chat with the nearest
/// NPC (or closes the chat if one is already open). While chatting,
/// <see cref="IsChatting"/> drives both the bottom-of-screen chat panel and
/// the GameManager modal-cursor lock.
///
/// Drop on any scene GameObject (GameManager is the natural home — singleton,
/// always loaded). Player position is auto-found via Sandbox.PlayerController
/// each frame, so no inspector wiring is needed.
/// </summary>
public sealed class EmployeeInteractor : Component
{
	public static EmployeeInteractor Instance { get; private set; }

	/// World-space distance (s&box units, ~1 inch each) at which an employee
	/// becomes greetable. 150 ≈ ~3.8m — close enough that the player is
	/// clearly "near" the NPC, far enough to forgive imprecise positioning.
	[Property] public float InteractRange { get; set; } = 150f;

	/// Full cone angle (degrees) the player must be facing for an NPC to count.
	/// 100° = 50° each side of forward — wide enough to forgive imprecise
	/// mouse-look, narrow enough that an NPC behind the player isn't picked
	/// when the one they're looking at was the intended target.
	[Property] public float ViewConeDegrees { get; set; } = 100f;

	/// Closest assigned NPC inside InteractRange, or null if none is in range,
	/// no player exists in the scene, or any modal is open. The Hud reads
	/// this every render to gate the prompt.
	public EmployeeNPC NearestEmployee { get; private set; }

	/// The NPC the player is currently chatting with, or null when no chat
	/// is open. Set by <see cref="OpenChatWith"/>; cleared by
	/// <see cref="CloseChat"/>.
	public EmployeeNPC ChatTarget { get; private set; }

	/// The line the chat partner most recently said. Starts as their greeting
	/// when the chat opens; updated to a flavored response each time the
	/// player picks a reply via <see cref="HandleReply"/>.
	public string ChatLine { get; private set; }

	public bool IsChatting => ChatTarget is not null;

	/// Currently-highlighted reply in the chat panel. Driven by W/S and
	/// up/down arrows while chatting; E confirms. The Hud reads this to
	/// apply the `.selected` class to the matching button. Reset to 0
	/// every time a chat opens so the player always starts on the first
	/// reply.
	public int SelectedReplyIndex { get; private set; }

	/// Captured at chat-open time when the partner is in <see cref="EmployeeMood.Good"/>.
	/// Snapshotting decouples the chat flow from the NPC's live mood — if
	/// production ends mid-chat the NPC's PendingSuggestion clears, but we
	/// still know what the player is responding to.
	public EmployeeSuggestion ChatSuggestion { get; private set; }

	/// True while the gift-picker is overlaying the default reply set —
	/// player has clicked "I got something for you." and is browsing what
	/// they own. Cleared by <see cref="CloseGiftPicker"/>, by gifting an
	/// item, or by closing the chat. Mood-specific reply sets (Bad /
	/// suggestion) intentionally ignore this flag — the gift picker only
	/// overlays the default conversational set.
	bool _showingGiftPicker;

	readonly System.Random _rng = new();

	/// Reply set surfaced in the chat panel. Switches between the default
	/// (AskWork / Compliment / exit) and the suggestion-mode set
	/// (Accept / Maybe later / exit) based on whether <see cref="ChatSuggestion"/>
	/// is set. Built every getter call so hot-reload sees label changes
	/// (e.g. cost text) immediately — no static cache.
	public IReadOnlyList<ReplyOption> ReplyOptions
	{
		get
		{
			// Bad mood: two role-flavored advice lines + exit. One matches
			// the NPC's RightAdvice (rolled at SetMood), the other doesn't.
			// Wrong picks leave mood Bad so the player can retry the other.
			if ( ChatTarget?.Mood == EmployeeMood.Bad )
			{
				var labels = EmployeeNPC.AdviceLabels( ChatTarget.Role );
				return new ReplyOption[]
				{
					new( labels[0],          () => OnAdvice( 0 ) ),
					new( labels[1],          () => OnAdvice( 1 ) ),
					new( "Catch you later.", CloseChat            ),
				};
			}

			if ( ChatSuggestion is { } sg )
			{
				// Scale cost with year so the price-tag tracks studio
				// progress. ScaledCost is a no-op when sg.Cost == 0.
				var scaledCost  = EmployeeSuggestions.ScaledCost( sg.Cost );
				var acceptLabel = scaledCost > 0
					? $"Accept idea (-${scaledCost:N0})"
					: "Accept idea";
				return new ReplyOption[]
				{
					new( acceptLabel,       OnAccept    ),
					new( "Maybe later.",    OnDecline   ),
					new( "Catch you later.", CloseChat   ),
				};
			}

			// Gift picker overlay — replaces the default reply set with one
			// row per giftable owned ItemKind, plus a "Never mind" exit.
			// Driven by OpenGiftPicker / CloseGiftPicker; effects route
			// through InventoryManager.TryGiveConsumable (consumables) or
			// TryAssignItemToDesk (Chair / Computer / Monitor).
			if ( _showingGiftPicker )
				return BuildGiftPickerOptions();

			return new ReplyOption[]
			{
				new( "How's work going?",       () => HandleReply( ChatReplyKind.AskWork    ) ),
				new( "You're doing great work.", () => HandleReply( ChatReplyKind.Compliment ) ),
				new( "I got something for you.", OpenGiftPicker ),
				new( "Catch you later.",         CloseChat ),
			};
		}
	}

	/// Build one ReplyOption per giftable kind in the player's stash. A "kind"
	/// shows up as a single row even when multiple unplaced copies exist —
	/// label appends " × N" so the player can see the count without the
	/// picker stretching to N rows. Workstation kinds (Chair / Computer /
	/// Monitor) are filtered out for remote-worker recipients (no
	/// <c>DeskSlotId</c>) so the picker never offers an ungiftable option.
	IReadOnlyList<ReplyOption> BuildGiftPickerOptions()
	{
		var result = new List<ReplyOption>();
		var inv    = InventoryManager.Instance;
		var target = ChatTarget;
		bool hasDesk = target?.DeskSlotId is not null;

		if ( inv is not null && target is not null )
		{
			// Aggregate unplaced owned items per kind. Multiple copies of the
			// same kind collapse into one row; clicking gifts ONE copy and the
			// row re-renders next frame with the new count (or vanishes when
			// the count hits 0). We capture the *first* unplaced item per kind
			// at click time inside a lambda — recomputing in the click handler
			// keeps the picker resilient to mid-chat inventory changes.
			var counts = new Dictionary<ItemKind, int>();
			foreach ( var it in inv.Owned )
			{
				if ( it.IsPlaced ) continue;
				var entry = InventoryCatalogue.Get( it.Kind );
				if ( entry is null ) continue;
				if ( !IsGiftable( entry, hasDesk ) ) continue;
				counts.TryGetValue( it.Kind, out var n );
				counts[it.Kind] = n + 1;
			}

			foreach ( var kv in counts )
			{
				var kind  = kv.Key;
				var n     = kv.Value;
				var label = n > 1
					? $"{kind.DisplayName()} × {n}"
					: kind.DisplayName();
				result.Add( new ReplyOption( label, () => OnGift( kind ) ) );
			}
		}

		if ( result.Count == 0 )
		{
			result.Add( new ReplyOption( "(Nothing to give)", CloseGiftPicker ) );
		}
		result.Add( new ReplyOption( "Never mind.", CloseGiftPicker ) );
		return result;
	}

	static bool IsGiftable( InventoryCatalogue.Entry entry, bool recipientHasDesk )
	{
		if ( entry.Category == ItemCategory.Consumable ) return true;
		if ( !recipientHasDesk ) return false;
		return entry.Slot == SlotKind.Chair
		    || entry.Slot == SlotKind.Computer
		    || entry.Slot == SlotKind.Monitor;
	}

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnUpdate()
	{
		// While chatting, the cursor is unlocked (see GameManager) so the
		// player can click reply buttons. W / S / up / down arrows navigate
		// the highlighted reply; E confirms (selecting "Catch you later"
		// closes the chat — gives the player a keyboard-only path out).
		if ( IsChatting )
		{
			NearestEmployee = null;
			HandleChatInput();
			return;
		}

		// Suppress proximity-checking while any other modal is open. Mouse
		// visibility is the cheapest proxy for "GameManager has unlocked the
		// cursor" without re-listing the modal set.
		var player = Scene.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
		if ( player is null
		  || Sandbox.Mouse.Visibility == Sandbox.MouseVisibility.Visible )
		{
			NearestEmployee = null;
			return;
		}

		NearestEmployee = FindNearestTo( player.WorldPosition, player.EyeAngles.Forward );

		if ( NearestEmployee is not null && Input.Pressed( "Use" ) )
			OpenChatWith( NearestEmployee );
	}

	EmployeeNPC FindNearestTo( Vector3 origin, Vector3 forward )
	{
		// Compare on the horizontal plane: looking slightly up/down at an
		// NPC's body shouldn't shrink the effective cone. If the player is
		// staring straight at the floor/ceiling there's no horizontal
		// facing — bail and let the prompt clear.
		var forwardH = forward.WithZ( 0 );
		if ( forwardH.LengthSquared < 0.0001f ) return null;
		forwardH = forwardH.Normal;

		var rangeSq = InteractRange * InteractRange;
		var minDot  = System.MathF.Cos( ViewConeDegrees * 0.5f * System.MathF.PI / 180f );

		EmployeeNPC best       = null;
		float       bestDistSq = rangeSq;

		foreach ( var npc in Scene.GetAllComponents<EmployeeNPC>() )
		{
			if ( !npc.IsAssigned ) continue;

			var delta = npc.WorldPosition - origin;
			var dSq   = delta.LengthSquared;
			if ( dSq >= bestDistSq ) continue;

			// NPC stacked directly above/below the player (no horizontal
			// offset) — accept on distance alone, the cone test is undefined.
			var dirH = delta.WithZ( 0 );
			if ( dirH.LengthSquared < 0.0001f )
			{
				bestDistSq = dSq;
				best       = npc;
				continue;
			}

			var dot = Vector3.Dot( dirH.Normal, forwardH );
			if ( dot < minDot ) continue;

			bestDistSq = dSq;
			best       = npc;
		}
		return best;
	}

	// ── Chat API (called by Hud.razor reply buttons) ──────────────────────────

	public void OpenChatWith( EmployeeNPC npc )
	{
		if ( npc is null ) return;

		ChatTarget         = npc;
		SelectedReplyIndex = 0;
		_showingGiftPicker = false;

		// Set up the line shown the moment chat opens. Bad mood doesn't
		// auto-heal anymore — the player has to pick the right advice
		// (advice/advice/exit reply set, see ReplyOptions below). Good mood
		// snapshots the suggestion so accept/decline still works even if
		// mood resets mid-chat (e.g. on ship).
		switch ( npc.Mood )
		{
			case EmployeeMood.Bad:
				ChatLine       = npc.PickBadMoodGreeting();
				ChatSuggestion = null;
				break;
			case EmployeeMood.Good when npc.PendingSuggestion is { } sg:
				ChatLine       = sg.Text;
				ChatSuggestion = sg;
				break;
			default:
				ChatLine       = npc.PickGreeting();
				ChatSuggestion = null;
				break;
		}

		// First-ever mood toasts are pushed sticky (duration = 0) so the
		// player can't miss the new mechanic — clear them now that the
		// player has acknowledged the prompt by opening chat with the
		// moody NPC. The flag flip persists via TutorialSave so future
		// toasts auto-expire normally.
		var tut = TutorialManager.Instance;
		if ( tut is not null )
		{
			if ( npc.Mood == EmployeeMood.Good && !tut.FirstGoodMoodToastSeen )
			{
				tut.FirstGoodMoodToastSeen = true;
				Notifications.RemoveByTag( "first-good-mood" );
			}
			else if ( npc.Mood == EmployeeMood.Bad && !tut.FirstBadMoodToastSeen )
			{
				tut.FirstBadMoodToastSeen = true;
				Notifications.RemoveByTag( "first-bad-mood" );
			}
		}

		GameManager.RefreshPlayerLock();
	}

	public void HandleReply( ChatReplyKind reply )
	{
		if ( ChatTarget is null ) return;
		ChatLine = ChatTarget.RespondTo( reply );
	}

	public void CloseChat()
	{
		ChatTarget         = null;
		ChatLine           = null;
		ChatSuggestion     = null;
		_showingGiftPicker = false;
		GameManager.RefreshPlayerLock();
	}

	// ── Gift picker ───────────────────────────────────────────────────────────

	/// Wired to the "I got something for you." default-set row. Flips the
	/// picker overlay on, resets the highlight to the first row, and swaps
	/// the chat line so the NPC acknowledges the offered gift while the
	/// player browses their stash.
	public void OpenGiftPicker()
	{
		if ( ChatTarget is null ) return;
		_showingGiftPicker = true;
		SelectedReplyIndex = 0;
		ChatLine           = "Oh? What did you bring me?";
	}

	/// Cancel out of the picker without gifting. Returns to the default
	/// reply set; restores a generic resume line so the panel doesn't leave
	/// a stale "What did you bring me?" hanging when the player backed out.
	public void CloseGiftPicker()
	{
		_showingGiftPicker = false;
		SelectedReplyIndex = 0;
		ChatLine           = "Anything else?";
	}

	/// Hand one unplaced copy of <paramref name="kind"/> to <see cref="ChatTarget"/>.
	/// Consumables route through <see cref="InventoryManager.TryGiveConsumable"/>;
	/// workstation upgrades (Chair / Computer / Monitor) route through
	/// <see cref="InventoryManager.TryAssignItemToDesk"/>, mounting the upgrade
	/// at the recipient's own desk and displacing whatever was there.
	/// Either path emits its own toast — chat line just shows a brief thanks.
	void OnGift( ItemKind kind )
	{
		var inv = InventoryManager.Instance;
		var npc = ChatTarget;
		if ( inv is null || npc is null ) return;

		var entry = InventoryCatalogue.Get( kind );
		if ( entry is null ) return;

		bool ok;
		if ( entry.Category == ItemCategory.Consumable )
		{
			ok = inv.TryGiveConsumable( npc, kind );
		}
		else
		{
			// Find any unplaced copy of this kind to mount. The picker only
			// surfaced the row because at least one was available — but the
			// inventory could change between render and click, so we re-resolve.
			InventoryItem item = null;
			foreach ( var it in inv.Owned )
				if ( !it.IsPlaced && it.Kind == kind ) { item = it; break; }

			if ( item is null || npc.DeskSlotId is not System.Guid deskId )
				ok = false;
			else
				ok = inv.TryAssignItemToDesk( item, deskId, notify: true );
		}

		// Either way return to the default replies. On success the chat
		// line reflects the NPC's reaction; on failure we stay in the picker
		// so the player can pick something else (the toast already explained
		// what went wrong).
		if ( ok )
		{
			_showingGiftPicker = false;
			ChatLine           = npc.PickThanksLine();
		}
		SelectedReplyIndex = 0;
	}

	// ── Suggestion accept / decline ───────────────────────────────────────────

	/// Accept the captured suggestion. Free ideas are a 50 % gamble; paid asks
	/// deduct money and pay the bonus reliably. Either path consumes the
	/// suggestion and returns the chat to default-reply mode.
	void OnAccept()
	{
		var npc = ChatTarget;
		var sg  = ChatSuggestion;
		if ( npc is null || sg is null ) return;

		// Bonus only credits during Production (AddPillarBonus enforces this
		// too, but checking here means we don't deduct money for a no-op
		// bonus if the project shipped while the chat was open).
		var phase = GameProjectManager.Instance?.Current?.Phase;
		if ( phase != GameProjectPhase.Production )
		{
			ChatLine           = "We'll catch this idea next project.";
			ChatSuggestion     = null;
			npc.SetMood( EmployeeMood.Neutral, silent: true );
			SelectedReplyIndex = 0;
			return;
		}

		// Paid: must afford. Cost is year-scaled, so "$1,500" early-game
		// reads as "$3,000" mid-late game. If broke, NPC just notes it and
		// we keep the suggestion alive so the player can save up and come back.
		var actualCost = EmployeeSuggestions.ScaledCost( sg.Cost );
		if ( actualCost > 0 )
		{
			var money = GameManager.Instance?.Money ?? 0;
			if ( money < actualCost )
			{
				ChatLine = "We can't afford that right now.";
				return;
			}
			GameManager.Instance?.AddMoney( -actualCost );
		}

		// Free ideas land 50 % of the time; paid ideas are guaranteed
		// (the cost IS the de-risking — paying buys certainty).
		bool landed = actualCost > 0 || _rng.NextDouble() < 0.5;

		if ( landed )
		{
			var pillar = EmployeeSuggestions.PillarForRole( npc.Role, _rng );
			int  bonus = (int)System.MathF.Round(
				EmployeeSuggestions.PrimaryStatValue( npc.Role, npc.Stats ) * 0.10f );
			bonus = System.Math.Max( 1, bonus );
			// Paid ideas pay 3× — the player spent money for certainty AND
			// significance, so the payoff has to feel meaningfully larger
			// than a free 50/50 idea or there's no reason to ever buy one.
			if ( actualCost > 0 )
				bonus *= 3;
			GameProjectManager.Instance?.AddPillarBonus( pillar, bonus );
			ChatLine = $"On it! +{bonus} {PillarLabel( pillar )}.";
		}
		else
		{
			ChatLine = "I'll give it a shot… didn't quite land. Maybe next time.";
		}

		// Consume the suggestion + reset mood so the reply set reverts to
		// the default conversational options.
		ChatSuggestion = null;
		npc.SetMood( EmployeeMood.Neutral, silent: true );
		SelectedReplyIndex = 0;
	}

	void OnDecline()
	{
		var npc = ChatTarget;
		if ( npc is null ) return;

		ChatLine           = "No worries — maybe next time.";
		ChatSuggestion     = null;
		npc.SetMood( EmployeeMood.Neutral, silent: true );
		SelectedReplyIndex = 0;
	}

	/// Bad-mood advice handler. Picking the matching index heals; the
	/// other one shows a "still stuck" line and leaves Mood = Bad so
	/// the player can retry the other choice without re-opening chat.
	void OnAdvice( int chosen )
	{
		var npc = ChatTarget;
		if ( npc is null ) return;

		if ( chosen == npc.RightAdvice )
		{
			ChatLine = npc.RightAdviceResponse();
			npc.SetMood( EmployeeMood.Neutral, silent: true );
		}
		else
		{
			ChatLine = npc.WrongAdviceResponse();
			// Mood stays Bad. ReplyOptions keeps showing both advice rows
			// + "Catch you later" so the player can pick the other one.
		}

		SelectedReplyIndex = 0;
	}

	static string PillarLabel( ProjectPillar p ) => p switch
	{
		ProjectPillar.Design   => "Design",
		ProjectPillar.Sound    => "Sound",
		ProjectPillar.Graphics => "Graphics",
		_                      => "??",
	};

	/// Drive <see cref="SelectedReplyIndex"/> via W/S/up/down and confirm on E.
	/// Confirming the "exit" entry (Kind = null) closes the chat; otherwise
	/// the NPC responds and the panel stays open.
	void HandleChatInput()
	{
		var options = ReplyOptions;
		var n = options.Count;
		if ( n <= 0 ) return;

		// Navigation. Both the WSAD movement actions and raw arrow-key
		// names work — Input.Pressed accepts either action names from
		// Input.config or raw key tokens for unmapped keys.
		if ( Input.Pressed( "Forward" ) || Input.Pressed( "up" ) )
			SelectedReplyIndex = (SelectedReplyIndex - 1 + n) % n;

		if ( Input.Pressed( "Backward" ) || Input.Pressed( "down" ) )
			SelectedReplyIndex = (SelectedReplyIndex + 1) % n;

		// Confirm: E (Use action) or Enter. "Chat" action is bound to
		// Enter in Input.config; raw "enter" token is a belt-and-braces
		// fallback in case Input.Pressed only accepts action names on
		// this s&box build.
		if ( Input.Pressed( "Use" )
		  || Input.Pressed( "Chat" )
		  || Input.Pressed( "enter" ) )
			ConfirmSelected();
	}

	/// Move the keyboard highlight to a specific reply (used by the Hud's
	/// click handlers so a mouse click visually selects the row before
	/// confirming, keeping the keyboard- and mouse-nav modes in sync).
	public void SelectReply( int index )
	{
		var n = ReplyOptions.Count;
		if ( n <= 0 ) return;
		SelectedReplyIndex = System.Math.Clamp( index, 0, n - 1 );
	}

	/// Run the action for the currently-highlighted reply. The reply's
	/// embedded <see cref="ReplyOption.OnSelect"/> handles everything —
	/// could be a regular NPC response, accept/decline of a suggestion,
	/// or a panel close.
	public void ConfirmSelected()
	{
		if ( !IsChatting ) return;
		var options = ReplyOptions;
		if ( options.Count == 0 ) return;

		var idx = System.Math.Clamp( SelectedReplyIndex, 0, options.Count - 1 );
		options[idx].OnSelect?.Invoke();
	}
}

/// <summary>
/// One row in <see cref="EmployeeInteractor.ReplyOptions"/>. The
/// <see cref="OnSelect"/> action runs when the player confirms this row —
/// it might call <c>HandleReply</c>, accept a suggestion, or close the chat.
/// Building the action into the option (rather than returning an enum and
/// switching on it elsewhere) lets each reply own its own behaviour.
/// </summary>
public sealed record ReplyOption( string Label, System.Action OnSelect );
