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

    // Direct face-neighbour view used by the fast mesher path. This avoids
    // cloning six boundary faces into full pooled chunk arrays for every build.
    public BlockChunkNeighborhood BlockNeighborhood;

    public Dictionary<int, ushort> OverrideSnapshot;
  }
}