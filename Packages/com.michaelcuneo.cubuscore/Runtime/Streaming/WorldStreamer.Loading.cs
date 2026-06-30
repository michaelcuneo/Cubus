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

        if (knownEmptyChunks.Contains(c) && !IsDensityTerrainEnabled)
        {
          continue;
        }

        if (HasRequiredChunkData(c))
        {
          if (desiredChunkCoords.Contains(c) && ShouldQueueMeshWorkForChunk(c))
          {
            QueueLoadedChunkRenderLayers(c);
          }

          continue;
        }

        if (!world.Settings.IsInsideEffectiveWorldBounds3D(c))
        {
          knownEmptyChunks.Add(c);
          knownEmptyDensityChunks.Add(c);
          continue;
        }

        if (!ShouldStartChunkLoadForCurrentVisibility(c))
        {
          // Do not keep invisible chunks spinning through the load queue every
          // frame. Visibility refresh / camera movement will enqueue them again
          // when they become worth generating.
          continue;
        }

        if (chunkLoadQueue.IsInFlight(c)) continue;

        bool loadBlockLayer = ShouldLoadBlockLayerNow(c);
        bool loadDensityLayer = ShouldLoadDensityLayerNow(c, loadBlockLayer);
        if (!loadBlockLayer && !loadDensityLayer)
        {
          continue;
        }

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
              loadBlockLayer ? world.CreateBlockOverrideSnapshot(c) : null,
              loadDensityLayer ? world.CreateDensityOverrideSnapshot(c) : null,
              loadBlockLayer,
              loadDensityLayer))
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
          bool loadedBlock = false;
          bool loadedDensity = false;

          if (IsBlockTerrainEnabled && result.BlockChunkData != null)
          {
            world.Data.BlockChunks[c] = result.BlockChunkData;
            knownEmptyChunks.Remove(c);
            RequeueSettledBlockNeighbors(c);
            loadedBlock = true;
          }

          if (IsDensityTerrainEnabled && result.DensityChunkData != null)
          {
            world.Data.DensityChunks[c] = result.DensityChunkData;
            knownEmptyDensityChunks.Remove(c);
            loadedDensity = true;
          }

          if (result.IsMissingFromStorage && persistStreamedChunks && storage != null && storage.ActiveStore != null && HasRequiredChunkData(c))
          {
            storage.SaveChunk(c);
          }

          if (desiredChunkCoords.Contains(c) && ShouldQueueMeshWorkForChunk(c))
          {
            if (loadedBlock)
            {
              QueueBlockRender(c);
            }

            if (loadedDensity)
            {
              QueueDensityRender(c);
            }
          }

          if (desiredChunkCoords.Contains(c) && (ShouldLoadBlockLayerNow(c) || ShouldLoadDensityLayerNow(c, false)))
          {
            QueueLoad(c);
          }

          continue;
        }

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;
        totalChunkLoadFailures++;
        QueueLoad(c);
      }
    }

    private void QueueLoadedChunkRenderLayers(Vector3Int c)
    {
      if (!ShouldQueueMeshWorkForChunk(c))
      {
        return;
      }

      if (IsBlockTerrainEnabled && world.Data.BlockChunks.ContainsKey(c))
      {
        QueueBlockRender(c);
      }

      if (IsDensityTerrainEnabled && world.Data.DensityChunks.ContainsKey(c))
      {
        QueueDensityRender(c);
      }
    }

    private bool ShouldLoadBlockLayerNow(Vector3Int c)
    {
      return IsBlockTerrainEnabled &&
             !knownEmptyChunks.Contains(c) &&
             !world.Data.BlockChunks.ContainsKey(c);
    }

    private bool ShouldLoadDensityLayerNow(Vector3Int c, bool blockLayerIsBeingLoadedNow)
    {
      if (!IsDensityTerrainEnabled || knownEmptyDensityChunks.Contains(c) || world.Data.DensityChunks.ContainsKey(c))
      {
        return false;
      }

      if (!IsBlockTerrainEnabled)
      {
        return true;
      }

      if (blockLayerIsBeingLoadedNow)
      {
        return false;
      }

      // In hybrid worlds, get block terrain visible/collidable first. Density
      // loading begins once the block layer exists, and is allowed immediately
      // if a block mesh is already rendered or the block layer no longer needs
      // a render queue slot.
      return world.Data.BlockChunks.ContainsKey(c) &&
             (HasRenderedBlockChunk(c) || !NeedsBlockRenderOrLoad(c));
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