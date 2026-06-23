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
    // True when the background build threw instead of producing a result. The
    // chunk is NOT genuinely empty and must be retried, not marked known-empty.
    public bool Failed;
    public int GenerationId;
  }
}