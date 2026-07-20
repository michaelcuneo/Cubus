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

      // Resolve the world XZ bounds once per rebuild instead of re-deriving them for
      // every chunk inside the inner loop. World bounds are unbounded in Y (the
      // vertical extent is already clamped to [minChunkY, maxChunkY]), so a column
      // that passes this XZ test needs no further per-chunk bounds check.
      bool useWorldBounds = world.Settings.UseWorldBounds;
      world.Settings.GetEffectiveWorldChunkBounds3D(
        out int worldMinChunkX, out int worldMaxChunkX,
        out _, out _,
        out int worldMinChunkZ, out int worldMaxChunkZ);

      for (int z = -horizontalRadius; z <= horizontalRadius; z++)
        for (int x = -horizontalRadius; x <= horizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;

          if (useWorldBounds &&
              (chunkX < worldMinChunkX || chunkX > worldMaxChunkX ||
               chunkZ < worldMinChunkZ || chunkZ > worldMaxChunkZ))
          {
            continue;
          }

          int surfaceChunkY = GetSurfaceChunkYForColumn(
            chunkX,
            chunkZ,
            maxChunkY * VoxelConstants.ChunkSize
          );

          AddVerticalChunkRange(targetSet, chunkX, chunkZ, surfaceChunkY - SurfaceVerticalChunkMargin, surfaceChunkY + SurfaceVerticalChunkMargin, minChunkY, maxChunkY);
          AddVerticalChunkRange(targetSet, chunkX, chunkZ, viewerChunkCoord.y - ViewerVerticalChunkMargin, viewerChunkCoord.y + ViewerVerticalChunkMargin, minChunkY, maxChunkY);
        }
    }

    private bool IsInPrimaryVerticalCoverage(int chunkY, int surfaceChunkY, int viewerChunkY)
    {
      return IsInVerticalRange(chunkY, surfaceChunkY - SurfaceVerticalChunkMargin, surfaceChunkY + SurfaceVerticalChunkMargin) ||
             IsInVerticalRange(chunkY, viewerChunkY - ViewerVerticalChunkMargin, viewerChunkY + ViewerVerticalChunkMargin);
    }

    private static bool IsInVerticalRange(int chunkY, int minY, int maxY)
    {
      return chunkY >= Mathf.Min(minY, maxY) && chunkY <= Mathf.Max(minY, maxY);
    }

    private bool IsSecondaryVerticalCoverageChunk(Vector3Int chunkCoord)
    {
      if (!hasLastViewerChunkCoord)
      {
        return false;
      }

      int surfaceChunkY = GetSurfaceChunkYForColumn(
        chunkCoord.x,
        chunkCoord.z,
        lastViewerChunkCoord.y * VoxelConstants.ChunkSize);

      return !IsInPrimaryVerticalCoverage(chunkCoord.y, surfaceChunkY, lastViewerChunkCoord.y);
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

      // The column's XZ has already been bounds-checked by BuildChunkSet, and world
      // bounds are unbounded in Y, so every chunk in [fromY, toY] is in-bounds.
      for (int y = fromY; y <= toY; y++)
      {
        targetSet.Add(new Vector3Int(chunkX, y, chunkZ));
      }
    }

    private void QueueGeneratedChunksForRender(Vector3Int viewerChunkCoord)
    {
      ResolveVisibilityCamera();
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

        if (!ShouldQueueMeshWorkForChunk(chunkCoord))
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

      SortChunksByPriority(candidateChunksBuffer, ComputeSortPivot());
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

    private void SortChunksByPriority(List<Vector3Int> chunks, Vector3Int pivotChunkCoord)
    {
      sortPivotChunkCoord = pivotChunkCoord;
      cachedChunkPriorityComparison ??= CompareChunkPriorityCached;

      // The score is expensive (camera projection + frustum test), so evaluate it
      // once per chunk here instead of O(n log n) times inside the sort comparator.
      chunkScoreCache.Clear();
      for (int i = 0; i < chunks.Count; i++)
      {
        Vector3Int c = chunks[i];
        if (!chunkScoreCache.ContainsKey(c))
        {
          chunkScoreCache[c] = GetChunkPriorityScore(c, pivotChunkCoord);
        }
      }

      chunks.Sort(cachedChunkPriorityComparison);
    }

    private Vector3Int ComputeSortPivot()
    {
      Camera camera = cachedVisibilityCamera != null ? cachedVisibilityCamera : ResolveVisibilityCamera();
      if (camera != null)
      {
        Vector3 forward = camera.transform.forward;
        forward.y = 0.0f;

        if (forward.sqrMagnitude > 0.0001f)
        {
          forward.Normalize();
          int lead = Mathf.Max(2, ActiveDesiredRadiusInChunks / 2);
          return new Vector3Int(
            lastViewerChunkCoord.x + Mathf.RoundToInt(forward.x * lead),
            lastViewerChunkCoord.y,
            lastViewerChunkCoord.z + Mathf.RoundToInt(forward.z * lead));
        }
      }

      if (!hasLastViewerChunkCoord) return lastViewerChunkCoord;

      int fallbackLead = Mathf.Max(2, ActiveDesiredRadiusInChunks / 2);
      Vector3 heading = new(viewerHeading.x, 0.0f, viewerHeading.z);
      if (heading.sqrMagnitude < 0.0001f) return lastViewerChunkCoord;

      heading.Normalize();
      return new Vector3Int(
        lastViewerChunkCoord.x + Mathf.RoundToInt(heading.x * fallbackLead),
        lastViewerChunkCoord.y,
        lastViewerChunkCoord.z + Mathf.RoundToInt(heading.z * fallbackLead));
    }

    private int CompareChunkPriorityCached(Vector3Int a, Vector3Int b)
    {
      int aScore = chunkScoreCache[a];
      int bScore = chunkScoreCache[b];
      if (aScore != bScore) return aScore.CompareTo(bScore);

      Vector3Int pivot = sortPivotChunkCoord;

      int ad = ChunkDistanceSquared(a, pivot);
      int bd = ChunkDistanceSquared(b, pivot);
      if (ad != bd) return ad.CompareTo(bd);

      int av = Mathf.Abs(a.y - pivot.y);
      int bv = Mathf.Abs(b.y - pivot.y);
      if (av != bv) return av.CompareTo(bv);

      int ah = HorizontalChunkDistanceSquared(a, pivot);
      int bh = HorizontalChunkDistanceSquared(b, pivot);
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
