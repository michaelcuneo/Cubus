using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
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
      BlockChunkData chunkData = BlockChunkBuilder.GenerateChunkData(
          request.ChunkCoord,
          request.WorldSnapshot,
          request.OverrideSnapshot
      );

      if (!chunkData.HasAnySolidVoxel())
      {
        return new BlockChunkBuildResult
        {
          ChunkCoord = request.ChunkCoord,
          ChunkData = chunkData,
          MeshData = null,
          IsEmpty = true,
          GenerationId = request.GenerationId
        };
      }

      // Generate 6 neighbours entirely on the background thread for face-culling.
      // Overrides are not needed for neighbours — only the centre chunk's overrides
      // affect its own mesh; neighbour meshes rebuild separately when they are queued.
      static BlockChunkData GenNeighbour(Vector3Int coord, WorldGenerationSnapshot snap) =>
          BlockChunkBuilder.GenerateChunkData(coord, snap, null);

      WorldGenerationSnapshot s = request.WorldSnapshot;
      Meshing.BlockChunkNeighborhood neighborhood = new(
          chunkData,
          GenNeighbour(request.ChunkCoord + Vector3Int.right, s),
          GenNeighbour(request.ChunkCoord + Vector3Int.left, s),
          GenNeighbour(request.ChunkCoord + Vector3Int.up, s),
          GenNeighbour(request.ChunkCoord + Vector3Int.down, s),
          GenNeighbour(request.ChunkCoord + new Vector3Int(0, 0, 1), s),
          GenNeighbour(request.ChunkCoord + new Vector3Int(0, 0, -1), s)
      );

      Meshing.MeshData meshData = BlockChunkBuilder.GenerateMesh(
          chunkData,
          neighborhood,
          request.WorldSnapshot.VoxelSize
      );

      return new BlockChunkBuildResult
      {
        ChunkCoord = request.ChunkCoord,
        ChunkData = chunkData,
        MeshData = meshData,
        IsEmpty = meshData == null || meshData.IsEmpty,
        GenerationId = request.GenerationId
      };
    }
  }
}