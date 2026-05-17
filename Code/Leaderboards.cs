using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Fullscreen Leaderboards modal + the static submission / fetch API
/// that backs it. Implements ADR-0002.
///
/// Two surfaces in this class:
///   • The <see cref="Component"/> half (instance) — owns the open/close
///     state for the panel, mirrors the HR / Shop / Settings pattern.
///   • The static half — the platform-services bridge. Stat constants,
///     <see cref="SubmitOnShip"/>, <see cref="SubmitOnSave"/>, and
///     <see cref="FetchAsync"/>.
/// </summary>
public sealed class Leaderboards : Component
{
	// ── Component half (modal state) ───────────────────────────────────────

	public static Leaderboards Instance { get; private set; }

	public bool IsOpen { get; private set; }

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open ) Modals.CloseAllExcept( this );
		GameManager.RefreshPlayerLock();
	}

	// ── Static half (platform bridge) ──────────────────────────────────────

	/// Project ident used by <c>Sandbox.Services.Leaderboards.GetFromStat</c>.
	/// Must match the .sbproj's <c>"Org"</c> + <c>"."</c> + <c>"Ident"</c>.
	/// Update both this constant and the .sbproj if either changes.
	public const string GameIdent = "dellort.game2";

	/// Master kill switch. Set to <c>false</c> to silence every platform
	/// call (submissions and fetches). Mirrors the bridge pattern in
	/// <c>Achievements.BridgeToPlatform</c>. <c>static readonly</c> rather
	/// than <c>const</c> so the early-return branches aren't dead code at
	/// compile time (CS0162). Named <c>SubmitEnabled</c> rather than
	/// <c>Enabled</c> because the latter would shadow
	/// <c>Component.Enabled</c> (CS0108).
	public static readonly bool SubmitEnabled = true;

	// Stat names — keep in sync with the table in ADR-0002 §"Stats schema".
	public const string StatBestGameRevenue = "best_game_revenue";
	public const string StatBestGameScore   = "best_game_score";
	public const string StatLifetimeEarn    = "lifetime_earnings";
	public const string StatPeakBalance     = "peak_balance";

	/// Display order + labels for the four boards. The panel iterates this
	/// to build its tab strip, so the source-of-truth lives here next to
	/// the stat constants.
	public static readonly LeaderboardDef[] All = new[]
	{
		new LeaderboardDef( StatBestGameRevenue, "Top Game Sales",     "Best single game's lifetime sales", "💰", IsCurrency: true  ),
		new LeaderboardDef( StatBestGameScore,   "Top Game Score",     "Best single game's pillar score",   "🎯", IsCurrency: false ),
		new LeaderboardDef( StatLifetimeEarn,    "Top Run Earnings",   "Best lifetime earnings in any one run", "📈", IsCurrency: true  ),
		new LeaderboardDef( StatPeakBalance,     "Top Wallet",         "Highest balance ever held",         "🏦", IsCurrency: true  ),
	};

	/// Submit the four ship-time stats. Called from
	/// <see cref="Gallery.RecordShippedGame"/> right after
	/// <see cref="Achievements.RecordShippedGame"/>.
	public static void SubmitOnShip( ShippedGame game )
	{
		if ( !SubmitEnabled || game is null ) return;

		long score = game.DesignPoints + game.SoundPoints + game.GraphicsPoints;
		// game.TotalSales is set during ComputeScoreAndMetrics() at ship
		// time — it represents the lifetime target, which is the value the
		// release window will eventually pay out. Submit it now (rather
		// than RevenueEarned, which is still 0 at ship time and only ramps
		// up over the in-game release window).
		TrySetValue( StatBestGameRevenue, game.TotalSales );
		TrySetValue( StatBestGameScore,   score );
		TrySetValue( StatLifetimeEarn,    Achievements.LifetimeEarned );
		TrySetValue( StatPeakBalance,     Achievements.PeakBalance );
	}

	/// Submit the two stats that drift between ships. Called from
	/// <see cref="GameSaveManager.TrySaveRun"/> on a successful write.
	public static void SubmitOnSave()
	{
		if ( !SubmitEnabled ) return;
		TrySetValue( StatLifetimeEarn, Achievements.LifetimeEarned );
		TrySetValue( StatPeakBalance,  Achievements.PeakBalance );
	}

	/// Fetch the top <paramref name="max"/> entries for a stat. Returns
	/// <c>null</c> on platform failure (editor session, network down,
	/// service-side error) — caller renders an empty / unavailable state.
	public static async Task<List<LeaderboardRow>> FetchAsync( string statName, int max = 50 )
	{
		if ( !SubmitEnabled || string.IsNullOrEmpty( statName ) ) return null;

		try
		{
			var board = Sandbox.Services.Leaderboards.GetFromStat( GameIdent, statName );
			board.MaxEntries = max;
			board.SetAggregationMax();   // every leaderboard in ADR-0002 uses Max
			// Default sort is descending — explicit for clarity.

			await board.Refresh();

			// Plain null-check + foreach — avoids `Entries?.Count` (which
			// trips CS8978 if Count is an extension method on
			// IEnumerable<Entry>) and `Entries is null` (which can't
			// distinguish "no data" from "service failure").
			var rows = new List<LeaderboardRow>();
			var entries = board.Entries;
			if ( entries == null ) return rows;

			foreach ( var e in entries )
			{
				rows.Add( new LeaderboardRow(
					// Cast Rank to int defensively — the platform's Entry.Rank
					// type is documented loosely; if it's actually long, an
					// implicit conversion would be CS1503.
					Rank:        (int)e.Rank,
					DisplayName: e.DisplayName ?? "",
					Value:       (double)e.Value,
					CountryCode: e.CountryCode ?? "" ) );
			}
			return rows;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Leaderboards] fetch '{statName}' failed: {ex.Message}" );
			return null;
		}
	}

	static void TrySetValue( string statName, double value )
	{
		try
		{
			Sandbox.Services.Stats.SetValue( statName, value );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Leaderboards] SetValue '{statName}' failed: {ex.Message}" );
		}
	}
}

/// One leaderboard's display metadata. Source of truth for tab labels
/// and value formatting.
public sealed record LeaderboardDef(
	string StatName,
	string Title,
	string Description,
	string Icon,
	bool   IsCurrency );

/// One row from a leaderboard query. Engine-agnostic: the panel UI
/// reads this; we don't expose <c>Sandbox.Services.Leaderboards.Entry</c>
/// directly so the call site can be smoke-tested independent of a
/// published build.
public sealed record LeaderboardRow(
	int    Rank,
	string DisplayName,
	double Value,
	string CountryCode );
