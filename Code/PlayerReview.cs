using System;
using System.Collections.Generic;

/// <summary>
/// Sentiment bucket for a generated <see cref="PlayerReview"/>. Drives both
/// template selection AND the visual treatment in <c>GalleryPanel.razor</c>.
/// </summary>
public enum ReviewSentiment
{
	Positive = 0,
	Neutral  = 1,
	Negative = 2,
}

/// <summary>
/// One generated player-facing review tile. Pure data — produced by
/// <see cref="PlayerReviewGenerator.GenerateForGame"/> and rendered in the
/// Past Games view of the Gallery.
/// </summary>
public sealed record PlayerReview(
	string          Username,
	string          Body,
	ReviewSentiment Sentiment );

/// <summary>
/// Deterministic, seeded review generator. Same <see cref="ShippedGame"/>
/// always produces the same set of reviews so the player can revisit the
/// list without watching it shuffle.
///
/// Sentiment mix is driven by <see cref="ShippedGame.ReviewScore"/>:
///   ≥80     — heavy positive, sprinkle of neutral
///   60–79   — mostly positive / neutral
///   40–59   — mostly neutral, mixed extremes
///   20–39   — mostly negative
///   &lt;20  — heavy negative
///
/// Templates with <c>{combo}</c> or <c>{genre}</c> placeholders pull from the
/// game's actual genre tags; positive reviews preferentially highlight pairs
/// with positive synergy from <see cref="GameGenres.Synergy"/> (so the player
/// gets a free hint at "what worked"), and negative reviews preferentially
/// highlight clashing pairs.
/// </summary>
public static class PlayerReviewGenerator
{
	// ── Username word banks ──────────────────────────────────────────────────
	// Three pools blended into gamertag-style handles. Patterns vary so the
	// list doesn't look templated:
	//   pool A + pool B + 2-digit number          → "adultbacon88"
	//   number + pool A + Pool B (PascalCase)     → "67noHands"
	//   pool A + Pool B (PascalCase, no digits)   → "neoSlayer"
	//   pool A + pool B + 4-digit year            → "voidkid2010"
	//   pool A + pool B (no digits, lowercase)    → "lazerhawk"
	// Strings are intentionally short and casual — these are randos, not lore.

	static readonly string[] PoolA =
	{
		"adult", "super", "mega", "neo", "pro", "ace", "dark", "swift", "cyber",
		"ghost", "ninja", "dragon", "void", "lazer", "turbo", "viper", "snake",
		"lone", "big", "tiny", "true", "pure", "arctic", "lunar", "solar",
		"blood", "fire", "ice", "storm", "shadow", "alpha", "omega", "captain",
		"lord", "doctor", "agent", "sir", "lady", "prince", "epic", "noob",
		"toxic", "based", "cringe", "real", "pog", "salty", "spicy", "nasty",
		"chill", "fancy",
	};

	static readonly string[] PoolB =
	{
		"bacon", "gamer", "slayer", "smasher", "hunter", "ranger", "blade",
		"kid", "panda", "fox", "wolf", "eagle", "hawk", "rhino", "lobster",
		"wizard", "knight", "beast", "fury", "hands", "eyes", "shot", "kill",
		"frag", "plays", "runs", "jumps", "dodges", "sniper", "scout",
		"master", "flex", "banger", "baller", "demon", "angel", "juggler",
		"loser", "winner", "dad", "mom", "bro", "guy", "girl", "main",
		"enjoyer", "hater", "pilled", "moded", "gamer69", "diff",
	};

	static readonly string[] Numbers =
	{
		"01", "07", "13", "21", "22", "23", "33", "42", "47", "55", "67", "69",
		"77", "88", "99", "420", "777", "1337", "2007", "2010", "2014", "1995",
	};

	// ── Review templates ─────────────────────────────────────────────────────
	// Placeholders:
	//   {combo} — replaced with "Genre1+Genre2" pulled from the game's tags,
	//             biased toward positive-synergy pairs in positive reviews and
	//             clash pairs in negative reviews. Falls back to "this combo".
	//   {genre} — single tag from the game's list. Falls back to "this".
	//
	// Generic templates (no placeholder) survive 0/1-tag games unchanged.

