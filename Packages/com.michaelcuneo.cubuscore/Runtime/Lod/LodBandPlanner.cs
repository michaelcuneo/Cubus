using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Pure geometry helper that decides which Distant-Horizon LOD tiles should be
  /// resident around a viewer. Levels are arranged as concentric, mutually
  /// exclusive rings measured in base-chunk units:
  ///
  ///   LOD0 (full-detail chunks, owned by WorldStreamer): radius &lt; viewDistanceInChunks
  ///   LOD L (stride 2^L):  band [inner_L, outer_L) where
  ///       width_L = ringWidthInTiles * 2^L   (a ring is N tiles thick)
  ///       inner_L = viewDistanceInChunks + sum_{k&lt;L} width_k
  ///       outer_L = inner_L + width_L
  ///
  /// A level-L tile spans 2^L base chunks per axis, so a tile is classified by the
  /// horizontal (Chebyshev) distance of its centre chunk from the viewer. Vertical
  /// coverage is a thin band of tiles centred on the terrain surface, so distant
  /// tiles follow the horizon instead of filling empty sky/underground.
  ///
  /// Kept allocation-free and free of Unity scene state so it can be unit-tested
  /// and called off the main thread.
  /// </summary>
  public static class LodBandPlanner
  {
    /// <summary>
    /// Appends the desired LOD tile keys (levels 1..levelCount) into
    /// <paramref name="results"/>. Does not clear <paramref name="results"/>.
    /// </summary>
    /// <param name="viewerChunkCoord">Viewer position in base-chunk coordinates.</param>
    /// <param name="viewDistanceInChunks">Full-detail radius (LOD0) in base chunks.</param>
    /// <param name="levelCount">Number of LOD levels to emit (clamped to [0, MaxLodLevel]).</param>
    /// <param name="ringWidthInTiles">Thickness of each level's ring, in that level's tiles (min 1).</param>
    /// <param name="verticalRadiusInTiles">Extra tiles above/below the surface tile to include on top of the full world span (min 0).</param>
    /// <param name="worldMinChunkY">Bottom of the world's vertical chunk extent. The band always covers down to here.</param>
    /// <param name="worldMaxChunkY">Top of the world's vertical chunk extent. The band always covers up to here.</param>
    /// <param name="surfaceChunkYProvider">(chunkX, chunkZ) =&gt; surface base-chunk Y. Required.</param>
    /// <param name="inBounds">Base-chunk coord =&gt; inside the world. Null means unbounded.</param>
    /// <param name="results">Destination list (appended to).</param>
    public static void ComputeDesiredTiles(
      Vector3Int viewerChunkCoord,
      int viewDistanceInChunks,
      int levelCount,
      int ringWidthInTiles,
      int verticalRadiusInTiles,
      int worldMinChunkY,
      int worldMaxChunkY,
      Func<int, int, int> surfaceChunkYProvider,
      Func<Vector3Int, bool> inBounds,
      List<LodTileKey> results)
    {
      if (results == null)
      {
        throw new ArgumentNullException(nameof(results));
      }

      if (surfaceChunkYProvider == null)
      {
        throw new ArgumentNullException(nameof(surfaceChunkYProvider));
      }

      int maxLevel = Mathf.Clamp(levelCount, 0, LodConstants.MaxLodLevel);
      int ringWidth = Mathf.Max(1, ringWidthInTiles);
      int verticalRadius = Mathf.Max(0, verticalRadiusInTiles);
      int baseRadius = Mathf.Max(0, viewDistanceInChunks);

      int inner = baseRadius;

      for (int level = LodConstants.MinLodLevel; level <= maxLevel; level++)
      {
        int strideChunks = LodConstants.StrideForLevel(level); // 2^level base chunks per tile axis
        int halfStride = strideChunks / 2;
        int width = ringWidth * strideChunks;
        int outer = inner + width;

        int viewerTileX = VoxelMath.FloorDiv(viewerChunkCoord.x, strideChunks);
        int viewerTileZ = VoxelMath.FloorDiv(viewerChunkCoord.z, strideChunks);

        // Enough tiles to reach 'outer' base chunks away, plus a margin.
        int extentInTiles = (outer / strideChunks) + 2;

        for (int tz = viewerTileZ - extentInTiles; tz <= viewerTileZ + extentInTiles; tz++)
        {
          int centreChunkZ = tz * strideChunks + halfStride;
          int distZ = Mathf.Abs(centreChunkZ - viewerChunkCoord.z);

          for (int tx = viewerTileX - extentInTiles; tx <= viewerTileX + extentInTiles; tx++)
          {
            int centreChunkX = tx * strideChunks + halfStride;
            int distX = Mathf.Abs(centreChunkX - viewerChunkCoord.x);

            int chebyshev = Mathf.Max(distX, distZ);
            if (chebyshev < inner || chebyshev >= outer)
            {
              continue;
            }

            int surfaceChunkY = surfaceChunkYProvider(centreChunkX, centreChunkZ);
            int surfaceTileY = VoxelMath.FloorDiv(surfaceChunkY, strideChunks);

            // Cover the world's ENTIRE vertical extent at this column, not a thin
            // band around the surface. Following the surface with an above/below
            // limit left voids wherever distant terrain rose or fell past the
            // band, which showed up as floating island fragments at the horizon.
            // verticalRadius still extends past the world span as a safety margin.
            int worldMinTileY = VoxelMath.FloorDiv(worldMinChunkY, strideChunks);
            int worldMaxTileY = VoxelMath.FloorDiv(worldMaxChunkY, strideChunks);
            int minTileY = Mathf.Min(worldMinTileY, surfaceTileY - verticalRadius);
            int maxTileY = Mathf.Max(worldMaxTileY, surfaceTileY + verticalRadius);

            for (int tileY = minTileY; tileY <= maxTileY; tileY++)
            {
              if (inBounds != null)
              {
                var centreChunkCoord = new Vector3Int(centreChunkX, tileY * strideChunks + halfStride, centreChunkZ);
                if (!inBounds(centreChunkCoord))
                {
                  continue;
                }
              }

              results.Add(new LodTileKey(level, new Vector3Int(tx, tileY, tz)));
            }
          }
        }

        inner = outer;
      }
    }
  }
}
