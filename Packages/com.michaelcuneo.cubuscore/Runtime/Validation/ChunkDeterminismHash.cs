using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public static class ChunkDeterminismHash
  {
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Hash(BlockChunkData chunkData)
    {
      if (chunkData == null)
      {
        throw new ArgumentNullException(nameof(chunkData));
      }

      ulong hash = OffsetBasis;

      AddInt(ref hash, chunkData.ChunkCoord.x);
      AddInt(ref hash, chunkData.ChunkCoord.y);
      AddInt(ref hash, chunkData.ChunkCoord.z);

      Voxel[] voxels = chunkData.GetRawVoxelArray();
      for (int i = 0; i < voxels.Length; i++)
      {
        AddUShort(ref hash, voxels[i].MaterialId);
      }

      return hash;
    }

    public static ulong Hash(DensityChunkData chunkData)
    {
      if (chunkData == null)
      {
        throw new ArgumentNullException(nameof(chunkData));
      }

      ulong hash = OffsetBasis;

      AddInt(ref hash, chunkData.ChunkCoord.x);
      AddInt(ref hash, chunkData.ChunkCoord.y);
      AddInt(ref hash, chunkData.ChunkCoord.z);

      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();
      for (int i = 0; i < voxels.Length; i++)
      {
        AddFloat(ref hash, voxels[i].Density);
        AddUShort(ref hash, voxels[i].MaterialId);
      }

      return hash;
    }

    public static bool AreEqual(BlockChunkData a, BlockChunkData b, out string error)
    {
      if (a == null || b == null)
      {
        error = $"Block chunk null mismatch. A={(a == null ? "null" : "set")}, B={(b == null ? "null" : "set")}";
        return false;
      }

      if (a.ChunkCoord != b.ChunkCoord)
      {
        error = $"Block chunk coordinate mismatch. A={a.ChunkCoord}, B={b.ChunkCoord}";
        return false;
      }

      Voxel[] av = a.GetRawVoxelArray();
      Voxel[] bv = b.GetRawVoxelArray();

      if (av.Length != bv.Length)
      {
        error = $"Block voxel array length mismatch. A={av.Length}, B={bv.Length}";
        return false;
      }

      for (int i = 0; i < av.Length; i++)
      {
        if (av[i].MaterialId != bv[i].MaterialId)
        {
          error = $"Block voxel mismatch at {a.ChunkCoord}, flat index {i}. Material A={av[i].MaterialId}, B={bv[i].MaterialId}";
          return false;
        }
      }

      error = null;
      return true;
    }

    public static bool AreEqual(DensityChunkData a, DensityChunkData b, out string error)
    {
      if (a == null || b == null)
      {
        error = $"Density chunk null mismatch. A={(a == null ? "null" : "set")}, B={(b == null ? "null" : "set")}";
        return false;
      }

      if (a.ChunkCoord != b.ChunkCoord)
      {
        error = $"Density chunk coordinate mismatch. A={a.ChunkCoord}, B={b.ChunkCoord}";
        return false;
      }

      DensityVoxel[] av = a.GetRawVoxelArray();
      DensityVoxel[] bv = b.GetRawVoxelArray();

      if (av.Length != bv.Length)
      {
        error = $"Density voxel array length mismatch. A={av.Length}, B={bv.Length}";
        return false;
      }

      for (int i = 0; i < av.Length; i++)
      {
        if (!NearlyEqual(av[i].Density, bv[i].Density) || av[i].MaterialId != bv[i].MaterialId)
        {
          error =
            $"Density voxel mismatch at {a.ChunkCoord}, flat index {i}. " +
            $"Density A={av[i].Density}, B={bv[i].Density}, " +
            $"Material A={av[i].MaterialId}, B={bv[i].MaterialId}";
          return false;
        }
      }

      error = null;
      return true;
    }

    private static bool NearlyEqual(float a, float b)
    {
      return Math.Abs(a - b) <= 0.000001f;
    }

    private static void AddFloat(ref ulong hash, float value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      for (int i = 0; i < bytes.Length; i++)
      {
        AddByte(ref hash, bytes[i]);
      }
    }

    private static void AddInt(ref ulong hash, int value)
    {
      unchecked
      {
        AddByte(ref hash, (byte)value);
        AddByte(ref hash, (byte)(value >> 8));
        AddByte(ref hash, (byte)(value >> 16));
        AddByte(ref hash, (byte)(value >> 24));
      }
    }

    private static void AddUShort(ref ulong hash, ushort value)
    {
      unchecked
      {
        AddByte(ref hash, (byte)value);
        AddByte(ref hash, (byte)(value >> 8));
      }
    }

    private static void AddByte(ref ulong hash, byte value)
    {
      unchecked
      {
        hash ^= value;
        hash *= Prime;
      }
    }
  }
}
