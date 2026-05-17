/// <summary>
/// The root persisted shape of a single playthrough.
///
/// Composed of typed sub-DTOs, one per owning system. New persisted
/// systems opt in by adding a sub-DTO here and a matching ToSave()/
/// LoadFrom() pair on the system itself — never an interface, so the
/// schema stays visible in one file.
///
/// Authoritative format: see ADR-0001 (docs/architecture/adr-0001-save-system.md).
///
/// **Property-only rule** (forbidden_pattern: dto_with_field_not_property):
/// every member here and in nested DTOs must be a public auto-property.
/// s&box's WriteJson / ReadJson&lt;T&gt; silently ignore fields, which would
/// load as defaults with no error and corrupt the save invisibly.
///
/// **No Component references** (forbidden_pattern: dto_with_component_reference):
/// store identifiers (Guid for slots, name for hires, ItemKind for items)
/// — never a live Component or GameObject.
/// </summary>
public sealed class GameSave
{
	/// Schema version of THIS save file. Compared against
	/// <see cref="GameSaveManager.SchemaVersion"/> on load; if older,
	/// migrators in <see cref="GameSaveMigrations"/> bring it forward.
	public int            SchemaVersion { get; set; } = GameSaveManager.SchemaVersion;

	/// Wall-clock time the save was written. Surfaced in the load screen.
	public DateTimeOffset SavedAt       { get; set; } = DateTimeOffset.UtcNow;

	/// Engine / build identifier. Diagnostic only — not used for migration
	/// (SchemaVersion is the source of truth there).
	public string         GameVersion   { get; set; } = "";

	// ── Sub-DTOs (one per owning system) ─────────────────────────────────

	public CalendarSave     Calendar     { get; set; } = new();
	public EconomySave      Economy      { get; set; } = new();
	public AchievementsSave Achievements { get; set; } = new();
	public FounderSave      Founder      { get; set; } = new();
	public HRSave           HR           { get; set; } = new();
	public InventorySave    Inventory    { get; set; } = new();
	public TrainingSave     Training     { get; set; } = new();
	public GalleryShipsSave Gallery      { get; set; } = new();
	public GameProjectSave  ActiveProject { get; set; }
	public TutorialSave     Tutorial     { get; set; } = new();

	/// Letterbox + Quests state (ADR-0003). New at SchemaVersion 3 —
	/// legacy v1 / v2 saves get an empty <see cref="LetterboxSave"/>
	/// via the migrator, so the next ship spawns letters normally.
	public LetterboxSave    Letterbox    { get; set; } = new();
}

// ── Tutorial ────────────────────────────────────────────────────────────

/// <summary>
/// First-launch tutorial state. Per-run today; will move to meta-progression
/// once that lands so the tutorial only fires on the player's very first run.
/// </summary>
public sealed class TutorialSave
{
	public TutorialPhase  Phase                 { get; set; } = TutorialPhase.Welcome;
	public bool           BinBought             { get; set; } = false;
	public bool           StartupTrainingDone   { get; set; } = false;
	public bool           ChairBought           { get; set; } = false;
	public bool           ChairAssigned         { get; set; } = false;
	public TutorialPhase? LastAcknowledgedPhase { get; set; } = null;

	/// True once the player has acknowledged (opened chat with the moody NPC
	/// for) the first-ever Good-mood toast. Future Good toasts auto-expire
	/// normally; the flag exists to keep the very first one sticky so the
	/// player can't miss the new mechanic.
	public bool           FirstGoodMoodToastSeen { get; set; } = false;
}

// ── Calendar / Economy ──────────────────────────────────────────────────

/// <summary>Day/Month/Year + game-speed snapshot. Owned by <see cref="GameManager"/>.</summary>
public sealed class CalendarSave
{
	public int           Day            { get; set; } = 1;
	public int           Month          { get; set; } = 1;
	public int           Year           { get; set; } = 2026;
	public float         TimeMultiplier { get; set; } = 1f;
	public GameSpeedTier ActiveSpeed    { get; set; } = GameSpeedTier.Normal;
}

/// <summary>Money + energy snapshot. Owned by <see cref="GameManager"/>.</summary>
public sealed class EconomySave
{
	public long Money  { get; set; }
	public int  Energy { get; set; }
}

