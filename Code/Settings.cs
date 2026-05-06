/// <summary>
/// Studio-wide preferences (audio, controls, display, game speed).
///
/// Lives as a singleton Component (mirrors <see cref="Shop"/>) so the
/// Settings panel can pop up from anywhere without scene plumbing. Each
/// setter applies the side-effect immediately — volume changes hit
/// <see cref="Sandbox.Audio.Mixer"/>, sensitivity writes to the active
/// <see cref="Sandbox.PlayerController"/>, etc.
/// </summary>
public sealed class Settings : Component
{
	public static Settings Instance { get; private set; }

	public bool IsOpen { get; private set; }

	// ── Audio (0–1) ──────────────────────────────────────────────────────────

	[Property] public float MasterVolume { get; set; } = 1.00f;
	[Property] public float MusicVolume  { get; set; } = 0.70f;
	[Property] public float SfxVolume    { get; set; } = 0.85f;

	// ── Controls ─────────────────────────────────────────────────────────────

	[Property] public float MouseSensitivity { get; set; } = 1.0f;
	[Property] public bool  InvertY          { get; set; } = false;

	// ── Display ──────────────────────────────────────────────────────────────

	[Property] public int FieldOfView { get; set; } = 90;

	// ── Ranges / steps (used by the panel's +/- buttons) ────────────────────

	public const float MinSensitivity  = 0.25f;
	public const float MaxSensitivity  = 3.00f;
	public const float SensitivityStep = 0.25f;

	public const int   MinFov  = 60;
	public const int   MaxFov  = 110;
	public const int   FovStep = 5;

	// ── Lifecycle ────────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnStart()
	{
		// Push the editor-set defaults into the engine on scene start.
		ApplyAll();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	// ── Window ───────────────────────────────────────────────────────────────

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		// Closing the in-game menu (and the shop) keeps modal stacking simple.
		if ( open )
		{
			GameMenu.Instance?.SetOpen( false );
			Shop.Instance?.SetOpen( false );
		}
		GameManager.RefreshPlayerLock();
	}

	// ── Apply to engine ──────────────────────────────────────────────────────

	public void ApplyAll()
	{
		ApplyAudio();
		ApplySensitivity();
		// FOV is read directly by whatever camera component cares; nothing to push.
	}

	void ApplyAudio()
	{
		// Mixer is a struct (no IValid), and writing Volume on a sub-mixer
		// that doesn't exist throws — so each write is wrapped individually
		// to keep one missing mixer from killing the others.
		TryWriteMixer( "Master", Sandbox.Audio.Mixer.Master, MasterVolume );

		var music = Sandbox.Audio.Mixer.FindMixerByName( "Music" );
		TryWriteMixer( "Music", music, MusicVolume );

		var sfx = Sandbox.Audio.Mixer.FindMixerByName( "SFX" );
		TryWriteMixer( "SFX", sfx, SfxVolume );
	}

	static void TryWriteMixer( string label, Sandbox.Audio.Mixer mixer, float volume )
	{
		try
		{
			mixer.Volume = Math.Clamp( volume, 0f, 1f );
		}
		catch ( System.NullReferenceException )
		{
			// Mixer wasn't found in the project's audio asset (e.g. no
			// "Music" / "SFX" sub-mixers configured). Silently skip — the
			// slider still moves, it just has nothing to drive.
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Settings] Mixer '{label}' write failed: {ex.Message}" );
		}
	}

	void ApplySensitivity()
	{
		var player = Scene.GetAllComponents<Sandbox.PlayerController>().FirstOrDefault();
		if ( player is not null )
			player.LookSensitivity = MouseSensitivity;
	}

	// ── Setters (the panel's click handlers call these) ─────────────────────

	public void SetMasterVolume( float v )
	{
		MasterVolume = Math.Clamp( v, 0f, 1f );
		ApplyAudio();
	}

	public void SetMusicVolume( float v )
	{
		MusicVolume = Math.Clamp( v, 0f, 1f );
		ApplyAudio();
	}

	public void SetSfxVolume( float v )
	{
		SfxVolume = Math.Clamp( v, 0f, 1f );
		ApplyAudio();
	}

	public void IncSensitivity() => SetSensitivity( MouseSensitivity + SensitivityStep );
	public void DecSensitivity() => SetSensitivity( MouseSensitivity - SensitivityStep );

	public void SetSensitivity( float v )
	{
		// Snap to the step grid so the displayed number stays clean.
		var clamped = Math.Clamp( v, MinSensitivity, MaxSensitivity );
		MouseSensitivity = MathF.Round( clamped / SensitivityStep ) * SensitivityStep;
		ApplySensitivity();
	}

	public void ToggleInvertY() => InvertY = !InvertY;

	public void IncFov() => SetFov( FieldOfView + FovStep );
	public void DecFov() => SetFov( FieldOfView - FovStep );

	public void SetFov( int v )
	{
		FieldOfView = Math.Clamp( v, MinFov, MaxFov );
	}

	// ── Save / Load (per ADR-0001) ────────────────────────────────────────

	public SettingsSave Save() => new()
	{
		MasterVolume     = MasterVolume,
		MusicVolume      = MusicVolume,
		SfxVolume        = SfxVolume,
		MouseSensitivity = MouseSensitivity,
		InvertY          = InvertY,
		FieldOfView      = FieldOfView,
	};

	public void Load( SettingsSave dto )
	{
		if ( dto is null ) return;
		MasterVolume     = Math.Clamp( dto.MasterVolume,     0f, 1f );
		MusicVolume      = Math.Clamp( dto.MusicVolume,      0f, 1f );
		SfxVolume        = Math.Clamp( dto.SfxVolume,        0f, 1f );
		MouseSensitivity = Math.Clamp( dto.MouseSensitivity, MinSensitivity, MaxSensitivity );
		InvertY          = dto.InvertY;
		FieldOfView      = Math.Clamp( dto.FieldOfView,      MinFov, MaxFov );
		ApplyAll();
	}
}
