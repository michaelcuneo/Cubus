using System;
using UnityEngine;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks
{
  public sealed class DensityChunkData
  {
    private readonly DensityVoxel[] voxels;
    private bool surfaceCrossingKnown;
    private bool cachedSurfaceCrossing;

    public Vector3Int ChunkCoord { get; }

    public DensityChunkData(Vector3Int chunkCoord)
        : this(chunkCoord, true)
    {
    }

    internal DensityChunkData(Vector3Int chunkCoord, bool initializeEmpty)
    {
      ChunkCoord = chunkCoord;
      voxels = new DensityVoxel[VoxelConstants.ChunkVolume];

      if (!initializeEmpty)
      {
        return;
      }

      for (int i = 0; i < voxels.Length; i++)
      {
        voxels[i] = DensityVoxel.Empty;
      }

      MarkSurfaceCrossingKnown(false);
    }

    private DensityChunkData(Vector3Int chunkCoord, DensityVoxel[] voxelData)
    {
      ChunkCoord = chunkCoord;
      voxels = voxelData ?? new DensityVoxel[VoxelConstants.ChunkVolume];
    }

    public DensityVoxel GetVoxel(int x, int y, int z)
    {
      return voxels[GetIndex(x, y, z)];
    }

    public ref DensityVoxel GetVoxelMutable(int x, int y, int z)
    {
      InvalidateSurfaceCrossingCache();
      return ref voxels[GetIndex(x, y, z)];
    }

    public void SetVoxel(int x, int y, int z, DensityVoxel voxel)
    {
      voxels[GetIndex(x, y, z)] = voxel;
      InvalidateSurfaceCrossingCache();
    }

    public int GetIndex(int x, int y, int z)
    {
      return VoxelMath.FlattenIndex(x, y, z);
    }

    public bool IsInBounds(int x, int y, int z)
    {
      const int size = VoxelConstants.ChunkSize;

      return x >= 0 && x < size &&
             y >= 0 && y < size &&
             z >= 0 && z < size;
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

    public bool HasSurfaceCrossing()
    {
      if (surfaceCrossingKnown)
      {
        return cachedSurfaceCrossing;
      }

      bool hasSolid = false;
      bool hasAir = false;

      for (int i = 0; i < voxels.Length; i++)
      {
        if (voxels[i].Density > 0.0f)
        {
          hasSolid = true;
        }
        else
        {
          hasAir = true;
        }

        if (hasSolid && hasAir)
        {
          MarkSurfaceCrossingKnown(true);
          return true;
        }
      }

      MarkSurfaceCrossingKnown(false);
      return false;
    }

    internal void MarkSurfaceCrossingKnown(bool hasSurfaceCrossing)
    {
      cachedSurfaceCrossing = hasSurfaceCrossing;
      surfaceCrossingKnown = true;
    }

    internal void InvalidateSurfaceCrossingCache()
    {
      surfaceCrossingKnown = false;
    }

    public void FillFromDensityFunction(
        Func<Vector3, float> densityFunction,
        ushort solidMaterialId)
    {
      const int size = VoxelConstants.ChunkSize;
      bool hasSolid = false;
      bool hasAir = false;

      for (int z = 0; z < size; z++)
      {
        for (int y = 0; y < size; y++)
        {
          for (int x = 0; x < size; x++)
          {
            Vector3Int worldVoxel = LocalToWorldVoxel(x, y, z);
            Vector3 worldVoxelPosition = new(
                worldVoxel.x,
                worldVoxel.y,
                worldVoxel.z
            );

            float density = densityFunction(worldVoxelPosition);
            ushort materialId = density > 0.0f ? solidMaterialId : (ushort)0;
            if (density > 0.0f) hasSolid = true;
            else hasAir = true;

            voxels[GetIndex(x, y, z)] = new DensityVoxel(density, materialId);
          }
        }
      }

      MarkSurfaceCrossingKnown(hasSolid && hasAir);
    }

    public DensityVoxel[] GetRawVoxelArray()
    {
      return voxels;
    }

    public DensityChunkData Clone()
    {
      DensityVoxel[] copiedVoxels = new DensityVoxel[voxels.Length];
      System.Array.Copy(voxels, copiedVoxels, voxels.Length);
      DensityChunkData clone = new(ChunkCoord, copiedVoxels);
      if (surfaceCrossingKnown)
      {
        clone.MarkSurfaceCrossingKnown(cachedSurfaceCrossing);
      }
      return clone;
    }
  }
}
