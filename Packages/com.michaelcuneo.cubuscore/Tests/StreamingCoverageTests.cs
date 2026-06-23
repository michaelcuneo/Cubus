using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 4 - Chunk streaming coverage (generation-side, EditMode-safe slice).
  /// The live streamer/renderer validators require Play mode, but the invariant
  /// that bounded generation covers exactly the configured chunk volume - with no
  /// gaps or duplicates - can be validated headlessly here.
  /// </summary>
  [TestFixture]
  public sealed class StreamingCoverageTests
  {
    [Test]
    public void BoundedDensityGeneration_CoversEveryChunk_NoGaps()
    {
      var min = new Vector3Int(-1, 0, -2);
      var max = new Vector3Int(1, 0, 2);
      WorldSettings settings = CubusTestFactory.BuildBoundedSettings(TerrainSystem.SmoothDensity, 7, min, max);

      var worldData = new WorldData();
      new WorldGenerator(settings).Generate(worldData);

      HashSet<Vector3Int> expected = ExpectedCoords(settings);

      Assert.AreEqual(expected.Count, worldData.DensityChunks.Count, "Generated chunk count differs from bounds volume.");
      foreach (Vector3Int coord in expected)
      {
        Assert.IsTrue(worldData.DensityChunks.ContainsKey(coord), $"Missing generated chunk at {coord}.");
      }
    }

    [Test]
    public void BoundedBlockGeneration_CoversEveryChunk_NoGaps()
    {
      var min = new Vector3Int(0, 0, 0);
      var max = new Vector3Int(2, 1, 2);
      WorldSettings settings = CubusTestFactory.BuildBoundedSettings(TerrainSystem.Block, 11, min, max);

      var worldData = new WorldData();
      new WorldGenerator(settings).Generate(worldData);

      HashSet<Vector3Int> expected = ExpectedCoords(settings);

      Assert.AreEqual(expected.Count, worldData.BlockChunks.Count, "Generated chunk count differs from bounds volume.");
      foreach (Vector3Int coord in expected)
      {
        Assert.IsTrue(worldData.BlockChunks.ContainsKey(coord), $"Missing generated chunk at {coord}.");
      }
    }

    private static HashSet<Vector3Int> ExpectedCoords(WorldSettings settings)
    {
      settings.GetEffectiveGenerationChunkBounds3D(
        out int minX, out int maxX,
        out int minY, out int maxY,
        out int minZ, out int maxZ);

      var coords = new HashSet<Vector3Int>();
      for (int y = minY; y <= maxY; y++)
      {
        for (int z = minZ; z <= maxZ; z++)
        {
          for (int x = minX; x <= maxX; x++)
          {
            coords.Add(new Vector3Int(x, y, z));
          }
        }
      }

      return coords;
    }
  }
}
