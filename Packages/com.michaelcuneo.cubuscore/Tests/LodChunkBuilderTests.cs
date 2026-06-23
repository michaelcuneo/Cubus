using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Guards the Distant-Horizon LOD generator (<see cref="LodChunkBuilder"/>): a
  /// level-L tile is a coarse 32^3 chunk (stride 2^L) whose solidity is purely
  /// height-based, so columns must fill solid from the bottom up to the surface
  /// with no floating cells. Crucially, <see cref="LodChunkBuilder.Build"/> and the
  /// mesher's neighbour <see cref="LodChunkBuilder.CreateMaterialLookup"/> must
  /// classify identical coordinates identically, otherwise adjacent LOD tiles draw
  /// seam walls instead of culling shared faces. Asset-free: builds a snapshot from
  /// default <see cref="WorldSettings"/> (fallback biome, no rules).
  /// </summary>
  [TestFixture]
  public sealed class LodChunkBuilderTests
  {
    private const int Size = VoxelConstants.ChunkSize;

    private static WorldGenerationSnapshot DefaultSnapshot()
    {
      return WorldGenerationSnapshot.FromSettings(new WorldSettings());
    }

    [Test]
    public void StrideAndVoxelSizeMath_FollowPowersOfTwo()
    {
      Assert.AreEqual(2, LodConstants.StrideForLevel(1), "Level 1 stride is 2.");
      Assert.AreEqual(16, LodConstants.StrideForLevel(4), "Level 4 stride is 16.");
      Assert.AreEqual(4.0f, LodConstants.VoxelSizeForLevel(2, 1.0f), "Level 2 mesh voxel size is stride * base.");
      Assert.AreEqual(64.0f, LodConstants.TileWorldSize(1, 1.0f), "A level-1 tile spans 32 * 2 * 1 world units.");
    }

    [Test]
    public void Build_IsDeterministic()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      var tile = new Vector3Int(3, 0, -2);

      BlockChunkData a = LodChunkBuilder.Build(snapshot, 2, tile, out _);
      BlockChunkData b = LodChunkBuilder.Build(snapshot, 2, tile, out _);

      for (int z = 0; z < Size; z++)
      {
        for (int y = 0; y < Size; y++)
        {
          for (int x = 0; x < Size; x++)
          {
            Assert.AreEqual(
                a.GetVoxel(x, y, z).MaterialId,
                b.GetVoxel(x, y, z).MaterialId,
                $"Generation must be deterministic at ({x}, {y}, {z})."
            );
          }
        }
      }
    }

    [Test]
    public void Build_ColumnsAreSolidFromBottomUp()
    {
      // Height-based solidity means that within any column, once a cell is air
      // every cell above it must also be air - no floating coarse terrain.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      BlockChunkData data = LodChunkBuilder.Build(snapshot, 1, new Vector3Int(0, 0, 0), out _);

      for (int z = 0; z < Size; z++)
      {
        for (int x = 0; x < Size; x++)
        {
          bool sawAir = false;

          for (int y = 0; y < Size; y++)
          {
            bool solid = data.GetVoxel(x, y, z).IsSolid;

            if (!solid)
            {
              sawAir = true;
            }
            else
            {
              Assert.IsFalse(
                  sawAir,
                  $"Found a solid LOD cell above an air cell in column ({x}, {z}) at y={y}."
              );
            }
          }
        }
      }
    }

    [Test]
    public void Build_MatchesMaterialLookupForInTileCells()
    {
      // The mesher resolves out-of-tile neighbours through CreateMaterialLookup;
      // for the tile's own cells that lookup must agree with the baked data, or
      // adjacent tiles will disagree on the shared boundary and draw seam walls.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      const int level = 3;
      var tile = new Vector3Int(-1, 0, 5);

      BlockChunkData data = LodChunkBuilder.Build(snapshot, level, tile, out _);
      BlockGreedyMesher.MaterialLookup lookup = LodChunkBuilder.CreateMaterialLookup(snapshot, level);

      for (int z = 0; z < Size; z++)
      {
        for (int y = 0; y < Size; y++)
        {
          for (int x = 0; x < Size; x++)
          {
            Vector3Int coarseGlobal = VoxelMath.LocalToWorldVoxel(tile, x, y, z);

            Assert.AreEqual(
                data.GetVoxel(x, y, z).MaterialId,
                lookup(coarseGlobal),
                $"Build and material lookup disagree at coarse-global {coarseGlobal}."
            );
          }
        }
      }
    }

    [Test]
    public void Build_TileFarAboveSurfaceIsEmpty()
    {
      // Real terrain heights are modest; a tile whose entire range sits tens of
      // thousands of voxels up must resolve to pure air.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      BlockChunkData data = LodChunkBuilder.Build(snapshot, 1, new Vector3Int(0, 1000, 0), out bool hasSolid);

      Assert.IsFalse(hasSolid, "A tile far above the surface should contain no solid cells.");
      Assert.IsFalse(data.HasAnySolidVoxel(), "HasAnySolidVoxel must agree with the out flag.");
    }

    [Test]
    public void Build_TileFarBelowSurfaceIsFullySolid()
    {
      // Far underground every coarse cell is below the surface, so the whole tile
      // is solid.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      BlockChunkData data = LodChunkBuilder.Build(snapshot, 1, new Vector3Int(0, -1000, 0), out bool hasSolid);

      Assert.IsTrue(hasSolid, "A deep underground tile should be solid.");

      for (int z = 0; z < Size; z++)
      {
        for (int y = 0; y < Size; y++)
        {
          for (int x = 0; x < Size; x++)
          {
            Assert.IsTrue(
                data.GetVoxel(x, y, z).IsSolid,
                $"Deep underground cell ({x}, {y}, {z}) should be solid."
            );
          }
        }
      }
    }
  }
}
