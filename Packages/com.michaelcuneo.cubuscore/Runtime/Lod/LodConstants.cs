using UnityEngine;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Shared constants and coordinate math for the Distant-Horizon LOD system.
  ///
  /// A level-<c>L</c> LOD tile is just an ordinary <c>32^3</c> voxel chunk whose
  /// every voxel is a <c>stride^3</c> block of the real world, where
  /// <c>stride = 2^L</c>. It is meshed at <c>voxelSize_L = stride * VoxelSize</c>
  /// and positioned exactly like a normal chunk, so the existing greedy mesher,
  /// chunk view and biome-atlas shader all work unchanged - distant terrain just
  /// becomes coarser (and exponentially cheaper) the further out it is.
  /// </summary>
  public static class LodConstants
  {
    // Level 0 is full detail (stride 1) and is produced by the normal block
    // pipeline, so the LOD system itself only ever builds levels >= 1.
    public const int MinLodLevel = 1;

    // Hard ceiling on LOD levels. stride = 2^MaxLodLevel = 256 means a single
    // tile spans 256 base chunks per axis, far past any practical view.
    public const int MaxLodLevel = 8;

    // Block terrain classification ignores DensitySampleScale (biome size is
    // sampled in unscaled world XZ) and samples density at scale 1.0, exactly
    // like BlockChunkBuildQueue.ResolveMaterialForMeshing. LOD must match.
    public const float BlockDensityScale = 1.0f;

    // Default solid material used when a coarse cell is below the surface but the
    // terrain sample yields no material id (mirrors the block path's "solid
    // default" so a downsampled column is never accidentally air).
    public const ushort SolidFallbackMaterial = 1;

    /// <summary>Real-world voxel stride for an LOD level: <c>2^level</c>.</summary>
    public static int StrideForLevel(int level)
    {
      return 1 << Mathf.Clamp(level, 0, MaxLodLevel);
    }

    /// <summary>Mesh voxel size for an LOD level: <c>stride * baseVoxelSize</c>.</summary>
    public static float VoxelSizeForLevel(int level, float baseVoxelSize)
    {
      return StrideForLevel(level) * baseVoxelSize;
    }

    /// <summary>
    /// World-space edge length of one LOD tile at <paramref name="level"/>:
    /// <c>ChunkSize * stride * baseVoxelSize</c>.
    /// </summary>
    public static float TileWorldSize(int level, float baseVoxelSize)
    {
      return VoxelConstants.ChunkSize * VoxelSizeForLevel(level, baseVoxelSize);
    }
  }
}
