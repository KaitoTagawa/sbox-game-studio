using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Fan-letter system. Subscribes to <see cref="GameManager.OnDayStart"/>;
/// 7 in-game days after each shipped game lands, spawns one letter
/// referencing the game. The first letter ever received pushes a
/// sticky notification (per-run flag <see cref="LetterboxSave.FirstLetterSeen"/>);
/// subsequent letters use a normal-duration toast.
///
/// Quest letters (genre requests) and the +20% sales hook are stubbed
/// here and activated in ADR-0003 Phase 3.
///
/// See <c>docs/architecture/adr-0003-letterbox-quests.md</c> for the
/// full design.
/// </summary>
public sealed class Letterbox : Component
{
	public static Letterbox Instance { get; private set; }

	// ── Modal state ────────────────────────────────────────────────────────

	public bool IsOpen      { get; private set; }
	public bool IsQuestOpen { get; private set; }

	/// Letter currently shown in the LetterReaderPanel sub-modal. Non-null
	/// means the reader is open. Set via <see cref="StartReading"/>;
	/// cleared via <see cref="StopReading"/>. Mirrors the
	/// <c>HRManager.InterviewSubject</c> pattern.
	public FanLetter CurrentlyReading { get; private set; }
	public bool      IsReadingLetter => CurrentlyReading is not null;

	// ── Letter store ───────────────────────────────────────────────────────

	readonly List<FanLetter> _letters = new();
	readonly HashSet<string> _decidedFor = new();
	bool _firstLetterSeen;

	// Motivation buff state (per-run, applied studio-wide). Reading a
	// non-quest letter grants +14 in-game days at +2% Good-mood chance.
	int _motivationActiveUntilDay;
	int _motivationQueuedDays;

	// Press-release letters waiting on their target in-game day. Each
	// entry corresponds to a ship that fulfilled 2+ quests at once.
	readonly List<PressLetterPending> _pendingPress = new();

	// Synergy-hint discovery: every unordered positive-synergy genre pair
	// the player has shipped at least once gets recorded here so the
	// "first time" hint letter is a one-shot. Key format is canonical
	// "GenreA|GenreB" with the alphabetically earlier enum name first
	// (see PairKey). Persisted in LetterboxSave.DiscoveredPairs.
	readonly HashSet<string> _discoveredPairs = new();

	// Non-persisted "did we backfill from Gallery yet" flag. Backfill
	// runs once per process startup (in Tick) to populate
	// _discoveredPairs from already-shipped games — covers legacy saves
	// and hot-reload, where the in-memory set would otherwise be empty.
	// HashSet.Add is idempotent so re-running is harmless.
	bool _discoveryBackfillDone;

	public IReadOnlyList<FanLetter> Letters => _letters;

	/// True while the studio-wide fan-letter motivation buff is active.
	/// Drives the +2% goodChance bonus in <see cref="EmployeeNPC.TickMood"/>.
	public bool IsMotivated => CurrentDayAbs() < _motivationActiveUntilDay;

	/// In-game day index when the active motivation buff ends. 0 when
	/// no buff is active. Surfaced for an optional UI indicator.
	public int  MotivationActiveUntilDay => _motivationActiveUntilDay;
	public int  MotivationQueuedDays     => _motivationQueuedDays;

	/// Letters with <see cref="FanLetter.IsQuest"/> set and not yet
	/// fulfilled. Read by QuestsPanel (Phase 3).
	public IEnumerable<FanLetter> OpenQuests
		=> _letters.Where( l => l.IsQuest && !l.QuestFulfilled );

	public int  UnreadCount   => _letters.Count( l => !l.Read );
	public bool HasOpenQuests => _letters.Any( l => l.IsQuest && !l.QuestFulfilled );

