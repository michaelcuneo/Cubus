using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class BlockChunkBuildQueue
  {
    private readonly Queue<BlockChunkBuildResult> completedResults = new();
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

    public int CompletedCount
    {
      get
      {
        lock (completedResults)
        {
          return completedResults.Count;
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

    public bool TryStartBuild(BlockChunkBuildRequest request, int maxActiveTasks)
    {
      if (request == null)
      {
        return false;
      }

      lock (stateLock)
      {
        if (activeTaskCount >= maxActiveTasks)
        {
          return false;
        }

        if (inFlightChunkCoords.Contains(request.ChunkCoord))
        {
          return false;
        }

        inFlightChunkCoords.Add(request.ChunkCoord);
        activeTaskCount++;
      }

      _ = Task.Run(() => Build(request)).ContinueWith(task =>
      {
        BlockChunkBuildResult result = null;

        if (task.Status == TaskStatus.RanToCompletion)
        {
          result = task.Result;
        }
        else if (task.Exception != null)
        {
          Debug.LogException(task.Exception);
        }

        lock (completedResults)
        {
          if (result != null)
          {
            completedResults.Enqueue(result);
          }
          else
          {
            completedResults.Enqueue(new BlockChunkBuildResult
            {
              ChunkCoord = request.ChunkCoord,
              ChunkData = null,
              MeshData = null,
              IsEmpty = true,
              Failed = true,
              GenerationId = request.GenerationId
            });
          }
        }

        lock (stateLock)
        {
          activeTaskCount = Mathf.Max(0, activeTaskCount - 1);
          inFlightChunkCoords.Remove(request.ChunkCoord);
        }
      });

      return true;
    }

    private struct TerrainColumnLookupCache
    {
      public bool HasColumn;
      public int X;
      public int Z;
      public TerrainColumnSampler Column;

      public TerrainColumnSampler Get(WorldGenerationSnapshot snapshot, int x, int z)
      {
        if (!HasColumn || X != x || Z != z || Column == null)
        {
          Column = new TerrainColumnSampler();
          Column.Prepare(snapshot, x, z, 1.0f);
          X = x;
          Z = z;
          HasColumn = true;
        }

        return Column;
      }
    }

    public bool TryDequeueCompleted(out BlockChunkBuildResult result)
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

    private static BlockChunkBuildResult Build(BlockChunkBuildRequest request)
    {
      BlockChunkData chunkData = request.ChunkDataSnapshot;
      bool hasAnySolidVoxel;

      if (chunkData == null)
      {
        // Managed (thread-safe) generation. The Burst job path uses job.Run,
        // which Unity only permits on the main thread; calling it from this
        // worker thread throws and stalls streaming. See ChunkLoadQueue.Load.
        chunkData = BlockChunkBuilder.GenerateChunkData(
            request.ChunkCoord,
            request.WorldSnapshot,
            request.OverrideSnapshot,
            out hasAnySolidVoxel
        );
      }
      else
      {
        hasAnySolidVoxel = chunkData.HasAnySolidVoxel();
      }

      MeshData meshData = null;
      if (hasAnySolidVoxel)
      {
        if (request.BlockNeighborhood != null)
        {
          meshData = BlockGreedyMesher.GenerateNeighbourAware(
              chunkData,
              request.BlockNeighborhood,
              Mathf.Max(0.0001f, request.VoxelSize)
          );
        }
        else
        {
          // Fallback for non-streaming callers. Cache a sampler per (x, z)
          // column so the surface noise is resolved once per boundary column
          // instead of once per boundary voxel.
          TerrainColumnLookupCache meshColumnCache = new();

          meshData = BlockGreedyMesher.GenerateNeighbourAware(
              chunkData,
              worldVoxelCoord => ResolveMaterialForMeshing(request, worldVoxelCoord, ref meshColumnCache),
              Mathf.Max(0.0001f, request.VoxelSize)
          );
        }
      }

      return new BlockChunkBuildResult
      {
        ChunkCoord = request.ChunkCoord,
        ChunkData = chunkData,
        MeshData = meshData,
        IsEmpty = !hasAnySolidVoxel || meshData == null || meshData.IsEmpty,
        GenerationId = request.GenerationId
      };
    }

    private static ushort ResolveMaterialForMeshing(
        BlockChunkBuildRequest request,
        Vector3Int worldVoxelCoord,
        ref TerrainColumnLookupCache columnCache)
    {
      TerrainColumnSampler column = columnCache.Get(
        request.WorldSnapshot,
        worldVoxelCoord.x,
        worldVoxelCoord.z
      );

      TerrainSample sample = column.SampleAt(worldVoxelCoord, 1.0f);
      return sample.Density > 0.0f
          ? (ushort)Mathf.Clamp(sample.SolidMaterialId > 0 ? sample.SolidMaterialId : 1, 1, 65535)
          : (ushort)0;
    }
  }
}