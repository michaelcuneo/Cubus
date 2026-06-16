using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  [Serializable]
  public sealed class BlockWorldSaveData
  {
    public int SaveVersion = WorldSaveFormat.CurrentBlockWorldSaveVersion;
    public int ChunkSize;
    public float VoxelSize;
    public List<SavedBlockChunk> SavedBlockChunks = new();
  }

  [Serializable]
  public sealed class SavedBlockChunk
  {
    public Vector3Int ChunkCoord;
    public List<SavedBlockVoxel> Voxels = new();
  }

  [Serializable]
  public struct SavedBlockVoxel
  {
    public int VoxelIndex;
    public int MaterialId;

    public SavedBlockVoxel(int voxelIndex, int materialId)
    {
      VoxelIndex = voxelIndex;
      MaterialId = materialId;
    }
  }
}
