using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core
{
  public static class VoxelMath
  {
    public static int FloorDiv(int value, int divisor)
    {
      if (divisor == 0)
      {
        throw new System.DivideByZeroException();
      }

      int quotient = value / divisor;
      int remainder = value % divisor;

      if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
      {
        quotient--;
      }

      return quotient;
    }

    public static int PositiveMod(int value, int divisor)
    {
      if (divisor <= 0)
      {
        throw new System.ArgumentOutOfRangeException(
            nameof(divisor),
            "Divisor must be positive."
        );
      }

      int result = value % divisor;
      return result < 0 ? result + divisor : result;
    }

    public static int FlattenIndex(int x, int y, int z)
    {
      const int size = VoxelConstants.ChunkSize;

      if ((uint)x >= size || (uint)y >= size || (uint)z >= size)
      {
        throw new System.ArgumentOutOfRangeException(
            $"Voxel coordinate out of bounds: ({x}, {y}, {z})"
        );
      }

      return x + size * (y + size * z);
    }

    public static Vector3Int WorldVoxelToChunkCoord(Vector3Int worldVoxelCoord)
    {
      const int size = VoxelConstants.ChunkSize;

      return new Vector3Int(
          FloorDiv(worldVoxelCoord.x, size),
          FloorDiv(worldVoxelCoord.y, size),
          FloorDiv(worldVoxelCoord.z, size)
      );
    }

    public static Vector3Int WorldVoxelToLocalCoord(Vector3Int worldVoxelCoord)
    {
      const int size = VoxelConstants.ChunkSize;

      return new Vector3Int(
          PositiveMod(worldVoxelCoord.x, size),
          PositiveMod(worldVoxelCoord.y, size),
          PositiveMod(worldVoxelCoord.z, size)
      );
    }

    public static Vector3Int LocalToWorldVoxel(Vector3Int chunkCoord, int x, int y, int z)
    {
      const int size = VoxelConstants.ChunkSize;

      return new Vector3Int(
          chunkCoord.x * size + x,
          chunkCoord.y * size + y,
          chunkCoord.z * size + z
      );
    }

    public static Vector3 LocalVoxelToPosition(int x, int y, int z, float voxelSize)
    {
      return new Vector3(
          x * voxelSize,
          y * voxelSize,
          z * voxelSize
      );
    }

    public static Vector3 ChunkCoordToWorldPosition(Vector3Int chunkCoord, float voxelSize)
    {
      float chunkWorldSize = VoxelConstants.ChunkSize * voxelSize;

      return new Vector3(
          chunkCoord.x * chunkWorldSize,
          chunkCoord.y * chunkWorldSize,
          chunkCoord.z * chunkWorldSize
      );
    }
  }
}