// ── Achievements (static) ──────────────────────────────────────────────

/// <summary>
/// Snapshot of <see cref="Achievements"/>. The class is static so the
/// Save/Load entry-points live there directly, not on a Component.
/// </summary>
public sealed class AchievementsSave
{
	public List<AchievementId>   Unlocked            { get; set; } = new();
	public List<EmployeeAbility> AbilitiesDiscovered { get; set; } = new();
	public List<GameGenre>       ResearchedGenres    { get; set; } = new();
	public long                  LifetimeEarned      { get; set; }
	public int                   TotalHires          { get; set; }
	public long                  PeakMonthlyPlayers  { get; set; }
	public int                   GamesShipped        { get; set; }

	/// Highest <c>GameManager.Money</c> ever held during this run.
	/// Tracked per-frame in <c>GameManager.OnUpdate</c> via
	/// <c>Achievements.RecordBalance</c>. Drives the "Peak Wallet"
	/// leaderboard (see ADR-0002). Added at SchemaVersion 2 — legacy v1
	/// saves load with PeakBalance = 0 (DTO default).
	public long                  PeakBalance         { get; set; }

	/// Lifetime fan-letter count this run. Drives the FirstLetter /
	/// Letters10 / Letters50 achievements (ADR-0003). Additive field —
	/// legacy saves load with 0 (the run hasn't received any letters
	/// before the ADR-0003 implementation landed).
	public int                   LettersReceived     { get; set; }

	/// Per-tier counts of medals awarded this run. Drive the
	/// FirstBronzeMedal / FirstSilverMedal / FirstGoldMedal achievements.
	/// Additive — legacy saves load with 0.
	public int                   BronzeMedalsAwarded { get; set; }
	public int                   SilverMedalsAwarded { get; set; }
	public int                   GoldMedalsAwarded   { get; set; }
}

// ── Founder ─────────────────────────────────────────────────────────────

/// <summary>Snapshot of <see cref="PlayerStats"/> (the studio founder).</summary>
public sealed class FounderSave
{
	public string                Name                    { get; set; } = "Founder";
	public EmployeeRole          Role                    { get; set; } = EmployeeRole.Founder;
	public int                   Level                   { get; set; } = 1;
	public long                  Xp                      { get; set; }
	public EmployeeStats         Stats                   { get; set; } = new();
	public List<PlayerAbility>   Abilities               { get; set; } = new();
	public TrainingSession       ActiveTraining          { get; set; }
	public string                ResearchTopicId         { get; set; } = "";
	public float                 DaysIntoCurrentResearch { get; set; }
}

// ── HR / hires / applicants ─────────────────────────────────────────────

/// <summary>Snapshot of <see cref="HRManager"/> — staff, applicants, alumni, posting.</summary>
public sealed class HRSave
{
	public List<EmployeeSave>      Staff             { get; set; } = new();
	public List<ApplicantSave>     Applicants        { get; set; } = new();
	public List<InternAlumnusSave> Alumni            { get; set; } = new();
	public bool                    FirstInternHired  { get; set; }
	public JobPostingTier          PostingTier       { get; set; } = JobPostingTier.BulletinBoard;
	public float                   ApplicantTimer    { get; set; }

	/// True between tutorial-applicant seed and the next Regular applicant
	/// roll: the next Regular applicant (applicant #2 overall) is forced to
	/// Tier 1 (Mid → 9⚡) so a new player has a fair shot at recovering
	/// financially post-tutorial. One-shot; <see cref="HRManager.RollApplicant"/>
	/// clears it on use, and applicants from #3 onward roll natural tier
	/// weighting.
	public bool                    PendingSecondApplicantGuarantee { get; set; }
}

