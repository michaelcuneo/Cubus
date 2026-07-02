using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessCompletedBuildResults(int meshApplyBudget)
    {
      float applyStartTime = Time.realtimeSinceStartup;

      if (IsBlockTerrainEnabled)
      {
        ProcessCompletedBlockBuildResults(meshApplyBudget, applyStartTime);
      }

      if (IsDensityTerrainEnabled)
      {
        ProcessCompletedDensityBuildResults(meshApplyBudget, applyStartTime);
      }
    }

    private void ProcessCompletedBlockBuildResults(int meshApplyBudget, float applyStartTime)
    {
      int blockCount = 0;

      while (blockCount < meshApplyBudget &&
             (blockCount == 0 || Time.realtimeSinceStartup - applyStartTime < MeshApplyTimeBudgetSeconds) &&
             buildQueue.TryDequeueCompleted(out BlockChunkBuildResult r))
      {
        if (r == null) { blockCount++; continue; }

        if (r.GenerationId != buildQueue.GenerationId || (!desiredChunkCoords.Contains(r.ChunkCoord) && !keepChunkCoords.Contains(r.ChunkCoord)))
        {
          ReturnMeshData(r?.MeshData);
          blockCount++;
          continue;
        }

        if (r.ChunkData != null)
        {
          if (
            world.Data.BlockChunks.TryGetValue(r.ChunkCoord, out BlockChunkData previousChunk) &&
            previousChunk != null &&
            !ReferenceEquals(previousChunk, r.ChunkData)
          )
          {
            VoxelArrayPool.Return(previousChunk.GetRawVoxelArray());
            world.Data.BlockChunks[r.ChunkCoord] = r.ChunkData;
          }
          else if (!world.Data.BlockChunks.ContainsKey(r.ChunkCoord))
          {
            world.Data.BlockChunks[r.ChunkCoord] = r.ChunkData;
          }
        }

        if (r.Failed)
        {
          if (world.Data.BlockChunks.ContainsKey(r.ChunkCoord))
          {
            QueueBlockRender(r.ChunkCoord, false);
          }
          ReturnMeshData(r.MeshData);
          blockCount++;
          continue;
        }

        if (r.IsEmpty || r.MeshData == null || r.MeshData.IsEmpty)
        {
          bool hasSolids = world.Data.BlockChunks.TryGetValue(r.ChunkCoord, out BlockChunkData rb) && rb != null && rb.HasAnySolidVoxel();
          if (!hasSolids || HasAllInBoundsBlockNeighborsLoaded(r.ChunkCoord))
          {
            knownEmptyChunks.Add(r.ChunkCoord);
          }
          worldRenderer.RemoveBlockChunkMesh(r.ChunkCoord);
          ReturnMeshData(r.MeshData);
          blockCount++;
          continue;
        }

        knownEmptyChunks.Remove(r.ChunkCoord);
        worldRenderer.RenderBlockChunkMesh(r.ChunkCoord, r.MeshData);
        totalBlockMeshApplies++;
        r.MeshData = null;
        blockCount++;
      }
    }

    private void ProcessCompletedDensityBuildResults(int meshApplyBudget, float applyStartTime)
    {
      int count = 0;

      while (count < meshApplyBudget &&
             (count == 0 || Time.realtimeSinceStartup - applyStartTime < MeshApplyTimeBudgetSeconds) &&
             densityBuildQueue.TryDequeueCompleted(out DensityChunkBuildResult r))
      {
        if (r == null) { count++; continue; }
        if (r.GenerationId != densityBuildQueue.GenerationId || (!desiredChunkCoords.Contains(r.ChunkCoord) && !keepChunkCoords.Contains(r.ChunkCoord)))
        {
          densityEditRenderSet.Remove(r.ChunkCoord);
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (densityRebuildAfterInFlight.Remove(r.ChunkCoord))
        {
          // This build was in flight when the chunk was re-queued (e.g. an edit changed the
          // data after it started), so its result may be stale. Re-mesh with the current data;
          // the result below still applies for a frame, then the fresh build supersedes it.
          knownEmptyDensityChunks.Remove(r.ChunkCoord);
          QueueDensityRender(r.ChunkCoord, false);
        }

        if (r.ChunkData != null) world.Data.DensityChunks[r.ChunkCoord] = r.ChunkData;

        if (r.Failed)
        {
          if (world.Data.DensityChunks.ContainsKey(r.ChunkCoord))
          {
            QueueDensityRender(r.ChunkCoord, false);
          }
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (r.MeshData == null || r.MeshData.IsEmpty)
        {
          knownEmptyDensityChunks.Add(r.ChunkCoord);
          densityEditRenderSet.Remove(r.ChunkCoord);
          worldRenderer.RemoveDensityChunkMesh(r.ChunkCoord);
          ReturnMeshData(r);
          count++;
          continue;
        }

        knownEmptyDensityChunks.Remove(r.ChunkCoord);
        densityEditRenderSet.Remove(r.ChunkCoord);
        Mesh mesh = r.MeshData.ToUnityMeshFast();
        MeshDataPool.Return(r.MeshData); r.MeshData = null;
        worldRenderer.RenderDensityChunkMesh(
            r.ChunkCoord,
            mesh,
            hasPriorityChunkCoord && r.ChunkCoord == priorityChunkCoord && !world.IsInitialTerrainReady
          );
        totalDensityMeshApplies++;
        count++;
      }
    }

    private static void ReturnMeshData(MeshData meshData)
    {
      if (meshData != null) MeshDataPool.Return(meshData);
    }
  }
}
