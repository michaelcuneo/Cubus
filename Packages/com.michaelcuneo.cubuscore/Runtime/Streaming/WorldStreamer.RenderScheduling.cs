using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessRenderQueue(int renderBudget)
    {
      if (IsHybridTerrainEnabled)
      {
        int densityBudget = Mathf.Max(1, Mathf.CeilToInt(renderBudget * 0.9f));
        int densityStarted = ProcessDensityRenderQueue(densityBudget);
        int spareBudget = Mathf.Max(0, renderBudget - densityStarted);

        if (spareBudget > 0)
        {
          ProcessBlockRenderQueue(spareBudget);
        }

        return;
      }

      if (IsBlockTerrainEnabled && IsDensityTerrainEnabled)
      {
        int blockBudget = PendingBlockRenderCount > 0
          ? Mathf.Max(1, Mathf.CeilToInt(renderBudget * 0.5f))
          : 0;
        int densityBudget = Mathf.Max(1, renderBudget - blockBudget);

        int blockStarted = blockBudget > 0 ? ProcessBlockRenderQueue(blockBudget) : 0;
        int unusedBlockBudget = Mathf.Max(0, blockBudget - blockStarted);

        ProcessDensityRenderQueue(densityBudget + unusedBlockBudget);
        return;
      }

      if (IsBlockTerrainEnabled)
      {
        ProcessBlockRenderQueue(renderBudget);
      }

      if (IsDensityTerrainEnabled)
      {
        ProcessDensityRenderQueue(renderBudget);
      }
    }

    private int ProcessBlockRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 4, pendingBlockRenderQueue.Count + pendingBlockRenderRetryQueue.Count);

      while ((pendingBlockRenderQueue.Count > 0 || pendingBlockRenderRetryQueue.Count > 0) && count < renderBudget && scanned < maxScans)
      {
        scanned++;

        if (!TryDequeueRenderCandidate(pendingBlockRenderQueue, pendingBlockRenderSet, pendingBlockRenderRetryQueue, pendingBlockRenderRetrySet, out Vector3Int c))
        {
          break;
        }

        if (!desiredChunkCoords.Contains(c)) continue;
        if (!ShouldQueueMeshWorkForChunk(c)) continue;

        if (TryProcessBlockRenderCandidate(c)) count++;
      }

      return count;
    }

    private bool TryProcessBlockRenderCandidate(Vector3Int c)
    {
      if (!world.Data.BlockChunks.TryGetValue(c, out BlockChunkData b) || b == null)
      {
        if (!knownEmptyChunks.Contains(c)) QueueLoad(c, false);
        return false;
      }

      if (buildQueue.IsInFlight(c))
      {
        QueueBlockRender(c, false);
        return false;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
      if (buildQueue.ActiveTaskCount >= MaxBlockAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueBlockRenderRetry(c);
        return false;
      }

      if (TryStartBlockMeshBuild(c, b)) return true;

      QueueBlockRender(c, false);
      return false;
    }

    private int ProcessDensityRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 8, pendingDensityRenderQueue.Count + pendingDensityRenderRetryQueue.Count);

      while ((pendingDensityRenderRetryQueue.Count > 0 || pendingDensityRenderQueue.Count > 0) && count < renderBudget && scanned < maxScans)
      {
        scanned++;

        if (!TryDequeueRenderCandidate(pendingDensityRenderQueue, pendingDensityRenderSet, pendingDensityRenderRetryQueue, pendingDensityRenderRetrySet, out Vector3Int c))
        {
          break;
        }

        if (!desiredChunkCoords.Contains(c)) continue;
        if (knownEmptyDensityChunks.Contains(c)) continue;
        if (!ShouldQueueMeshWorkForChunk(c)) continue;

        if (TryProcessDensityRenderCandidate(c)) count++;
      }

      return count;
    }

    private bool TryProcessDensityRenderCandidate(Vector3Int c)
    {
      if (knownEmptyDensityChunks.Contains(c)) return false;

      if (!world.Data.DensityChunks.TryGetValue(c, out DensityChunkData d) || d == null)
      {
        if (!knownEmptyDensityChunks.Contains(c)) QueueLoad(c, false);
        return false;
      }

      if (densityBuildQueue.IsInFlight(c))
      {
        QueueDensityRender(c, false);
        return false;
      }

      if (!EnsureDensitySampleChunksAvailableForMesh(c))
      {
        QueueDensityRender(c, false);
        return false;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
      if (densityBuildQueue.ActiveTaskCount >= MaxDensityAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueDensityRenderRetry(c);
        return false;
      }

      if (TryStartDensityMeshBuild(c, d)) return true;

      QueueDensityRender(c, false);
      return false;
    }

    private void QueueBlockRenderRetry(Vector3Int chunkCoord)
    {
      if (pendingBlockRenderSet.Contains(chunkCoord) || pendingBlockRenderRetrySet.Contains(chunkCoord)) return;
      pendingBlockRenderRetrySet.Add(chunkCoord);
      pendingBlockRenderRetryQueue.Enqueue(chunkCoord);
    }

    private void QueueDensityRenderRetry(Vector3Int chunkCoord)
    {
      if (pendingDensityRenderSet.Contains(chunkCoord) || pendingDensityRenderRetrySet.Contains(chunkCoord)) return;
      pendingDensityRenderRetrySet.Add(chunkCoord);
      pendingDensityRenderRetryQueue.Enqueue(chunkCoord);
    }

    private bool TryDequeueRenderCandidate(
      Queue<Vector3Int> queue,
      HashSet<Vector3Int> membership,
      Queue<Vector3Int> retryQueue,
      HashSet<Vector3Int> retryMembership,
      out Vector3Int chunkCoord)
    {
      while (retryQueue.Count > 0)
      {
        chunkCoord = retryQueue.Dequeue();
        if (retryMembership.Remove(chunkCoord)) return true;
      }

      while (queue.Count > 0)
      {
        chunkCoord = queue.Dequeue();
        if (membership.Remove(chunkCoord)) return true;
      }

      chunkCoord = default;
      return false;
    }
  }
}