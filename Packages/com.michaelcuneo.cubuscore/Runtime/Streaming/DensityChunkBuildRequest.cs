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
    public DensityChunkData ChunkDataSnapshot;
    public Dictionary<Vector3Int, DensityChunkData> ChunkDataSnapshots;

    public int GeneratedMinChunkX;
    public int GeneratedMaxChunkX;
    public int GeneratedMinChunkY;
    public int GeneratedMaxChunkY;
    public int GeneratedMinChunkZ;
    public int GeneratedMaxChunkZ;

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