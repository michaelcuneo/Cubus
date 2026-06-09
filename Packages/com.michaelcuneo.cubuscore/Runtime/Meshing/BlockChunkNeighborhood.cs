using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public sealed class BlockChunkNeighborhood
  {
    private readonly BlockChunkData center;
    private readonly BlockChunkData xPositive;
    private readonly BlockChunkData xNegative;
    private readonly BlockChunkData yPositive;
    private readonly BlockChunkData yNegative;
    private readonly BlockChunkData zPositive;
    private readonly BlockChunkData zNegative;

    public BlockChunkNeighborhood(
        BlockChunkData center,
        BlockChunkData xPositive,
        BlockChunkData xNegative,
        BlockChunkData yPositive,
        BlockChunkData yNegative,
        BlockChunkData zPositive,
        BlockChunkData zNegative)
    {
      this.center = center;
      this.xPositive = xPositive;
      this.xNegative = xNegative;
      this.yPositive = yPositive;
      this.yNegative = yNegative;
      this.zPositive = zPositive;
      this.zNegative = zNegative;
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
            : (ushort)0;
      }

      if (localX >= size)
      {
        return xPositive != null
            ? xPositive.GetVoxel(0, localY, localZ).MaterialId
            : (ushort)0;
      }

      if (localY < 0)
      {
        return yNegative != null
            ? yNegative.GetVoxel(localX, size - 1, localZ).MaterialId
            : (ushort)0;
      }

      if (localY >= size)
      {
        return yPositive != null
            ? yPositive.GetVoxel(localX, 0, localZ).MaterialId
            : (ushort)0;
      }

      if (localZ < 0)
      {
        return zNegative != null
            ? zNegative.GetVoxel(localX, localY, size - 1).MaterialId
            : (ushort)0;
      }

      if (localZ >= size)
      {
        return zPositive != null
            ? zPositive.GetVoxel(localX, localY, 0).MaterialId
            : (ushort)0;
      }

      return 0;
    }
  }
}