/// <summary>
/// Central sound-effect placeholder. Add this Component to a scene root
/// GameObject (e.g. the same one that holds GameManager), drag a
/// <c>SoundEvent</c> resource into each <c>[Property]</c> slot via the
/// inspector, and the engine will play that sound at the wired-up moments.
/// Slots left empty silently no-op — useful while the audio pipeline is
/// still being filled in.
///
/// **Auto-wired call sites** (already firing — no extra work needed):
/// - <see cref="PlayWarning"/>: every "warning"-kind toast in
///   <c>Notifications.Show</c>.
/// - <see cref="PlayPurchase"/>: successful inventory buys via
///   <c>InventoryManager.NotifyBuySuccess</c> + the desk-bound branch.
/// - <see cref="PlayClick"/>: every menu tile activation in
///   <c>GameMenu.Activate</c>.
///
/// **To extend a click sound to more buttons**: in any <c>.razor</c>
/// onclick handler, add <c>SFX.PlayClick();</c> at the top of the method.
/// Example:
/// <code>
/// private void DoThing()
/// {
///     SFX.PlayClick();
///     // ... existing logic
/// }
/// </code>
///
/// If the engine's <c>Sound.Play(SoundEvent)</c> API differs on the
/// pinned s&amp;box build, only this one file needs adjustment.
/// </summary>
public sealed class SFX : Component
{
	public static SFX Instance { get; private set; }

	[Property, Description( "Plays on UI button clicks. Auto-fires on GameMenu tile clicks; sprinkle SFX.PlayClick() in other onclick handlers as needed." )]
	public SoundEvent UIClick { get; set; }

	[Property, Description( "Plays when a warning (red/attention) notification appears. Triggered automatically from Notifications.Show." )]
	public SoundEvent Warning { get; set; }

	[Property, Description( "Plays on successful item purchase. Triggered automatically from InventoryManager.NotifyBuySuccess + the desk-bound buy branch." )]
	public SoundEvent Purchase { get; set; }

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	// ── Static accessors ─────────────────────────────────────────────────
	// Each is null-safe at every step: SFX Component might not be in the
	// scene yet, the SoundEvent slot might be unassigned. Both cases no-op.
	// Volume is scaled at play time by MasterVolume × SfxVolume from
	// Settings, since the .sound files don't all route through the "SFX"
	// mixer in the project's mixer graph.

	public static void PlayClick()    { var s = Instance?.UIClick;  if ( s is not null ) PlayScaled( s ); }
	public static void PlayWarning()  { var s = Instance?.Warning;  if ( s is not null ) PlayScaled( s ); }
	public static void PlayPurchase() { var s = Instance?.Purchase; if ( s is not null ) PlayScaled( s ); }

	static void PlayScaled( SoundEvent snd )
	{
		var h = Sound.Play( snd );
		if ( h is null ) return;
		var st = Settings.Instance;
		if ( st is null ) return;
		h.Volume = st.MasterVolume * st.SfxVolume;
	}
}
