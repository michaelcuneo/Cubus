using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 3 - Deterministic world generation validation.
  /// Mirrors WorldGenerationDeterminismValidator as repeatable EditMode tests:
  /// identical settings must produce byte-identical chunk data regardless of
  /// generation order or repeated runs.
  /// </summary>
  [TestFixture]
  public sealed class WorldDeterminismTests
  {
    private static readonly Vector3Int[] Coords =
    {
      new(0, 0, 0),
      new(1, 0, 0),
      new(0, 0, 1),
      new(-1, 0, -1),
      new(2, 0, -2)
    };

    [Test]
    public void BlockGeneration_IsRepeatable_ForSameSeed()
    {
      WorldSettings settings = CubusTestFactory.BuildSettings(TerrainSystem.Block, worldSeed: 1337);
      var generatorA = new WorldGenerator(settings);
      var generatorB = new WorldGenerator(settings);

      foreach (Vector3Int coord in Coords)
      {
        var first = new BlockChunkData(coord);
        generatorA.GenerateBlockChunkData(first);

        var second = new BlockChunkData(coord);
        generatorB.GenerateBlockChunkData(second);

        Assert.IsTrue(
          ChunkDeterminismHash.AreEqual(first, second, out string diff),
          $"Block chunk {coord} differed between runs. {diff}");
        Assert.AreEqual(
          ChunkDeterminismHash.Hash(first),
          ChunkDeterminismHash.Hash(second),
          $"Block chunk {coord} hash mismatch.");
      }
    }

    [Test]
    public void DensityGeneration_IsRepeatable_ForSameSeed()
    {
      WorldSettings settings = CubusTestFactory.BuildSettings(TerrainSystem.SmoothDensity, worldSeed: 4242);
      var generatorA = new WorldGenerator(settings);
      var generatorB = new WorldGenerator(settings);

      foreach (Vector3Int coord in Coords)
      {
        var first = new DensityChunkData(coord);
        generatorA.FillDensityChunkFromTerrainSampler(first);

        var second = new DensityChunkData(coord);
        generatorB.FillDensityChunkFromTerrainSampler(second);

        Assert.IsTrue(
          ChunkDeterminismHash.AreEqual(first, second, out string diff),
          $"Density chunk {coord} differed between runs. {diff}");
      }
    }

    [Test]
    public void BlockGeneration_IsOrderIndependent()
    {
      WorldSettings settings = CubusTestFactory.BuildSettings(TerrainSystem.Block, worldSeed: 9001);
      var generator = new WorldGenerator(settings);

      // Forward pass.
      var forwardHashes = new Dictionary<Vector3Int, ulong>(Coords.Length);
      for (int i = 0; i < Coords.Length; i++)
      {
        var chunk = new BlockChunkData(Coords[i]);
        generator.GenerateBlockChunkData(chunk);
        forwardHashes[Coords[i]] = ChunkDeterminismHash.Hash(chunk);
      }

      // Reverse pass with the same generator instance.
      for (int i = Coords.Length - 1; i >= 0; i--)
      {
        var chunk = new BlockChunkData(Coords[i]);
        generator.GenerateBlockChunkData(chunk);
        Assert.AreEqual(
          forwardHashes[Coords[i]],
          ChunkDeterminismHash.Hash(chunk),
          $"Block chunk {Coords[i]} depended on generation order.");
      }
    }

    [Test]
    public void WorldSeed_ChangesGeneratedTerrain()
    {
      // Sanity check that the seed is actually wired into generation: across a
      // span of chunks, two distinct seeds must diverge somewhere.
      var generatorA = new WorldGenerator(CubusTestFactory.BuildSettings(TerrainSystem.SmoothDensity, worldSeed: 1));
      var generatorB = new WorldGenerator(CubusTestFactory.BuildSettings(TerrainSystem.SmoothDensity, worldSeed: 2));

      bool anyDifference = false;

      foreach (Vector3Int coord in Coords)
      {
        var chunkA = new DensityChunkData(coord);
        generatorA.FillDensityChunkFromTerrainSampler(chunkA);

        var chunkB = new DensityChunkData(coord);
        generatorB.FillDensityChunkFromTerrainSampler(chunkB);

        if (!ChunkDeterminismHash.AreEqual(chunkA, chunkB, out _))
        {
          anyDifference = true;
          break;
        }
      }

      Assert.IsTrue(
        anyDifference,
        "Different world seeds produced identical density data across all sampled chunks; "
        + "the seed may not be wired into generation.");
    }
  }
}
