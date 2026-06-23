using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 5 - Edit persistence and dirty chunk overrides.
  /// Validates the override-application path used when re-applying saved edits to
  /// freshly generated chunks, and the WorldData override-retention contract that
  /// keeps player edits alive when generated chunk data is cleared.
  /// </summary>
  [TestFixture]
  public sealed class EditPersistenceTests
  {
    private static int VoxelIndex(int x, int y, int z)
    {
      return x + (y * VoxelConstants.ChunkSize) + (z * VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);
    }

    [Test]
    public void DensityOverrides_AreAppliedToChunkData()
    {
      var coord = new Vector3Int(0, 0, 0);
      var chunk = new DensityChunkData(coord);

      var overrides = new Dictionary<int, DensityVoxelOverride>
      {
        { VoxelIndex(5, 6, 7), new DensityVoxelOverride(0.75f, 3) },
        { VoxelIndex(10, 0, 31), new DensityVoxelOverride(-0.25f, 9) }
      };

      DensityChunkBuilder.ApplyOverrides(chunk, overrides);

      DensityVoxel a = chunk.GetVoxel(5, 6, 7);
      Assert.AreEqual(0.75f, a.Density, 1e-6f);
      Assert.AreEqual(3, a.MaterialId);

      DensityVoxel b = chunk.GetVoxel(10, 0, 31);
      Assert.AreEqual(-0.25f, b.Density, 1e-6f);
      // Negative density = air; DensityVoxel coerces air voxels to material 0.
      Assert.AreEqual(0, b.MaterialId);
    }

    [Test]
    public void DensityOverrides_IgnoreOutOfRangeIndices()
    {
      var coord = new Vector3Int(0, 0, 0);
      var chunk = new DensityChunkData(coord);

      var overrides = new Dictionary<int, DensityVoxelOverride>
      {
        { -1, new DensityVoxelOverride(1.0f, 1) },
        { VoxelConstants.ChunkVolume, new DensityVoxelOverride(1.0f, 1) }
      };

      Assert.DoesNotThrow(() => DensityChunkBuilder.ApplyOverrides(chunk, overrides));
    }

    [Test]
    public void WorldData_ClearGeneratedChunks_RetainsOverrides()
    {
      var worldData = new WorldData();
      var coord = new Vector3Int(1, 0, 1);

      worldData.DensityChunks[coord] = new DensityChunkData(coord);
      worldData.BlockChunks[coord] = new BlockChunkData(coord);
      worldData.BlockVoxelOverridesByChunk[coord] = new Dictionary<int, ushort> { { VoxelIndex(0, 0, 0), 4 } };
      worldData.DensityVoxelOverridesByChunk[coord] =
        new Dictionary<int, DensityVoxelOverride> { { VoxelIndex(1, 1, 1), new DensityVoxelOverride(0.5f, 2) } };

      worldData.ClearGeneratedChunks();

      Assert.AreEqual(0, worldData.DensityChunks.Count, "Generated density chunks should be cleared.");
      Assert.AreEqual(0, worldData.BlockChunks.Count, "Generated block chunks should be cleared.");
      Assert.AreEqual(1, worldData.BlockVoxelOverridesByChunk.Count, "Block overrides must survive a generated-chunk clear.");
      Assert.AreEqual(1, worldData.DensityVoxelOverridesByChunk.Count, "Density overrides must survive a generated-chunk clear.");
    }

    [Test]
    public void WorldData_ClearOverrides_RemovesOnlyOverrides()
    {
      var worldData = new WorldData();
      var coord = new Vector3Int(0, 0, 0);

      worldData.BlockChunks[coord] = new BlockChunkData(coord);
      worldData.BlockVoxelOverridesByChunk[coord] = new Dictionary<int, ushort> { { 0, 1 } };

      worldData.ClearOverrides();

      Assert.AreEqual(1, worldData.BlockChunks.Count, "ClearOverrides must not touch generated chunks.");
      Assert.AreEqual(0, worldData.BlockVoxelOverridesByChunk.Count, "ClearOverrides must remove block overrides.");
    }

    [Test]
    public void WorldData_ClearAll_RemovesEverything()
    {
      var worldData = new WorldData();
      var coord = new Vector3Int(0, 0, 0);

      worldData.BlockChunks[coord] = new BlockChunkData(coord);
      worldData.DensityChunks[coord] = new DensityChunkData(coord);
      worldData.BlockVoxelOverridesByChunk[coord] = new Dictionary<int, ushort> { { 0, 1 } };
      worldData.DensityVoxelOverridesByChunk[coord] =
        new Dictionary<int, DensityVoxelOverride> { { 0, new DensityVoxelOverride(1f, 1) } };

      worldData.ClearAll();

      Assert.AreEqual(0, worldData.BlockChunks.Count);
      Assert.AreEqual(0, worldData.DensityChunks.Count);
      Assert.AreEqual(0, worldData.BlockVoxelOverridesByChunk.Count);
      Assert.AreEqual(0, worldData.DensityVoxelOverridesByChunk.Count);
    }
  }
}
