/// <summary>
/// Background music placeholder. Add this Component to a scene root
/// GameObject, assign a <c>SoundEvent</c> resource to the <c>Track</c>
/// slot, and it will play continuously, restarting when the track ends.
///
/// **Looping** is done in code (this file). s&amp;box 1.0's SoundEvent
/// format has no Loop field, so we poll the handle in OnUpdate and
/// fire <c>Sound.Play</c> again whenever the previous play finishes.
///
/// **Volume / mixer**: set <c>DefaultMixer = "Master"</c> (or whichever
/// mixer your project's mixer graph exposes for music) on the
/// <c>.sound</c> resource. The mixer's runtime volume drives loudness.
/// </summary>
public sealed class Music : Component
{
	public static Music Instance { get; private set; }

	[Property, Description( "Background track. Plays continuously; restarts when finished." )]
	public SoundEvent Track { get; set; }

	SoundHandle _handle;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnUpdate()
	{
		if ( Track is null ) return;

		// Start initially, restart when the previous play finishes.
		if ( _handle is null || _handle.Finished )
		{
			_handle = Sound.Play( Track );
		}

		// Live-scale to the music slider every frame so dragging the
		// slider in Settings takes effect on the currently-playing track.
		if ( _handle is not null )
		{
			_handle.Volume = ResolvedVolume();
		}
	}

	protected override void OnDestroy()
	{
		_handle?.Stop();
		if ( Instance == this ) Instance = null;
	}

	static float ResolvedVolume()
	{
		var s = Settings.Instance;
		if ( s is null ) return 1f;
		return s.MasterVolume * s.MusicVolume;
	}
}
