using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void QueueLoad(Vector3Int chunkCoord, bool requestPrioritization = true)
    {
      if (pendingLoadSet.Add(chunkCoord))
      {
        pendingLoadQueue.Enqueue(chunkCoord);

        if (requestPrioritization)
        {
          pendingLoadQueueNeedsPrioritization = true;
        }
      }
    }

    private void QueueRender(Vector3Int chunkCoord, bool requestPrioritization = true)
    {
      if (pendingRenderSet.Add(chunkCoord))
      {
        pendingRenderQueue.Enqueue(chunkCoord);

        if (requestPrioritization)
        {
          pendingRenderQueueNeedsPrioritization = true;
        }
      }
    }

    private void PrioritizePendingQueues()
    {
      if (!hasLastViewerChunkCoord)
      {
        return;
      }

      if (pendingLoadQueueNeedsPrioritization)
      {
        PrioritizeQueue(pendingLoadQueue, pendingLoadSet, true);
        pendingLoadQueueNeedsPrioritization = false;
      }

      if (pendingRenderQueueNeedsPrioritization)
      {
        PrioritizeQueue(pendingRenderQueue, pendingRenderSet, false);
        pendingRenderQueueNeedsPrioritization = false;
      }
    }

    private void PrioritizeQueue(Queue<Vector3Int> queue, HashSet<Vector3Int> membership, bool allowKeepOnlyChunks)
    {
      if (queue.Count < 2) return;
      queueSortBuffer.Clear();
      while (queue.Count > 0)
      {
        Vector3Int c = queue.Dequeue();
        if (!membership.Contains(c)) continue;
        if (!desiredChunkCoords.Contains(c) && !(allowKeepOnlyChunks && keepChunkCoords.Contains(c))) continue;
        queueSortBuffer.Add(c);
      }

      queueSortBuffer.Sort(GetChunkPriorityComparison(ComputeSortPivot()));
      for (int i = 0; i < queueSortBuffer.Count; i++) queue.Enqueue(queueSortBuffer[i]);
    }

    private void UnloadOutsideKeepSet()
    {
      unloadChunksBuffer.Clear();
      foreach (Vector3Int c in worldRenderer.ActiveChunkViews.Keys) if (!keepChunkCoords.Contains(c)) unloadChunksBuffer.Add(c);
      for (int i = 0; i < unloadChunksBuffer.Count; i++)
      {
        Vector3Int chunkCoord = unloadChunksBuffer[i];
        pendingUnload.Enqueue(chunkCoord);
      }
    }

    private void ProcessUnloadQueue(int unloadBudget)
    {
      int count = 0;

      while (count < unloadBudget && pendingUnload.TryDequeue(out Vector3Int chunkCoord))
      {
        if (keepChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        worldRenderer.RemoveChunk(chunkCoord);

        if (evictCachedChunkDataOutsideKeepSet && storage != null)
        {
          world.Data.BlockChunks.Remove(chunkCoord);
          world.Data.DensityChunks.Remove(chunkCoord);
        }

        totalChunkUnloadsApplied++;
        count++;
      }
    }
  }
}