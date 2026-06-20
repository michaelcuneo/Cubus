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
    private static readonly object GeneratedDensityCacheLock = new();
    private static readonly Dictionary<Vector3Int, DensityChunkData> GeneratedDensityCache = new();
    private static readonly Vector3Int[] DensityNeighbourOffsets =
    {
      new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
      new(1, 1, 0), new(1, 0, 1), new(0, 1, 1), new(1, 1, 1)
    };

    private readonly Queue<ChunkLoadResult> completedResults = new();
    private readonly HashSet<Vector3Int> inFlightChunkCoords = new();

    private int activeTaskCount;
    private int generationId;

    public int ActiveTaskCount => activeTaskCount;
    public int GenerationId => generationId;

    public bool IsInFlight(Vector3Int chunkCoord)
    {
      return inFlightChunkCoords.Contains(chunkCoord);
    }

    public void IncrementGeneration()
    {
      generationId++;
      activeTaskCount = 0;
      inFlightChunkCoords.Clear();

      lock (completedResults)
      {
        completedResults.Clear();
      }

      lock (GeneratedDensityCacheLock)
      {
        GeneratedDensityCache.Clear();
      }
    }

    public bool TryStartLoad(IWorldChunkStore store, string worldId, Vector3Int chunkCoord, int maxActiveTasks)
    {
      if (store == null || string.IsNullOrWhiteSpace(worldId)) return false;
      if (activeTaskCount >= maxActiveTasks) return false;
      if (inFlightChunkCoords.Contains(chunkCoord)) return false;

      int requestGeneration = generationId;
      inFlightChunkCoords.Add(chunkCoord);
      activeTaskCount++;

      _ = Task.Run(() => Load(store, worldId, chunkCoord, requestGeneration)).ContinueWith(task =>
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
          activeTaskCount = Mathf.Max(0, activeTaskCount - 1);
          inFlightChunkCoords.Remove(result.ChunkCoord);
          return true;
        }
      }

      result = null;
      return false;
    }

    private static ChunkLoadResult Load(IWorldChunkStore store, string worldId, Vector3Int chunkCoord, int generationId)
    {
      if (!store.TryLoadChunk(worldId, chunkCoord, out WorldChunkRecord record))
      {
        if (StreamingGenerationContext.TryGet(out TerrainSystem mode, out WorldGenerationSnapshot snapshot))
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
            result.BlockChunkData = BlockChunkBuilder.GenerateChunkData(chunkCoord, snapshot, null);
            return result;
          }

          if (mode == TerrainSystem.SmoothDensity)
          {
            result.DensityChunkData = GetOrGenerateDensityChunk(chunkCoord, snapshot);
            PrefillDensityNeighbours(chunkCoord, snapshot);
            return result;
          }
        }

        return new ChunkLoadResult { ChunkCoord = chunkCoord, GenerationId = generationId, Loaded = false, IsMissingFromStorage = true };
      }

      ChunkLoadResult loaded = new() { ChunkCoord = chunkCoord, GenerationId = generationId, Loaded = true, TerrainSystem = record.TerrainSystem };

      switch (record.TerrainSystem)
      {
        case TerrainSystem.Block:
          loaded.BlockChunkData = CubusChunkPayloadCodec.DecodeBlockChunk(record);
          break;
        case TerrainSystem.SmoothDensity:
          loaded.DensityChunkData = CubusChunkPayloadCodec.DecodeDensityChunk(record);
          break;
        default:
          loaded.Loaded = false;
          break;
      }

      return loaded;
    }

    private static DensityChunkData GetOrGenerateDensityChunk(Vector3Int chunkCoord, WorldGenerationSnapshot snapshot)
    {
      lock (GeneratedDensityCacheLock)
      {
        if (GeneratedDensityCache.TryGetValue(chunkCoord, out DensityChunkData cached) && cached != null)
        {
          return cached;
        }
      }

      DensityChunkData generated = DensityChunkBuilder.GenerateChunkData(chunkCoord, snapshot, null);

      lock (GeneratedDensityCacheLock)
      {
        GeneratedDensityCache[chunkCoord] = generated;
      }

      return generated;
    }

    private static void PrefillDensityNeighbours(Vector3Int root, WorldGenerationSnapshot snapshot)
    {
      for (int i = 0; i < DensityNeighbourOffsets.Length; i++)
      {
        GetOrGenerateDensityChunk(root + DensityNeighbourOffsets[i], snapshot);
      }
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
