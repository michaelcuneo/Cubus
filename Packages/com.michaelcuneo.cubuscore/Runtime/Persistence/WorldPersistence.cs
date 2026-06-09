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
          SaveDefaultWorld();
        }
      }
    }

    [ContextMenu("Save Default Block World")]
    public bool SaveDefaultBlockWorld()
    {
      return SaveBlockWorld(defaultSaveName);
    }

    [ContextMenu("Save Default World")]
    public bool SaveDefaultWorld()
    {
      return SaveWorld(defaultSaveName);
    }

    [ContextMenu("Load Default Block World")]
    public bool LoadDefaultBlockWorld()
    {
      return LoadBlockWorld(defaultSaveName);
    }

    [ContextMenu("Load Default World")]
    public bool LoadDefaultWorld()
    {
      return LoadWorld(defaultSaveName);
    }

    [ContextMenu("Clear Default Block World Save")]
    public bool ClearDefaultBlockWorldSave()
    {
      return ClearBlockWorldSave(defaultSaveName);
    }

    [ContextMenu("Clear Default World Save")]
    public bool ClearDefaultWorldSave()
    {
      return ClearWorldSave(defaultSaveName);
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
          $"BlockChunks={saveData.SavedBlockChunks.Count}, DensityChunks={saveData.SavedDensityChunks.Count}"
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

      bool applySavedTerrainSystem = loadTerrainPolicy == WorldLoadTerrainPolicy.UseSaveTerrainSystem;

      if (!world.LoadWorldSaveData(saveData, applySavedTerrainSystem))
      {
        return false;
      }

      RefreshAfterLoad();

      Debug.Log(
          $"Loaded Cubus world. Path={path}, Terrain={saveData.TerrainSystem}, " +
          $"BlockChunks={saveData.SavedBlockChunks.Count}, DensityChunks={saveData.SavedDensityChunks.Count}"
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

      BlockWorldSaveData saveData = world.CreateBlockSaveData();

      string json = JsonUtility.ToJson(saveData, true);
      string path = GetSavePath(saveName);

      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, json);

      Debug.Log(
          $"Saved Cubus block world. Path={path}, SavedChunks={saveData.SavedBlockChunks.Count}"
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

      world.GenerateWorld();

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
          Application.persistentDataPath,
          "CubusCore",
          $"{safeName}.json"
      );
    }
  }
}