using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  public static class WorldSaveMigrator
  {
    public static bool TryMigrateToCurrent(
      WorldSaveData saveData,
      WorldSaveMigrationDefaults defaults,
      out string error)
    {
      error = null;

      if (saveData == null)
      {
        error = "World save data is null.";
        return false;
      }

      saveData.SavedBlockChunks ??= new System.Collections.Generic.List<SavedBlockChunk>();
      saveData.SavedDensityChunks ??= new System.Collections.Generic.List<SavedDensityChunk>();

      if (saveData.SaveVersion <= 0)
      {
        saveData.SaveVersion = WorldSaveFormat.MinimumSupportedWorldSaveVersion;
      }

      if (saveData.SaveVersion < WorldSaveFormat.MinimumSupportedWorldSaveVersion)
      {
        error = $"World save version {saveData.SaveVersion} is older than the minimum supported version {WorldSaveFormat.MinimumSupportedWorldSaveVersion}.";
        return false;
      }

      if (saveData.SaveVersion > WorldSaveFormat.CurrentWorldSaveVersion)
      {
        error = $"World save version {saveData.SaveVersion} is newer than the current supported version {WorldSaveFormat.CurrentWorldSaveVersion}.";
        return false;
      }

      while (saveData.SaveVersion < WorldSaveFormat.CurrentWorldSaveVersion)
      {
        switch (saveData.SaveVersion)
        {
          case 1:
            MigrateWorldSaveV1ToV2(saveData, defaults);
            break;

          case 2:
            MigrateWorldSaveV2ToV3(saveData, defaults);
            break;

          case 3:
            MigrateWorldSaveV3ToV4(saveData, defaults);
            break;

          default:
            error = $"No world save migration exists from version {saveData.SaveVersion}.";
            return false;
        }
      }

      return true;
    }

    public static bool TryMigrateToCurrent(
      BlockWorldSaveData saveData,
      WorldSaveMigrationDefaults defaults,
      out string error)
    {
      error = null;

      if (saveData == null)
      {
        error = "Block world save data is null.";
        return false;
      }

      saveData.SavedBlockChunks ??= new System.Collections.Generic.List<SavedBlockChunk>();

      if (saveData.SaveVersion <= 0)
      {
        saveData.SaveVersion = WorldSaveFormat.MinimumSupportedBlockWorldSaveVersion;
      }

      if (saveData.SaveVersion < WorldSaveFormat.MinimumSupportedBlockWorldSaveVersion)
      {
        error = $"Block world save version {saveData.SaveVersion} is older than the minimum supported version {WorldSaveFormat.MinimumSupportedBlockWorldSaveVersion}.";
        return false;
      }

      if (saveData.SaveVersion > WorldSaveFormat.CurrentBlockWorldSaveVersion)
      {
        error = $"Block world save version {saveData.SaveVersion} is newer than the current supported version {WorldSaveFormat.CurrentBlockWorldSaveVersion}.";
        return false;
      }

      return true;
    }

    private static void MigrateWorldSaveV1ToV2(
      WorldSaveData saveData,
      WorldSaveMigrationDefaults defaults)
    {
      if (saveData.VoxelSize <= 0.0f)
      {
        saveData.VoxelSize = Mathf.Max(0.01f, defaults.VoxelSize);
      }

      if (saveData.ViewDistanceInChunks <= 0)
      {
        saveData.ViewDistanceInChunks = Mathf.Max(1, defaults.ViewDistanceInChunks);
      }

      bool hasLegacyStyleRanges =
        saveData.BlockMinChunkY == 0 &&
        saveData.BlockMaxChunkY == 0 &&
        saveData.DensityMinChunkY == 0 &&
        saveData.DensityMaxChunkY == 0;

      if (hasLegacyStyleRanges)
      {
        saveData.BlockMinChunkY = defaults.BlockMinChunkY;
        saveData.BlockMaxChunkY = defaults.BlockMaxChunkY;
        saveData.DensityMinChunkY = defaults.DensityMinChunkY;
        saveData.DensityMaxChunkY = defaults.DensityMaxChunkY;
      }

      if (saveData.TerrainSystem == TerrainSystem.SmoothDensity &&
          saveData.SavedDensityChunks.Count == 0 &&
          saveData.SavedBlockChunks.Count > 0)
      {
        saveData.TerrainSystem = TerrainSystem.Block;
      }

      saveData.SaveVersion = 2;
    }

    private static void MigrateWorldSaveV2ToV3(
      WorldSaveData saveData,
      WorldSaveMigrationDefaults defaults)
    {
      if (saveData.ViewDistanceInChunks <= 0)
      {
        saveData.ViewDistanceInChunks = Mathf.Max(1, defaults.ViewDistanceInChunks);
      }

      saveData.SaveVersion = 3;
    }

    private static void MigrateWorldSaveV3ToV4(
      WorldSaveData saveData,
      WorldSaveMigrationDefaults defaults)
    {
      if (saveData.BlockMinChunkY == 0 && saveData.BlockMaxChunkY == 0)
      {
        saveData.BlockMinChunkY = defaults.BlockMinChunkY;
        saveData.BlockMaxChunkY = defaults.BlockMaxChunkY;
      }

      if (saveData.DensityMinChunkY == 0 && saveData.DensityMaxChunkY == 0)
      {
        saveData.DensityMinChunkY = defaults.DensityMinChunkY;
        saveData.DensityMaxChunkY = defaults.DensityMaxChunkY;
      }

      saveData.SaveVersion = 4;
    }
  }

  public readonly struct WorldSaveMigrationDefaults
  {
    public readonly float VoxelSize;
    public readonly int ViewDistanceInChunks;
    public readonly int BlockMinChunkY;
    public readonly int BlockMaxChunkY;
    public readonly int DensityMinChunkY;
    public readonly int DensityMaxChunkY;

    public WorldSaveMigrationDefaults(
      float voxelSize,
      int viewDistanceInChunks,
      int blockMinChunkY,
      int blockMaxChunkY,
      int densityMinChunkY,
      int densityMaxChunkY)
    {
      VoxelSize = voxelSize;
      ViewDistanceInChunks = viewDistanceInChunks;
      BlockMinChunkY = blockMinChunkY;
      BlockMaxChunkY = blockMaxChunkY;
      DensityMinChunkY = densityMinChunkY;
      DensityMaxChunkY = densityMaxChunkY;
    }
  }
}
