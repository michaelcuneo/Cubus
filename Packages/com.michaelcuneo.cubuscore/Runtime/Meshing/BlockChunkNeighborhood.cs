using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public sealed class BlockChunkNeighborhood
  {
    // Material used for an in-bounds neighbour voxel whose chunk has not loaded
    // yet. Any non-zero material hides the shared boundary face; it is never
    // rendered because the hidden face emits no geometry.
    private const ushort SolidBoundaryFallbackMaterial = 1;

    private readonly BlockChunkData center;
    private readonly BlockChunkData xPositive;
    private readonly BlockChunkData xNegative;
    private readonly BlockChunkData yPositive;
    private readonly BlockChunkData yNegative;
    private readonly BlockChunkData zPositive;
    private readonly BlockChunkData zNegative;

    private readonly bool xPositiveSolidFallback;
    private readonly bool xNegativeSolidFallback;
    private readonly bool yPositiveSolidFallback;
    private readonly bool yNegativeSolidFallback;
    private readonly bool zPositiveSolidFallback;
    private readonly bool zNegativeSolidFallback;

    public BlockChunkNeighborhood(
        BlockChunkData center,
        BlockChunkData xPositive,
        BlockChunkData xNegative,
        BlockChunkData yPositive,
        BlockChunkData yNegative,
        BlockChunkData zPositive,
        BlockChunkData zNegative,
        bool xPositiveSolidFallback = false,
        bool xNegativeSolidFallback = false,
        bool yPositiveSolidFallback = false,
        bool yNegativeSolidFallback = false,
        bool zPositiveSolidFallback = false,
        bool zNegativeSolidFallback = false)
    {
      this.center = center;
      this.xPositive = xPositive;
      this.xNegative = xNegative;
      this.yPositive = yPositive;
      this.yNegative = yNegative;
      this.zPositive = zPositive;
      this.zNegative = zNegative;
      this.xPositiveSolidFallback = xPositiveSolidFallback;
      this.xNegativeSolidFallback = xNegativeSolidFallback;
      this.yPositiveSolidFallback = yPositiveSolidFallback;
      this.yNegativeSolidFallback = yNegativeSolidFallback;
      this.zPositiveSolidFallback = zPositiveSolidFallback;
      this.zNegativeSolidFallback = zNegativeSolidFallback;
    }

    public ushort GetMaterial(int localX, int localY, int localZ)
    {
      const int size = VoxelConstants.ChunkSize;

      if (
          localX >= 0 && localX < size &&
          localY >= 0 && localY < size &&
          localZ >= 0 && localZ < size)
      {
        return center.GetVoxel(localX, localY, localZ).MaterialId;
      }

      if (localX < 0)
      {
        return xNegative != null
            ? xNegative.GetVoxel(size - 1, localY, localZ).MaterialId
            : xNegativeSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      if (localX >= size)
      {
        return xPositive != null
            ? xPositive.GetVoxel(0, localY, localZ).MaterialId
            : xPositiveSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      if (localY < 0)
      {
        return yNegative != null
            ? yNegative.GetVoxel(localX, size - 1, localZ).MaterialId
            : yNegativeSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      if (localY >= size)
      {
        return yPositive != null
            ? yPositive.GetVoxel(localX, 0, localZ).MaterialId
            : yPositiveSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      if (localZ < 0)
      {
        return zNegative != null
            ? zNegative.GetVoxel(localX, localY, size - 1).MaterialId
            : zNegativeSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      if (localZ >= size)
      {
        return zPositive != null
            ? zPositive.GetVoxel(localX, localY, 0).MaterialId
            : zPositiveSolidFallback ? SolidBoundaryFallbackMaterial : (ushort)0;
      }

      return 0;
    }
  }
}