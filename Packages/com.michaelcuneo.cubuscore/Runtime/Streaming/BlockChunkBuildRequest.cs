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

    // Face-neighbour chunk coords that are in-bounds but not yet loaded at build
    // time. The mesher treats these as solid (hides the shared boundary face) so
    // no transient one-sided wall is drawn at the load frontier; the chunk is
    // re-meshed against real voxels once the neighbour loads.
    public HashSet<Vector3Int> SolidFallbackNeighborChunks;

    public Dictionary<int, ushort> OverrideSnapshot;
  }
}