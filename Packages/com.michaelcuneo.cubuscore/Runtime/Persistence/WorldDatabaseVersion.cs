namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  public static class WorldDatabaseVersion
  {
    public static bool IsSupported(int version)
    {
      return version >= WorldSaveFormat.MinimumSupportedDatabaseVersion &&
             version <= WorldSaveFormat.CurrentDatabaseVersion;
    }

    public static bool NeedsMigration(int version)
    {
      return version < WorldSaveFormat.CurrentDatabaseVersion;
    }

    public static bool TryGetUnsupportedReason(int version, out string reason)
    {
      if (version < WorldSaveFormat.MinimumSupportedDatabaseVersion)
      {
        reason = $"Database version {version} is older than the minimum supported version {WorldSaveFormat.MinimumSupportedDatabaseVersion}.";
        return true;
      }

      if (version > WorldSaveFormat.CurrentDatabaseVersion)
      {
        reason = $"Database version {version} is newer than the current supported version {WorldSaveFormat.CurrentDatabaseVersion}.";
        return true;
      }

      reason = null;
      return false;
    }
  }
}
