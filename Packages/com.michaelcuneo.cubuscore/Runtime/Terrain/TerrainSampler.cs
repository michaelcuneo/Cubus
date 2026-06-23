using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public sealed class TerrainSampler
  {
    private readonly TerrainGenerationProfileSnapshot profileSnapshot;
    private readonly byte biomeId;

    public TerrainSampler(TerrainGenerationProfile profile, byte biomeId = 1)
    {
      profileSnapshot = new TerrainGenerationProfileSnapshot(profile);
      this.biomeId = biomeId;
    }

    public TerrainSampler(TerrainGenerationProfileSnapshot profileSnapshot, byte biomeId = 1)
    {
      this.profileSnapshot = profileSnapshot;
      this.biomeId = biomeId;
    }

    public TerrainSample Sample(Vector3 worldVoxelPosition)
    {
      return Sample(profileSnapshot, biomeId, worldVoxelPosition);
    }

    public static TerrainSample Sample(
        TerrainGenerationProfileSnapshot profileSnapshot,
        byte biomeId,
        Vector3 worldVoxelPosition)
    {
      // Surface height only depends on world X/Z, so it can be computed once
      // per column and reused for every voxel in that column.
      double surfaceHeight = ComputeSurfaceHeight(
          profileSnapshot,
          worldVoxelPosition.x,
          worldVoxelPosition.z
      );

      return SampleWithSurfaceHeight(
          profileSnapshot,
          biomeId,
          worldVoxelPosition,
          surfaceHeight
      );
    }

    // Column-invariant pass: everything here is a pure function of world X/Z.
    // Hoist this out of per-voxel loops and reuse the result for the column.
    // Delegates to the shared TerrainHeight so all sampler paths stay identical.
    public static double ComputeSurfaceHeight(
        TerrainGenerationProfileSnapshot profileSnapshot,
        double wx,
        double wy)
    {
      return TerrainHeight.ComputeSurfaceHeight(profileSnapshot, wx, wy);
    }

    // Per-voxel pass: given a precomputed column surface height, this resolves
    // density, caves and material for a single voxel.
    public static TerrainSample SampleWithSurfaceHeight(
        TerrainGenerationProfileSnapshot profileSnapshot,
        byte biomeId,
        Vector3 worldVoxelPosition,
        double surfaceHeight)
    {
      TerrainSample sample = new();

      double wx = worldVoxelPosition.x;
      double wy = worldVoxelPosition.z;
      double wz = worldVoxelPosition.y;

      double density = surfaceHeight - wz;
      double caveAmount = 0.0;

      if (profileSnapshot.CaveStrength > 0.0f)
      {
        // wx = world X, wy = world Z, wz = world Y (vertical).
        caveAmount = TerrainCaves.CarveAmount(profileSnapshot, wx, wy, wz, surfaceHeight);
        density -= caveAmount;
      }

      sample.SurfaceHeight = (float)surfaceHeight;
      sample.CaveAmount = (float)caveAmount;
      sample.Density = (float)density;
      sample.SolidMaterialId = GetSolidMaterial(profileSnapshot, worldVoxelPosition, sample.Density, sample.SurfaceHeight);
      sample.LiquidMaterialId = 0;
      sample.BiomeId = biomeId;
      sample.IsLiquid = false;

      return sample;
    }

    private static int GetSolidMaterial(
      TerrainGenerationProfileSnapshot profileSnapshot,
      Vector3 worldVoxelPosition,
      float density,
      float surfaceHeight)
    {
      return TerrainMaterialResolver.ResolveSolidMaterial(
          profileSnapshot,
          worldVoxelPosition.x,
          worldVoxelPosition.z,
          worldVoxelPosition.y,
          density,
          surfaceHeight
      );
    }
  }
}
