using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class WorldData
  {
    public readonly Dictionary<Vector3Int, BlockChunkData> BlockChunks = new();
    public readonly Dictionary<Vector3Int, DensityChunkData> DensityChunks = new();
    public readonly Dictionary<Vector3Int, Dictionary<int, ushort>> BlockVoxelOverridesByChunk = new();
    public readonly Dictionary<Vector3Int, Dictionary<int, DensityVoxelOverride>> DensityVoxelOverridesByChunk = new();

    public void ClearGeneratedChunks()
    {
      BlockChunks.Clear();
      DensityChunks.Clear();
    }

    public void ClearOverrides()
    {
      BlockVoxelOverridesByChunk.Clear();
      DensityVoxelOverridesByChunk.Clear();
    }

    public void ClearAll()
    {
      ClearGeneratedChunks();
      ClearOverrides();
    }
  }
}