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
      if (pendingRenderQueue.Count > renderBudget * 2)
      {
        // When render backlog is high, throttle loading so mesh generation can catch up.
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

        if (worldRenderer.HasChunkView(c) || knownEmptyChunks.Contains(c))
        {
          continue;
        }

        if (HasChunkData(c))
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
            // Avoid scanning the full voxel array on the main thread; emptiness is resolved by mesh build results.
            knownEmptyChunks.Remove(c);

            // A newly-available chunk changes its neighbours' boundary faces.
            // Re-mesh neighbours that have already been meshed so they use this
            // chunk's real voxels instead of the terrain-sampling fallback,
            // which otherwise leaves stale boundary walls at the load frontier.
            RequeueSettledBlockNeighbors(c);
          }
          else if (IsDensityTerrainEnabled && result.DensityChunkData != null) world.Data.DensityChunks[c] = result.DensityChunkData;

          // Chunks that were just generated on the streaming worker (no record on
          // disk yet) are written back so subsequent visits load them from storage
          // instead of regenerating. This is the wired-up counterpart to
          // ChunkLoadResult.IsMissingFromStorage. Skipped when the store is hidden
          // for a terrain-system mismatch (ActiveStore == null), so we never
          // overwrite a world saved in the other terrain mode.
          if (result.IsMissingFromStorage && persistStreamedChunks && storage != null && storage.ActiveStore != null && HasChunkData(c))
          {
            storage.SaveChunk(c);
          }

          if (desiredChunkCoords.Contains(c) && HasChunkData(c))
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

    private bool HasChunkData(Vector3Int c) => world.Settings.TerrainSystem switch
    {
      TerrainSystem.Block => world.Data.BlockChunks.ContainsKey(c),
      TerrainSystem.SmoothDensity => world.Data.DensityChunks.ContainsKey(c),
      _ => false
    };
  }
}