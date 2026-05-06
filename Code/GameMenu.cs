public sealed class GameMenu : Component
{
	public static GameMenu Instance { get; private set; }

	public bool IsOpen        { get; private set; }
	public int  HoveredIndex  { get; set; } = -1;

	/// Index of the tile whose sub-page is currently shown.
	/// -1 = no tile selected (description-on-hover mode).
	public int ActiveSection { get; set; } = -1;

	/// Inner sub-tab for the HR section.
	public HRSubview ActiveHRSubview { get; set; } = HRSubview.None;

	public IReadOnlyList<MenuTile> Tiles { get; } = new MenuTile[]
	{
		new( "Create Game",    "Start developing a new game project and pick a genre.",          "🎮" ),
		new( "Human Resource", "Hire, manage, and fire employees.",                              "👥" ),
		new( "Training",       "Improve your team's skills and specialties.",                    "📚" ),
		new( "Inventory",      "Manage office furniture and consumables.",                       "📦" ),
		new( "Shop",           "Buy equipment, decorations, and upgrades for your studio.",      "🛒" ),
		new( "Edit Desks",     "Edit chair, computer, and monitor at every desk.",               "🖥️" ),
		new( "Settings",       "Adjust audio, controls, and display options.",                   "⚙️" ),
		new( "Gallery",        "Browse your released games and achievements.",                   "🏆" ),
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

		// Tab is the universal "back to gameplay" key. If any sibling modal is
		// already up (Create-Game, Shop, Settings, Gallery), close that one
		// instead of popping the GameMenu open on top of it. Without this
		// guard, pressing Tab inside the Create-Game flow stacks GameMenu
		// over the modal and the player ends up with two UIs visible.
		if ( Shop.Instance is { IsOpen: true } )               { Shop.Instance.SetOpen( false );               return; }
		if ( Settings.Instance is { IsOpen: true } )           { Settings.Instance.SetOpen( false );           return; }
		if ( Gallery.Instance is { IsOpen: true } )            { Gallery.Instance.SetOpen( false );            return; }
		if ( TrainingManager.Instance is { IsOpen: true } )    { TrainingManager.Instance.SetOpen( false );    return; }
		if ( InventoryManager.Instance is { IsOpen: true } )   { InventoryManager.Instance.SetOpen( false );   return; }
		if ( GameProjectManager.Instance is { IsOpen: true } ) { GameProjectManager.Instance.SetOpen( false ); return; }
		if ( Workstations.Instance is { IsOpen: true } )       { Workstations.Instance.SetOpen( false );       return; }

		Toggle();
	}

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( !open ) HoveredIndex = -1;
		if ( open ) Shop.Instance?.SetOpen( false );
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

		// Tiles that own a dedicated full-screen modal: route to the modal
		// and DON'T touch ActiveSection. Without that, ActiveSection sticks
		// on the modal-tile after the menu closes and reopens — so the next
		// time the player opens HR, they land on a "Coming soon" placeholder
		// for the last modal tile they clicked. Only Save / Stats / HR set
		// ActiveSection (they render in-menu).
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

		// In-menu tile (HR / Save / Stats) — render its sub-page inline.
		ActiveSection   = index;
		ActiveHRSubview = HRSubview.None;
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

