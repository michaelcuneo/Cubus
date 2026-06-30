using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private void BuildChunkSet(Vector3Int viewerChunkCoord, int horizontalRadius, HashSet<Vector3Int> targetSet)
    {
      targetSet.Clear();
      world.Settings.GetActiveVerticalChunkBounds(out int minChunkY, out int maxChunkY);

      for (int z = -horizontalRadius; z <= horizontalRadius; z++)
        for (int x = -horizontalRadius; x <= horizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;

          for (int y = minChunkY; y <= maxChunkY; y++)
          {
            Vector3Int chunkCoord = new(chunkX, y, chunkZ);
            if (world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
            {
              targetSet.Add(chunkCoord);
            }
          }
        }
    }

    private void QueueGeneratedChunksForRender(Vector3Int viewerChunkCoord)
    {
      candidateChunksBuffer.Clear();
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
        {
          knownEmptyChunks.Add(chunkCoord);
          knownEmptyDensityChunks.Add(chunkCoord);
          continue;
        }

        bool blockNeedsRender =
          IsBlockTerrainEnabled &&
          !HasRenderedBlockChunk(chunkCoord) &&
          !pendingBlockRenderSet.Contains(chunkCoord) &&
          !pendingBlockRenderRetrySet.Contains(chunkCoord) &&
          !knownEmptyChunks.Contains(chunkCoord);

        bool densityNeedsRender =
          IsDensityTerrainEnabled &&
          !HasRenderedDensityChunk(chunkCoord) &&
          !pendingDensityRenderSet.Contains(chunkCoord) &&
          !pendingDensityRenderRetrySet.Contains(chunkCoord) &&
          !knownEmptyDensityChunks.Contains(chunkCoord);

        if (!blockNeedsRender && !densityNeedsRender)
        {
          continue;
        }

        if (pendingLoadSet.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord))
        {
          continue;
        }

        candidateChunksBuffer.Add(chunkCoord);
      }

      candidateChunksBuffer.Sort(GetChunkPriorityComparison(ComputeSortPivot()));
      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        if (HasRequiredChunkData(candidateChunksBuffer[i])) QueueRender(candidateChunksBuffer[i]);
        else QueueLoad(candidateChunksBuffer[i]);
      }
    }

    private Comparison<Vector3Int> GetChunkPriorityComparison(Vector3Int pivotChunkCoord)
    {
      sortPivotChunkCoord = pivotChunkCoord;
      chunkPriorityComparison ??= CompareChunkPriorityByPivot;
      return chunkPriorityComparison;
    }

    private Vector3Int ComputeSortPivot()
    {
      if (!hasLastViewerChunkCoord) return lastViewerChunkCoord;

      int lead = Mathf.Max(2, ActiveDesiredRadiusInChunks / 2);
      Vector3 heading = new(viewerHeading.x, 0.0f, viewerHeading.z);
      if (heading.sqrMagnitude < 0.0001f) return lastViewerChunkCoord;

      heading.Normalize();
      return new Vector3Int(
        lastViewerChunkCoord.x + Mathf.RoundToInt(heading.x * lead),
        lastViewerChunkCoord.y,
        lastViewerChunkCoord.z + Mathf.RoundToInt(heading.z * lead));
    }

    private int CompareChunkPriorityByPivot(Vector3Int a, Vector3Int b) => CompareChunkPriority(a, b, sortPivotChunkCoord);

    private int CompareChunkPriority(Vector3Int a, Vector3Int b, Vector3Int viewerChunkCoord)
    {
      if (hasPriorityChunkCoord)
      {
        if (a == priorityChunkCoord) return -1;
        if (b == priorityChunkCoord) return 1;
      }

      int ad = ChunkDistanceSquared(a, viewerChunkCoord);
      int bd = ChunkDistanceSquared(b, viewerChunkCoord);
      if (ad != bd) return ad.CompareTo(bd);

      int av = Mathf.Abs(a.y - viewerChunkCoord.y);
      int bv = Mathf.Abs(b.y - viewerChunkCoord.y);
      if (av != bv) return av.CompareTo(bv);

      int ah = HorizontalChunkDistanceSquared(a, viewerChunkCoord);
      int bh = HorizontalChunkDistanceSquared(b, viewerChunkCoord);
      return ah.CompareTo(bh);
    }

    private static int ChunkDistanceSquared(Vector3Int a, Vector3Int b)
    {
      int dx = a.x - b.x;
      int dy = a.y - b.y;
      int dz = a.z - b.z;
      return dx * dx + dy * dy + dz * dz;
    }

    private static int HorizontalChunkDistanceSquared(Vector3Int a, Vector3Int b)
    {
      int dx = a.x - b.x;
      int dz = a.z - b.z;
      return dx * dx + dz * dz;
    }

    private void UpdateStreamingSetIfNeeded(bool force = false)
    {
      Vector3Int viewerChunkCoord = GetStreamingFocusChunkCoord();
      if (!force && hasLastViewerChunkCoord && viewerChunkCoord == lastViewerChunkCoord) return;

      if (force)
      {
        knownEmptyDensityChunks.Clear();
      }

      if (hasLastViewerChunkCoord)
      {
        Vector3Int delta = viewerChunkCoord - lastViewerChunkCoord;
        if (delta.x != 0 || delta.z != 0)
        {
          viewerHeading = delta;
        }
      }

      lastViewerChunkCoord = viewerChunkCoord;
      hasLastViewerChunkCoord = true;
      BuildChunkSet(viewerChunkCoord, ActiveDesiredRadiusInChunks, desiredChunkCoords);
      BuildChunkSet(viewerChunkCoord, ActiveKeepRadiusInChunks, keepChunkCoords);
      PruneKnownEmptyChunksOutsideCurrentInterest();
      QueueGeneratedChunksForRender(viewerChunkCoord);
      UnloadOutsideKeepSet();
      pendingLoadQueueNeedsPrioritization = true;
      pendingRenderQueueNeedsPrioritization = true;
    }

    private void PruneKnownEmptyChunksOutsideCurrentInterest()
    {
      PruneKnownEmptySetOutsideCurrentInterest(knownEmptyChunks);
      PruneKnownEmptySetOutsideCurrentInterest(knownEmptyDensityChunks);
    }

    private void PruneKnownEmptySetOutsideCurrentInterest(HashSet<Vector3Int> knownEmptySet)
    {
      if (knownEmptySet.Count == 0)
      {
        return;
      }

      candidateChunksBuffer.Clear();

      foreach (Vector3Int chunkCoord in knownEmptySet)
      {
        if (!desiredChunkCoords.Contains(chunkCoord) && !keepChunkCoords.Contains(chunkCoord))
        {
          candidateChunksBuffer.Add(chunkCoord);
        }
      }

      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        knownEmptySet.Remove(candidateChunksBuffer[i]);
      }

      candidateChunksBuffer.Clear();
    }
  }
}
