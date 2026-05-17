public sealed class GameMenu : Component
{
	public static GameMenu Instance { get; private set; }

	public bool IsOpen        { get; private set; }
	public int  HoveredIndex  { get; set; } = -1;

	/// Inner sub-tab for the HR section. Vestigial as of ADR-0003 Phase 1
	/// (HR moved to its own modal in ADR-0002 prep work) — kept for the
	/// HRSubview enum's other readers (TutorialManager.IsHRSubviewEnabled).
	public HRSubview ActiveHRSubview { get; set; } = HRSubview.None;

	public IReadOnlyList<MenuTile> Tiles { get; } = new MenuTile[]
	{
		new( "Create Game",    "Start developing a new game project and pick a genre.",          "🎮" ),
		new( "Human Resource", "Hire, manage, and fire employees.",                              "👥" ),
		new( "Training",       "Improve your team's skills and specialties.",                    "📚" ),
		new( "Inventory",      "Manage office furniture and consumables.",                       "📦" ),
		new( "Shop",           "Buy equipment, decorations, and upgrades for your studio.",      "🛒" ),
		new( "Edit Desks",     "Edit chair, computer, and monitor at every desk.",               "🖥️" ),
		new( "Quests",         "View open requests from fans for specific genres.",              "📜" ),
		new( "Gallery",        "Browse your released games and achievements.",                   "🏆" ),
		new( "Letterbox",      "Read fan letters about your shipped games.",                     "📬" ),
		new( "Leaderboards",   "Compete against other studios on the global boards.",            "🌐" ),
		new( "Settings",       "Adjust audio, controls, and display options.",                   "⚙️" ),
		new( "Save",           "Save your progress to disk.",                                    "💾" ),
	};

	// Index of named tiles, looked up once. Used by the panel to switch
	// between sub-page renderers without hard-coding integers.
	public int IndexOf( string name )
	{
		for ( int i = 0; i < Tiles.Count; i++ )
			if ( Tiles[i].Name == name ) return i;
		return -1;
	}

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Input.Pressed( "Score" ) ) return;

		// First-launch tutorial: while ANY tutorial modal is up (Welcome,
		// CreateAndBin, Complete), swallow the Tab press so the menu doesn't
		// open BEHIND the modal. Once the player dismisses each step, Tab
		// works normally (with phase-appropriate tiles greyed out).
		if ( TutorialManager.Instance is { IsAnyModalVisible: true } ) return;

		// Tab is the universal "back to gameplay" key. If any sibling modal
		// is already up, close that one instead of popping the GameMenu
		// open on top of it. Without this guard, pressing Tab inside any
		// modal flow stacks GameMenu over it and the player ends up with
		// two UIs visible.
		//
		// Sub-modals are checked BEFORE their parent modals so a Tab press
		// while drilled into a sub-view (HR Stats card, HR interview card,
		// inventory desk-assign picker, Letterbox letter reader) closes
		// BOTH the sub-modal AND its parent in one tap — matching the
		// "back to gameplay" intent rather than forcing two presses to
		// escape a deep flow.

		// ── Sub-modals (close sub + parent in one tap) ───────────────────
		if ( Letterbox.Instance is { IsReadingLetter: true } )
		{
			Letterbox.Instance.StopReading();
			Letterbox.Instance.SetOpen( false );
			return;
		}
		if ( HRManager.Instance is { IsViewingStaff: true } )
		{
			HRManager.Instance.CloseWorkerView();
			HR.Instance?.SetOpen( false );
			return;
		}
		if ( HRManager.Instance is { IsInterviewing: true } )
		{
			// PassOnInterviewSubject refuses the tutorial hire (returns
			// silently with a "must hire them" toast); the tutorial
			// IsAnyModalVisible guard above already short-circuits Tab
			// during the tutorial flow, so this is a clean drop in
			// post-tutorial state.
			HRManager.Instance.PassOnInterviewSubject();
			HR.Instance?.SetOpen( false );
			return;
		}
		if ( InventoryManager.Instance is { IsAssigning: true } )
		{
			InventoryManager.Instance.CancelAssign();
			// Picker can be triggered without the inventory modal open
			// (Workstations also pops it), so guard the close.
			if ( InventoryManager.Instance.IsOpen )
				InventoryManager.Instance.SetOpen( false );
			return;
		}

		// ── Top-level modals ─────────────────────────────────────────────
		if ( Shop.Instance is { IsOpen: true } )               { Shop.Instance.SetOpen( false );               return; }
		if ( Settings.Instance is { IsOpen: true } )           { Settings.Instance.SetOpen( false );           return; }
		if ( Gallery.Instance is { IsOpen: true } )            { Gallery.Instance.SetOpen( false );            return; }
		if ( TrainingManager.Instance is { IsOpen: true } )    { TrainingManager.Instance.SetOpen( false );    return; }
		if ( InventoryManager.Instance is { IsOpen: true } )   { InventoryManager.Instance.SetOpen( false );   return; }
		if ( GameProjectManager.Instance is { IsOpen: true } ) { GameProjectManager.Instance.SetOpen( false ); return; }
		if ( Workstations.Instance is { IsOpen: true } )       { Workstations.Instance.SetOpen( false );       return; }
		if ( HR.Instance is { IsOpen: true } )                 { HR.Instance.SetOpen( false );                 return; }
		if ( Leaderboards.Instance is { IsOpen: true } )       { Leaderboards.Instance.SetOpen( false );       return; }
		if ( SaveMenu.Instance is { IsOpen: true } )           { SaveMenu.Instance.SetOpen( false );           return; }
		if ( Letterbox.Instance is { IsOpen: true } )          { Letterbox.Instance.SetOpen( false );          return; }
		if ( Letterbox.Instance is { IsQuestOpen: true } )     { Letterbox.Instance.SetQuestOpen( false );     return; }

		Toggle();
	}

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( !open ) HoveredIndex = -1;
		if ( open ) Shop.Instance?.SetOpen( false );
		// Tab also closes any open chat — the menu and the chat panel both
		// own the bottom-of-screen real estate, so they shouldn't co-exist.
		if ( open ) EmployeeInteractor.Instance?.CloseChat();
		GameManager.RefreshPlayerLock();
	}

	/// Click a tile. By default this just selects it — the menu stays open and
	/// the bottom area renders the tile's sub-page. The Shop tile is special:
	/// it also opens the dedicated fullscreen Shop panel.
	public void Activate( int index )
	{
		if ( index < 0 || index >= Tiles.Count ) return;

		var tile = Tiles[index];

		// Tutorial gate: while the first-launch tutorial is gating menu
		// access (HireFirst → only HR; CreateAndBin → HR + Shop + Create
		// Game + Settings + Save), tiles outside the allowed set silently
		// no-op on click. The matching .disabled visual treatment is
		// applied in GameMenuPanel.
		if ( TutorialManager.Instance is { IsMenuLocked: true }
			&& !TutorialManager.Instance.IsTileEnabled( tile.Name ) )
		{
			return;
		}

		SFX.PlayClick();

		// Every tile owns a dedicated full-screen modal as of ADR-0003
		// Phase 1. Each branch closes this menu and pops the appropriate
		// singleton; no inline content remains.
		if ( tile.Name == "Shop" )
		{
			SetOpen( false );
			Shop.Instance?.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Settings" )
		{
			// Settings has its own full-screen panel — close the in-menu UI
			// and pop the dedicated panel instead.
			SetOpen( false );
			Settings.Instance?.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Gallery" )
		{
			// Gallery (unlocks / past games / trophies) has its own popup,
			// modeled on Shop / Settings / Create-Game. Surface a warning if
			// the singleton component hasn't been placed in the scene yet —
			// otherwise the click silently does nothing and feels broken.
			if ( Gallery.Instance is null )
			{
				Log.Warning( "[Menu] Gallery clicked but Gallery component " +
				             "is missing from the scene. Add the Gallery " +
				             "component to a scene GameObject." );
				Notifications.Push( "Setup Needed",
					"Gallery component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			Gallery.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Create Game" )
		{
			// Create Game opens a dedicated multi-page modal (title / genres,
			// employee allocation, time allocation). The menu closes so the
			// modal owns the screen.
			if ( GameProjectManager.Instance is null )
			{
				Log.Warning( "[Menu] Create Game clicked but GameProjectManager " +
				             "is missing from the scene. Add the GameProjectManager " +
				             "component to a scene GameObject." );
				Notifications.Push( "Setup Needed",
					"GameProjectManager component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			GameProjectManager.Instance.StartNewProject();
			return;
		}
		else if ( tile.Name == "Training" )
		{
			// Training has its own full-screen modal (Individual / Group /
			// Research). Open it and close the menu so the modal owns the
			// screen — same pattern as Shop / Settings / Gallery.
			if ( TrainingManager.Instance is null )
			{
				Log.Warning( "[Menu] Training clicked but TrainingManager " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"TrainingManager component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			TrainingManager.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Inventory" )
		{
			// Inventory replaces the old "Edit Room" tile and gets its own
			// full-screen modal. Two tabs: Furniture (everything physical)
			// and Consumable (give-to-employee buff items).
			if ( InventoryManager.Instance is null )
			{
				Log.Warning( "[Menu] Inventory clicked but InventoryManager " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"InventoryManager component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			InventoryManager.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Human Resource" )
		{
			// HR moved out of the inline GameMenu sub-page into its own
			// fullscreen modal (Code/HR.cs + HRPanel) so the four sub-tabs
			// (Hire / Fire / Stats / Posting) can breathe. Same hand-off
			// pattern as Shop / Inventory / Training.
			if ( HR.Instance is null )
			{
				Log.Warning( "[Menu] Human Resource clicked but HR component " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"HR component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			HR.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Leaderboards" )
		{
			// Leaderboards modal — see ADR-0002. Same hand-off pattern as
			// Shop / HR / Inventory.
			if ( Leaderboards.Instance is null )
			{
				Log.Warning( "[Menu] Leaderboards clicked but Leaderboards " +
				             "component is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"Leaderboards component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			Leaderboards.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Letterbox" )
		{
			// Letterbox modal — see ADR-0003 Phase 2. Same hand-off pattern
			// as Shop / HR / Inventory / Leaderboards.
			if ( Letterbox.Instance is null )
			{
				Log.Warning( "[Menu] Letterbox clicked but Letterbox component " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"Letterbox component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			Letterbox.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Quests" )
		{
			// Quests dashboard — see ADR-0003 Phase 3. Reads open + fulfilled
			// quest letters from Letterbox.Instance, no separate singleton.
			if ( Letterbox.Instance is null )
			{
				Log.Warning( "[Menu] Quests clicked but Letterbox component " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"Letterbox component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			Letterbox.Instance.SetQuestOpen( true );
			return;
		}
		else if ( tile.Name == "Save" )
		{
			// Save / Load modal — extracted from the inline content area in
			// ADR-0003 Phase 1 so every menu tile now opens a fullscreen UI.
			if ( SaveMenu.Instance is null )
			{
				Log.Warning( "[Menu] Save clicked but SaveMenu component " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"SaveMenu component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			SaveMenu.Instance.SetOpen( true );
			return;
		}
		else if ( tile.Name == "Edit Desks" )
		{
			// Edit Desks: per-desk equipment management hub. Replaced
			// the unimplemented top-level Stats placeholder — Gallery
			// already covers studio performance / shipped games / unlocks.
			if ( Workstations.Instance is null )
			{
				Log.Warning( "[Menu] Edit Desks clicked but Workstations " +
				             "is missing from the scene." );
				Notifications.Push( "Setup Needed",
					"Workstations component is missing from the scene.",
					"warning" );
				return;
			}
			SetOpen( false );
			Workstations.Instance.SetOpen( true );
			return;
		}

		// Every tile now routes to a dedicated fullscreen modal above.
		// Reaching this point means a tile name was added without a
		// matching Activate branch — log and no-op.
		Log.Warning( $"[Menu] No Activate branch for tile '{tile.Name}'." );
	}

	public void SetHRSubview( HRSubview s )
	{
		ActiveHRSubview = s;
	}
}

public record MenuTile( string Name, string Description, string Icon = "" );

/// Inner page of the HR section.
public enum HRSubview
{
	None,
	Hire,
	Fire,
	Stats,
	Posting,
}