/// <summary>
/// One hired NPC. The on-load rehire pass clones the citizen prefab,
/// calls <c>Assign(applicant)</c> with these values, then re-applies
/// runtime fields (morale, training, research) that <c>Assign</c> resets.
/// </summary>
public sealed class EmployeeSave
{
	public string                Name              { get; set; } = "";
	public int                   Age               { get; set; }
	public EmployeeRole          Role              { get; set; }
	public EmployeeKind          Kind              { get; set; } = EmployeeKind.Regular;
	public long                  Salary            { get; set; }
	public int                   HireDay           { get; set; }
	public bool                  SkipFirstSalary   { get; set; }
	public Guid?                 DeskSlotId        { get; set; }
	public int                   MonthsRemaining   { get; set; } = -1;
	public bool                  WasReturningIntern{ get; set; }
	public EmployeeAbility?      Ability1          { get; set; }
	public EmployeeAbility?      Ability2          { get; set; }
	public EmployeeStats         Stats             { get; set; } = new();
	public float                 Morale            { get; set; } = 1f;
	public TrainingSession       ActiveTraining    { get; set; }
	public string                ResearchTopicId   { get; set; } = "";
	public float                 DaysIntoCurrentResearch { get; set; }
	public int                   EnergyDrinkUntilTotalDay   { get; set; }
	public int                   AppearanceSeed    { get; set; }
	public List<Sandbox.ClothingContainer.ClothingEntry> SavedClothing    { get; set; } = new();
}

/// <summary>
/// One pending applicant in the HR inbox. Stripped-down <see cref="Employee"/>
/// — the on-load rebuild reconstructs an <c>Employee</c> directly from these
/// fields (no prefab clone since they aren't yet hired).
/// </summary>
public sealed class ApplicantSave
{
	public string                Name              { get; set; } = "";
	public int                   Age               { get; set; }
	public EmployeeRole          Role              { get; set; }
	public EmployeeKind          Kind              { get; set; } = EmployeeKind.Regular;
	public int                   Tier              { get; set; }
	public long                  Salary            { get; set; }
	public float                 SalaryBias        { get; set; } = 1f;
	public List<EmployeeAbility> Abilities         { get; set; } = new();
	public List<MainStat>        RevealedStats     { get; set; } = new();
	public EmployeeStats         Stats             { get; set; } = new();
	public bool                  IsReturningIntern { get; set; }
	public bool                  IsTutorialHire    { get; set; }
	public int                   AppearanceSeed    { get; set; }
}

/// <summary>
/// One alumnus on cooldown waiting to re-enter the inbox as a returning
/// intern. Mirrors <c>HRManager.InternAlumnus</c> (which is private) so
/// the load path can rebuild the cooldown queue.
/// </summary>
public sealed class InternAlumnusSave
{
	public string       Name              { get; set; } = "";
	public EmployeeRole Role              { get; set; }
	public int          MonthsUntilReturn { get; set; }
	public bool         GuaranteedReturn  { get; set; }
}

// ── Inventory ───────────────────────────────────────────────────────────

/// <summary>Snapshot of <see cref="InventoryManager"/> — owned items + placement + shop stock.</summary>
public sealed class InventorySave
{
	public List<InventoryItemSave>     Items                   { get; set; } = new();
	public Dictionary<ItemKind, int>   ConsumableStock         { get; set; } = new();
	public Dictionary<ItemKind, float> ConsumableRestockTimers { get; set; } = new();

	/// Number of Smoke Break consumables used so far this run. Drives the
	/// exponential price curve in <c>InventoryManager.PriceFor</c> — the
	/// next purchase is <c>min(1_000_000, 100 × 2^count)</c>.
	public int SmokeBreakUsesCount { get; set; }
}

/// <summary>
/// One owned <see cref="InventoryItem"/>. <c>PlacedAtSlotId</c> persists the
/// scene-stable slot Guid; on load the manager re-binds <c>SpawnedGameObject</c>
/// from the live scene's <c>PlacementSlot</c> graph, then calls
/// <c>ApplySlotVisibility()</c>.
/// </summary>
public sealed class InventoryItemSave
{
	public Guid     Id             { get; set; }
	public ItemKind Kind           { get; set; }
	public Guid?    PlacedAtSlotId { get; set; }
}

// ── Training (research inbox + offer queue) ─────────────────────────────

/// <summary>
/// Snapshot of <see cref="TrainingManager"/>'s discovery / offer queues.
/// Per-worker active training state lives on the workers themselves
/// (<see cref="EmployeeSave"/> / <see cref="FounderSave"/>) rather than here.
/// </summary>
public sealed class TrainingSave
{
	public List<string>         ResearchInbox { get; set; } = new();
	public List<TrainingOffer>  Offers        { get; set; } = new();
	public float                OfferTimer    { get; set; }
}