	static readonly string[] PositiveTemplates =
	{
		"i loved the {combo} combination!",
		"best {genre} game I've played in years",
		"the {combo} mix just works, dev knew what they were doing",
		"{combo}? sign me up. instant buy",
		"10/10 would play again. nailed the {genre} feel",
		"spent way too many hours on this. addictive",
		"absolutely peak {genre}. devs cooked",
		"the way this blends {combo} is genius",
		"if you like {genre}, you'll love this",
		"shut up and take my money",
		"GOTY contender. {genre} fans will eat this up",
		"finally a {genre} game that respects my time",
		"music + gameplay synergy is unreal",
		"this is what {genre} should be",
		"showed this to my partner. now they're hooked too",
		"started as a meme purchase. ended as my favorite game this year",
		"the {genre} mechanics feel so satisfying",
		"{genre} done right. that's all I needed.",
		"warning: will eat your sleep schedule",
		"haven't put it down since launch",
	};

	static readonly string[] NeutralTemplates =
	{
		"fine {genre} game. nothing special",
		"wait for sale",
		"{combo} is interesting but underbaked",
		"had its moments. forgot about it after",
		"competent. that's the kindest word I have",
		"okay if you're a hardcore {genre} fan",
		"{genre} fans will probably like it. I didn't",
		"could've been great. settled for fine",
		"the {genre} mechanics work. story is whatever",
		"finished it. don't really feel anything",
		"decent first attempt",
		"it's a {genre} game. exists.",
		"neither good nor bad. just there",
		"ok. would not buy at full price",
		"{genre} purists will find issues. casuals will be fine",
		"passable. {combo} additions feel tacked on",
		"I've played worse. I've played better",
		"competent {genre} fare. seen it all before",
		"decent music. mid gameplay",
		"exists. that's about it.",
	};

	static readonly string[] NegativeTemplates =
	{
		"{combo} just don't mix. trainwreck",
		"30 minutes was 30 too many. {genre} fans steer clear",
		"what is this? feels like two different games stapled together",
		"{combo} is an idea that should've stayed in the doc",
		"0/10. waste of money",
		"broken on launch. devs need to playtest",
		"decent {genre} ruined by the rest of it",
		"this should not have been released in this state",
		"dev clearly never played a {genre} game",
		"stop trying to make {combo} happen",
		"felt like a tech demo. half-baked",
		"asset flip. avoid",
		"I want my hour back",
		"the {genre} side is ok. the rest is unforgivable",
		"no thoughts. complete waste of time.",
		"looks like it was made in a weekend",
		"{combo} = disaster. studios should know better",
		"performance is awful. {genre} doesn't save it",
		"boring. {genre} alone would have been better",
		"did anyone QA this?",
	};

	// ── Public API ───────────────────────────────────────────────────────────

	/// <summary>
	/// Build the review list for one shipped game. Deterministic — calling
	/// twice with the same input returns identical content (seeded by the
	/// game's Title + ship date).
	/// </summary>
	public static IReadOnlyList<PlayerReview> GenerateForGame( ShippedGame game, int count = 5 )
	{
		if ( game is null || count <= 0 ) return Array.Empty<PlayerReview>();

		// Stable per-game seed. Different from a random seed every call, so
		// the same Past Games card always shows the same reviews — players
		// can revisit the tile without the list reshuffling.
		int seed = HashCode.Combine( game.Title, game.ShipYear, game.ShipMonth, game.ShipDay );
		var rng  = new Random( seed );

		// Sentiment mix for this game, based on review score. Reviews stay
		// blank ("Reviews pending") while ReviewReleased == false, so we
		// always have a valid score by the time the player can open the list.
		var distribution = SentimentMix( game.ReviewScore );

		var list = new List<PlayerReview>( count );
		for ( int i = 0; i < count; i++ )
		{
			var sentiment = PickSentiment( rng, distribution );
			var body      = PickTemplate( rng, sentiment, game );
			var username  = MakeUsername( rng );
			list.Add( new PlayerReview( username, body, sentiment ) );
		}
		return list;
	}

	// ── Sentiment selection ──────────────────────────────────────────────────

	/// Probabilities (positive, neutral, negative) summing to 1, derived from
	/// the game's review score. Buckets, not a smooth curve — tuning by hand
	/// is easier in 5 brackets than in a continuous formula.
	static (float pos, float neu, float neg) SentimentMix( int score )
	{
		if ( score >= 80 ) return ( 0.65f, 0.25f, 0.10f );
		if ( score >= 60 ) return ( 0.45f, 0.40f, 0.15f );
		if ( score >= 40 ) return ( 0.25f, 0.45f, 0.30f );
		if ( score >= 20 ) return ( 0.10f, 0.30f, 0.60f );
		return                  ( 0.05f, 0.15f, 0.80f );
	}

