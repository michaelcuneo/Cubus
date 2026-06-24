using UnityEngine;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks
{
  public sealed class BlockChunkData
  {
    private readonly Voxel[] voxels;
    private bool hasCachedSolidState;
    private bool cachedHasAnySolidVoxel;

    public Vector3Int ChunkCoord { get; }

    public BlockChunkData(Vector3Int chunkCoord)
    {
      ChunkCoord = chunkCoord;
      voxels = new Voxel[VoxelConstants.ChunkVolume];
    }

    // Wraps an externally-owned (e.g. pooled) voxel array instead of allocating.
    // The caller is responsible for the array's lifetime; used for throwaway
    // neighbour snapshots whose backing array is rented from VoxelArrayPool.
    public BlockChunkData(Vector3Int chunkCoord, Voxel[] backingVoxels)
    {
      ChunkCoord = chunkCoord;
      voxels = backingVoxels;
    }

    public BlockChunkData(
      Vector3Int chunkCoord,
      Voxel[] backingVoxels,
      bool knownHasAnySolidVoxel)
    {
      ChunkCoord = chunkCoord;
      voxels = backingVoxels;
      cachedHasAnySolidVoxel = knownHasAnySolidVoxel;
      hasCachedSolidState = true;
    }

    public Voxel GetVoxel(int x, int y, int z)
    {
      return voxels[VoxelMath.FlattenIndex(x, y, z)];
    }

    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
      voxels[VoxelMath.FlattenIndex(x, y, z)] = voxel;
      hasCachedSolidState = false;
    }

    public bool IsSolid(int x, int y, int z)
    {
      return GetVoxel(x, y, z).IsSolid;
    }

    public Vector3Int LocalToWorldVoxel(int x, int y, int z)
    {
      return VoxelMath.LocalToWorldVoxel(ChunkCoord, x, y, z);
    }

    public void SetKnownHasAnySolidVoxel(bool hasAnySolidVoxel)
    {
      cachedHasAnySolidVoxel = hasAnySolidVoxel;
      hasCachedSolidState = true;
    }

    public bool HasAnySolidVoxel()
    {
      if (hasCachedSolidState)
      {
        return cachedHasAnySolidVoxel;
      }

      for (int i = 0; i < voxels.Length; i++)
      {
        if (voxels[i].IsSolid)
        {
          cachedHasAnySolidVoxel = true;
          hasCachedSolidState = true;
          return true;
        }
      }

      cachedHasAnySolidVoxel = false;
      hasCachedSolidState = true;
      return false;
    }

    public Voxel[] GetRawVoxelArray()
    {
      return voxels;
    }
  }
}