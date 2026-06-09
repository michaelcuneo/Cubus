using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public readonly struct WorldGenerationSnapshot
  {
    public readonly float VoxelSize;
    public readonly byte BiomeId;
    public readonly TerrainGenerationProfileSnapshot TerrainProfile;
    public readonly float DensitySampleScale;

    public WorldGenerationSnapshot(
        float voxelSize,
        byte biomeId,
        TerrainGenerationProfileSnapshot terrainProfile,
        float densitySampleScale)
    {
      VoxelSize = voxelSize;
      BiomeId = biomeId;
      TerrainProfile = terrainProfile;
      DensitySampleScale = densitySampleScale;
    }

    public static WorldGenerationSnapshot FromSettings(WorldSettings settings)
    {
      return new WorldGenerationSnapshot(
          settings.VoxelSize,
          settings.GetActiveBiomeId(),
          new TerrainGenerationProfileSnapshot(settings.GetActiveGenerationProfile()),
          Mathf.Max(0.001f, settings.DensitySampleScale)
      );
    }
  }
}