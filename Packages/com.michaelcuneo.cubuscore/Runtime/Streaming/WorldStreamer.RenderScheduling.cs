using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessRenderQueue(int renderBudget)
    {
      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        ProcessBlockRenderQueue(renderBudget);
        return;
      }

      if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity)
      {
        ProcessDensityRenderQueue(renderBudget);
      }
    }

    private void ProcessBlockRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 4, pendingRenderQueue.Count + pendingRenderRetryQueue.Count);

      while ((pendingRenderQueue.Count > 0 || pendingRenderRetryQueue.Count > 0) && count < renderBudget && scanned < maxScans)
      {
        scanned++;

        if (!TryDequeueRenderCandidate(out Vector3Int c))
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

        return false;
      }

      if (buildQueue.IsInFlight(c))
      {
        QueueRender(c, false);
        return false;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;

      if (buildQueue.ActiveTaskCount >= MaxBlockAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueRenderRetry(c);
        return false;
      }

      if (TryStartBlockMeshBuild(c, b))
      {
        return true;
      }

      QueueRender(c, false);
      return false;
    }

    private void ProcessDensityRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 4, pendingRenderQueue.Count + pendingRenderRetryQueue.Count);

      while ((pendingRenderRetryQueue.Count > 0 || pendingRenderQueue.Count > 0) && count < renderBudget && scanned < maxScans)
      {
        scanned++;

        if (!TryDequeueRenderCandidate(out Vector3Int c))
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
        QueueLoad(c);
        return false;
      }

      if (densityBuildQueue.IsInFlight(c))
      {
        QueueRender(c, false);
        return false;
      }

      if (!EnsureDensitySampleChunksAvailableForMesh(c))
      {
        QueueRender(c, false);
        return false;
      }

      int totalActiveTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;

      if (densityBuildQueue.ActiveTaskCount >= MaxDensityAsyncTasks || totalActiveTasks >= MaxTotalAsyncTasks)
      {
        QueueRenderRetry(c);
        return false;
      }

      if (TryStartDensityMeshBuild(c, d))
      {
        return true;
      }

      QueueRender(c, false);
      return false;
    }

    private void QueueRenderRetry(Vector3Int chunkCoord)
    {
      if (pendingRenderSet.Contains(chunkCoord) || pendingRenderRetrySet.Contains(chunkCoord))
      {
        return;
      }

      pendingRenderRetrySet.Add(chunkCoord);
      pendingRenderRetryQueue.Enqueue(chunkCoord);
    }

    private bool TryDequeueRenderCandidate(out Vector3Int chunkCoord)
    {
      while (pendingRenderRetryQueue.Count > 0)
      {
        chunkCoord = pendingRenderRetryQueue.Dequeue();

        if (pendingRenderRetrySet.Remove(chunkCoord))
        {
          return true;
        }
      }

      while (pendingRenderQueue.Count > 0)
      {
        chunkCoord = pendingRenderQueue.Dequeue();

        if (pendingRenderSet.Remove(chunkCoord))
        {
          return true;
        }
      }

      chunkCoord = default;
      return false;
    }
  }
}