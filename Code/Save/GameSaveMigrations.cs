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

		// Per ADR-0001 we ship at SchemaVersion = 1 with no prior format
		// to migrate from. Migrators land here as schemas evolve.

		dto.SchemaVersion = GameSaveManager.SchemaVersion;
		return dto;
	}
}
