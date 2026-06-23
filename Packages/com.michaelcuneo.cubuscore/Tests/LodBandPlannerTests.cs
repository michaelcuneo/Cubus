using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Verifies the Distant-Horizon ring planner: levels form mutually exclusive
  /// distance bands, every emitted tile is the correct level for its distance,
  /// and the vertical band follows the terrain surface.
  /// </summary>
  [TestFixture]
  public sealed class LodBandPlannerTests
  {
    private const int ViewDistanceInChunks = 4;
    private const int LevelCount = 3;
    private const int RingWidthInTiles = 2;

    // Flat surface at chunk Y = 0 everywhere, unbounded world.
    private static List<LodTileKey> Plan(
      Vector3Int viewerChunkCoord,
      int verticalRadiusInTiles,
      Func<int, int, int> surfaceProvider = null,
      Func<Vector3Int, bool> inBounds = null)
    {
      var results = new List<LodTileKey>();
      LodBandPlanner.ComputeDesiredTiles(
        viewerChunkCoord,
        ViewDistanceInChunks,
        LevelCount,
        RingWidthInTiles,
        verticalRadiusInTiles,
        surfaceProvider ?? ((x, z) => 0),
        inBounds,
        results);
      return results;
    }

    private static int Chebyshev(LodTileKey key, Vector3Int viewerChunkCoord)
    {
      int stride = LodConstants.StrideForLevel(key.Level);
      int cx = key.Coord.x * stride + stride / 2;
      int cz = key.Coord.z * stride + stride / 2;
      return Mathf.Max(Mathf.Abs(cx - viewerChunkCoord.x), Mathf.Abs(cz - viewerChunkCoord.z));
    }

    [Test]
    public void EmitsTiles_ForEveryLevel()
    {
      List<LodTileKey> tiles = Plan(Vector3Int.zero, 0);

      for (int level = LodConstants.MinLodLevel; level <= LevelCount; level++)
      {
        int captured = level;
        Assert.IsTrue(tiles.Exists(t => t.Level == captured), $"Expected at least one tile at LOD level {captured}.");
      }
    }

    [Test]
    public void EveryTile_FallsWithinItsLevelBand()
    {
      var viewer = Vector3Int.zero;
      List<LodTileKey> tiles = Plan(viewer, 0);

      // Reconstruct band ranges: inner_1 = R0, width_L = ring * 2^L.
      int inner = ViewDistanceInChunks;
      var innerByLevel = new Dictionary<int, int>();
      var outerByLevel = new Dictionary<int, int>();
      for (int level = LodConstants.MinLodLevel; level <= LevelCount; level++)
      {
        int width = RingWidthInTiles * LodConstants.StrideForLevel(level);
        innerByLevel[level] = inner;
        outerByLevel[level] = inner + width;
        inner += width;
      }

      foreach (LodTileKey tile in tiles)
      {
        int d = Chebyshev(tile, viewer);
        Assert.GreaterOrEqual(d, innerByLevel[tile.Level], $"Tile {tile} is nearer than its band start.");
        Assert.Less(d, outerByLevel[tile.Level], $"Tile {tile} is farther than its band end.");
      }
    }

    [Test]
    public void Bands_DoNotOverlapInDistance()
    {
      List<LodTileKey> tiles = Plan(Vector3Int.zero, 0);

      // The maximum distance of level L must be below the minimum distance of L+1
      // boundaries are contiguous, so a tile centre never lands in two bands.
      var minByLevel = new Dictionary<int, int>();
      var maxByLevel = new Dictionary<int, int>();
      foreach (LodTileKey tile in tiles)
      {
        int d = Chebyshev(tile, Vector3Int.zero);
        if (!minByLevel.ContainsKey(tile.Level) || d < minByLevel[tile.Level]) minByLevel[tile.Level] = d;
        if (!maxByLevel.ContainsKey(tile.Level) || d > maxByLevel[tile.Level]) maxByLevel[tile.Level] = d;
      }

      for (int level = LodConstants.MinLodLevel; level < LevelCount; level++)
      {
        if (maxByLevel.ContainsKey(level) && minByLevel.ContainsKey(level + 1))
        {
          Assert.Less(maxByLevel[level], minByLevel[level + 1],
            $"LOD{level} reaches farther than LOD{level + 1} begins (bands overlap).");
        }
      }
    }

    [Test]
    public void VerticalRadius_AddsTilesAboveAndBelowSurface()
    {
      List<LodTileKey> flat = Plan(Vector3Int.zero, 0);
      List<LodTileKey> stacked = Plan(Vector3Int.zero, 1);

      Assert.Greater(stacked.Count, flat.Count, "A vertical radius must add tiles above/below the surface.");

      int stride = LodConstants.StrideForLevel(1);
      int surfaceTileY = VoxelMath.FloorDiv(0, stride);

      Assert.Contains(surfaceTileY, GetLevelYs(stacked, 1, surfaceTileY), "Surface tile Y should be present at LOD1.");
      Assert.Contains(surfaceTileY + 1, GetLevelYs(stacked, 1, surfaceTileY), "Tile above surface should be present at LOD1.");
      Assert.Contains(surfaceTileY - 1, GetLevelYs(stacked, 1, surfaceTileY), "Tile below surface should be present at LOD1.");
    }

    private static int[] GetLevelYs(List<LodTileKey> tiles, int level, int aroundY)
    {
      var ys = new List<int>();
      foreach (LodTileKey tile in tiles)
      {
        if (tile.Level == level && Mathf.Abs(tile.Coord.y - aroundY) <= 1)
        {
          ys.Add(tile.Coord.y);
        }
      }

      return ys.ToArray();
    }

    [Test]
    public void SurfaceProvider_ShiftsVerticalBand()
    {
      // Surface at chunk Y = 16 -> level-1 surface tile Y = FloorDiv(16, 2) = 8.
      List<LodTileKey> tiles = Plan(Vector3Int.zero, 0, surfaceProvider: (x, z) => 16);

      foreach (LodTileKey tile in tiles)
      {
        int stride = LodConstants.StrideForLevel(tile.Level);
        int expectedTileY = VoxelMath.FloorDiv(16, stride);
        Assert.AreEqual(expectedTileY, tile.Coord.y, $"Tile {tile} should sit on the surface tile row.");
      }
    }

    [Test]
    public void InBoundsPredicate_FiltersTiles()
    {
      List<LodTileKey> unbounded = Plan(Vector3Int.zero, 0);
      // Only allow tiles with non-negative X centre.
      List<LodTileKey> bounded = Plan(Vector3Int.zero, 0, inBounds: c => c.x >= 0);

      Assert.Less(bounded.Count, unbounded.Count, "A bounds predicate must remove some tiles.");
      foreach (LodTileKey tile in bounded)
      {
        int stride = LodConstants.StrideForLevel(tile.Level);
        int centreX = tile.Coord.x * stride + stride / 2;
        Assert.GreaterOrEqual(centreX, 0, $"Tile {tile} should have been filtered out by bounds.");
      }
    }
  }
}
