using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private const int SurfaceVerticalChunkMargin = 2;
    private const int ViewerVerticalChunkMargin = 1;

    private void BuildChunkSet(Vector3Int viewerChunkCoord, int horizontalRadius, HashSet<Vector3Int> targetSet)
    {
      targetSet.Clear();
      world.Settings.GetActiveVerticalChunkBounds(out int minChunkY, out int maxChunkY);

      for (int z = -horizontalRadius; z <= horizontalRadius; z++)
        for (int x = -horizontalRadius; x <= horizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;

          int surfaceChunkY = GetSurfaceChunkYForColumn(
            chunkX,
            chunkZ,
            maxChunkY * VoxelConstants.ChunkSize
          );

          AddVerticalChunkRange(targetSet, chunkX, chunkZ, surfaceChunkY - SurfaceVerticalChunkMargin, surfaceChunkY + SurfaceVerticalChunkMargin, minChunkY, maxChunkY);
          AddVerticalChunkRange(targetSet, chunkX, chunkZ, viewerChunkCoord.y - ViewerVerticalChunkMargin, viewerChunkCoord.y + ViewerVerticalChunkMargin, minChunkY, maxChunkY);
        }
    }

    private void AddVerticalChunkRange(
      HashSet<Vector3Int> targetSet,
      int chunkX,
      int chunkZ,
      int requestedMinY,
      int requestedMaxY,
      int minChunkY,
      int maxChunkY)
    {
      int fromY = Mathf.Clamp(Mathf.Min(requestedMinY, requestedMaxY), minChunkY, maxChunkY);
      int toY = Mathf.Clamp(Mathf.Max(requestedMinY, requestedMaxY), minChunkY, maxChunkY);

      for (int y = fromY; y <= toY; y++)
      {
        Vector3Int chunkCoord = new(chunkX, y, chunkZ);
        if (world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
        {
          targetSet.Add(chunkCoord);
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

        bool blockNeedsRender = NeedsBlockRenderOrLoad(chunkCoord);
        bool densityNeedsRender = NeedsDensityRenderOrLoad(chunkCoord);

        if (!blockNeedsRender && !densityNeedsRender)
        {
          continue;
        }

        if (pendingLoadSet.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord))
        {
          if ((blockNeedsRender && world.Data.BlockChunks.ContainsKey(chunkCoord)) ||
              (densityNeedsRender && world.Data.DensityChunks.ContainsKey(chunkCoord)))
          {
            candidateChunksBuffer.Add(chunkCoord);
          }

          continue;
        }

        candidateChunksBuffer.Add(chunkCoord);
      }

      candidateChunksBuffer.Sort(GetChunkPriorityComparison(ComputeSortPivot()));
      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        QueueNeededRenderOrLoadLayers(candidateChunksBuffer[i]);
      }
    }

    private bool NeedsBlockRenderOrLoad(Vector3Int chunkCoord)
    {
      return IsBlockTerrainEnabled &&
             !HasRenderedBlockChunk(chunkCoord) &&
             !pendingBlockRenderSet.Contains(chunkCoord) &&
             !pendingBlockRenderRetrySet.Contains(chunkCoord) &&
             !knownEmptyChunks.Contains(chunkCoord);
    }

    private bool NeedsDensityRenderOrLoad(Vector3Int chunkCoord)
    {
      return IsDensityTerrainEnabled &&
             !HasRenderedDensityChunk(chunkCoord) &&
             !pendingDensityRenderSet.Contains(chunkCoord) &&
             !pendingDensityRenderRetrySet.Contains(chunkCoord) &&
             !knownEmptyDensityChunks.Contains(chunkCoord);
    }

    private void QueueNeededRenderOrLoadLayers(Vector3Int chunkCoord)
    {
      bool needsLoad = false;

      if (NeedsBlockRenderOrLoad(chunkCoord))
      {
        if (world.Data.BlockChunks.ContainsKey(chunkCoord))
        {
          QueueBlockRender(chunkCoord);
        }
        else
        {
          needsLoad = true;
        }
      }

      if (NeedsDensityRenderOrLoad(chunkCoord))
      {
        if (world.Data.DensityChunks.ContainsKey(chunkCoord))
        {
          QueueDensityRender(chunkCoord);
        }
        else
        {
          needsLoad = true;
        }
      }

      if (needsLoad)
      {
        QueueLoad(chunkCoord);
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
