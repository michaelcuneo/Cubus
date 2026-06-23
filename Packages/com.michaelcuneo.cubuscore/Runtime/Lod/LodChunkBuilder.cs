using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Builds a coarse <c>32^3</c> <see cref="BlockChunkData"/> for one Distant-Horizon
  /// LOD tile by downsampling the terrain. Each coarse voxel represents a
  /// <c>stride^3</c> block of the real world (<c>stride = 2^level</c>) and is
  /// classified from a single representative sample at the block centre, using the
  /// exact same height-based solidity rule as the full-detail block pipeline
  /// (<c>worldY &lt;= floor(column.SurfaceHeight)</c>) so LOD silhouettes line up
  /// with near terrain.
  ///
  /// Pure and deterministic given a <see cref="WorldGenerationSnapshot"/>; safe to
  /// run off the main thread (uses only the managed <see cref="TerrainColumnSampler"/>,
  /// no Unity scene state).
  /// </summary>
  public static class LodChunkBuilder
  {
    /// <summary>
    /// Generates the coarse voxel data for an LOD tile.
    /// </summary>
    /// <param name="snapshot">Immutable world-generation parameters.</param>
    /// <param name="level">LOD level (>= 1); stride is <c>2^level</c>.</param>
    /// <param name="tileCoord">
    /// Tile coordinate in this level's tile grid. The tile covers coarse voxels
    /// <c>[tileCoord*32, tileCoord*32 + 32)</c>, i.e. real voxels
    /// <c>[tileCoord*32*stride, ...)</c>. Used directly as the returned data's
    /// <see cref="BlockChunkData.ChunkCoord"/> so positioning with
    /// <c>voxelSize = stride*VoxelSize</c> lands the tile in world space correctly.
    /// </param>
    /// <param name="hasAnySolidVoxel">True if any coarse cell resolved solid.</param>
    public static BlockChunkData Build(
        WorldGenerationSnapshot snapshot,
        int level,
        Vector3Int tileCoord,
        out bool hasAnySolidVoxel)
    {
      const int size = VoxelConstants.ChunkSize;
      int stride = LodConstants.StrideForLevel(level);
      int half = stride / 2;

      var data = new BlockChunkData(tileCoord);
      hasAnySolidVoxel = false;

      var column = new TerrainColumnSampler();

      for (int cz = 0; cz < size; cz++)
      {
        int coarseGlobalZ = tileCoord.z * size + cz;
        int realCenterZ = coarseGlobalZ * stride + half;

        for (int cx = 0; cx < size; cx++)
        {
          int coarseGlobalX = tileCoord.x * size + cx;
          int realCenterX = coarseGlobalX * stride + half;

          column.Prepare(snapshot, realCenterX, realCenterZ, LodConstants.BlockDensityScale);
          int surfaceFloor = Mathf.FloorToInt(column.SurfaceHeight);

          for (int cy = 0; cy < size; cy++)
          {
            int coarseGlobalY = tileCoord.y * size + cy;
            int realCenterY = coarseGlobalY * stride + half;

            ushort material = ClassifyPreparedColumn(
                column,
                realCenterX,
                realCenterY,
                realCenterZ,
                surfaceFloor
            );

            if (material != 0)
            {
              data.SetVoxel(cx, cy, cz, new Voxel(material));
              hasAnySolidVoxel = true;
            }
          }
        }
      }

      return data;
    }

    /// <summary>
    /// Material lookup for meshing an LOD tile, matching <see cref="Build"/>'s
    /// classification so adjacent same-level tiles cull their shared boundary
    /// faces. The greedy mesher calls this with the coarse-global voxel coordinate
    /// (<c>tileCoord*32 + local</c>, local in <c>[-1, 32]</c>); out-of-tile cells
    /// are sampled exactly like in-tile cells, so neighbouring tiles agree.
    /// Columns are cached per call so the surface noise is resolved once per
    /// <c>(x, z)</c> coarse column.
    /// </summary>
    public static BlockGreedyMesher.MaterialLookup CreateMaterialLookup(
        WorldGenerationSnapshot snapshot,
        int level)
    {
      int stride = LodConstants.StrideForLevel(level);
      int half = stride / 2;

      var columnCache = new Dictionary<long, TerrainColumnSampler>();

      return coarseGlobalCoord =>
      {
        int realCenterX = coarseGlobalCoord.x * stride + half;
        int realCenterY = coarseGlobalCoord.y * stride + half;
        int realCenterZ = coarseGlobalCoord.z * stride + half;

        long columnKey = ((long)coarseGlobalCoord.x << 32) | (uint)coarseGlobalCoord.z;

        if (!columnCache.TryGetValue(columnKey, out TerrainColumnSampler column))
        {
          column = new TerrainColumnSampler();
          column.Prepare(snapshot, realCenterX, realCenterZ, LodConstants.BlockDensityScale);
          columnCache[columnKey] = column;
        }

        int surfaceFloor = Mathf.FloorToInt(column.SurfaceHeight);

        return ClassifyPreparedColumn(column, realCenterX, realCenterY, realCenterZ, surfaceFloor);
      };
    }

    // Height-based solidity (mirrors the block pipeline): a coarse cell is solid
    // when its centre is at or below the floored blended surface height. Material
    // comes from the terrain sample, falling back to a solid default so a below-
    // surface cell is never accidentally air. The column must already be Prepared
    // at (realCenterX, realCenterZ).
    private static ushort ClassifyPreparedColumn(
        TerrainColumnSampler column,
        int realCenterX,
        int realCenterY,
        int realCenterZ,
        int surfaceFloor)
    {
      if (realCenterY > surfaceFloor)
      {
        return 0;
      }

      TerrainSample sample = column.SampleAt(
          new Vector3Int(realCenterX, realCenterY, realCenterZ),
          LodConstants.BlockDensityScale
      );

      int material = sample.SolidMaterialId > 0
          ? sample.SolidMaterialId
          : LodConstants.SolidFallbackMaterial;

      return (ushort)Mathf.Clamp(material, 1, 65535);
    }
  }
}
