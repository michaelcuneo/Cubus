using System;
using UnityEngine;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks
{
  public sealed class BlockChunkData
  {
    private readonly Voxel[] voxels;

    public Vector3Int ChunkCoord { get; }

    internal Voxel[] RawVoxelsInternal => voxels;

    public BlockChunkData(Vector3Int chunkCoord)
    {
      ChunkCoord = chunkCoord;
      voxels = new Voxel[VoxelConstants.ChunkVolume];
    }

    public Voxel GetVoxel(int x, int y, int z)
    {
      return voxels[VoxelMath.FlattenIndex(x, y, z)];
    }

    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
      voxels[VoxelMath.FlattenIndex(x, y, z)] = voxel;
    }

    public bool IsSolid(int x, int y, int z)
    {
      return GetVoxel(x, y, z).IsSolid;
    }

    public Vector3Int LocalToWorldVoxel(int x, int y, int z)
    {
      return VoxelMath.LocalToWorldVoxel(ChunkCoord, x, y, z);
    }

    public bool HasAnySolidVoxel()
    {
      for (int i = 0; i < voxels.Length; i++)
      {
        if (voxels[i].IsSolid)
        {
          return true;
        }
      }

      return false;
    }

    public void CopyVoxelsTo(Voxel[] target)
    {
      if (target == null) throw new ArgumentNullException(nameof(target));
      if (target.Length < voxels.Length) throw new ArgumentException("Target voxel array is too small.", nameof(target));

      Array.Copy(voxels, target, voxels.Length);
    }

    public BlockChunkData Clone()
    {
      BlockChunkData clone = new(ChunkCoord);
      Array.Copy(voxels, clone.voxels, voxels.Length);
      return clone;
    }

    public Voxel[] GetRawVoxelArray()
    {
      return voxels;
    }
  }
}