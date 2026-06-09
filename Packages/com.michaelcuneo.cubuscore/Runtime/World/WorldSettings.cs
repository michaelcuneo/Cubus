using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  [Serializable]
  public sealed class WorldSettings
  {
    [Header("World")]
    public TerrainSystem TerrainSystem = TerrainSystem.SmoothDensity;

    [Header("View Distance")]
    [Min(1)]
    public int ViewDistanceInChunks = 4;

    [Min(0.01f)]
    public float VoxelSize = VoxelConstants.DefaultVoxelSize;

    [Header("Block")]
    public int BlockMinChunkY = -1;
    public int BlockMaxChunkY = 2;

    [Header("Smooth Density")]
    public int DensityMinChunkY = -1;
    public int DensityMaxChunkY = 2;

    [Range(1, 8)]
    public int DensityMeshStep = 1;

    [Tooltip("Multiplies world voxel coordinates before sampling the density field. >1.0 increases frequency (more detail), <1.0 stretches features.")]
    public float DensitySampleScale = 1.0f;

    [Header("Terrain")]
    public BiomeDefinition ActiveBiome;

    public TerrainGenerationProfile FallbackGenerationProfile = new();

    public TerrainGenerationProfile GetActiveGenerationProfile()
    {
      return ActiveBiome != null
          ? ActiveBiome.GenerationProfile
          : FallbackGenerationProfile;
    }

    public byte GetActiveBiomeId()
    {
      if (ActiveBiome == null)
      {
        return 1;
      }

      return (byte)Mathf.Clamp(ActiveBiome.BiomeId, 0, 255);
    }

    public void GetEffectiveBlockChunkYRange(out int minY, out int maxY)
    {
      minY = BlockMinChunkY;
      maxY = BlockMaxChunkY;

      if (minY > maxY)
      {
        (minY, maxY) = (maxY, minY);
      }
    }

    public void GetEffectiveDensityChunkYRange(out int minY, out int maxY)
    {
      minY = DensityMinChunkY;
      maxY = DensityMaxChunkY;

      if (minY > maxY)
      {
        (minY, maxY) = (maxY, minY);
      }
    }
  }
}