using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class BlockChunkBuildQueue
  {
    // Material used for an in-bounds neighbour voxel whose chunk hasn't loaded
    // yet. Any non-zero (solid) id hides the shared boundary face; the value
    // itself is never rendered because the hidden face emits no geometry.
    private const ushort SolidBoundaryFallbackMaterial = 1;

    private readonly Queue<BlockChunkBuildResult> completedResults = new();
    private readonly HashSet<Vector3Int> inFlightChunkCoords = new();

    private readonly object stateLock = new();

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
      lock (stateLock)
      {
        return inFlightChunkCoords.Contains(chunkCoord);
      }
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

        ReturnNeighborSnapshotsToPool(request);

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

        activeTaskCount = Mathf.Max(0, activeTaskCount - 1);
        inFlightChunkCoords.Remove(request.ChunkCoord);
      });

      return true;
    }

    private static void ReturnNeighborSnapshotsToPool(BlockChunkBuildRequest request)
    {
      if (request?.NeighborChunkSnapshots == null)
      {
        return;
      }

      foreach (KeyValuePair<Vector3Int, BlockChunkData> snapshot in request.NeighborChunkSnapshots)
      {
        if (snapshot.Key == request.ChunkCoord)
        {
          continue;
        }

        VoxelArrayPool.Return(snapshot.Value?.GetRawVoxelArray());
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
        // Boundary voxels with no neighbor snapshot fall back to terrain
        // sampling. Cache a sampler per (x, z) column so the surface noise is
        // resolved once per column instead of once per boundary voxel.
        Dictionary<long, TerrainColumnSampler> meshColumnCache = new();

        meshData = BlockGreedyMesher.GenerateNeighbourAware(
            chunkData,
            worldVoxelCoord => ResolveMaterialForMeshing(request, worldVoxelCoord, meshColumnCache),
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

    private static ushort ResolveMaterialForMeshing(
        BlockChunkBuildRequest request,
        Vector3Int worldVoxelCoord,
        Dictionary<long, TerrainColumnSampler> columnCache)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);

      if (request?.NeighborChunkSnapshots != null
          && request.NeighborChunkSnapshots.TryGetValue(chunkCoord, out BlockChunkData snapshotChunk)
          && snapshotChunk != null)
      {
        Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);
        return snapshotChunk.GetVoxel(localCoord.x, localCoord.y, localCoord.z).MaterialId;
      }

      // In-bounds face neighbour that hasn't loaded yet: treat it as solid so the
      // shared boundary face is hidden rather than meshed against the terrain
      // fallback (which briefly draws a one-sided wall that vanishes when the real
      // neighbour arrives). The chunk is re-meshed once the neighbour loads, so
      // the correct faces appear then.
      if (request?.SolidFallbackNeighborChunks != null && request.SolidFallbackNeighborChunks.Contains(chunkCoord))
      {
        return SolidBoundaryFallbackMaterial;
      }

      long columnKey = ((long)worldVoxelCoord.x << 32) | (uint)worldVoxelCoord.z;

      if (!columnCache.TryGetValue(columnKey, out TerrainColumnSampler column))
      {
        column = new TerrainColumnSampler();
        column.Prepare(request.WorldSnapshot, worldVoxelCoord.x, worldVoxelCoord.z, 1.0f);
        columnCache[columnKey] = column;
      }

      TerrainSample sample = column.SampleAt(worldVoxelCoord, 1.0f);

      // Solidity must match the generation job, which floors the column's
      // double-precision blended surface. Using the per-voxel float-blended
      // sample.SurfaceHeight here can disagree by a voxel at biome transitions
      // and leave seam faces, so use column.SurfaceHeight instead.
      bool isBelowSurface = worldVoxelCoord.y <= Mathf.FloorToInt(column.SurfaceHeight);

      return isBelowSurface
          ? (ushort)Mathf.Clamp(sample.SolidMaterialId > 0 ? sample.SolidMaterialId : 1, 1, 65535)
          : (ushort)0;
    }
  }
}