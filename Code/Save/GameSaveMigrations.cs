/// <summary>
/// Forward-migration chain for <see cref="GameSave"/> files.
///
/// On load, <see cref="GameSaveManager.TryLoadRun"/> compares the file's
/// <see cref="GameSave.SchemaVersion"/> to <see cref="GameSaveManager.SchemaVersion"/>.
/// For every step the file is behind, the matching <c>MigrateFromVN</c>
/// method runs in order, returning the upgraded DTO.
///
/// **Add a migrator on every breaking schema change.** A new field with
/// a sensible default does not need one (s&amp;box's deserializer fills
/// the default automatically). A removed field, a renamed field, a type
/// change, or a value-shape change does.
///
/// Migrators must be pure and idempotent — same input, same output, no
/// side-effects on game state. Game state is reconstructed AFTER the
/// migrator chain finishes, in <c>LoadFrom</c> on each owning system.
/// </summary>
public static class GameSaveMigrations
{
	/// <summary>
	/// Run the full migration chain over <paramref name="dto"/>, bringing
	/// it from its file-recorded SchemaVersion up to
	/// <see cref="GameSaveManager.SchemaVersion"/>.
	/// </summary>
	/// <returns>
	/// The upgraded DTO, or <c>null</c> if the file is from a future
	/// SchemaVersion (downgrades are not supported).
	/// </returns>
	public static GameSave Migrate( GameSave dto )
	{
		if ( dto is null ) return null;
		if ( dto.SchemaVersion > GameSaveManager.SchemaVersion ) return null;

		// Step the file forward one version at a time. Each MigrateFromVN
		// runs only if the recorded version is strictly less than N+1, so
		// repeated calls are safe and the chain auto-extends as schemas
		// evolve.
		if ( dto.SchemaVersion < 2 ) MigrateFromV1( dto );
		if ( dto.SchemaVersion < 3 ) MigrateFromV2( dto );

		dto.SchemaVersion = GameSaveManager.SchemaVersion;
		return dto;
	}

	/// v1 → v2: <c>AchievementsSave.PeakBalance</c> introduced for the
	/// "Peak Wallet" leaderboard (ADR-0002). Legacy v1 saves have no
	/// record of the player's all-time wallet peak, so we seed it from
	/// the live <c>EconomySave.Money</c> at load time — best available
	/// proxy for "highest balance ever held this run." Future runs will
	/// ratchet from there.
	static void MigrateFromV1( GameSave dto )
	{
		if ( dto.Achievements is null ) dto.Achievements = new AchievementsSave();
		if ( dto.Achievements.PeakBalance == 0 && dto.Economy is not null )
			dto.Achievements.PeakBalance = dto.Economy.Money;
	}

	/// v2 → v3: <c>LetterboxSave</c> introduced for fan letters + quests
	/// (ADR-0003). New field has a safe DTO default (empty list,
	/// <c>FirstLetterSeen = false</c>), so this migrator is effectively a
	/// no-op — kept for symmetry and future expansion. The schema bump
	/// also coincides with the SavePanel extraction + 3-manual-slot layout
	/// (Phase 1 of ADR-0003), neither of which needs data migration.
	static void MigrateFromV2( GameSave dto )
	{
		if ( dto.Letterbox is null ) dto.Letterbox = new LetterboxSave();
	}
}
