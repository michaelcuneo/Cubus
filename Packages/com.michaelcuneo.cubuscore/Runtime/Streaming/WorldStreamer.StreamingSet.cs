using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {

    // Number of chunks of vertical headroom kept above each column's surface
    // chunk so the chunk straddling the surface (and any thin overhang) is always
    // streamed.
    private const int SurfaceVerticalChunkMargin = 1;

    // Builds a desired/keep set whose horizontal extent is the view radius. The
    // vertical extent is surface-AWARE AND honours the configured world band: for
    // every (x, z) column we cover the UNION of the world band
    // (BlockMin/MaxChunkY or DensityMin/MaxChunkY) and this column's surface chunk
    // (+/- a small margin).
    //
    // The world-band union is the SAFETY NET: it guarantees every column always
    // covers the full playable terrain band regardless of how the per-column
    // surface sample lands, so steep/undulating terrain never drops the chunk that
    // actually contains the surface (which shows up as missing chunks/holes). The
    // surface term extends that coverage upward/downward when terrain rises above
    // or sinks below the band. Empty chunks above the terrain are cheap - flagged
    // known-empty as soon as they mesh to nothing.
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

          int columnMinY = surfaceChunkY - SurfaceVerticalChunkMargin;
          int columnMaxY = surfaceChunkY + SurfaceVerticalChunkMargin;

          columnMinY = Mathf.Clamp(columnMinY, minChunkY, maxChunkY);
          columnMaxY = Mathf.Clamp(columnMaxY, minChunkY, maxChunkY);

          for (int y = columnMinY; y <= columnMaxY; y++)
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
      // Proactively load + mesh ONLY the desired (view-radius) set. The keep set
      // is a DATA / hysteresis cache, not a render region: meshing the whole keep
      // set is catastrophic when the unload padding is large (e.g. view 16 +
      // padding 16 => keep radius 32 => ~4x the desired chunk count), which buries
      // the worker pool under tens of thousands of invisible chunks and stalls the
      // frontier ("walk to the edge and it never loads"). Already-built meshes are
      // left untouched out to the keep radius (UnloadOutsideKeepSet only removes
      // beyond it), so terrain still lingers smoothly as you move - we just stop
      // GENERATING terrain you cannot see past the render radius.
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (worldRenderer.HasChunkView(chunkCoord) || pendingLoadSet.Contains(chunkCoord) || pendingRenderSet.Contains(chunkCoord) || knownEmptyChunks.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord)) continue;
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) { knownEmptyChunks.Add(chunkCoord); continue; }
        candidateChunksBuffer.Add(chunkCoord);
      }

      candidateChunksBuffer.Sort(GetChunkPriorityComparison(ComputeSortPivot()));
      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        if (HasChunkData(candidateChunksBuffer[i])) QueueRender(candidateChunksBuffer[i]);
        else QueueLoad(candidateChunksBuffer[i]);
      }
    }

    // Returns the cached comparison delegate configured for the given pivot.
    // Avoids allocating a new closure/Comparison on every Sort call.
    private Comparison<Vector3Int> GetChunkPriorityComparison(Vector3Int pivotChunkCoord)
    {
      sortPivotChunkCoord = pivotChunkCoord;
      chunkPriorityComparison ??= CompareChunkPriorityByPivot;
      return chunkPriorityComparison;
    }

    // The point the build queues sort around. Normally the viewer, but shifted
    // forward along the recent heading by half the desired radius so chunks in the
    // direction of travel are generated BEFORE the viewer arrives (predictive
    // prefetch). Falls back to the viewer when stationary.
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

      // Remember which way the viewer is travelling (in chunk space) so the build
      // queues can be biased toward the frontier ahead instead of always nearest-
      // first. Without this the chunks you are walking INTO are the lowest priority
      // and only get built once you are on top of them, leaving gaps at the edge.
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
      PruneKnownEmptyChunksOutsideCurrentInterest(); QueueGeneratedChunksForRender(viewerChunkCoord);
      UnloadOutsideKeepSet();
      pendingLoadQueueNeedsPrioritization = true;
      pendingRenderQueueNeedsPrioritization = true;
    }

    private void PruneKnownEmptyChunksOutsideCurrentInterest()
    {
      if (knownEmptyChunks.Count == 0)
      {
        return;
      }

      candidateChunksBuffer.Clear();

      foreach (Vector3Int chunkCoord in knownEmptyChunks)
      {
        if (!desiredChunkCoords.Contains(chunkCoord) && !keepChunkCoords.Contains(chunkCoord))
        {
          candidateChunksBuffer.Add(chunkCoord);
        }
      }

      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        knownEmptyChunks.Remove(candidateChunksBuffer[i]);
      }

      candidateChunksBuffer.Clear();
    }
  }
}