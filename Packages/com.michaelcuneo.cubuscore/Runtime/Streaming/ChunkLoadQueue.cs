using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class ChunkLoadQueue
  {
    private readonly Queue<ChunkLoadResult> completedResults = new();
    private readonly HashSet<Vector3Int> inFlightChunkCoords = new();
    private readonly object stateLock = new();

    private int activeTaskCount;
    private int generationId;

    public int ActiveTaskCount
    {
      get
      {
        lock (stateLock)
        {
          return activeTaskCount;
        }
      }
    }

    public int GenerationId
    {
      get
      {
        lock (stateLock)
        {
          return generationId;
        }
      }
    }

    public bool IsInFlight(Vector3Int chunkCoord)
    {
      lock (stateLock)
      {
        return inFlightChunkCoords.Contains(chunkCoord);
      }
    }

    public void IncrementGeneration()
    {
      lock (stateLock)
      {
        generationId++;
        activeTaskCount = 0;
        inFlightChunkCoords.Clear();
      }

      lock (completedResults)
      {
        completedResults.Clear();
      }
    }

    public bool TryStartLoad(
        IWorldChunkStore store,
        string worldId,
        Vector3Int chunkCoord,
        int maxActiveTasks,
        IReadOnlyDictionary<int, ushort> blockOverrides = null,
        IReadOnlyDictionary<int, DensityVoxelOverride> densityOverrides = null)
    {
      int requestGeneration;

      lock (stateLock)
      {
        if (activeTaskCount >= maxActiveTasks) return false;
        if (inFlightChunkCoords.Contains(chunkCoord)) return false;

        requestGeneration = generationId;
        inFlightChunkCoords.Add(chunkCoord);
        activeTaskCount++;
      }

      _ = Task.Run(() => Load(store, worldId, chunkCoord, requestGeneration, blockOverrides, densityOverrides)).ContinueWith(task =>
      {
        ChunkLoadResult result;

        if (task.Status == TaskStatus.RanToCompletion)
        {
          result = task.Result;
        }
        else
        {
          if (task.Exception != null) Debug.LogException(task.Exception);
          result = new ChunkLoadResult { ChunkCoord = chunkCoord, GenerationId = requestGeneration, Loaded = false };
        }

        lock (completedResults)
        {
          completedResults.Enqueue(result);
        }

        lock (stateLock)
        {
          activeTaskCount = Mathf.Max(0, activeTaskCount - 1);
          inFlightChunkCoords.Remove(chunkCoord);
        }
      });

      return true;
    }

    public bool TryDequeueCompleted(out ChunkLoadResult result)
    {
      lock (completedResults)
      {
        if (completedResults.Count > 0)
        {
          result = completedResults.Dequeue();
          return true;
        }
      }

      result = null;
      return false;
    }

    private static ChunkLoadResult Load(
        IWorldChunkStore store,
        string worldId,
        Vector3Int chunkCoord,
        int generationId,
        IReadOnlyDictionary<int, ushort> blockOverrides,
        IReadOnlyDictionary<int, DensityVoxelOverride> densityOverrides)
    {
      bool hasGenerationContext = StreamingGenerationContext.TryGet(
        out TerrainSystem mode,
        out WorldGenerationSnapshot snapshot,
        out HybridTerrainLayerGenerationMode blockLayerMode,
        out HybridTerrainLayerGenerationMode densityLayerMode);

      if (hasGenerationContext && store != null && !string.IsNullOrWhiteSpace(worldId))
      {
        ChunkLoadResult loaded = new()
        {
          ChunkCoord = chunkCoord,
          GenerationId = generationId,
          Loaded = true,
          TerrainSystem = mode
        };

        if ((mode == TerrainSystem.Block || mode == TerrainSystem.Hybrid) &&
            store.TryLoadChunkLayer(worldId, chunkCoord, TerrainSystem.Block, out WorldChunkRecord blockRecord))
        {
          loaded.BlockChunkData = CubusChunkPayloadCodec.DecodeBlockChunk(blockRecord);
          BlockChunkBuilder.ApplyOverrides(loaded.BlockChunkData, blockOverrides);
        }

        if ((mode == TerrainSystem.SmoothDensity || mode == TerrainSystem.Hybrid) &&
            store.TryLoadChunkLayer(worldId, chunkCoord, TerrainSystem.SmoothDensity, out WorldChunkRecord densityRecord))
        {
          loaded.DensityChunkData = CubusChunkPayloadCodec.DecodeDensityChunk(densityRecord);
          DensityChunkBuilder.ApplyOverrides(loaded.DensityChunkData, densityOverrides);
        }

        FillMissingSparseHybridLayers(mode, blockLayerMode, densityLayerMode, loaded, chunkCoord, blockOverrides, densityOverrides);

        if (HasRequiredLoadedLayers(mode, loaded))
        {
          return loaded;
        }
      }
      else if (store != null && !string.IsNullOrWhiteSpace(worldId) && store.TryLoadChunk(worldId, chunkCoord, out WorldChunkRecord record))
      {
        ChunkLoadResult loaded = new() { ChunkCoord = chunkCoord, GenerationId = generationId, Loaded = true, TerrainSystem = record.TerrainSystem };

        switch (record.TerrainSystem)
        {
          case TerrainSystem.Block:
            loaded.BlockChunkData = CubusChunkPayloadCodec.DecodeBlockChunk(record);
            BlockChunkBuilder.ApplyOverrides(loaded.BlockChunkData, blockOverrides);
            break;
          case TerrainSystem.SmoothDensity:
            loaded.DensityChunkData = CubusChunkPayloadCodec.DecodeDensityChunk(record);
            DensityChunkBuilder.ApplyOverrides(loaded.DensityChunkData, densityOverrides);
            break;
          default:
            loaded.Loaded = false;
            break;
        }

        return loaded;
      }

      if (hasGenerationContext)
      {
        ChunkLoadResult result = new()
        {
          ChunkCoord = chunkCoord,
          GenerationId = generationId,
          Loaded = true,
          IsMissingFromStorage = true,
          TerrainSystem = mode
        };

        if (mode == TerrainSystem.Block)
        {
          result.BlockChunkData = BlockChunkBuilder.GenerateChunkData(chunkCoord, snapshot, blockOverrides, out _);
        }
        else if (mode == TerrainSystem.Hybrid)
        {
          result.BlockChunkData = blockLayerMode == HybridTerrainLayerGenerationMode.ProceduralTerrain
            ? BlockChunkBuilder.GenerateChunkData(chunkCoord, snapshot, blockOverrides, out _)
            : CreateSparseBlockChunk(chunkCoord, blockOverrides);
        }

        if (mode == TerrainSystem.SmoothDensity)
        {
          result.DensityChunkData = DensityChunkBuilder.GenerateChunkData(chunkCoord, snapshot, densityOverrides);
        }
        else if (mode == TerrainSystem.Hybrid)
        {
          result.DensityChunkData = densityLayerMode == HybridTerrainLayerGenerationMode.ProceduralTerrain
            ? DensityChunkBuilder.GenerateChunkData(chunkCoord, snapshot, densityOverrides)
            : CreateSparseDensityChunk(chunkCoord, densityOverrides);
        }

        if (result.BlockChunkData != null || result.DensityChunkData != null)
        {
          return result;
        }
      }

      return new ChunkLoadResult { ChunkCoord = chunkCoord, GenerationId = generationId, Loaded = false, IsMissingFromStorage = true };
    }

    private static void FillMissingSparseHybridLayers(
        TerrainSystem mode,
        HybridTerrainLayerGenerationMode blockLayerMode,
        HybridTerrainLayerGenerationMode densityLayerMode,
        ChunkLoadResult loaded,
        Vector3Int chunkCoord,
        IReadOnlyDictionary<int, ushort> blockOverrides,
        IReadOnlyDictionary<int, DensityVoxelOverride> densityOverrides)
    {
      if (mode != TerrainSystem.Hybrid || loaded == null)
      {
        return;
      }

      if (loaded.BlockChunkData == null && blockLayerMode == HybridTerrainLayerGenerationMode.SparseOnly)
      {
        loaded.BlockChunkData = CreateSparseBlockChunk(chunkCoord, blockOverrides);
      }

      if (loaded.DensityChunkData == null && densityLayerMode == HybridTerrainLayerGenerationMode.SparseOnly)
      {
        loaded.DensityChunkData = CreateSparseDensityChunk(chunkCoord, densityOverrides);
      }
    }

    private static BlockChunkData CreateSparseBlockChunk(Vector3Int chunkCoord, IReadOnlyDictionary<int, ushort> blockOverrides)
    {
      BlockChunkData chunkData = new(chunkCoord);
      BlockChunkBuilder.ApplyOverrides(chunkData, blockOverrides);
      chunkData.SetKnownHasAnySolidVoxel(chunkData.HasAnySolidVoxel());
      return chunkData;
    }

    private static DensityChunkData CreateSparseDensityChunk(Vector3Int chunkCoord, IReadOnlyDictionary<int, DensityVoxelOverride> densityOverrides)
    {
      DensityChunkData chunkData = new(chunkCoord);
      DensityChunkBuilder.ApplyOverrides(chunkData, densityOverrides);
      return chunkData;
    }

    private static bool HasRequiredLoadedLayers(TerrainSystem mode, ChunkLoadResult loaded)
    {
      return mode switch
      {
        TerrainSystem.Block => loaded.BlockChunkData != null,
        TerrainSystem.SmoothDensity => loaded.DensityChunkData != null,
        TerrainSystem.Hybrid => loaded.BlockChunkData != null && loaded.DensityChunkData != null,
        _ => false
      };
    }
  }

  public sealed class ChunkLoadResult
  {
    public Vector3Int ChunkCoord;
    public int GenerationId;
    public bool Loaded;
    public bool IsMissingFromStorage;
    public TerrainSystem TerrainSystem;
    public BlockChunkData BlockChunkData;
    public DensityChunkData DensityChunkData;
  }
}