	static ReviewSentiment PickSentiment( Random rng, (float pos, float neu, float neg) mix )
	{
		float roll = (float)rng.NextDouble();
		if ( roll < mix.pos ) return ReviewSentiment.Positive;
		if ( roll < mix.pos + mix.neu ) return ReviewSentiment.Neutral;
		return ReviewSentiment.Negative;
	}

	// ── Template + placeholder fill ──────────────────────────────────────────

	static string PickTemplate( Random rng, ReviewSentiment sentiment, ShippedGame game )
	{
		var pool = sentiment switch
		{
			ReviewSentiment.Positive => PositiveTemplates,
			ReviewSentiment.Negative => NegativeTemplates,
			_                        => NeutralTemplates,
		};
		string template = pool[rng.Next( pool.Length )];

		// Combo placeholder — pick a pair biased by sentiment direction.
		if ( template.Contains( "{combo}" ) )
		{
			string combo = PickComboString( rng, sentiment, game );
			template = template.Replace( "{combo}", combo );
		}

		// Single-genre placeholder — any tag works, pick uniformly.
		if ( template.Contains( "{genre}" ) )
		{
			string genre = PickSingleGenre( rng, game );
			template = template.Replace( "{genre}", genre );
		}

		return template;
	}

	/// "Genre1+Genre2" string. For positive reviews we prefer pairs with
	/// positive synergy (the "I loved the X+Y combo!" lesson), for negatives
	/// we prefer pairs with negative synergy. Falls back to a random pair if
	/// no biased candidate exists, or to the literal "this combo" if the
	/// game has fewer than 2 genres.
	static string PickComboString( Random rng, ReviewSentiment sentiment, ShippedGame game )
	{
		if ( game.Genres is null || game.Genres.Count < 2 ) return "this combo";

		// Build all unordered pairs once.
		var pairs = new List<(GameGenre a, GameGenre b, float syn)>();
		for ( int i = 0; i < game.Genres.Count; i++ )
			for ( int j = i + 1; j < game.Genres.Count; j++ )
				pairs.Add( (game.Genres[i], game.Genres[j],
				            GameGenres.Synergy( game.Genres[i], game.Genres[j] )) );

		// Prefer pairs aligned with the review's tone.
		List<(GameGenre a, GameGenre b, float syn)> candidates = sentiment switch
		{
			ReviewSentiment.Positive => pairs.FindAll( p => p.syn > 0f ),
			ReviewSentiment.Negative => pairs.FindAll( p => p.syn < 0f ),
			_                        => null,
		};
		if ( candidates is null || candidates.Count == 0 ) candidates = pairs;

		var pick = candidates[rng.Next( candidates.Count )];
		string aName = GameGenres.Get( pick.a )?.Name ?? pick.a.ToString();
		string bName = GameGenres.Get( pick.b )?.Name ?? pick.b.ToString();
		return $"{aName}+{bName}";
	}

	static string PickSingleGenre( Random rng, ShippedGame game )
	{
		if ( game.Genres is null || game.Genres.Count == 0 ) return "this";
		var pick = game.Genres[rng.Next( game.Genres.Count )];
		return GameGenres.Get( pick )?.Name ?? pick.ToString();
	}

	// ── Username generator ──────────────────────────────────────────────────

	static string MakeUsername( Random rng )
	{
		// Five formats, picked uniformly. Output is intentionally lowercase /
		// camelCase / digits-blended to match real Steam-handle vibes.
		int format = rng.Next( 5 );
		string a = PoolA[rng.Next( PoolA.Length )];
		string b = PoolB[rng.Next( PoolB.Length )];
		string n = Numbers[rng.Next( Numbers.Length )];

		return format switch
		{
			0 => $"{a}{b}{n}",                    // adultbacon88
			1 => $"{n}{a}{Capitalize( b )}",      // 67noHands
			2 => $"{a}{Capitalize( b )}",         // neoSlayer
			3 => $"{a}{b}",                       // lazerhawk
			_ => $"{a}_{b}_{n}",                  // adult_bacon_88
		};
	}

	static string Capitalize( string s )
	{
		if ( string.IsNullOrEmpty( s ) ) return s;
		if ( char.IsUpper( s[0] ) )      return s;
		return char.ToUpperInvariant( s[0] ) + s.Substring( 1 );
	}
}
