using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class BlockChunkBuildRequest
  {
    public Vector3Int ChunkCoord;
    public int GenerationId;
    public WorldGenerationSnapshot WorldSnapshot;
    public Dictionary<int, ushort> OverrideSnapshot;
    public BlockChunkData ChunkDataSnapshot;
    public float VoxelSize;
  }
}
