/// <summary>
/// Static catalogue of named training-offer variants. The training queue
/// (see <see cref="TrainingManager"/>) rolls a random tier + mode and then
/// picks a flavor name from the appropriate sub-list.
///
/// 20 strings per (Mode, Tier) for Individual/Group → 160 total. Research
/// uses a single 20-string pool (no tier dimension) since the drip rate is
/// the same regardless of "topic".
///
/// All 180 strings are intentionally short — they're what the player sees
/// on each offer card and on the per-worker research badge.
/// </summary>
public static class TrainingVariants
{
	// ── Individual ──────────────────────────────────────────────────────────

	public static readonly IReadOnlyList<string> IndividualInstant = new[]
	{
		"Coffee Chat",          "Pep Talk",            "Lightning Talk",
		"Whiteboard Session",   "Hallway Coaching",    "Brown Bag Lunch",
		"Lunch & Learn",        "Stand-up Tip",        "Skill Cheat Sheet",
		"Drive-by Code Review", "Knowledge Drop",      "Espresso Workshop",
		"1-on-1 Hour",          "Doc Skim",            "Demo Watching",
		"Quick Pairing",        "Hot Take Session",    "Office Hours",
		"Cheat-Code Slack",     "Twenty-Minute Tip",
	};

	public static readonly IReadOnlyList<string> IndividualBasic = new[]
	{
		"Book Club",      "Online Workshop",  "Tennis Class",       "Dance Class",
		"Cooking Class",  "Pottery Workshop", "Fishing Day",        "Hiking Trip",
		"Yoga Class",     "Photography Walk", "Painting Class",     "Improv Workshop",
		"Language Class", "Local Conference", "Skill-Share Meetup", "Trivia League",
		"Park Picnic",    "Karaoke Night",    "Board Game Night",   "Open-Source Day",
	};

	public static readonly IReadOnlyList<string> IndividualStandard = new[]
	{
		"Industry Conference", "Specialist Bootcamp", "Master Class",      "Studio Tour",
		"Mountain Cabin Trip", "Surfing Camp",        "Indie Game Jam",    "Mentorship Program",
		"Beach Workshop",      "City Trip",           "Networking Cruise", "Skill Sabbatical",
		"VR Bootcamp",         "Wine & Design Stay",  "Camping & Coding",  "Riverboat Workshop",
		"Art Gallery Tour",    "Music Festival Trip", "Filmmaking Camp",   "Convention Pass",
	};

	public static readonly IReadOnlyList<string> IndividualPremium = new[]
	{
		"GDC Pass",              "Tokyo Tour",            "Silicon Valley Trip", "Berlin Studio Tour",
		"Pixar Visit",           "Mediterranean Cruise",  "Ski Resort Retreat",  "Safari Sabbatical",
		"Bali Surf Retreat",     "Iceland Aurora Trip",   "Japanese Onsen Stay", "New Zealand Adventure",
		"French Riviera Stay",   "Tuscany Villa Retreat", "Alpine Chalet Stay",  "Cherry Blossom Tour",
		"Kyoto Temple Stay",     "Aspen Ski Camp",        "Maldives Retreat",    "World-Tour Workshop",
	};

	// ── Group ───────────────────────────────────────────────────────────────

	public static readonly IReadOnlyList<string> GroupInstant = new[]
	{
		"Morning Stand-up",  "All-Hands Huddle",  "Fire Drill",          "Pizza Briefing",
		"Kanban Kickoff",    "Weekly Demo",       "Team Trivia",         "Kitchen Learn",
		"Pep Rally",         "Stand-up Demo",     "Office Olympics Lite", "Lightning Show & Tell",
		"Brown Bag Talk",    "Hallway Town Hall", "Snack-time Drill",    "Quick Retro",
		"Daily War Room",    "Hot-Seat Q&A",      "Lunch Debrief",       "Boardroom Burst",
	};

	public static readonly IReadOnlyList<string> GroupBasic = new[]
	{
		"Movie Night",      "Seminar",         "Pizza Workshop",   "Game Night",
		"Trivia Night",     "Bowling Trip",    "Dinner Outing",    "Karaoke Night",
		"Escape Room",      "Brewery Visit",   "Mini-Golf Outing", "Ice Cream Social",
		"Local Conference", "Bar Crawl",       "Talent Show",      "Office Olympics",
		"Park Day",         "Block Party",     "Volunteer Day",    "Group Picnic",
	};

	public static readonly IReadOnlyList<string> GroupStandard = new[]
	{
		"Conference Trip",   "Beach Weekend",     "Mountain Retreat", "Studio Tour",
		"Wine Tour",         "Roadtrip",          "City Adventure",   "Cabin Stay",
		"Camping Weekend",   "Lake House Retreat", "Boat Trip",        "Gallery Hop",
		"Theme Park Day",    "Cooking Retreat",   "Music Festival",   "Sports Weekend",
		"Resort Stay",       "Casino Trip",       "Spa Weekend",      "Industry Mixer",
	};

	public static readonly IReadOnlyList<string> GroupPremium = new[]
	{
		"International Conference", "Tokyo Studio Tour",  "Cruise",              "Ski Resort Week",
		"Hawaii Retreat",           "GDC + LA Trip",      "Italian Tour",        "Greek Islands",
		"World Tour",               "Antarctic Cruise",   "Safari Lodge",        "Vegas Bonanza",
		"Maldives Retreat",         "Caribbean Cruise",   "Northern Lights Tour","Australia Tour",
		"Egypt Tour",               "Iceland Trip",       "French Riviera Trip", "Aspen Snow Trip",
	};

	// ── Research (no tier — all variants behave identically mechanically) ──

	public static readonly IReadOnlyList<string> ResearchTopics = new[]
	{
		"Self-Study",          "Side Project",         "Open Source Project",  "Personal Research",
		"Reading Group",       "Podcast Marathon",     "Tutorial Binge",       "Blog Drafting",
		"Hobby Project",       "Skill Drilling",       "Documentation Read",   "Tech Talks",
		"Online Course",       "GitHub Lurking",       "Industry Newsletter",  "Whitepaper Reading",
		"Mock Reviews",        "Forum Browsing",       "Solo Game Jam",        "Practice Sessions",
	};

	// ── Lookup ──────────────────────────────────────────────────────────────

	public static IReadOnlyList<string> For( TrainingMode mode, TrainingTier tier ) =>
		(mode, tier) switch
		{
			(TrainingMode.Individual, TrainingTier.Instant)  => IndividualInstant,
			(TrainingMode.Individual, TrainingTier.Basic)    => IndividualBasic,
			(TrainingMode.Individual, TrainingTier.Standard) => IndividualStandard,
			(TrainingMode.Individual, TrainingTier.Premium)  => IndividualPremium,
			(TrainingMode.Group,      TrainingTier.Instant)  => GroupInstant,
			(TrainingMode.Group,      TrainingTier.Basic)    => GroupBasic,
			(TrainingMode.Group,      TrainingTier.Standard) => GroupStandard,
			(TrainingMode.Group,      TrainingTier.Premium)  => GroupPremium,
			_                                                  => System.Array.Empty<string>(),
		};

	/// Random research-topic name for the per-worker flavor badge. The drip
	/// mechanic doesn't read this — it's purely "what is Mateo studying" UI.
	public static string RollResearchTopic( System.Random rng ) =>
		ResearchTopics[rng.Next( ResearchTopics.Count )];
}
