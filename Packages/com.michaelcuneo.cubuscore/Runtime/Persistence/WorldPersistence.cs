using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  [RequireComponent(typeof(CubusWorld))]
  public sealed class WorldPersistence : MonoBehaviour
  {
    [SerializeField] private string defaultSaveName = "cubus_world";
    [SerializeField] private WorldLoadTerrainPolicy loadTerrainPolicy = WorldLoadTerrainPolicy.UseSaveTerrainSystem;
    [SerializeField] private bool overrideActiveTerrainSystemOnLoad = false;
    [SerializeField] private WorldPersistenceMode persistenceMode = WorldPersistenceMode.ManualOnly;
    [SerializeField] private bool autoSaveOnlyWhenModified = true;

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private WorldStreamer streamer;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      worldRenderer = GetComponent<WorldRenderer>();
      streamer = GetComponent<WorldStreamer>();
    }

    private void Start()
    {
      if (persistenceMode == WorldPersistenceMode.AutoLoadOnly ||
          persistenceMode == WorldPersistenceMode.AutoLoadAndSave)
      {
        LoadDefaultWorld();
      }
    }

    private void OnApplicationQuit()
    {
      if (persistenceMode == WorldPersistenceMode.AutoSaveOnly ||
          persistenceMode == WorldPersistenceMode.AutoLoadAndSave)
      {
        if (!autoSaveOnlyWhenModified || world.HasBlockOverrides() ||
            world.Data.DensityVoxelOverridesByChunk.Count > 0)
        {
          string modeSaveName = GetModeSpecificDefaultSaveName(world.Settings.TerrainSystem);
          SaveWorld(modeSaveName);
        }
      }
    }

    [ContextMenu("Save Default Block World")]
    public bool SaveDefaultBlockWorld()
    {
      return SaveBlockWorld(GetModeSpecificDefaultSaveName(TerrainSystem.Block));
    }

    [ContextMenu("Save Default World")]
    public bool SaveDefaultWorld()
    {
      return SaveWorld(GetModeSpecificDefaultSaveName(world.Settings.TerrainSystem));
    }

    [ContextMenu("Load Default Block World")]
    public bool LoadDefaultBlockWorld()
    {
      return LoadBlockWorld(GetModeSpecificDefaultSaveName(TerrainSystem.Block));
    }

    [ContextMenu("Load Default World")]
    public bool LoadDefaultWorld()
    {
      string modeSaveName = GetModeSpecificDefaultSaveName(world.Settings.TerrainSystem);

      if (LoadWorld(modeSaveName))
      {
        return true;
      }

      // Backward compatibility: try the legacy unsuffixed default save name once.
      if (!string.Equals(modeSaveName, defaultSaveName, System.StringComparison.OrdinalIgnoreCase))
      {
        return LoadWorld(defaultSaveName);
      }

      return false;
    }

    [ContextMenu("Clear Default Block World Save")]
    public bool ClearDefaultBlockWorldSave()
    {
      return ClearBlockWorldSave(GetModeSpecificDefaultSaveName(TerrainSystem.Block));
    }

    [ContextMenu("Clear Default World Save")]
    public bool ClearDefaultWorldSave()
    {
      string modeSaveName = GetModeSpecificDefaultSaveName(world.Settings.TerrainSystem);
      bool clearedModeSave = ClearWorldSave(modeSaveName);

      if (string.Equals(modeSaveName, defaultSaveName, System.StringComparison.OrdinalIgnoreCase))
      {
        return clearedModeSave;
      }

      bool clearedLegacySave = ClearWorldSave(defaultSaveName);
      return clearedModeSave || clearedLegacySave;
    }

    public bool WillAutoLoadDefaultWorldOnStart()
    {
      return persistenceMode == WorldPersistenceMode.AutoLoadOnly ||
             persistenceMode == WorldPersistenceMode.AutoLoadAndSave;
    }

    public bool HasDefaultWorldSave()
    {
      string modeSaveName = GetModeSpecificDefaultSaveName(world.Settings.TerrainSystem);

      if (File.Exists(GetSavePath(modeSaveName)))
      {
        return true;
      }

      return File.Exists(GetSavePath(defaultSaveName));
    }

    [ContextMenu("Clear All World Saves")]
    public bool ClearAllWorldSaves()
    {
      string saveDirectory = GetSaveDirectoryPath();

      if (!Directory.Exists(saveDirectory))
      {
        Debug.LogWarning($"No Cubus world save directory exists to clear. Path={saveDirectory}");
        return false;
      }

      string[] saveFiles = Directory.GetFiles(saveDirectory, "*.json", SearchOption.TopDirectoryOnly);

      if (saveFiles.Length == 0)
      {
        Debug.LogWarning($"No Cubus world saves exist to clear. Path={saveDirectory}");
        return false;
      }

      for (int i = 0; i < saveFiles.Length; i++)
      {
        File.Delete(saveFiles[i]);
      }

      Debug.Log($"Cleared Cubus world saves. Path={saveDirectory}, Deleted={saveFiles.Length}");
      return true;
    }

    public bool SaveWorld(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot save world with an empty save name.");
        return false;
      }

      WorldSaveData saveData = world.CreateWorldSaveData();

      string json = JsonUtility.ToJson(saveData, true);
      string path = GetSavePath(saveName);

      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, json);

      Debug.Log(
          $"Saved Cubus world. Path={path}, Terrain={saveData.TerrainSystem}, " +
          $"BlockOverrideChunks={saveData.SavedBlockChunks.Count}, DensityOverrideChunks={saveData.SavedDensityChunks.Count}"
      );

      return true;
    }

    public bool LoadWorld(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot load world with an empty save name.");
        return false;
      }

      string path = GetSavePath(saveName);

      if (!File.Exists(path))
      {
        Debug.LogWarning($"No Cubus world save exists. Path={path}");
        return false;
      }

      string json = File.ReadAllText(path);
      if (!TryDeserializeWorldSave(json, out WorldSaveData saveData))
      {
        Debug.LogWarning($"Failed to parse Cubus world save. Path={path}");
        return false;
      }

      ApplyCompatibilityDefaults(saveData);

      if (loadTerrainPolicy == WorldLoadTerrainPolicy.SkipIfDifferent &&
          world.Settings.TerrainSystem != saveData.TerrainSystem)
      {
        Debug.LogWarning(
            $"Skipped loading Cubus world save because terrain systems differ. " +
            $"Current={world.Settings.TerrainSystem}, Save={saveData.TerrainSystem}, Path={path}"
        );
        return false;
      }

      bool applySavedTerrainSystem =
        overrideActiveTerrainSystemOnLoad &&
        loadTerrainPolicy == WorldLoadTerrainPolicy.UseSaveTerrainSystem;

      if (!overrideActiveTerrainSystemOnLoad &&
        world.Settings.TerrainSystem != saveData.TerrainSystem)
      {
        Debug.Log(
          "Loaded save while preserving active terrain mode. " +
          $"Active={world.Settings.TerrainSystem}, Save={saveData.TerrainSystem}, Path={path}"
        );
      }

      if (!world.LoadWorldSaveData(saveData, applySavedTerrainSystem))
      {
        return false;
      }

      RefreshAfterLoad();

      Debug.Log(
          $"Loaded Cubus world. Path={path}, Terrain={saveData.TerrainSystem}, " +
          $"BlockOverrideChunks={saveData.SavedBlockChunks.Count}, DensityOverrideChunks={saveData.SavedDensityChunks.Count}"
      );

      return true;
    }

    public bool ClearWorldSave(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot clear world save with an empty save name.");
        return false;
      }

      string path = GetSavePath(saveName);

      if (!File.Exists(path))
      {
        Debug.LogWarning($"No Cubus world save exists to clear. Path={path}");
        return false;
      }

      File.Delete(path);

      Debug.Log($"Cleared Cubus world save. Path={path}");
      return true;
    }

    public bool SaveBlockWorld(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot save block world with an empty save name.");
        return false;
      }

      if (world.Settings.TerrainSystem != TerrainSystem.Block)
      {
        Debug.LogWarning(
            $"Cannot save block world while terrain system is {world.Settings.TerrainSystem}. Switch to Block mode first."
        );
        return false;
      }

      // Persist as terrain-aware world save so default AutoLoad restores the same mode
      WorldSaveData saveData = world.CreateWorldSaveData();
      saveData.TerrainSystem = TerrainSystem.Block;

      string json = JsonUtility.ToJson(saveData, true);
      string path = GetSavePath(saveName);

      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, json);

      Debug.Log(
          $"Saved Cubus block world. Path={path}, " +
          $"BlockOverrideChunks={saveData.SavedBlockChunks.Count}"
      );

      return true;
    }

    public bool LoadBlockWorld(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot load block world with an empty save name.");
        return false;
      }

      string path = GetSavePath(saveName);

      if (!File.Exists(path))
      {
        Debug.LogWarning($"No Cubus block world save exists. Path={path}");
        return false;
      }

      string json = File.ReadAllText(path);
      BlockWorldSaveData saveData = JsonUtility.FromJson<BlockWorldSaveData>(json);

      if (!world.LoadBlockSaveData(saveData))
      {
        return false;
      }

      RefreshAfterLoad();

      Debug.Log(
          $"Loaded Cubus block world. Path={path}, SavedChunks={saveData.SavedBlockChunks.Count}"
      );

      return true;
    }

    public bool ClearBlockWorldSave(string saveName)
    {
      if (string.IsNullOrWhiteSpace(saveName))
      {
        Debug.LogWarning("Cannot clear block world save with an empty save name.");
        return false;
      }

      string path = GetSavePath(saveName);

      if (!File.Exists(path))
      {
        Debug.LogWarning($"No Cubus block world save exists to clear. Path={path}");
        return false;
      }

      File.Delete(path);

      Debug.Log($"Cleared Cubus block world save. Path={path}");
      return true;
    }

    private void RefreshAfterLoad()
    {
      if (worldRenderer != null)
      {
        worldRenderer.ClearAll();
      }

      if (streamer != null)
      {
        streamer.ClearStreamingState();
        streamer.ForceRefreshStreamingSet();
        return;
      }

      if (!world.IsWorldReady)
      {
        world.GenerateWorld();
      }

      if (worldRenderer != null)
      {
        worldRenderer.RebuildAll();
      }
    }

    private void ApplyCompatibilityDefaults(WorldSaveData saveData)
    {
      saveData.SavedBlockChunks ??= new System.Collections.Generic.List<SavedBlockChunk>();
      saveData.SavedDensityChunks ??= new System.Collections.Generic.List<SavedDensityChunk>();

      if (saveData.VoxelSize <= 0.0f)
      {
        saveData.VoxelSize = world.Settings.VoxelSize;
      }

      if (saveData.ViewDistanceInChunks <= 0)
      {
        saveData.ViewDistanceInChunks = world.Settings.ViewDistanceInChunks;
      }

      bool hasLegacyStyleRanges =
          saveData.SaveVersion <= 1 &&
          saveData.BlockMinChunkY == 0 &&
          saveData.BlockMaxChunkY == 0 &&
          saveData.DensityMinChunkY == 0 &&
          saveData.DensityMaxChunkY == 0;

      if (hasLegacyStyleRanges)
      {
        saveData.BlockMinChunkY = world.Settings.BlockMinChunkY;
        saveData.BlockMaxChunkY = world.Settings.BlockMaxChunkY;
        saveData.DensityMinChunkY = world.Settings.DensityMinChunkY;
        saveData.DensityMaxChunkY = world.Settings.DensityMaxChunkY;
      }

      // Older block-only saves have no terrain system metadata; infer block mode.
      if (saveData.SaveVersion <= 1 &&
          saveData.TerrainSystem == TerrainSystem.SmoothDensity &&
          saveData.SavedDensityChunks.Count == 0 &&
          saveData.SavedBlockChunks.Count > 0)
      {
        saveData.TerrainSystem = TerrainSystem.Block;
      }
    }

    private static bool TryDeserializeWorldSave(string json, out WorldSaveData saveData)
    {
      saveData = JsonUtility.FromJson<WorldSaveData>(json);

      if (saveData != null && saveData.ChunkSize > 0)
      {
        return true;
      }

      BlockWorldSaveData legacySave = JsonUtility.FromJson<BlockWorldSaveData>(json);

      if (legacySave == null || legacySave.ChunkSize <= 0)
      {
        saveData = null;
        return false;
      }

      saveData = new WorldSaveData
      {
        SaveVersion = legacySave.SaveVersion,
        TerrainSystem = TerrainSystem.Block,
        ChunkSize = legacySave.ChunkSize,
        VoxelSize = legacySave.VoxelSize,
        SavedBlockChunks = legacySave.SavedBlockChunks ?? new System.Collections.Generic.List<SavedBlockChunk>(),
        SavedDensityChunks = new System.Collections.Generic.List<SavedDensityChunk>()
      };

      return true;
    }

    private static string GetSavePath(string saveName)
    {
      string safeName = saveName.Trim();
      return Path.Combine(
          GetSaveDirectoryPath(),
          $"{safeName}.json"
      );
    }

    private string GetModeSpecificDefaultSaveName(TerrainSystem terrainSystem)
    {
      string safeBase = string.IsNullOrWhiteSpace(defaultSaveName)
          ? "cubus_world"
          : defaultSaveName.Trim();

      string suffix = terrainSystem == TerrainSystem.Block ? "block" : "density";
      return $"{safeBase}_{suffix}";
    }

    private static string GetSaveDirectoryPath()
    {
      return Path.Combine(Application.persistentDataPath, "CubusCore");
    }
  }
}