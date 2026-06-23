using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using NUnit.Framework;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 2 - World/version migration layer.
  /// Validates the version gates (supported / needs-migration / unsupported) and
  /// that an old-format save is upgraded to the current version without loss.
  /// </summary>
  [TestFixture]
  public sealed class WorldMigrationTests
  {
    private static WorldSaveMigrationDefaults BuildDefaults()
    {
      return new WorldSaveMigrationDefaults(
        voxelSize: 1.0f,
        viewDistanceInChunks: 4,
        blockMinChunkY: -2,
        blockMaxChunkY: 4,
        densityMinChunkY: -2,
        densityMaxChunkY: 4);
    }

    [Test]
    public void DatabaseVersion_CurrentIsSupported_AndNeedsNoMigration()
    {
      int current = WorldSaveFormat.CurrentDatabaseVersion;

      Assert.IsTrue(WorldDatabaseVersion.IsSupported(current));
      Assert.IsFalse(WorldDatabaseVersion.NeedsMigration(current));
      Assert.IsFalse(WorldDatabaseVersion.TryGetUnsupportedReason(current, out _));
    }

    [Test]
    public void DatabaseVersion_NewerThanCurrent_IsRejected()
    {
      int future = WorldSaveFormat.CurrentDatabaseVersion + 1;

      Assert.IsFalse(WorldDatabaseVersion.IsSupported(future));
      Assert.IsTrue(WorldDatabaseVersion.TryGetUnsupportedReason(future, out string reason));
      Assert.IsNotEmpty(reason);
    }

    [Test]
    public void DatabaseVersion_OlderThanMinimum_IsRejected()
    {
      int tooOld = WorldSaveFormat.MinimumSupportedDatabaseVersion - 1;

      Assert.IsFalse(WorldDatabaseVersion.IsSupported(tooOld));
      Assert.IsTrue(WorldDatabaseVersion.TryGetUnsupportedReason(tooOld, out string reason));
      Assert.IsNotEmpty(reason);
    }

    [Test]
    public void WorldSave_MigratesLegacyVersion_ToCurrent()
    {
      var legacy = new WorldSaveData
      {
        SaveVersion = WorldSaveFormat.MinimumSupportedWorldSaveVersion,
        TerrainSystem = TerrainSystem.SmoothDensity,
        VoxelSize = 0.0f,            // forces V1->V2 default fill
        ViewDistanceInChunks = 0     // forces default fill
      };

      bool migrated = WorldSaveMigrator.TryMigrateToCurrent(legacy, BuildDefaults(), out string error);

      Assert.IsTrue(migrated, $"Legacy world save failed to migrate: {error}");
      Assert.AreEqual(WorldSaveFormat.CurrentWorldSaveVersion, legacy.SaveVersion);
      Assert.Greater(legacy.VoxelSize, 0.0f, "Migration did not backfill VoxelSize.");
      Assert.GreaterOrEqual(legacy.ViewDistanceInChunks, 1, "Migration did not backfill ViewDistanceInChunks.");
    }

    [Test]
    public void WorldSave_AlreadyCurrent_RemainsCurrent()
    {
      var save = new WorldSaveData
      {
        SaveVersion = WorldSaveFormat.CurrentWorldSaveVersion,
        TerrainSystem = TerrainSystem.SmoothDensity,
        VoxelSize = 1.0f,
        ViewDistanceInChunks = 6
      };

      bool migrated = WorldSaveMigrator.TryMigrateToCurrent(save, BuildDefaults(), out string error);

      Assert.IsTrue(migrated, $"Current world save reported migration failure: {error}");
      Assert.AreEqual(WorldSaveFormat.CurrentWorldSaveVersion, save.SaveVersion);
      Assert.AreEqual(6, save.ViewDistanceInChunks, "Migration mutated already-valid data.");
    }

    [Test]
    public void WorldSave_NewerThanCurrent_FailsMigration()
    {
      var save = new WorldSaveData
      {
        SaveVersion = WorldSaveFormat.CurrentWorldSaveVersion + 1,
        TerrainSystem = TerrainSystem.SmoothDensity
      };

      bool migrated = WorldSaveMigrator.TryMigrateToCurrent(save, BuildDefaults(), out string error);

      Assert.IsFalse(migrated, "A newer-than-current save should not migrate.");
      Assert.IsNotEmpty(error);
    }

    [Test]
    public void WorldSave_Null_FailsGracefully()
    {
      bool migrated = WorldSaveMigrator.TryMigrateToCurrent((WorldSaveData)null, BuildDefaults(), out string error);

      Assert.IsFalse(migrated);
      Assert.IsNotEmpty(error);
    }
  }
}