	// ── Lifecycle ─────────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		Instance = this;
		// 7-days-since-ship calculation runs on each in-game day tick —
		// fire-and-forget per game, idempotency guarded inside Tick().
		GameManager.OnDayStart += Tick;
	}

	protected override void OnDestroy()
	{
		GameManager.OnDayStart -= Tick;
		if ( Instance == this ) Instance = null;
	}

	// ── Modal open/close ──────────────────────────────────────────────────

	public void Toggle() => SetOpen( !IsOpen );

	public void SetOpen( bool open )
	{
		IsOpen = open;
		if ( open )
		{
			Modals.CloseAllExcept( this );
			SetQuestOpen( false );   // siblings — only one of {inbox, quests} at a time
		}
		else
		{
			// Closing the inbox also closes any open reader sub-modal so
			// the player isn't left with a floating reader after the
			// parent goes away.
			CurrentlyReading = null;
		}
		GameManager.RefreshPlayerLock();
	}

	/// Open / close the QuestsPanel sub-modal. Read-only view of the
	/// quest letters' state — open quests on top, fulfilled history below.
	public void SetQuestOpen( bool open )
	{
		IsQuestOpen = open;
		if ( open )
		{
			Modals.CloseAllExcept( this );
			if ( IsOpen ) SetOpen( false );   // sibling — inbox closes when Quests opens
		}
		GameManager.RefreshPlayerLock();
	}

	/// Open the reader sub-modal on a specific letter. Marks it Read +
	/// stashes it as the panel-bound subject so LetterReaderPanel renders.
	/// First read of a non-quest letter grants the studio-wide motivation
	/// buff (+2% Good-mood chance for 14 in-game days). Re-reads no-op.
	public void StartReading( FanLetter letter )
	{
		if ( letter is null ) return;
		bool firstRead = !letter.Read;
		letter.Read      = true;
		CurrentlyReading = letter;

		if ( firstRead )
		{
			// First read of any letter clears the first-letter sticky toast.
			// "Go read your letter" intent — just opening the inbox isn't
			// enough; the player has to actually open one. Idempotent: the
			// flag/tag-clear no-op once already set / cleared.
			_firstLetterSeen = true;
			Notifications.RemoveByTag( FirstLetterTag );
		}

		// Motivation buff fires only on regular fan / press / thank-you
		// letters. Quest letters and synergy-discovery hints are exempt:
		// quests have their own +20% sales bonus on fulfillment, and
		// discovery letters are pure hints with no gameplay effect.
		if ( firstRead && !letter.IsQuest && !letter.IsDiscovery )
			GrantMotivation();

		GameManager.RefreshPlayerLock();
	}

	// ── Motivation buff ──────────────────────────────────────────────────

	/// Length of one motivation period in in-game days (2 in-game weeks).
	const int MotivationDurationDays = 14;

	void GrantMotivation()
	{
		int today = CurrentDayAbs();
		if ( _motivationActiveUntilDay > today )
		{
			// Already motivated — queue another period to start when the
			// active one ends. User-spec: don't stack overlapping periods.
			_motivationQueuedDays += MotivationDurationDays;
			Notifications.Push( "Letter Saved For Later",
				$"The studio's still riding the last fan letter — this one's pinned to lift spirits in {MotivationDurationDays} more days.",
				"info", duration: 6f );
		}
		else
		{
			_motivationActiveUntilDay = today + MotivationDurationDays;
			Notifications.Push( "Team Inspired",
				$"The studio's reading the letter aloud over coffee. {MotivationDurationDays} days of higher spirits ahead.",
				"success", duration: 6f );
		}
	}

	/// Daily check — when the active motivation period elapses and a
	/// queued period is waiting, activate it. Called from <see cref="Tick"/>.
	void TickMotivation()
	{
		int today = CurrentDayAbs();
		if ( today < _motivationActiveUntilDay )       return;
		if ( _motivationQueuedDays <= 0 )              return;

		_motivationActiveUntilDay = today + _motivationQueuedDays;
		_motivationQueuedDays     = 0;

		Notifications.Push( "Spirits Rising Again",
			"That letter you saved is on the bulletin board now. Two more weeks of warmer studio vibes.",
			"success", duration: 6f );
	}

	static int CurrentDayAbs()
	{
		var gm = GameManager.Instance;
		if ( gm is null ) return 0;
		return gm.Year * 360 + (gm.Month - 1) * 30 + gm.Day;
	}

	/// Close the reader sub-modal. The list-modal stays open underneath
	/// so the player can pick another letter.
	public void StopReading()
	{
		CurrentlyReading = null;
		GameManager.RefreshPlayerLock();
	}

	// ── Spawn tick ────────────────────────────────────────────────────────

	void Tick()
	{
		// Active motivation period might have lapsed since last tick —
		// activate any queued period before evaluating spawns.
		TickMotivation();

		var gm  = GameManager.Instance;
		var gal = Gallery.Instance;
		if ( gm is null || gal is null ) return;

		// One-shot per process: walk existing shipped games and mark every
		// pair as already-discovered. Stops legacy saves (and post-hot-
		// reload state) from spamming "you discovered X+Y!" letters for
		// pairs the player has shipped many times before this feature
		// existed. HashSet.Add is idempotent, so re-running is harmless.
		if ( !_discoveryBackfillDone )
		{
			_discoveryBackfillDone = true;
			foreach ( var g in gal.ShippedGames )
				MarkPairsDiscovered( g.Genres );
		}

		int nowAbs = AbsoluteDay( gm.Year, gm.Month, gm.Day );

		// One Random instance per Tick covers every roll this tick.
		// `new Random()` per-iteration would seed from a low-resolution
		// system clock on some runtimes — if multiple ships hit day-7 on
		// the same tick they'd get identical seeds and all roll together
		// (bursty all-or-nothing instead of 35% each).
		var rng = new Random();

		// Pending press letters whose target day has elapsed land now.
		// Iterate backwards so RemoveAt during the walk is safe.
		for ( int i = _pendingPress.Count - 1; i >= 0; i-- )
		{
			var pending = _pendingPress[i];
			if ( pending.TargetDayAbs > nowAbs ) continue;
			SpawnPressLetter( pending, rng );
			_pendingPress.RemoveAt( i );
		}

		foreach ( var game in gal.ShippedGames )
		{
			// Once a game has been evaluated (spawn or skip) we're done
			// with it forever — the 35% roll happens exactly once per ship.
			if ( _decidedFor.Contains( game.Title ) ) continue;

			int shipAbs   = AbsoluteDay( game.ShipYear, game.ShipMonth, game.ShipDay );
			int daysSince = nowAbs - shipAbs;
			if ( daysSince < LetterDelayDays ) continue;

			_decidedFor.Add( game.Title );

			// Synergy-hint discovery scan — independent of the 35% fan-letter
			// roll. Spawns ONE discovery letter per never-before-seen
			// positive-synergy pair on this ship. A 3-genre game with two
			// good pairs spawns two discovery letters; a clash pair is
			// silent (no "this combo is bad" letter — design choice).
			SpawnDiscoveryLettersFor( game, rng );

			if ( DebugForceTwoLetters )
			{
				// Test mode: every game produces both letter types. Quest
				// first so it lands above the special letter in the inbox
				// (newest-first list ordering).
				SpawnLetter( game, rng, forceQuest: true  );
				SpawnLetter( game, rng, forceQuest: false );
				continue;
			}

			// 35% of ships produce a letter at all.
			if ( rng.NextDouble() >= LetterSpawnChance ) continue;
			SpawnLetter( game, rng );
		}
	}

	/// Canonical key for an unordered pair — alphabetically earlier enum
	/// name first, joined with '|'. Matches what's stored in
	/// <see cref="LetterboxSave.DiscoveredPairs"/>.
	static string PairKey( GameGenre a, GameGenre b )
	{
		var na = a.ToString();
		var nb = b.ToString();
		return string.CompareOrdinal( na, nb ) <= 0
			? $"{na}|{nb}"
			: $"{nb}|{na}";
	}

	/// Add every unordered pair from <paramref name="genres"/> to
	/// <see cref="_discoveredPairs"/>. Used by the first-Tick backfill
	/// and by <see cref="SpawnDiscoveryLettersFor"/>'s "mark as seen
	/// even if synergy is non-positive" sweep.
	void MarkPairsDiscovered( IReadOnlyList<GameGenre> genres )
	{
		if ( genres is null || genres.Count < 2 ) return;
		for ( int i = 0; i < genres.Count; i++ )
			for ( int j = i + 1; j < genres.Count; j++ )
				_discoveredPairs.Add( PairKey( genres[i], genres[j] ) );
	}

	/// Walk every unordered pair in the shipped game's genres. For each
	/// never-seen pair with strictly positive <see cref="GameGenres.Synergy"/>,
	/// spawn a tiered discovery letter (good / very good / very very good).
	/// Clash pairs and zero-synergy pairs still get added to the
	/// discovered set so re-shipping the same combo later doesn't fire
	/// "first time" hints — they just don't have a letter to spawn.
	void SpawnDiscoveryLettersFor( ShippedGame game, Random rng )
	{
		var genres = game.Genres;
		if ( genres is null || genres.Count < 2 ) return;

		for ( int i = 0; i < genres.Count; i++ )
		{
			for ( int j = i + 1; j < genres.Count; j++ )
			{
				var a = genres[i];
				var b = genres[j];
				var key = PairKey( a, b );
				if ( !_discoveredPairs.Add( key ) ) continue;   // already seen

				float syn = GameGenres.Synergy( a, b );
				if ( syn <= 0f ) continue;                       // clash / no fit — silent

				SpawnDiscoveryLetter( game, a, b, syn, rng );
			}
		}
	}

	/// Days after a game's ship date when the letter (if any) lands.
	const int    LetterDelayDays = 7;

	/// Probability that a shipped game produces a letter at all. The
	/// 35% rate stops every ship from auto-mailing — letters feel earned.
	const double LetterSpawnChance = 0.35;

	/// Probability that a spawned letter is a quest (genre request).
	/// Remaining 70% are "special" letters that grant the motivation
	/// buff when read.
	const double QuestChance = 0.30;

	// ── DEBUG / TESTING ─────────────────────────────────────────────────
	// Set to true to force every shipped game to produce BOTH a quest
	// letter AND a nice letter (bypasses the 35% spawn roll and the
	// 30/70 quest split). Useful for exercising both flows end-to-end
	// without grinding through dozens of ships. Flip back to false when
	// done — that's the only revert needed; no save data is touched.
	// static readonly (not const) so the compiler can't constant-fold the
	// branch and emit CS0162 unreachable-code on the path below.
	static readonly bool DebugForceTwoLetters = false;

	/// Tag for the first-letter sticky notification. Cleared when the
	/// player opens the LetterboxPanel.
	const string FirstLetterTag  = "letterbox-first";

	void SpawnLetter( ShippedGame game, Random rng, bool? forceQuest = null )
	{
		var letter = GenerateLetter( game, rng, forceQuest );
		_letters.Add( letter );
		Achievements.RecordLetterReceived();

		// First letter of the run gets a sticky toast — opening the
		// LetterboxPanel is the only way to clear it. Subsequent letters
		// use the normal short-lived ambient toast.
		if ( !_firstLetterSeen )
		{
			Notifications.Push( "You've received a fan letter!",
				"Open the Letterbox tile in the menu to read it.",
				"info", duration: 0f, tag: FirstLetterTag );
		}
		else
		{
			Notifications.Push( "You've received a fan letter!",
				$"From a fan of \"{game.Title}\".",
				"info", duration: 6f );
		}
	}

	// ── Letter generation ────────────────────────────────────────────────

	FanLetter GenerateLetter( ShippedGame game, Random rng, bool? forceQuest = null )
	{
		var primary = game.Genres != null && game.Genres.Count > 0
			? game.Genres[0]
			: GameGenre.Mystery;
		var primaryName = GameGenres.Get( primary )?.Name ?? primary.ToString();

		// 30% of spawned letters are quest letters (genre request).
		// Quest letter pools use a different copy bank that asks for a
		// genre other than the praised game's. Falls back to a normal
		// letter if no alternative genre is available. Caller can pass
		// `forceQuest` to override the random roll (debug + test paths).
		bool isQuest          = forceQuest ?? (rng.NextDouble() < QuestChance);
		GameGenre? questGenre = null;
		if ( isQuest )
		{
			questGenre = PickQuestGenre( rng );
			if ( questGenre is null ) isQuest = false;
		}

		string body;
		if ( isQuest && questGenre is { } qg )
		{
			var requestedName = GameGenres.Get( qg )?.Name ?? qg.ToString();
			body = PickQuestBody( rng )
				.Replace( "{GAME}",            game.Title )
				.Replace( "{REQUESTED_GENRE}", requestedName );
		}
		else
		{
			body = PickBody( rng )
				.Replace( "{GAME}",  game.Title )
				.Replace( "{GENRE}", primaryName );
		}

		// Capitalise the first letter of the body. Templates often start
		// with "{GAME}" — if the player named their studio's game with a
		// lowercase title the letter would otherwise read like "lazy
		// game is one of the best…" with no capital at sentence start.
		if ( body.Length > 0 && char.IsLower( body[0] ) )
			body = char.ToUpper( body[0] ) + body.Substring( 1 );

		return new FanLetter
		{
			Id          = Guid.NewGuid().ToString( "N" ),
			GameTitle   = game.Title,
			GameGenre   = primary,
			ReviewScore = game.ReviewScore,
			SenderName  = SenderNames[rng.Next( SenderNames.Length )],
			Body        = body,
			ReceivedAt  = DateTimeOffset.UtcNow,
			IsQuest     = isQuest,
			QuestGenre  = isQuest ? questGenre : null,
			Read        = false,
		};
	}

	/// Pick a currently-unlocked genre as the quest target. The
	/// requested genre IS allowed to match the praised game's genre
	/// (fans often want more of the same), but it is NOT allowed to
	/// duplicate an already-open quest's genre — two simultaneous
	/// open quests for the same genre would be redundant. Returns
	/// null if every unlocked genre is already requested or the
	/// player has no genres unlocked.
	GameGenre? PickQuestGenre( Random rng )
	{
		// Genres currently locked up by an unfulfilled quest. Excluded
		// so a single ship can fulfill at most one outstanding request
		// per genre and the player never sees "two fans want Horror"
		// in the Quests panel at once.
		var taken = new HashSet<GameGenre>();
		foreach ( var l in _letters )
		{
			if ( l.IsQuest && !l.QuestFulfilled && l.QuestGenre is { } g )
				taken.Add( g );
		}

		var candidates = new List<GameGenre>();
		foreach ( GameGenre g in Enum.GetValues( typeof( GameGenre ) ) )
		{
			if ( !GameGenres.IsUnlocked( g ) ) continue;
			if ( taken.Contains( g ) )         continue;
			candidates.Add( g );
		}
		if ( candidates.Count == 0 ) return null;
		return candidates[rng.Next( candidates.Count )];
	}

	static string PickBody( Random rng )
		=> LetterBodies[rng.Next( LetterBodies.Length )];

	static string PickQuestBody( Random rng )
		=> QuestBodies[rng.Next( QuestBodies.Length )];

	static string PickThankYouBody( Random rng )
		=> ThankYouBodies[rng.Next( ThankYouBodies.Length )];

	static string PickPressBody( Random rng )
		=> PressBodies[rng.Next( PressBodies.Length )];

	/// Pick a discovery-letter body keyed to the synergy magnitude. Returns
	/// the template with placeholders intact; caller substitutes
	/// {GAME} / {A} / {B}. Tier thresholds match the synergy table's
	/// natural breakpoints: +0.10/+0.20 = good, +0.30 = very good,
	/// +0.40+ = very very good.
	static string PickDiscoveryBody( float synergy, Random rng )
	{
		string[] pool;
		if      ( synergy >= 0.40f - 0.001f ) pool = DiscoveryBodiesVeryVeryGood;
		else if ( synergy >= 0.30f - 0.001f ) pool = DiscoveryBodiesVeryGood;
		else                                   pool = DiscoveryBodiesGood;
		return pool[rng.Next( pool.Length )];
	}

	// ── Copy pools (templated; placeholders filled at spawn time) ───────
	// Edit these arrays freely — no scene data, no save data, just text.

	static readonly string[] SenderNames =
	{
		"FrostbyteJr", "GameGirl99", "OldSchoolPlayer", "NeoWanderer",
		"PixelWitch",  "RetroDad",   "8BitHeart",       "QuestSeeker",
		"ConsoleKid",  "PCMasterJoe","Stardust42",      "GoblinSlayer",
		"IndieFan",    "TacoQueen",  "MidnightCoder",   "VHSDream",
		"ArcadeKing",  "Bookworm88", "DrJoystick",      "PuzzleNerd",
	};

	// All letters are positive. Reasoning: writing and mailing a physical
	// letter to a studio is a high-effort act — the kind of fan who does
	// it is overwhelmingly someone the game genuinely moved. Mid-band
	// "okay-ish" feedback and low-band "rough start" feedback don't
	// realistically translate into someone sitting down to write. Reviews
	// (in the Gallery) cover those tonal ranges; letters are reserved
	// for the people who really loved a game. Wrapping is handled by
	// LetterReaderPanel.WrappedBody at render time.

	static readonly string[] LetterBodies =
	{
		"{GAME} is one of the best {GENRE} games I've played all year. The pacing, the polish, the small details — everything clicks. Your team should be proud.",
		"Just wrapped up {GAME} after a marathon weekend. I haven't felt this hooked on a {GENRE} game in years. Whatever you're doing, please keep doing it.",
		"I went into {GAME} skeptical and came out a convert. Already telling everyone I know to play it. More {GENRE} from your studio — take my money.",
		"My partner doesn't game, but {GAME} got them hooked. They asked me to thank you. Highest praise I can give a {GENRE} game.",
		"{GAME} stole my entire weekend and a couple of weeknights. Already on my second playthrough. The amount of love poured into this is obvious.",
		"{GAME} reminded me why I fell in love with {GENRE} games as a kid. That feeling came rushing back the moment the credits rolled. Thank you, sincerely.",
		"Streamed {GAME} all weekend on my channel. Chat hasn't stopped talking about it. Your studio is officially on everyone's radar now.",
		"Been gaming since the 80s — {GAME} ranks right up there with the classics. Don't know how you did it, but every choice in this game lands.",
		"Bought {GAME} for my brother as a birthday gift. He hasn't put it down since. We've spent hours dissecting it on calls. Bravo to your team.",
		"The soundtrack alone in {GAME} would've earned a letter from me. But the whole package? Genuinely a {GENRE} masterpiece. Please don't stop.",
		"Started {GAME} at 9pm and looked up to see the sun rising. Worth every lost hour of sleep. Please make more {GENRE} like this one.",
		"{GAME} is making me want to pick up game development myself. That's the impact your team has on people. Don't ever stop creating.",
		"Tough year personally, and {GAME} got me through some rough nights. Sounds dramatic to write that — I mean it though. Thank you.",
		"A friend told me to play {GAME} 'or else.' Glad I listened. I'm already passing the recommendation forward. Cheers to the whole studio.",
		"I've played the big-budget {GENRE} releases this year. {GAME} held its own against every one of them. Indies are eating well.",
	};

	// Quest letter pool. Short, direct asks — "make a {REQUESTED_GENRE}
	// game" energy. Placeholders: {GAME}, {REQUESTED_GENRE}.
	static readonly string[] QuestBodies =
	{
		"Make a {REQUESTED_GENRE} game.",
		"Please make a {REQUESTED_GENRE} game next.",
		"I want a {REQUESTED_GENRE} game from your studio.",
		"Loved {GAME}. Make a {REQUESTED_GENRE} game next!",
		"Could you make a {REQUESTED_GENRE} game?",
		"{GAME} was great. Make a {REQUESTED_GENRE} game next?",
		"Pitch: a {REQUESTED_GENRE} game. Please consider it.",
		"Hoping your next one is a {REQUESTED_GENRE} game.",
	};

	// Press-release sender pool. Distinct from the fan list — these
	// names read as industry-watchers / indie press contacts.
	static readonly string[] PressSenderNames =
	{
		"IndieScout",   "PixelPress",   "GameDevDaily",  "TheCutscene",
		"BetaBeat",     "FrontPageDev", "PressMonkey",   "SaveStateMag",
	};

	// Press-release letter pool — sent ~2 in-game weeks after a ship
	// that fulfilled 2+ quests at once. Talks up the game as a
	// "studio actually listened" headline. Placeholder: {NEW_GAME}.
	static readonly string[] PressBodies =
	{
		"Word travels fast — {NEW_GAME} hit on multiple fan requests at once. People are saying you're actually listening. Sales are tracking above forecast.",
		"{NEW_GAME} is the talk of the indie scene this week. Fans noticed you delivered on multiple asks. Numbers are climbing.",
		"Industry watching: {NEW_GAME} read the room. Multiple genres folks were begging for, all in one game. Expecting bigger sales than usual.",
		"Heads up — {NEW_GAME} caught a tailwind. Word is the studio listened to more than one fan ask, and the projections are reflecting it.",
		"The community noticed. {NEW_GAME} delivered on what people were asking for. Sales are tracking above your usual ceiling.",
	};

	// Discovery-letter pools — sent the first time a player ships a
	// positive-synergy genre pair. Three tiers keyed to the synergy
	// magnitude. Voice is fan-style (same warm tone as LetterBodies)
	// but the focus is the COMBO, not the game's overall quality.
	// Placeholders: {GAME}, {A}, {B}.

	static readonly string[] DiscoveryBodiesGood =
	{
		"The {A} + {B} blend in {GAME} just clicks. I hadn't seen it done before, but it works — feels like the two halves were waiting for each other.",
		"{GAME} got me thinking. {A} and {B} pair surprisingly well. Subtle, but the combination is doing more lifting than it looks.",
		"Played {GAME} this weekend. The {A} / {B} crossover is a quiet little win — not a gimmick, just two things that fit. Curious what else you do with it.",
		"Most studios wouldn't try {A} with {B}. Yours did, and {GAME} is better for it. The pairing earns its place.",
		"There's something about {A} and {B} together. {GAME} found a fit a lot of bigger studios have missed. Keep an eye on that combo.",
	};

	static readonly string[] DiscoveryBodiesVeryGood =
	{
		"I love the synergy of {A} + {B} in {GAME}. The two genres feed each other in a way I haven't seen done this cleanly in ages. Lean into this pairing.",
		"{A} and {B} were made for each other, and {GAME} is the proof. Whoever pitched this combo at your studio — give them a raise.",
		"Stop everything: {GAME}'s {A} + {B} pairing is the real deal. The two halves multiply instead of just sitting next to each other. Do more of this.",
		"{GAME} sold me on {A} + {B}. I went in skeptical and came out a believer. That combo has serious legs — please don't sleep on it.",
		"Played {GAME} for the {A}, stayed for the {B}, came back the next day because together they're greater than the sum. This pairing is something special.",
	};

	static readonly string[] DiscoveryBodiesVeryVeryGood =
	{
		"{A} + {B} in {GAME} is one of the best genre pairings I've EVER seen. I'm not exaggerating. The two halves don't just complement each other — they multiply. Whatever your next game is, if it's {A} + {B} again, take my money sight unseen.",
		"This is the combo of the year. {GAME} cracked something. {A} and {B} together is genuine alchemy — the kind of pairing studios spend whole careers searching for. Build everything you can around this.",
		"Listen. {GAME}'s {A} + {B} combination is the strongest genre pairing I've experienced in a decade. Tell your team. This is your studio's signature now if you want it to be.",
		"I've replayed {GAME} four times since launch and the {A} + {B} blend keeps revealing new layers. This isn't normal. You've stumbled onto something most studios couldn't find with a treasure map.",
		"Industry-defining pairing. {A} + {B} in {GAME} is the kind of synergy that gets written up in postmortems years later as 'how did nobody do this sooner?' You did. Don't let go of it.",
	};

	// Thank-you letter pool — sent by the same fan after the studio
	// ships a game in the requested genre. Always non-quest, always
	// positive (grants motivation when read). Placeholders:
	// {NEW_GAME}, {REQUESTED_GENRE}.
	static readonly string[] ThankYouBodies =
	{
		"You actually made it! {NEW_GAME} is everything I hoped for. Thank you for listening.",
		"{NEW_GAME} delivered. I can't believe you actually shipped a {REQUESTED_GENRE}. Thank you, sincerely.",
		"Saw you shipped {NEW_GAME}. Thank you for hearing me out — I'll be playing this one for weeks.",
		"You did it! {NEW_GAME} is exactly the {REQUESTED_GENRE} I was hoping for. Bravo.",
		"Wow — you actually made it. {NEW_GAME} is fantastic. Knew you could pull it off.",
		"Thank you so much for {NEW_GAME}. Every minute of it landed. The wait was worth it.",
		"{NEW_GAME} was worth the ask. Best {REQUESTED_GENRE} I've played in a long time.",
	};

	// ── Quest fulfillment ────────────────────────────────────────────────

	/// Looks for an open quest whose <see cref="FanLetter.QuestGenre"/>
	/// matches any of <paramref name="genres"/>. Iterates oldest-first
	/// so the longest-pending quest gets fulfilled. On match: marks the
	/// quest as fulfilled, stamps the fulfilling game's title, and
	/// returns the letter so the caller can apply its +20% sales bonus.
	/// Only one quest fulfills per ship (no stacking — per ADR-0003
	/// design choice).
	/// Fulfill every open quest whose <see cref="FanLetter.QuestGenre"/>
	/// matches one of <paramref name="genres"/>. Returns the count.
	/// Caller (Gallery) computes the stacking sales bonus
	/// (+20% per match, max +60% since a single game can carry at most
	/// 3 genres and the dedup rule guarantees 1 quest per genre).
	/// On 2+ fulfillments, schedules a press-release letter to land
	/// in the inbox 14 in-game days later.
	public int TryFulfillQuests( IReadOnlyList<GameGenre> genres, string fulfilledByGameTitle )
	{
		if ( genres == null || genres.Count == 0 ) return 0;

		// Snapshot matching letters before mutating. SpawnThankYouLetter
		// adds to _letters, which throws "Collection was modified" if we
		// do it inside a foreach over _letters.
		var matches = new List<FanLetter>();
		foreach ( var letter in _letters )
		{
			if ( !letter.IsQuest )                  continue;
			if ( letter.QuestFulfilled )            continue;
			if ( letter.QuestGenre is not { } qg )  continue;
			if ( !genres.Contains( qg ) )           continue;
			matches.Add( letter );
		}

		// One Random covers every thank-you spawned in this call — avoids
		// the per-call new Random() seed-collision that would give all
		// thank-yous from the same ship identical body / no variation.
		var rng = new Random();

		int fulfilledCount = 0;
		foreach ( var letter in matches )
		{
			var qg = letter.QuestGenre.Value;
			letter.QuestFulfilled  = true;
			letter.FulfilledByGame = fulfilledByGameTitle ?? "";
			fulfilledCount++;

			Notifications.Push( "Quest fulfilled!",
				$"{letter.SenderName} got their {GameGenres.Get( qg )?.Name ?? qg.ToString()} game — sales boosted +20%.",
				"success", duration: 8f );

			SpawnThankYouLetter( letter, fulfilledByGameTitle ?? "", rng );
		}

		// Press follow-up — only worth a beat if the studio nailed
		// multiple asks at once. Single-quest ships still feel good
		// via the bonus + thank-you letter; pressmail is reserved for
		// the multi-hit "the studio's actually listening" headline.
		if ( fulfilledCount >= 2 )
		{
			_pendingPress.Add( new PressLetterPending
			{
				GameTitle    = fulfilledByGameTitle ?? "",
				QuestCount   = fulfilledCount,
				TargetDayAbs = CurrentDayAbs() + PressLetterDelayDays,
			} );
		}

		return fulfilledCount;
	}

	/// Number of in-game days between a multi-quest ship and the
	/// press-release follow-up letter landing in the inbox.
	const int PressLetterDelayDays = 14;

	/// Spawn a press-release letter when its scheduled day arrives.
	/// Industry-watcher persona, body templated to talk up the multi-
	/// quest ship as "the studio actually listened." Acts as a regular
	/// non-quest letter — grants the standard motivation buff on read.
	void SpawnPressLetter( PressLetterPending pending, Random rng )
	{
		if ( pending is null ) return;

		string body = PickPressBody( rng )
			.Replace( "{NEW_GAME}", pending.GameTitle ?? "" );

		if ( body.Length > 0 && char.IsLower( body[0] ) )
			body = char.ToUpper( body[0] ) + body.Substring( 1 );

		var letter = new FanLetter
		{
			Id          = Guid.NewGuid().ToString( "N" ),
			GameTitle   = pending.GameTitle ?? "",
			GameGenre   = GameGenre.Mystery,        // press doesn't speak for any one genre
			ReviewScore = 100,
			SenderName  = PressSenderNames[rng.Next( PressSenderNames.Length )],
			Body        = body,
			ReceivedAt  = DateTimeOffset.UtcNow,
			IsQuest     = false,
			Read        = false,
		};
		_letters.Add( letter );
		Achievements.RecordLetterReceived();

		Notifications.Push( "Press is talking",
			$"{letter.SenderName} has news about \"{pending.GameTitle}\".",
			"info", duration: 6f );
	}

	/// Spawn a synergy-discovery hint letter. Fired by
	/// <see cref="SpawnDiscoveryLettersFor"/> the first time a player
	/// ships a positive-synergy pair. Pure flavor — IsDiscovery=true so
	/// <see cref="StartReading"/> skips the motivation grant; this is a
	/// hint, not a buff. Sender uses the regular fan pool. Body tier is
	/// keyed off the synergy magnitude via <see cref="PickDiscoveryBody"/>.
	void SpawnDiscoveryLetter( ShippedGame game, GameGenre a, GameGenre b, float synergy, Random rng )
	{
		var nameA = GameGenres.Get( a )?.Name ?? a.ToString();
		var nameB = GameGenres.Get( b )?.Name ?? b.ToString();

		string body = PickDiscoveryBody( synergy, rng )
			.Replace( "{GAME}", game.Title ?? "" )
			.Replace( "{A}",    nameA )
			.Replace( "{B}",    nameB );

		if ( body.Length > 0 && char.IsLower( body[0] ) )
			body = char.ToUpper( body[0] ) + body.Substring( 1 );

		var letter = new FanLetter
		{
			Id          = Guid.NewGuid().ToString( "N" ),
			GameTitle   = game.Title ?? "",
			GameGenre   = a,
			ReviewScore = game.ReviewScore,
			SenderName  = SenderNames[rng.Next( SenderNames.Length )],
			Body        = body,
			ReceivedAt  = DateTimeOffset.UtcNow,
			IsQuest     = false,
			IsDiscovery = true,
			SynergyA    = a,
			SynergyB    = b,
			Read        = false,
		};
		_letters.Add( letter );
		Achievements.RecordLetterReceived();

		Notifications.Push( "You've received a fan letter!",
			$"Someone noticed your {nameA} + {nameB} combo in \"{game.Title}\".",
			"info", duration: 6f );
	}

	/// Spawn the follow-up thank-you letter when a quest is fulfilled.
	/// Same sender as the original quest, references the new game's
	/// title, picks from <see cref="ThankYouBodies"/>. Always non-quest,
	/// always grants the standard motivation buff when the player reads it.
	void SpawnThankYouLetter( FanLetter quest, string fulfillingGameTitle, Random rng )
	{
		if ( quest is null ) return;

		var genreName = quest.QuestGenre is { } g
			? (GameGenres.Get( g )?.Name ?? g.ToString())
			: "";

		string body = PickThankYouBody( rng )
			.Replace( "{NEW_GAME}",        fulfillingGameTitle )
			.Replace( "{REQUESTED_GENRE}", genreName );

		// Match the capitalisation guard used in GenerateLetter — if the
		// fulfilling game's title is lowercase and the template starts
		// with {NEW_GAME}, the rendered body would otherwise read with
		// a lowercase first letter.
		if ( body.Length > 0 && char.IsLower( body[0] ) )
			body = char.ToUpper( body[0] ) + body.Substring( 1 );

		var letter = new FanLetter
		{
			Id          = Guid.NewGuid().ToString( "N" ),
			GameTitle   = fulfillingGameTitle,
			GameGenre   = quest.QuestGenre ?? GameGenre.Mystery,
			ReviewScore = 100,
			SenderName  = quest.SenderName,
			Body        = body,
			ReceivedAt  = DateTimeOffset.UtcNow,
			IsQuest     = false,
			Read        = false,
		};
		_letters.Add( letter );
		Achievements.RecordLetterReceived();

		Notifications.Push( "You've received a fan letter!",
			$"{quest.SenderName} wrote back about \"{fulfillingGameTitle}\".",
			"info", duration: 6f );
	}

	// ── Calendar helpers ─────────────────────────────────────────────────

	static int AbsoluteDay( int year, int month, int day )
		=> year * 360 + (month - 1) * 30 + day;

	// ── Save / Load (per ADR-0001) ───────────────────────────────────────

	public LetterboxSave Save() => new()
	{
		Letters                  = new List<FanLetter>( _letters ),
		FirstLetterSeen          = _firstLetterSeen,
		DecidedFor               = new List<string>( _decidedFor ),
		MotivationActiveUntilDay = _motivationActiveUntilDay,
		MotivationQueuedDays     = _motivationQueuedDays,
		PendingPress             = new List<PressLetterPending>( _pendingPress ),
		DiscoveredPairs          = new List<string>( _discoveredPairs ),
	};

	public void Load( LetterboxSave dto )
	{
		_letters.Clear();
		_decidedFor.Clear();
		_pendingPress.Clear();
		_discoveredPairs.Clear();
		_discoveryBackfillDone    = false;
		_firstLetterSeen          = false;
		_motivationActiveUntilDay = 0;
		_motivationQueuedDays     = 0;
		if ( dto is null ) return;
		if ( dto.Letters         != null ) _letters.AddRange( dto.Letters );
		if ( dto.DecidedFor      != null ) foreach ( var t in dto.DecidedFor ) _decidedFor.Add( t );
		if ( dto.PendingPress    != null ) _pendingPress.AddRange( dto.PendingPress );
		if ( dto.DiscoveredPairs != null ) foreach ( var k in dto.DiscoveredPairs ) _discoveredPairs.Add( k );

		// Backfill: any game we already have a letter for is implicitly
		// decided. Covers older v3 saves written before the DecidedFor
		// field existed.
		foreach ( var l in _letters ) _decidedFor.Add( l.GameTitle );

		_firstLetterSeen          = dto.FirstLetterSeen;
		_motivationActiveUntilDay = dto.MotivationActiveUntilDay;
		_motivationQueuedDays     = dto.MotivationQueuedDays;
	}

	/// New-game wipe. Called from <see cref="GameSaveManager.RestartRun"/>
	/// alongside the other per-run resets.
	public void ResetProgress()
	{
		_letters.Clear();
		_decidedFor.Clear();
		_pendingPress.Clear();
		_discoveredPairs.Clear();
		_discoveryBackfillDone    = false;
		_firstLetterSeen          = false;
		_motivationActiveUntilDay = 0;
		_motivationQueuedDays     = 0;
		CurrentlyReading          = null;
		Notifications.RemoveByTag( FirstLetterTag );
	}
}
