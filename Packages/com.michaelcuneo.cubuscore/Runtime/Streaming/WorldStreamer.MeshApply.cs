using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessCompletedBuildResults(int meshApplyBudget)
    {
      float applyStartTime = Time.realtimeSinceStartup;

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        ProcessCompletedBlockBuildResults(meshApplyBudget, applyStartTime);
        return;
      }

      if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity)
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
          // The background build threw (typically transient). The chunk is NOT
          // genuinely empty, so it must not be marked known-empty: that would
          // leave a permanent hole that only a voxel edit could clear. The
          // chunk data is still present, so requeue it to retry the mesh build.
          if (world.Data.BlockChunks.ContainsKey(r.ChunkCoord))
          {
            QueueRender(r.ChunkCoord, false);
          }
          ReturnMeshData(r.MeshData);
          blockCount++;
          continue;
        }

        if (r.IsEmpty || r.MeshData == null || r.MeshData.IsEmpty)
        {
          // Only TRUST an empty mesh - and blacklist the chunk as known-empty -
          // when it is genuinely empty: either it has no solid voxels at all, or
          // it has solids but every in-bounds face neighbour was loaded at build
          // time (a real buried/air chunk). A chunk that HAS solids and meshed
          // empty while a face neighbour was still unloaded had that neighbour
          // treated as SOLID, hiding its only visible face - a FALSE empty. Don't
          // blacklist that, or it becomes a permanent hole; the neighbour is
          // queued to load and a re-mesh (or settled reconciliation) fills it in.
          bool hasSolids = world.Data.BlockChunks.TryGetValue(r.ChunkCoord, out BlockChunkData rb) && rb != null && rb.HasAnySolidVoxel();
          if (!hasSolids || HasAllInBoundsBlockNeighborsLoaded(r.ChunkCoord))
          {
            knownEmptyChunks.Add(r.ChunkCoord);
          }
          worldRenderer.RemoveChunk(r.ChunkCoord);
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

      return;
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
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (r.ChunkData != null) world.Data.DensityChunks[r.ChunkCoord] = r.ChunkData;

        if (r.Failed)
        {
          // Transient background-build exception. Requeue to retry instead of
          // dropping the chunk and waiting on a later reconciliation pass.
          if (world.Data.DensityChunks.ContainsKey(r.ChunkCoord))
          {
            QueueRender(r.ChunkCoord, false);
          }
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (r.MeshData == null || r.MeshData.IsEmpty)
        {
          worldRenderer.RemoveChunk(r.ChunkCoord);
          ReturnMeshData(r);
          count++;
          continue;
        }

        knownEmptyChunks.Remove(r.ChunkCoord);
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