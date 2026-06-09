using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  [Serializable]
  public sealed class WorldSaveData
  {
    public int SaveVersion = 2;
    public TerrainSystem TerrainSystem = TerrainSystem.SmoothDensity;
    public int ChunkSize;
    public float VoxelSize;
    public int ViewDistanceInChunks;
    public int BlockMinChunkY;
    public int BlockMaxChunkY;
    public int DensityMinChunkY;
    public int DensityMaxChunkY;
    public List<SavedBlockChunk> SavedBlockChunks = new();
    public List<SavedDensityChunk> SavedDensityChunks = new();
  }

  [Serializable]
  public sealed class SavedDensityChunk
  {
    public Vector3Int ChunkCoord;
    public List<SavedDensityVoxel> Voxels = new();
  }

  [Serializable]
  public struct SavedDensityVoxel
  {
    public int VoxelIndex;
    public float Density;
    public int MaterialId;

    public SavedDensityVoxel(int voxelIndex, float density, int materialId)
    {
      VoxelIndex = voxelIndex;
      Density = density;
      MaterialId = materialId;
    }
  }
}