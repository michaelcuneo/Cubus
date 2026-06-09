using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class DensityChunkBuildRequest
  {
    public Vector3Int ChunkCoord;
    public int GenerationId;
    public WorldGenerationSnapshot WorldSnapshot;
    public int CellStep = 1;
    public bool FlipWinding = true;
    public Dictionary<int, DensityVoxelOverride> OverrideSnapshot;

    /// <summary>
    /// When set, the build skips expensive terrain noise re-generation and
    /// uses this pre-edited chunk data instead. Must be a snapshot copy —
    /// never pass a live chunk reference to a background thread.
    /// </summary>
    public DensityChunkData ChunkDataSnapshot;
  }

  public readonly struct DensityVoxelOverride
  {
    public readonly float Density;
    public readonly ushort MaterialId;

    public DensityVoxelOverride(float density, ushort materialId)
    {
      Density = density;
      MaterialId = materialId;
    }
  }
}