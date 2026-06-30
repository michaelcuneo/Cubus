using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void ProcessLoadQueue(int loadBudget, int renderBudget)
    {
      int maxLoadsThisFrame = loadBudget;
      if (PendingRenderCount > renderBudget * 2)
      {
        maxLoadsThisFrame = Mathf.Max(1, loadBudget / 2);
      }

      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(maxLoadsThisFrame * 8, pendingLoadQueue.Count);
      int maxLoadTasks = MaxLoadAsyncTasks;
      int maxTotalTasks = MaxTotalAsyncTasks;

      while (pendingLoadQueue.Count > 0 && count < maxLoadsThisFrame && scanned < maxScans)
      {
        scanned++;
        Vector3Int c = pendingLoadQueue.Dequeue();
        pendingLoadSet.Remove(c);

        bool isDesiredChunk = desiredChunkCoords.Contains(c);
        bool isKeepChunk = keepChunkCoords.Contains(c);

        if (IsBlockTerrainEnabled)
        {
          if (!isDesiredChunk)
          {
            continue;
          }
        }
        else if (!isDesiredChunk && !isKeepChunk)
        {
          continue;
        }

        // knownEmptyChunks is block-layer state. Do not let an empty block mesh
        // suppress density loading in Hybrid mode.
        if (knownEmptyChunks.Contains(c) && !IsDensityTerrainEnabled)
        {
          continue;
        }

        if (HasRequiredChunkData(c))
        {
          if (desiredChunkCoords.Contains(c))
          {
            QueueRender(c);
          }

          continue;
        }

        if (!world.Settings.IsInsideEffectiveWorldBounds3D(c))
        {
          knownEmptyChunks.Add(c);
          knownEmptyDensityChunks.Add(c);
          continue;
        }

        if (chunkLoadQueue.IsInFlight(c)) continue;

        int totalActiveAsyncTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
        if (chunkLoadQueue.ActiveTaskCount >= maxLoadTasks || totalActiveAsyncTasks >= maxTotalTasks)
        {
          QueueLoad(c, false);
          break;
        }

        if (chunkLoadQueue.TryStartLoad(
              storage != null ? storage.ActiveStore : null,
              storage != null ? storage.WorldId : null,
              c,
              MaxLoadAsyncTasks,
              IsBlockTerrainEnabled ? world.CreateBlockOverrideSnapshot(c) : null,
              IsDensityTerrainEnabled ? world.CreateDensityOverrideSnapshot(c) : null))
        {
          totalChunkLoadRequestsStarted++;
          count++;
          continue;
        }

        QueueLoad(c, false);
        break;
      }
    }

    private void ProcessCompletedChunkLoads()
    {
      int count = 0;
      int completedBudget = Mathf.Max(ChunksLoadedPerFrame, MaxLoadAsyncTasks * 2);
      while (count < completedBudget && chunkLoadQueue.TryDequeueCompleted(out ChunkLoadResult result))
      {
        count++;
        if (result == null || result.GenerationId != chunkLoadQueue.GenerationId) continue;
        Vector3Int c = result.ChunkCoord;

        if (result.Loaded)
        {
          totalChunkLoadsCompleted++;
          if (IsBlockTerrainEnabled && result.BlockChunkData != null)
          {
            world.Data.BlockChunks[c] = result.BlockChunkData;
            knownEmptyChunks.Remove(c);
            RequeueSettledBlockNeighbors(c);
          }

          if (IsDensityTerrainEnabled && result.DensityChunkData != null)
          {
            world.Data.DensityChunks[c] = result.DensityChunkData;
            knownEmptyDensityChunks.Remove(c);
          }

          if (result.IsMissingFromStorage && persistStreamedChunks && storage != null && storage.ActiveStore != null && HasRequiredChunkData(c))
          {
            storage.SaveChunk(c);
          }

          if (desiredChunkCoords.Contains(c) && HasRequiredChunkData(c))
          {
            QueueRender(c);
          }

          continue;
        }

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;
        totalChunkLoadFailures++;
        QueueLoad(c);
      }
    }

    private bool HasRequiredChunkData(Vector3Int c)
    {
      if (IsBlockTerrainEnabled && !world.Data.BlockChunks.ContainsKey(c))
      {
        return false;
      }

      if (IsDensityTerrainEnabled && !world.Data.DensityChunks.ContainsKey(c))
      {
        return false;
      }

      return IsAnyTerrainEnabled;
    }
  }
}