// ── Gallery (shipped games + trophies) ─────────────────────────────────

/// <summary>Snapshot of <see cref="Gallery"/>'s persistent record book.</summary>
public sealed class GalleryShipsSave
{
	public List<ShippedGame> ShippedGames { get; set; } = new();
	public List<Trophy>      Trophies     { get; set; } = new();

	/// True once the player has opened the Gallery at least once. Drives
	/// the sticky "check your game" tip that nags after first ship until
	/// they go look. Defaults false on legacy saves.
	public bool              EverOpened   { get; set; }
}

// ── Active game project ────────────────────────────────────────────────

/// <summary>
/// In-progress <see cref="GameProject"/> snapshot. Null when no project is
/// in flight (clean slate / between games). Worker assignments persist as
/// stable identifiers — <see cref="WorkerRefSave"/> — so on-load rebuild
/// can resolve back to the live PlayerStats / EmployeeNPC singletons after
/// hires are restored.
/// </summary>
public sealed class GameProjectSave
{
	public string                 Title             { get; set; } = "";
	public List<GameGenre>        Genres            { get; set; } = new();
	public Dictionary<GameDevRole, List<WorkerRefSave>> Assignments { get; set; } = new();
	public float                  DesignTime        { get; set; } = 0.6f;
	public float                  SoundTime         { get; set; } = 0.6f;
	public float                  GraphicsTime      { get; set; } = 0.6f;
	public float                  DesignProgress    { get; set; }
	public float                  SoundProgress     { get; set; }
	public float                  GraphicsProgress  { get; set; }
	public int                    DesignPoints      { get; set; }
	public int                    SoundPoints       { get; set; }
	public int                    GraphicsPoints    { get; set; }
	public GameProjectPhase       Phase             { get; set; } = GameProjectPhase.Setup;
	public int                    SetupPage         { get; set; }
}

/// <summary>
/// Stable reference to a worker for project-assignment persistence.
/// Founder = <c>IsFounder=true</c>; hires = the employee's name (unique
/// in practice — the studio doesn't have two employees with the same name).
/// </summary>
public sealed class WorkerRefSave
{
	public bool   IsFounder    { get; set; }
	public string EmployeeName { get; set; } = "";
}

// ── Letterbox / Quests (per ADR-0003) ──────────────────────────────────

/// <summary>
/// Snapshot of <c>Letterbox</c> — fan letters received this run + the
/// "first-letter ever seen" ack flag. Quests are derived from
/// <see cref="Letters"/> (filter by <see cref="FanLetter.IsQuest"/>)
/// — no separate list. Lifetime received-letter count for achievement
/// evaluation lives on <c>Achievements.LettersReceived</c>, mirroring
/// the existing <c>GamesShipped</c> / <c>TotalHires</c> counter pattern.
/// </summary>
public sealed class LetterboxSave
{
	public List<FanLetter> Letters         { get; set; } = new();
	public bool            FirstLetterSeen { get; set; }

	/// Game titles we've already evaluated for letter spawn. Independent
	/// of <see cref="Letters"/> because a 35% spawn roll can decide "no
	/// letter this time" — without tracking the decision, the next
	/// OnDayStart would re-roll and break the once-per-ship guarantee.
	public List<string>    DecidedFor      { get; set; } = new();

	/// Absolute in-game day index (Year×360 + (Month-1)×30 + Day) at
	/// which the current motivation buff expires. 0 = no active buff.
	/// Granted by reading a non-quest fan letter (70% of letters).
	public int             MotivationActiveUntilDay { get; set; }

	/// Number of buff days queued to start when the active buff ends.
	/// Reading a non-quest letter while already motivated adds days
	/// here instead of stacking on top of the current period.
	public int             MotivationQueuedDays     { get; set; }

	/// Press-release letters scheduled to land in the inbox a couple
	/// in-game weeks after a ship that fulfilled 2+ quests at once.
	/// Persisted so a save/load roundtrip mid-window doesn't drop
	/// pending press beats.
	public List<PressLetterPending> PendingPress { get; set; } = new();

