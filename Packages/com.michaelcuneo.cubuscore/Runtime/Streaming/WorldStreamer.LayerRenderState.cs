using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private readonly HashSet<Vector3Int> knownEmptyDensityChunks = new();

    private bool HasRenderedBlockChunk(Vector3Int chunkCoord)
    {
      return worldRenderer != null && worldRenderer.HasBlockChunkView(chunkCoord);
    }

    private bool HasRenderedDensityChunk(Vector3Int chunkCoord)
    {
      return worldRenderer != null && worldRenderer.HasDensityChunkView(chunkCoord);
    }
  }
}

