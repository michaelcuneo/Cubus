using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Shared, asset-free builders for the EditMode validation suite. Worlds are
  /// configured with no biome rules so generation runs through the deterministic
  /// fallback profile (see WorldSettings.ResolveBiomeAtWorldXZ), keeping tests
  /// independent of project ScriptableObject assets.
  /// </summary>
  internal static class CubusTestFactory
  {
    internal static WorldSettings BuildSettings(TerrainSystem terrainSystem, int worldSeed)
    {
      return new WorldSettings
      {
        TerrainSystem = terrainSystem,
        WorldSeed = worldSeed
      };
    }

    internal static WorldSettings BuildBoundedSettings(
      TerrainSystem terrainSystem,
      int worldSeed,
      Vector3Int min,
      Vector3Int max)
    {
      return new WorldSettings
      {
        TerrainSystem = terrainSystem,
        WorldSeed = worldSeed,
        UseFixedGenerationBounds = true,
        GenerationMinChunk = min,
        GenerationMaxChunk = max
      };
    }

    /// <summary>Fills a block chunk with a deterministic, non-uniform material pattern.</summary>
    internal static BlockChunkData BuildPatternedBlockChunk(Vector3Int coord)
    {
      BlockChunkData chunk = new(coord);

      for (int z = 0; z < VoxelConstants.ChunkSize; z++)
      {
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
          for (int x = 0; x < VoxelConstants.ChunkSize; x++)
          {
            // Mix of air (0) and several solid materials to exercise the codec.
            ushort material = (ushort)((x * 7 + y * 13 + z * 17) % 8);
            chunk.SetVoxel(x, y, z, new Voxel(material));
          }
        }
      }

      return chunk;
    }

    /// <summary>
    /// Fills a density chunk with a deterministic, non-uniform field so the codec
    /// exercises its RLE path rather than the uniform fast path.
    /// </summary>
    internal static DensityChunkData BuildPatternedDensityChunk(Vector3Int coord)
    {
      DensityChunkData chunk = new(coord);

      for (int z = 0; z < VoxelConstants.ChunkSize; z++)
      {
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
          for (int x = 0; x < VoxelConstants.ChunkSize; x++)
          {
            float density = Mathf.Sin(x * 0.3f + y * 0.11f + z * 0.2f) * 3.0f;
            // Solid voxels (density > 0) must carry a non-zero material; air voxels
            // are forced to material 0 by DensityVoxel. Respect that invariant so the
            // codec round-trip stays lossless for material ids.
            ushort material = (ushort)(((x + y + z) % 4) + 1);
            chunk.SetVoxel(x, y, z, new DensityVoxel(density, material));
          }
        }
      }

      return chunk;
    }

    /// <summary>
    /// Builds a layered density chunk (solid lower half, empty upper half) that
    /// resembles real terrain and contains long uniform runs the RLE codec can
    /// compress, while avoiding the fully-uniform fast path.
    /// </summary>
    internal static DensityChunkData BuildLayeredDensityChunk(Vector3Int coord)
    {
      DensityChunkData chunk = new(coord);
      const int half = VoxelConstants.ChunkSize / 2;

      for (int z = 0; z < VoxelConstants.ChunkSize; z++)
      {
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
          for (int x = 0; x < VoxelConstants.ChunkSize; x++)
          {
            bool solid = y < half;
            float density = solid ? 1.0f : -1.0f;
            ushort material = (ushort)(solid ? 1 : 0);
            chunk.SetVoxel(x, y, z, new DensityVoxel(density, material));
          }
        }
      }

      return chunk;
    }
  }
}
