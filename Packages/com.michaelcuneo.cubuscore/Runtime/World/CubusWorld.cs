using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class CubusWorld : MonoBehaviour
  {
    [SerializeField] private WorldSettings settings = new();

    private readonly WorldData worldData = new();
    private WorldSurfaceFinder surfaceFinder;

    public WorldSettings Settings => settings;
    public WorldData Data => worldData;

    public bool IsWorldReady { get; private set; }
    public float GenerationProgress { get; private set; }

    public event Action<Vector3> OnInitialTerrainReady;

    public bool IsInitialTerrainReady { get; private set; }

    public Vector3 SuggestedSpawnLocation { get; private set; }

    private void Awake()
    {
      surfaceFinder = new WorldSurfaceFinder(this);
    }

    private void Start()
    {
      if (GetComponent<WorldStreamer>() != null)
      {
        return;
      }

      GenerateWorld();
    }

    [ContextMenu("Generate World")]
    public void GenerateWorld()
    {
      IsWorldReady = false;
      GenerationProgress = 0.0f;

      WorldGenerator generator = new(settings);
      generator.Generate(worldData);

      ApplyBlockOverridesToGeneratedChunks();

      GenerationProgress = 1.0f;
      IsWorldReady = true;

      if (FindInitialSpawnLocation(
          Vector3.zero,
          64.0f,
          512,
          512,
          2.0f,
          out Vector3 spawnLocation
      ))
      {
        BroadcastInitialTerrainReady(spawnLocation);
      }

      Debug.Log(
          $"Cubus world generated. " +
          $"Mode={settings.TerrainSystem}, " +
          $"BlockChunks={worldData.BlockChunks.Count}, " +
          $"DensityChunks={worldData.DensityChunks.Count}, " +
          $"BlockOverrideChunks={worldData.BlockVoxelOverridesByChunk.Count}"
      );
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
        TerrainSampler sampler = new(
            settings.GetActiveGenerationProfile(),
            settings.GetActiveBiomeId()
        );

        TerrainSample sample = sampler.Sample(
            new Vector3(
          worldVoxelCoord.x * scale,
          worldVoxelCoord.y * scale,
          worldVoxelCoord.z * scale
            )
        );

        return sample.Density;
      }

      return chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      ).Density;
    }

    public DensityVoxel GetDensityVoxelAtWorldVoxel(Vector3Int worldVoxelCoord)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (!worldData.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData))
      {
        DensityChunkData generatedChunk = DensityChunkBuilder.GenerateChunkData(
            chunkCoord,
            WorldGenerationSnapshot.FromSettings(settings),
            CreateDensityOverrideSnapshot(chunkCoord)
        );

        worldData.DensityChunks[chunkCoord] = generatedChunk;
        chunkData = generatedChunk;
      }

      return chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      );
    }

    private static void AddNeighbourDirtyChunksIfBoundaryVoxel(
    Vector3Int worldVoxel,
    HashSet<Vector3Int> dirtyChunks)
    {
      if (dirtyChunks == null)
      {
        return;
      }

      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxel);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxel);

      int max = VoxelConstants.ChunkSize - 1;

      if (localCoord.x == 0)
      {
        dirtyChunks.Add(chunkCoord + Vector3Int.left);
      }
      else if (localCoord.x == max)
      {
        dirtyChunks.Add(chunkCoord + Vector3Int.right);
      }

      if (localCoord.y == 0)
      {
        dirtyChunks.Add(chunkCoord + Vector3Int.down);
      }
      else if (localCoord.y == max)
      {
        dirtyChunks.Add(chunkCoord + Vector3Int.up);
      }

      if (localCoord.z == 0)
      {
        dirtyChunks.Add(chunkCoord + new Vector3Int(0, 0, -1));
      }
      else if (localCoord.z == max)
      {
        dirtyChunks.Add(chunkCoord + new Vector3Int(0, 0, 1));
      }
    }

    public ushort GetDensityMaterialAtWorldVoxel(Vector3Int worldVoxelCoord)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (!worldData.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData))
      {
        TerrainSampler sampler = new(
            settings.GetActiveGenerationProfile(),
            settings.GetActiveBiomeId()
        );

        TerrainSample sample = sampler.Sample(
            new Vector3(
                worldVoxelCoord.x,
                worldVoxelCoord.y,
                worldVoxelCoord.z
            )
        );

        return sample.Density > 0.0f
            ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
            : (ushort)0;
      }

      return chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      ).MaterialId;
    }

    public bool SetBlockMaterialAtWorldVoxel(Vector3Int worldVoxelCoord, ushort materialId)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      int voxelIndex = VoxelMath.FlattenIndex(
          localCoord.x,
          localCoord.y,
          localCoord.z
      );

      if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
      {
        chunkData = new BlockChunkData(chunkCoord);

        WorldGenerator generator = new(settings);

        if (worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out var existingOverrides))
        {
          generator.GenerateBlockChunkDataWithOverrides(chunkData, existingOverrides);
        }
        else
        {
          generator.GenerateBlockChunkData(chunkData);
        }

        if (chunkData.HasAnySolidVoxel())
        {
          worldData.BlockChunks[chunkCoord] = chunkData;
        }
      }

      ushort previousMaterial = chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      ).MaterialId;

      if (previousMaterial == materialId)
      {
        return false;
      }

      if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out var chunkOverrides))
      {
        chunkOverrides = new Dictionary<int, ushort>();
        worldData.BlockVoxelOverridesByChunk.Add(chunkCoord, chunkOverrides);
      }

      chunkOverrides[voxelIndex] = materialId;

      chunkData.SetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z,
          new Voxel(materialId)
      );

      if (chunkData.HasAnySolidVoxel())
      {
        worldData.BlockChunks[chunkCoord] = chunkData;
      }
      else
      {
        worldData.BlockChunks.Remove(chunkCoord);
      }

      return true;
    }

    private void ApplyBlockOverridesToGeneratedChunks()
    {
      if (settings.TerrainSystem != TerrainSystem.Block)
      {
        return;
      }

      WorldGenerator generator = new(settings);

      foreach (KeyValuePair<Vector3Int, Dictionary<int, ushort>> overridePair in worldData.BlockVoxelOverridesByChunk)
      {
        Vector3Int chunkCoord = overridePair.Key;
        Dictionary<int, ushort> overrides = overridePair.Value;

        if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
        {
          chunkData = new BlockChunkData(chunkCoord);
          generator.GenerateBlockChunkDataWithOverrides(chunkData, overrides);

          if (chunkData.HasAnySolidVoxel())
          {
            worldData.BlockChunks[chunkCoord] = chunkData;
          }

          continue;
        }

        foreach (KeyValuePair<int, ushort> voxelOverride in overrides)
        {
          int voxelIndex = voxelOverride.Key;

          if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          int x = voxelIndex % VoxelConstants.ChunkSize;
          int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
          int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

          chunkData.SetVoxel(
              x,
              y,
              z,
              new Voxel(voxelOverride.Value)
          );
        }

        if (!chunkData.HasAnySolidVoxel())
        {
          worldData.BlockChunks.Remove(chunkCoord);
        }
      }
    }

    public bool SetDensityVoxelAtWorldVoxel(
    Vector3Int worldVoxelCoord,
    float density,
    ushort materialId)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      int voxelIndex = VoxelMath.FlattenIndex(
          localCoord.x,
          localCoord.y,
          localCoord.z
      );

      if (!worldData.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData))
      {
        chunkData = DensityChunkBuilder.GenerateChunkData(
            chunkCoord,
            WorldGenerationSnapshot.FromSettings(settings),
            CreateDensityOverrideSnapshot(chunkCoord)
        );

        worldData.DensityChunks[chunkCoord] = chunkData;
      }

      DensityVoxel previous = chunkData.GetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z
      );

      if (Mathf.Approximately(previous.Density, density) &&
          previous.MaterialId == materialId)
      {
        return false;
      }

      DensityVoxel newVoxel = new(density, materialId);

      chunkData.SetVoxel(
          localCoord.x,
          localCoord.y,
          localCoord.z,
          newVoxel
      );

      if (!worldData.DensityVoxelOverridesByChunk.TryGetValue(
          chunkCoord,
          out Dictionary<int, DensityVoxelOverride> overrides))
      {
        overrides = new Dictionary<int, DensityVoxelOverride>();
        worldData.DensityVoxelOverridesByChunk.Add(chunkCoord, overrides);
      }

      overrides[voxelIndex] = new DensityVoxelOverride(
          density,
          materialId
      );

      return true;
    }

    public bool AddBlock(Vector3Int worldVoxelCoord, ushort materialId)
    {
      if (materialId == 0)
      {
        return false;
      }

      return SetBlockMaterialAtWorldVoxel(worldVoxelCoord, materialId);
    }

    public bool RemoveBlock(Vector3Int worldVoxelCoord)
    {
      return SetBlockMaterialAtWorldVoxel(worldVoxelCoord, 0);
    }

    public int AddBlocksInSphere(
        Vector3 worldVoxelCenter,
        float radiusVoxels,
        ushort materialId,
        HashSet<Vector3Int> dirtyChunks)
    {
      return EditBlocksInSphere(worldVoxelCenter, radiusVoxels, materialId, true, dirtyChunks);
    }

    public int RemoveBlocksInSphere(
        Vector3 worldVoxelCenter,
        float radiusVoxels,
        HashSet<Vector3Int> dirtyChunks)
    {
      return EditBlocksInSphere(worldVoxelCenter, radiusVoxels, 0, false, dirtyChunks);
    }

    public int AddBlocksInBox(
        BoundsInt voxelBounds,
        ushort materialId,
        HashSet<Vector3Int> dirtyChunks)
    {
      if (materialId == 0)
      {
        return 0;
      }

      return EditBlocksInBox(voxelBounds, materialId, true, dirtyChunks);
    }

    public int RemoveBlocksInBox(
        BoundsInt voxelBounds,
        HashSet<Vector3Int> dirtyChunks)
    {
      return EditBlocksInBox(voxelBounds, 0, false, dirtyChunks);
    }

    public int AddDensityInSphere(
    Vector3 worldVoxelCenter,
    float radiusVoxels,
    float densityDelta,
    ushort materialId,
    HashSet<Vector3Int> dirtyChunks)
    {
      return ModifyDensityInSphere(
          worldVoxelCenter,
          radiusVoxels,
          Mathf.Abs(densityDelta),
          materialId,
          dirtyChunks
      );
    }

    public int RemoveDensityInSphere(
        Vector3 worldVoxelCenter,
        float radiusVoxels,
        float densityDelta,
        HashSet<Vector3Int> dirtyChunks)
    {
      return ModifyDensityInSphere(
          worldVoxelCenter,
          radiusVoxels,
          -Mathf.Abs(densityDelta),
          0,
          dirtyChunks
      );
    }

    private int ModifyDensityInSphere(
        Vector3 worldVoxelCenter,
        float radiusVoxels,
        float densityDelta,
        ushort materialId,
        HashSet<Vector3Int> dirtyChunks)
    {
      float safeRadius = Mathf.Max(0.01f, radiusVoxels);
      float radiusSquared = safeRadius * safeRadius;

      Vector3Int min = new(
          Mathf.FloorToInt(worldVoxelCenter.x - safeRadius),
          Mathf.FloorToInt(worldVoxelCenter.y - safeRadius),
          Mathf.FloorToInt(worldVoxelCenter.z - safeRadius)
      );

      Vector3Int max = new(
          Mathf.CeilToInt(worldVoxelCenter.x + safeRadius),
          Mathf.CeilToInt(worldVoxelCenter.y + safeRadius),
          Mathf.CeilToInt(worldVoxelCenter.z + safeRadius)
      );

      int changedCount = 0;

      for (int z = min.z; z <= max.z; z++)
      {
        for (int y = min.y; y <= max.y; y++)
        {
          for (int x = min.x; x <= max.x; x++)
          {
            Vector3 voxel = new(x + 0.5f, y + 0.5f, z + 0.5f);
            float distanceSquared = (voxel - worldVoxelCenter).sqrMagnitude;

            if (distanceSquared > radiusSquared)
            {
              continue;
            }

            Vector3Int worldVoxel = new(x, y, z);

            DensityVoxel currentVoxel = GetDensityVoxelAtWorldVoxel(worldVoxel);

            float distance = Mathf.Sqrt(distanceSquared);
            float normalizedDistance = Mathf.Clamp01(distance / safeRadius);
            float t = 1.0f - normalizedDistance;
            float smoothFalloff = t * t * (3.0f - 2.0f * t);

            float nextDensity = currentVoxel.Density + densityDelta * smoothFalloff;

            ushort nextMaterial;
            if (nextDensity > 0.0f)
            {
              if (densityDelta >= 0.0f)
              {
                ushort safeAddMaterial = materialId != 0
                    ? (ushort)Mathf.Clamp(materialId, 1, 65535)
                    : currentVoxel.MaterialId != 0 ? currentVoxel.MaterialId : (ushort)1;
                nextMaterial = safeAddMaterial;
              }
              else
              {
                nextMaterial = currentVoxel.MaterialId != 0 ? currentVoxel.MaterialId : (ushort)1;
              }
            }
            else
            {
              nextMaterial = 0;
            }

            if (SetDensityVoxelAtWorldVoxel(
                worldVoxel,
                nextDensity,
                nextMaterial))
            {
              changedCount++;

              Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxel);
              dirtyChunks?.Add(chunkCoord);
              AddNeighbourDirtyChunksIfBoundaryVoxel(worldVoxel, dirtyChunks);
            }
          }
        }
      }

      return changedCount;
    }

    private int EditBlocksInSphere(
        Vector3 worldVoxelCenter,
        float radiusVoxels,
        ushort materialId,
        bool add,
        HashSet<Vector3Int> dirtyChunks)
    {
      if (radiusVoxels <= 0.0f)
      {
        return 0;
      }

      int radiusInt = Mathf.CeilToInt(radiusVoxels);

      Vector3Int centerVoxel = new(
          Mathf.FloorToInt(worldVoxelCenter.x),
          Mathf.FloorToInt(worldVoxelCenter.y),
          Mathf.FloorToInt(worldVoxelCenter.z)
      );

      float radiusSquared = radiusVoxels * radiusVoxels;
      int changedCount = 0;

      for (int y = centerVoxel.y - radiusInt; y <= centerVoxel.y + radiusInt; y++)
      {
        for (int z = centerVoxel.z - radiusInt; z <= centerVoxel.z + radiusInt; z++)
        {
          for (int x = centerVoxel.x - radiusInt; x <= centerVoxel.x + radiusInt; x++)
          {
            Vector3 voxelCenter = new(x + 0.5f, y + 0.5f, z + 0.5f);
            float distanceSquared = (voxelCenter - worldVoxelCenter).sqrMagnitude;

            if (distanceSquared > radiusSquared)
            {
              continue;
            }

            Vector3Int worldVoxel = new(x, y, z);

            ushort currentMaterial = GetBlockMaterialAtWorldVoxel(worldVoxel);

            if (add)
            {
              if (currentMaterial != 0)
              {
                continue;
              }
            }
            else
            {
              if (currentMaterial == 0)
              {
                continue;
              }
            }

            if (SetBlockMaterialAtWorldVoxel(worldVoxel, materialId))
            {
              changedCount++;
              AddDirtyChunkAndNeighbours(worldVoxel, dirtyChunks);
            }
          }
        }
      }

      return changedCount;
    }

    private int EditBlocksInBox(
        BoundsInt voxelBounds,
        ushort materialId,
        bool add,
        HashSet<Vector3Int> dirtyChunks)
    {
      int changedCount = 0;

      foreach (Vector3Int worldVoxel in voxelBounds.allPositionsWithin)
      {
        ushort currentMaterial = GetBlockMaterialAtWorldVoxel(worldVoxel);

        if (add)
        {
          if (currentMaterial != 0)
          {
            continue;
          }
        }
        else
        {
          if (currentMaterial == 0)
          {
            continue;
          }
        }

        if (SetBlockMaterialAtWorldVoxel(worldVoxel, materialId))
        {
          changedCount++;
          AddDirtyChunkAndNeighbours(worldVoxel, dirtyChunks);
        }
      }

      return changedCount;
    }

    public void AddDirtyChunkAndNeighbours(Vector3Int worldVoxelCoord, HashSet<Vector3Int> dirtyChunks)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);

      dirtyChunks.Add(chunkCoord);
      dirtyChunks.Add(chunkCoord + Vector3Int.right);
      dirtyChunks.Add(chunkCoord + Vector3Int.left);
      dirtyChunks.Add(chunkCoord + Vector3Int.up);
      dirtyChunks.Add(chunkCoord + Vector3Int.down);
      dirtyChunks.Add(chunkCoord + new Vector3Int(0, 0, 1));
      dirtyChunks.Add(chunkCoord + new Vector3Int(0, 0, -1));
    }

    private void ApplyBlockVoxelOverridesToChunk(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(chunkCoord, out var overrides))
      {
        return;
      }

      foreach (var pair in overrides)
      {
        int voxelIndex = pair.Key;
        ushort materialId = pair.Value;

        int x = voxelIndex % VoxelConstants.ChunkSize;
        int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
        int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

        chunkData.SetVoxel(x, y, z, new Voxel(materialId));
      }
    }

    public BlockWorldSaveData CreateBlockSaveData()
    {
      BlockWorldSaveData saveData = new()
      {
        SaveVersion = 1,
        ChunkSize = VoxelConstants.ChunkSize,
        VoxelSize = settings.VoxelSize
      };

      foreach (KeyValuePair<Vector3Int, Dictionary<int, ushort>> chunkPair in worldData.BlockVoxelOverridesByChunk)
      {
        SavedBlockChunk savedChunk = new()
        {
          ChunkCoord = chunkPair.Key
        };

        foreach (KeyValuePair<int, ushort> voxelPair in chunkPair.Value)
        {
          int voxelIndex = voxelPair.Key;

          if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          savedChunk.Voxels.Add(
              new SavedBlockVoxel(
                  voxelIndex,
                  voxelPair.Value
              )
          );
        }

        if (savedChunk.Voxels.Count > 0)
        {
          saveData.SavedBlockChunks.Add(savedChunk);
        }
      }

      return saveData;
    }

    public WorldSaveData CreateWorldSaveData()
    {
      WorldSaveData saveData = new()
      {
        SaveVersion = 2,
        TerrainSystem = settings.TerrainSystem,
        ChunkSize = VoxelConstants.ChunkSize,
        VoxelSize = settings.VoxelSize,
        ViewDistanceInChunks = settings.ViewDistanceInChunks,
        BlockMinChunkY = settings.BlockMinChunkY,
        BlockMaxChunkY = settings.BlockMaxChunkY,
        DensityMinChunkY = settings.DensityMinChunkY,
        DensityMaxChunkY = settings.DensityMaxChunkY
      };

      foreach (KeyValuePair<Vector3Int, Dictionary<int, ushort>> chunkPair in worldData.BlockVoxelOverridesByChunk)
      {
        SavedBlockChunk savedChunk = new()
        {
          ChunkCoord = chunkPair.Key
        };

        foreach (KeyValuePair<int, ushort> voxelPair in chunkPair.Value)
        {
          int voxelIndex = voxelPair.Key;

          if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          savedChunk.Voxels.Add(new SavedBlockVoxel(voxelIndex, voxelPair.Value));
        }

        if (savedChunk.Voxels.Count > 0)
        {
          saveData.SavedBlockChunks.Add(savedChunk);
        }
      }

      foreach (KeyValuePair<Vector3Int, Dictionary<int, DensityVoxelOverride>> chunkPair in worldData.DensityVoxelOverridesByChunk)
      {
        SavedDensityChunk savedChunk = new()
        {
          ChunkCoord = chunkPair.Key
        };

        foreach (KeyValuePair<int, DensityVoxelOverride> voxelPair in chunkPair.Value)
        {
          int voxelIndex = voxelPair.Key;

          if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          savedChunk.Voxels.Add(
              new SavedDensityVoxel(
                  voxelIndex,
                  voxelPair.Value.Density,
                  voxelPair.Value.MaterialId
              )
          );
        }

        if (savedChunk.Voxels.Count > 0)
        {
          saveData.SavedDensityChunks.Add(savedChunk);
        }
      }

      return saveData;
    }

    private void ApplyLoadedBlockOverridesToGeneratedChunks()
    {
      foreach (KeyValuePair<Vector3Int, Dictionary<int, ushort>> overridePair in worldData.BlockVoxelOverridesByChunk)
      {
        Vector3Int chunkCoord = overridePair.Key;

        if (!worldData.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
        {
          chunkData = new BlockChunkData(chunkCoord);

          WorldGenerator generator = new(settings);
          generator.GenerateBlockChunkDataWithOverrides(chunkData, overridePair.Value);

          if (chunkData.HasAnySolidVoxel())
          {
            worldData.BlockChunks[chunkCoord] = chunkData;
          }

          continue;
        }

        foreach (KeyValuePair<int, ushort> voxelOverride in overridePair.Value)
        {
          int voxelIndex = voxelOverride.Key;

          if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          int x = voxelIndex % VoxelConstants.ChunkSize;
          int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
          int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

          chunkData.SetVoxel(x, y, z, new Voxels.Voxel(voxelOverride.Value));
        }

        if (!chunkData.HasAnySolidVoxel())
        {
          worldData.BlockChunks.Remove(chunkCoord);
        }
      }
    }

    public bool LoadBlockSaveData(BlockWorldSaveData saveData)
    {
      if (saveData == null)
      {
        Debug.LogWarning("Cannot load null block world save data.");
        return false;
      }

      if (saveData.ChunkSize != VoxelConstants.ChunkSize)
      {
        Debug.LogWarning(
            $"Block world save chunk size mismatch. Save={saveData.ChunkSize}, Current={VoxelConstants.ChunkSize}"
        );

        return false;
      }

      worldData.ClearAll();

      foreach (SavedBlockChunk savedChunk in saveData.SavedBlockChunks)
      {
        if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(savedChunk.ChunkCoord, out Dictionary<int, ushort> overrides))
        {
          overrides = new Dictionary<int, ushort>();
          worldData.BlockVoxelOverridesByChunk.Add(savedChunk.ChunkCoord, overrides);
        }

        for (int i = 0; i < savedChunk.Voxels.Count; i++)
        {
          SavedBlockVoxel savedVoxel = savedChunk.Voxels[i];

          if (savedVoxel.VoxelIndex < 0 || savedVoxel.VoxelIndex >= VoxelConstants.ChunkVolume)
          {
            continue;
          }

          ushort materialId = (ushort)Mathf.Clamp(savedVoxel.MaterialId, 0, 65535);
          overrides[savedVoxel.VoxelIndex] = materialId;
        }
      }

      IsWorldReady = false;
      GenerationProgress = 0.0f;

      return true;
    }

    public bool LoadWorldSaveData(WorldSaveData saveData, bool applySavedTerrainSystem = true)
    {
      if (saveData == null)
      {
        Debug.LogWarning("Cannot load null world save data.");
        return false;
      }

      if (saveData.ChunkSize != VoxelConstants.ChunkSize)
      {
        Debug.LogWarning(
            $"World save chunk size mismatch. Save={saveData.ChunkSize}, Current={VoxelConstants.ChunkSize}"
        );

        return false;
      }

      if (applySavedTerrainSystem)
      {
        settings.TerrainSystem = saveData.TerrainSystem;
      }

      settings.VoxelSize = Mathf.Max(0.01f, saveData.VoxelSize);
      settings.ViewDistanceInChunks = Mathf.Max(1, saveData.ViewDistanceInChunks);
      settings.BlockMinChunkY = saveData.BlockMinChunkY;
      settings.BlockMaxChunkY = saveData.BlockMaxChunkY;
      settings.DensityMinChunkY = saveData.DensityMinChunkY;
      settings.DensityMaxChunkY = saveData.DensityMaxChunkY;

      worldData.ClearAll();

      if (saveData.SavedBlockChunks != null)
      {
        foreach (SavedBlockChunk savedChunk in saveData.SavedBlockChunks)
        {
          if (!worldData.BlockVoxelOverridesByChunk.TryGetValue(savedChunk.ChunkCoord, out Dictionary<int, ushort> overrides))
          {
            overrides = new Dictionary<int, ushort>();
            worldData.BlockVoxelOverridesByChunk.Add(savedChunk.ChunkCoord, overrides);
          }

          for (int i = 0; i < savedChunk.Voxels.Count; i++)
          {
            SavedBlockVoxel savedVoxel = savedChunk.Voxels[i];

            if (savedVoxel.VoxelIndex < 0 || savedVoxel.VoxelIndex >= VoxelConstants.ChunkVolume)
            {
              continue;
            }

            ushort materialId = (ushort)Mathf.Clamp(savedVoxel.MaterialId, 0, 65535);
            overrides[savedVoxel.VoxelIndex] = materialId;
          }
        }
      }

      if (saveData.SavedDensityChunks != null)
      {
        foreach (SavedDensityChunk savedChunk in saveData.SavedDensityChunks)
        {
          if (!worldData.DensityVoxelOverridesByChunk.TryGetValue(
              savedChunk.ChunkCoord,
              out Dictionary<int, DensityVoxelOverride> overrides))
          {
            overrides = new Dictionary<int, DensityVoxelOverride>();
            worldData.DensityVoxelOverridesByChunk.Add(savedChunk.ChunkCoord, overrides);
          }

          for (int i = 0; i < savedChunk.Voxels.Count; i++)
          {
            SavedDensityVoxel savedVoxel = savedChunk.Voxels[i];

            if (savedVoxel.VoxelIndex < 0 || savedVoxel.VoxelIndex >= VoxelConstants.ChunkVolume)
            {
              continue;
            }

            ushort materialId = savedVoxel.Density > 0.0f
                ? (ushort)Mathf.Clamp(savedVoxel.MaterialId, 1, 65535)
                : (ushort)0;

            overrides[savedVoxel.VoxelIndex] = new DensityVoxelOverride(savedVoxel.Density, materialId);
          }
        }
      }

      IsWorldReady = false;
      IsInitialTerrainReady = false;
      GenerationProgress = 0.0f;

      return true;
    }

    public bool HasBlockOverrides()
    {
      foreach (KeyValuePair<Vector3Int, Dictionary<int, ushort>> pair in worldData.BlockVoxelOverridesByChunk)
      {
        if (pair.Value.Count > 0)
        {
          return true;
        }
      }

      return false;
    }

    public TerrainSample SampleTerrainAtWorldVoxel(Vector3Int worldVoxel)
    {
      TerrainSampler sampler = new(
          settings.GetActiveGenerationProfile(),
          settings.GetActiveBiomeId()
      );

      return sampler.Sample(
          new Vector3(
              worldVoxel.x,
              worldVoxel.y,
              worldVoxel.z
          )
      );
    }

    public bool IsProceduralBlockSolid(Vector3Int worldVoxel)
    {
      TerrainSample sample = SampleTerrainAtWorldVoxel(worldVoxel);
      return sample.Density > 0.0f;
    }

    public bool FindInitialBlockSpawnLocation(
        Vector3 desiredWorldLocation,
        float horizontalSearchRadius,
        int searchBelowVoxels,
        int searchAboveVoxels,
        float spawnClearance,
        out Vector3 spawnWorldLocation)
    {
      surfaceFinder ??= new WorldSurfaceFinder(this);

      return surfaceFinder.FindInitialBlockSpawnLocation(
          desiredWorldLocation,
          horizontalSearchRadius,
          searchBelowVoxels,
          searchAboveVoxels,
          spawnClearance,
          out spawnWorldLocation
      );
    }

    public bool FindInitialSpawnLocation(
        Vector3 desiredWorldLocation,
        float horizontalSearchRadius,
        int searchBelowVoxels,
        int searchAboveVoxels,
        float spawnClearance,
        out Vector3 spawnWorldLocation)
    {
      surfaceFinder ??= new WorldSurfaceFinder(this);

      switch (settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          return FindInitialBlockSpawnLocation(
              desiredWorldLocation,
              horizontalSearchRadius,
              searchBelowVoxels,
              searchAboveVoxels,
              spawnClearance,
              out spawnWorldLocation);

        case TerrainSystem.SmoothDensity:
        default:
          return surfaceFinder.FindInitialDensitySpawnLocation(
              desiredWorldLocation,
              horizontalSearchRadius,
              searchBelowVoxels,
              searchAboveVoxels,
              spawnClearance,
              out spawnWorldLocation);
      }
    }

    [ContextMenu("Debug Spawn Column")]
    public void DebugSpawnColumn()
    {
      Vector3Int column = new(0, 0, 0);

      int highestGeneratedSolidY = int.MinValue;

      foreach (var pair in worldData.BlockChunks)
      {
        Vector3Int chunkCoord = pair.Key;
        BlockChunkData chunk = pair.Value;

        int chunkMinX = chunkCoord.x * VoxelConstants.ChunkSize;
        int chunkMaxX = chunkMinX + VoxelConstants.ChunkSize - 1;

        int chunkMinZ = chunkCoord.z * VoxelConstants.ChunkSize;
        int chunkMaxZ = chunkMinZ + VoxelConstants.ChunkSize - 1;

        if (column.x < chunkMinX || column.x > chunkMaxX)
        {
          continue;
        }

        if (column.z < chunkMinZ || column.z > chunkMaxZ)
        {
          continue;
        }

        int localX = column.x - chunkMinX;
        int localZ = column.z - chunkMinZ;

        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
          if (chunk.GetVoxel(localX, y, localZ).MaterialId == 0)
          {
            continue;
          }

          int worldY = chunkCoord.y * VoxelConstants.ChunkSize + y;

          if (worldY > highestGeneratedSolidY)
          {
            highestGeneratedSolidY = worldY;
          }
        }
      }

      int highestProceduralSolidY = int.MinValue;

      for (int y = -512; y <= 512; y++)
      {
        if (IsProceduralBlockSolid(new Vector3Int(0, y, 0)))
        {
          highestProceduralSolidY = y;
        }
      }

      Debug.LogError(
          $"SPAWN COLUMN DEBUG: " +
          $"HighestGeneratedSolidY={highestGeneratedSolidY}, " +
          $"GeneratedSurfaceY={highestGeneratedSolidY + 1}, " +
          $"HighestProceduralSolidY={highestProceduralSolidY}, " +
          $"ProceduralSurfaceY={highestProceduralSolidY + 1}, " +
          $"SuggestedSpawnLocation={SuggestedSpawnLocation}, " +
          $"IsInitialTerrainReady={IsInitialTerrainReady}"
      );
    }
  }
}
