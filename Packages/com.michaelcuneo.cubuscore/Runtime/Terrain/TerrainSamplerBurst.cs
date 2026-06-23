using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
#if UNITY_BURST
  [Unity.Burst.BurstCompile]
#endif
  public static class TerrainSamplerBurst
  {
    public static void Sample(
        TerrainGenerationProfileSnapshot profile,
        double wx,
        double wy,
        double wz,
        out float density,
        out int solidMaterialId)
    {
      Sample(profile, wx, wy, wz, out density, out solidMaterialId, out _);
    }

    public static void Sample(
        TerrainGenerationProfileSnapshot profile,
        double wx,
        double wy,
        double wz,
        out float density,
        out int solidMaterialId,
        out float surfaceHeightOut)
    {
      double surfaceHeight = TerrainHeight.ComputeSurfaceHeight(profile, wx, wy);

      double d = surfaceHeight - wz;

      if (profile.CaveStrength > 0.0f)
      {
        // wx = world X, wy = world Z, wz = world Y (vertical).
        d -= TerrainCaves.CarveAmount(profile, wx, wy, wz, surfaceHeight);
      }

      density = (float)d;

      solidMaterialId = TerrainMaterialResolver.ResolveSolidMaterial(
          profile,
          wx,
          wy,
          wz,
          density,
          (float)surfaceHeight
      );

      surfaceHeightOut = (float)surfaceHeight;
    }
  }
}
