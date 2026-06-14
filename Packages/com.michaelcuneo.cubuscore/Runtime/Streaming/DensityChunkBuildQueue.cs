using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
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

    public bool TryStartBuild(
        DensityChunkBuildRequest request,
        int maxActiveTasks)
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
          if (result != null)
          {
            completedResults.Enqueue(result);
          }
          else
          {
            completedResults.Enqueue(new DensityChunkBuildResult
            {
              ChunkCoord = request.ChunkCoord,
              ChunkData = null,
              MeshData = null,
              IsEmpty = true,
              HasSurfaceCrossing = false,
              GenerationId = request.GenerationId
            });
          }
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
        // Fast path: chunk was already loaded and edited on the main thread.
        // Clone it and apply any overrides instead of regenerating terrain noise.
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

      return new DensityChunkBuildResult
      {
        ChunkCoord = request.ChunkCoord,
        ChunkData = chunkData,
        MeshData = null,
        IsEmpty = false,
        HasSurfaceCrossing = true,
        GenerationId = request.GenerationId
      };
    }
  }
}