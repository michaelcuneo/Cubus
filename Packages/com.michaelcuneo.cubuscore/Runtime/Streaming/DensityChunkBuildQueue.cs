using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class DensityChunkBuildQueue
  {
    private readonly Queue<DensityChunkBuildResult> completedResults = new();
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

    public bool TryStartBuild(DensityChunkBuildRequest request, int maxActiveTasks)
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
        DensityChunkBuildResult result = null;

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
          completedResults.Enqueue(result ?? new DensityChunkBuildResult
          {
            ChunkCoord = request.ChunkCoord,
            ChunkData = null,
            MeshData = null,
            IsEmpty = true,
            HasSurfaceCrossing = false,
            GenerationId = request.GenerationId
          });
        }
      });

      return true;
    }

    public bool TryDequeueCompleted(out DensityChunkBuildResult result)
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

    private static DensityChunkBuildResult Build(DensityChunkBuildRequest request)
    {
      DensityChunkData chunkData;

      if (request.ChunkDataSnapshot != null)
      {
        chunkData = request.ChunkDataSnapshot.Clone();

        if (request.OverrideSnapshot != null && request.OverrideSnapshot.Count > 0)
        {
          DensityChunkBuilder.ApplyOverrides(chunkData, request.OverrideSnapshot);
        }
      }
      else
      {
        chunkData = DensityChunkBuilder.GenerateChunkData(
            request.ChunkCoord,
            request.WorldSnapshot,
            request.OverrideSnapshot
        );
      }

      request.ChunkDataSnapshots ??= new Dictionary<Vector3Int, DensityChunkData>();
      request.ChunkDataSnapshots[request.ChunkCoord] = chunkData;

      MeshData meshData = DensityMeshDataBuilder.Generate(
          request.ChunkCoord,
          request.WorldSnapshot,
          request.CellStep,
          request.FlipWinding,
          worldVoxel => SampleVoxelForBuild(worldVoxel, request)
      );

      return new DensityChunkBuildResult
      {
        ChunkCoord = request.ChunkCoord,
        ChunkData = chunkData,
        MeshData = meshData,
        IsEmpty = meshData == null || meshData.IsEmpty,
        HasSurfaceCrossing = meshData != null && !meshData.IsEmpty,
        GenerationId = request.GenerationId
      };
    }

    private static DensityVoxel SampleVoxelForBuild(Vector3Int worldVoxelCoord, DensityChunkBuildRequest request)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (request.ChunkDataSnapshots != null &&
          request.ChunkDataSnapshots.TryGetValue(chunkCoord, out DensityChunkData chunkData) &&
          chunkData != null &&
          chunkData.IsInBounds(localCoord.x, localCoord.y, localCoord.z))
      {
        return chunkData.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      bool insideGeneratedBounds =
          chunkCoord.x >= request.GeneratedMinChunkX &&
          chunkCoord.x <= request.GeneratedMaxChunkX &&
          chunkCoord.y >= request.GeneratedMinChunkY &&
          chunkCoord.y <= request.GeneratedMaxChunkY &&
          chunkCoord.z >= request.GeneratedMinChunkZ &&
          chunkCoord.z <= request.GeneratedMaxChunkZ;

      if (!insideGeneratedBounds)
      {
        return new DensityVoxel(1.0f, 1);
      }

      return DensityVoxel.Empty;
    }
  }
}
