using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class CubusWorld : MonoBehaviour
  {
    [SerializeField] private WorldSettings settings = new();
    [SerializeField] private bool generateOnStartWithoutStreamer = true;

    private readonly WorldData worldData = new();
    private WorldSurfaceFinder surfaceFinder;

    public static bool SuppressGenerateOnStart { get; set; }

    public WorldSettings Settings => settings;
    public WorldData Data => worldData;

    public bool IsWorldReady { get; private set; }
    public float GenerationProgress { get; private set; }

    public bool IsGeneratingWorld { get; private set; }
    public string GenerationStatus { get; private set; } = "Idle";

    public event Action<Vector3> OnInitialTerrainReady;

    public bool IsInitialTerrainReady { get; private set; }

    public Vector3 SuggestedSpawnLocation { get; private set; }

    private void Awake()
    {
      surfaceFinder = new WorldSurfaceFinder(this);
    }

    private void Start()
    {
      if (SuppressGenerateOnStart || !generateOnStartWithoutStreamer)
      {
        Debug.Log("CubusWorld GenerateWorld on Start suppressed. External bootstrap is responsible for preparation.");
        return;
      }

      if (GetComponent<WorldStreamer>() != null)
      {
        return;
      }

      GenerateWorld();
    }

    [ContextMenu("Generate World")]
    public void GenerateWorld()
    {
      SyncBiomeMaterialLayersFromRules();

      if (!settings.TryValidateConfiguration(out string configError))
      {
        Debug.LogError($"Cannot generate Cubus world due to invalid settings: {configError}");
        return;
      }

      IsWorldReady = false;
      IsInitialTerrainReady = false;
      GenerationProgress = 0.0f;

      WorldGenerator generator = new(settings);
      generator.Generate(worldData);

      ApplyBlockOverridesToGeneratedChunks();

      GenerationProgress = 1.0f;
      IsWorldReady = true;

      Debug.Log(
          $"Cubus world database generated. " +
          $"Mode={settings.TerrainSystem}, " +
          $"BlockChunks={worldData.BlockChunks.Count}, " +
          $"DensityChunks={worldData.DensityChunks.Count}, " +
          $"BlockOverrideChunks={worldData.BlockVoxelOverridesByChunk.Count}"
      );
    }

    public IEnumerator GenerateWorldAsync()
    {
      SyncBiomeMaterialLayersFromRules();

      if (!settings.TryValidateConfiguration(out string configError))
      {
        Debug.LogError($"Cannot generate Cubus world due to invalid settings: {configError}");
        yield break;
      }

      IsGeneratingWorld = true;
      IsWorldReady = false;
      IsInitialTerrainReady = false;
      GenerationProgress = 0.0f;
      GenerationStatus = "Preparing world database...";

      WorldGenerator generator = new(settings);

      IEnumerator routine = generator.GenerateAsync(
          worldData,
          1,
          (progress, status) =>
          {
            GenerationProgress = Mathf.Clamp01(progress);
            GenerationStatus = status;
          }
      );

      while (routine.MoveNext())
      {
        yield return null;
      }

      ApplyBlockOverridesToGeneratedChunks();

      GenerationProgress = 1.0f;
      IsWorldReady = true;
      IsGeneratingWorld = false;
      GenerationStatus =
          $"Complete. BlockChunks={worldData.BlockChunks.Count}, DensityChunks={worldData.DensityChunks.Count}";

      CubusWorldStorage storage = GetComponent<CubusWorldStorage>();

      if (storage != null)
      {
        storage.SaveAfterGenerationIfEnabled();
      }

      Debug.Log(
          $"Cubus world database generated. " +
          $"Mode={settings.TerrainSystem}, " +
          $"BlockChunks={worldData.BlockChunks.Count}, " +
          $"DensityChunks={worldData.DensityChunks.Count}"
      );
    }

    public void MarkDatabaseLoaded()
    {
      IsWorldReady = true;
      IsInitialTerrainReady = false;
      GenerationProgress = 1.0f;
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
      worldData.ClearGeneratedChunks();

      IsWorldReady = false;
      IsInitialTerrainReady = false;
      GenerationProgress = 0.0f;

      Debug.Log("Cubus world cleared.");
    }

    [ContextMenu("Clear World And Overrides")]
    public void ClearWorldAndOverrides()
    {
      worldData.ClearAll();

      IsWorldReady = false;
      IsInitialTerrainReady = false;
      GenerationProgress = 0.0f;

      Debug.Log("Cubus world and overrides cleared.");
    }

    public void BroadcastInitialTerrainReady(Vector3 suggestedSpawnLocation)
    {
      SuggestedSpawnLocation = suggestedSpawnLocation;
      IsInitialTerrainReady = true;

      OnInitialTerrainReady?.Invoke(suggestedSpawnLocation);
    }

    public bool TryGetBlockChunk(Vector3Int chunkCoord, out BlockChunkData chunkData)
    {
      return worldData.BlockChunks.TryGetValue(chunkCoord, out chunkData);
    }

    public bool TryGetDensityChunk(Vector3Int chunkCoord, out DensityChunkData chunkData)
    {
      return worldData.DensityChunks.TryGetValue(chunkCoord, out chunkData);
    }

    public bool TryGetBlockOverrides(Vector3Int chunkCoord, out Dictionary<int, ushort> overrides)
    {
      return worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out overrides);
    }

    public bool TryGetDensityOverrides(
    Vector3Int chunkCoord,
    out Dictionary<int, DensityVoxelOverride> overrides)
    {
      return worldData.DensityVoxelOverridesByChunk.TryGetValue(chunkCoord, out overrides);
    }

    public Dictionary<int, ushort> CreateBlockOverrideSnapshot(Vector3Int chunkCoord)
    {
      if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out Dictionary<int, ushort> overrides))
      {
        return null;
      }

      return new Dictionary<int, ushort>(overrides);
    }

    public Dictionary<int, DensityVoxelOverride> CreateDensityOverrideSnapshot(Vector3Int chunkCoord)
    {
      if (!worldData.DensityVoxelOverridesByChunk.TryGetValue(
          chunkCoord,
          out Dictionary<int, DensityVoxelOverride> overrides))
      {
        return null;
      }

      return new Dictionary<int, DensityVoxelOverride>(overrides);
    }

    public ushort GetBlockMaterialAtWorldVoxel(Vector3Int worldVoxelCoord)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
      {
        return 0;
      }

      return chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      ).MaterialId;
    }

    public float GetDensityAtWorldVoxel(Vector3Int worldVoxelCoord)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (!worldData.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData))
      {
        float scale = Mathf.Max(0.001f, settings.DensitySampleScale);
        settings.ResolveBiomeAtWorldXZ(
          worldVoxelCoord.x,
          worldVoxelCoord.z,
          out TerrainGenerationProfileSnapshot profile,
          out byte biomeId
        );

        TerrainSample sample = TerrainSampler.Sample(
          profile,
          biomeId,
            new Vector3(
          worldVoxelCoord.x * scale,
          worldVoxelCoord.y * scale,
          worldVoxelCoord.z * scale
        )
        );

        return sample.Density;
      }

      return chunkData.GetDensity(
          localCoord.x,
          localCoord.y,
          localCoord.z
      );
    }

    public void SetBlockMaterialAtWorldVoxel(Vector3Int worldVoxelCoord, ushort materialId)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      Dictionary<int, ushort> overrides = GetOrCreateBlockOverrideChunk(chunkCoord);
      int index = VoxelMath.LocalToIndex(localCoord.x, localCoord.y, localCoord.z);
      overrides[index] = materialId;

      if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
      {
        chunkData = new BlockChunkData(chunkCoord);
        worldData.BlockChunks[chunkCoord] = chunkData;
      }

      chunkData.SetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z,
          new Voxel(materialId)
      );
    }

    public void SetDensityAtWorldVoxel(Vector3Int worldVoxelCoord, float density, ushort materialId)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      Dictionary<int, DensityVoxelOverride> overrides = GetOrCreateDensityOverrideChunk(chunkCoord);
      int index = VoxelMath.LocalToIndex(localCoord.x, localCoord.y, localCoord.z);
      overrides[index] = new DensityVoxelOverride(density, materialId);

      if (!worldData.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData))
      {
        chunkData = new DensityChunkData(chunkCoord);
        worldData.DensityChunks[chunkCoord] = chunkData;
      }

      chunkData.SetDensity(
          localCoord.x,
          localCoord.y,
          localCoord.z,
          density,
          materialId
      );
    }

    public void ApplyBlockOverridesToGeneratedChunks()
    {
      foreach ((Vector3Int chunkCoord, Dictionary<int, ushort> overrides) in worldData.BlockVoxelOverridesByChunk)
      {
        if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
        {
          chunkData = new BlockChunkData(chunkCoord);
          worldData.BlockChunks[chunkCoord] = chunkData;
        }

        foreach ((int index, ushort materialId) in overrides)
        {
          Vector3Int local = VoxelMath.IndexToLocal(index);
          chunkData.SetVoxel(local.x, local.y, local.z, new Voxel(materialId));
        }
      }
    }

    public void SyncBiomeMaterialLayersFromRules()
    {
      settings.SyncBiomeMaterialLayersFromRules();
    }

    public bool TryFindSurfaceSpawn(Vector3 requestedWorldPosition, out Vector3 spawnWorldPosition, float clearance = 2.0f)
    {
      return surfaceFinder.TryFindSurfaceSpawn(requestedWorldPosition, out spawnWorldPosition, clearance);
    }

    private Dictionary<int, ushort> GetOrCreateBlockOverrideChunk(Vector3Int chunkCoord)
    {
      if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out Dictionary<int, ushort> overrides))
      {
        overrides = new Dictionary<int, ushort>();
        worldData.BlockVoxelOverridesByChunk[chunkCoord] = overrides;
      }

      return overrides;
    }

    private Dictionary<int, DensityVoxelOverride> GetOrCreateDensityOverrideChunk(Vector3Int chunkCoord)
    {
      if (!worldData.DensityVoxelOverridesByChunk.TryGetValue(chunkCoord, out Dictionary<int, DensityVoxelOverride> overrides))
      {
        overrides = new Dictionary<int, DensityVoxelOverride>();
        worldData.DensityVoxelOverridesByChunk[chunkCoord] = overrides;
      }

      return overrides;
    }
  }
}