	/// Unordered genre pairs the player has already shipped at least once
	/// (and received a discovery letter for, if positive synergy). Stored
	/// as "GenreA|GenreB" with the alphabetically-earlier enum name first
	/// so the key is canonical. Used by the synergy-hint system to keep
	/// each pair's "first time" letter a one-shot per run.
	public List<string>             DiscoveredPairs { get; set; } = new();
}

/// <summary>
/// Queued press-release letter, waiting on its target in-game day.
/// Spawned by <see cref="Letterbox.Tick"/> once
/// <see cref="TargetDayAbs"/> is reached.
/// </summary>
public sealed class PressLetterPending
{
	public string       GameTitle    { get; set; } = "";
	public int          QuestCount   { get; set; }
	public int          TargetDayAbs { get; set; }
}

/// <summary>
/// One fan letter. Generated from a templated copy pool keyed off the
/// referenced game's review score. Quest letters carry a non-null
/// <see cref="QuestGenre"/> the player must ship next to fulfill.
/// </summary>
public sealed class FanLetter
{
	public string         Id              { get; set; } = "";
	public string         GameTitle       { get; set; } = "";
	public GameGenre      GameGenre       { get; set; }
	public int            ReviewScore     { get; set; }
	public string         SenderName      { get; set; } = "";
	public string         Body            { get; set; } = "";
	public DateTimeOffset ReceivedAt      { get; set; }
	public bool           IsQuest         { get; set; }
	public GameGenre?     QuestGenre      { get; set; }
	public bool           QuestFulfilled  { get; set; }
	public string         FulfilledByGame { get; set; } = "";
	public bool           Read            { get; set; }

	/// True for "synergy noticed" hint letters spawned the first time a
	/// player ships a positive-synergy genre pair. Two extra genre fields
	/// (<see cref="SynergyA"/> / <see cref="SynergyB"/>) carry which pair
	/// triggered the letter so the reader can bold them inline.
	/// Discovery letters never grant the motivation buff on read — they're
	/// pure hints, no gameplay effect.
	public bool           IsDiscovery     { get; set; }
	public GameGenre?     SynergyA        { get; set; }
	public GameGenre?     SynergyB        { get; set; }
}

// ── Cross-run files (each lives in its own FileSystem.Data file) ───────

/// <summary>
/// Persisted across all runs of the game, separately from any per-run
/// save. Drives the Hall of Fame / Restart-carryover unlocks described in
/// step-5-payoff.md. The <c>MetaProgression</c> singleton that fills this
/// in isn't built yet — DTO is ready so the file scaffolding doesn't need
/// to land twice.
/// </summary>
public sealed class MetaSave
{
	public int                SchemaVersion     { get; set; } = GameSaveManager.SchemaVersion;
	public DateTimeOffset     SavedAt           { get; set; } = DateTimeOffset.UtcNow;

	/// Run scores from past playthroughs, surfaced in the Hall of Fame.
	/// Empty until <c>MetaProgression.RecordRunScore</c> exists.
	public List<RunScoreSave> HallOfFame        { get; set; } = new();

	/// Persistent unlock identifiers carried across Restart. Empty until
	/// the carryover system in step-5-payoff.md is implemented.
	public List<string>       CarryoverUnlocks  { get; set; } = new();
}

/// <summary>One past playthrough's score record. Surfaces in the Hall of Fame.</summary>
public sealed class RunScoreSave
{
	public string         StudioName { get; set; } = "";
	public long           Score      { get; set; }
	public int            EndYear    { get; set; }
	public DateTimeOffset SavedAt    { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Player preferences that follow the user, not the playthrough — audio
/// mixer levels, controls, display. Lives in its own
/// <c>FileSystem.Data</c> file so a corrupt per-run save can't take it
/// down. Mirrors <see cref="Settings"/>.
/// </summary>
public sealed class SettingsSave
{
	public int   SchemaVersion    { get; set; } = GameSaveManager.SchemaVersion;
	public float MasterVolume     { get; set; } = 1.00f;
	public float MusicVolume      { get; set; } = 0.70f;
	public float SfxVolume        { get; set; } = 0.85f;
	public float MouseSensitivity { get; set; } = 1.00f;
	public bool  InvertY          { get; set; }
	public int   FieldOfView      { get; set; } = 90;
}
