using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessRenderQueue(int renderBudget)
    {
      if (IsBlockTerrainEnabled && IsDensityTerrainEnabled)
      {
        int blockBudget = Mathf.Max(1, renderBudget / 2);
        int densityBudget = Mathf.Max(1, renderBudget - blockBudget);

        ProcessBlockRenderQueue(blockBudget);
        ProcessDensityRenderQueue(densityBudget);
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

    private void ProcessBlockRenderQueue(int renderBudget)
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

        if (!desiredChunkCoords.Contains(c))
        {
          continue;
        }

        if (TryProcessBlockRenderCandidate(c))
        {
          count++;
        }
      }
    }

    private bool TryProcessBlockRenderCandidate(Vector3Int c)
    {
      if (!world.Data.BlockChunks.TryGetValue(c, out BlockChunkData b) || b == null)
      {
        if (!knownEmptyChunks.Contains(c))
        {
          QueueLoad(c, false);
        }

        // Consume this render slot. The load queue owns this chunk until data exists.
        return true;
      }

      if (buildQueue.IsInFlight(c))
      {
        // Do not requeue into the same draining loop. ReconcileRenderCoverageIfSettled
        // will enqueue it again if the completed build did not render the chunk.
        return true;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;

      if (buildQueue.ActiveTaskCount >= MaxBlockAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueBlockRenderRetry(c);
        return true;
      }

      if (TryStartBlockMeshBuild(c, b))
      {
        return true;
      }

      QueueBlockRenderRetry(c);
      return true;
    }

    private void ProcessDensityRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 4, pendingDensityRenderQueue.Count + pendingDensityRenderRetryQueue.Count);

      while ((pendingDensityRenderRetryQueue.Count > 0 || pendingDensityRenderQueue.Count > 0) && count < renderBudget && scanned < maxScans)
      {
        scanned++;

        if (!TryDequeueRenderCandidate(pendingDensityRenderQueue, pendingDensityRenderSet, pendingDensityRenderRetryQueue, pendingDensityRenderRetrySet, out Vector3Int c))
        {
          break;
        }

        if (!desiredChunkCoords.Contains(c))
        {
          continue;
        }

        if (TryProcessDensityRenderCandidate(c))
        {
          count++;
        }
      }
    }

    private bool TryProcessDensityRenderCandidate(Vector3Int c)
    {
      if (!world.Data.DensityChunks.TryGetValue(c, out DensityChunkData d) || d == null)
      {
        if (!knownEmptyDensityChunks.Contains(c))
        {
          QueueLoad(c, false);
        }

        return true;
      }

      if (densityBuildQueue.IsInFlight(c))
      {
        return true;
      }

      if (!EnsureDensitySampleChunksAvailableForMesh(c))
      {
        QueueDensityRenderRetry(c);
        return true;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;

      if (densityBuildQueue.ActiveTaskCount >= MaxDensityAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueDensityRenderRetry(c);
        return true;
      }

      if (TryStartDensityMeshBuild(c, d))
      {
        return true;
      }

      QueueDensityRenderRetry(c);
      return true;
    }

    private void QueueBlockRenderRetry(Vector3Int chunkCoord)
    {
      if (pendingBlockRenderSet.Contains(chunkCoord) || pendingBlockRenderRetrySet.Contains(chunkCoord))
      {
        return;
      }

      pendingBlockRenderRetrySet.Add(chunkCoord);
      pendingBlockRenderRetryQueue.Enqueue(chunkCoord);
    }

    private void QueueDensityRenderRetry(Vector3Int chunkCoord)
    {
      if (pendingDensityRenderSet.Contains(chunkCoord) || pendingDensityRenderRetrySet.Contains(chunkCoord))
      {
        return;
      }

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

        if (retryMembership.Remove(chunkCoord))
        {
          return true;
        }
      }

      while (queue.Count > 0)
      {
        chunkCoord = queue.Dequeue();

        if (membership.Remove(chunkCoord))
        {
          return true;
        }
      }

      chunkCoord = default;
      return false;
    }
  }
}
