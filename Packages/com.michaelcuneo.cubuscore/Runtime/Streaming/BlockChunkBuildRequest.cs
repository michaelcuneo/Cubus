using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class BlockChunkBuildRequest
  {
    public Vector3Int ChunkCoord;
    public int GenerationId;
    public WorldGenerationSnapshot WorldSnapshot;
    public float VoxelSize;
    public BlockChunkData ChunkDataSnapshot;
    public Dictionary<Vector3Int, BlockChunkData> NeighborChunkSnapshots;

    public Dictionary<int, ushort> OverrideSnapshot;
  }
}