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
    }

    public bool TryStartLoad(IWorldChunkStore store, string worldId, Vector3Int chunkCoord, int maxActiveTasks)
    {
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
      if (store != null && !string.IsNullOrWhiteSpace(worldId) && store.TryLoadChunk(worldId, chunkCoord, out WorldChunkRecord record))
      {
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
          result.DensityChunkData = DensityChunkBuilder.GenerateChunkData(chunkCoord, snapshot, null);
          return result;
        }
      }

      return new ChunkLoadResult { ChunkCoord = chunkCoord, GenerationId = generationId, Loaded = false, IsMissingFromStorage = true };
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
