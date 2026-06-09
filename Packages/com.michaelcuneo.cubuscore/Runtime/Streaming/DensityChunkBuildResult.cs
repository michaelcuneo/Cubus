using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class DensityChunkBuildResult
  {
    public Vector3Int ChunkCoord;
    public DensityChunkData ChunkData;
    public MeshData MeshData;
    public bool IsEmpty;
    public bool HasSurfaceCrossing;
    public int GenerationId;
  }
}