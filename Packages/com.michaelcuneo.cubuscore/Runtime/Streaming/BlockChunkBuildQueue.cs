using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class BlockChunkBuildQueue
  {
    private readonly Queue<BlockChunkBuildResult> completedResults = new();
    private readonly HashSet<Vector3Int> inFlightChunkCoords = new();

    private int activeTaskCount;
    private int generationId;

    public int ActiveTaskCount => activeTaskCount;
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

    public bool TryStartBuild(BlockChunkBuildRequest request, int maxActiveTasks)
    {
      if (request == null)
      {
        return false;
      }

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
              GenerationId = request.GenerationId
            });
          }
        }
      });

      return true;
    }

    public bool TryDequeueCompleted(out BlockChunkBuildResult result)
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

    private static BlockChunkBuildResult Build(BlockChunkBuildRequest request)
    {
      BlockChunkData chunkData = request.ChunkDataSnapshot;
      bool hasAnySolidVoxel;

      if (chunkData == null)
      {
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
        request.NeighborChunkSnapshots ??= new Dictionary<Vector3Int, BlockChunkData>();
        request.NeighborChunkSnapshots[chunkData.ChunkCoord] = chunkData;

        meshData = BlockGreedyMesher.GenerateNeighbourAware(
            chunkData,
            worldVoxelCoord => ResolveMaterialForMeshing(request, worldVoxelCoord),
            Mathf.Max(0.0001f, request.VoxelSize)
        );
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

    private static ushort ResolveMaterialForMeshing(BlockChunkBuildRequest request, Vector3Int worldVoxelCoord)
    {
      if (request?.NeighborChunkSnapshots != null)
      {
        Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
        Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);
        if (request.NeighborChunkSnapshots.TryGetValue(chunkCoord, out BlockChunkData snapshotChunk) && snapshotChunk != null)
        {
          return snapshotChunk.GetVoxel(localCoord.x, localCoord.y, localCoord.z).MaterialId;
        }
      }

      return BlockChunkBuilder.SampleMaterialAtWorldVoxel(worldVoxelCoord, request.WorldSnapshot);
    }
  }
}