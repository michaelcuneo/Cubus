using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public sealed class TerrainSampler
  {
    private readonly TerrainGenerationProfile profile;
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
      TerrainSample sample = new();

      double wx = worldVoxelPosition.x;
      double wy = worldVoxelPosition.z;
      double wz = worldVoxelPosition.y;

      double edgeT = 0.0;
      double edgeFalloff = 1.0;

      if (profileSnapshot.UseWorldEdgeFalloff)
      {
        double distance2D = System.Math.Sqrt(wx * wx + wy * wy);
        double safeWorldEdgeRadius = System.Math.Max(1.0, profileSnapshot.WorldEdgeRadius);

        edgeT = Clamp01(distance2D / safeWorldEdgeRadius);

        double smoothEdge = SmoothStep(edgeT);
        edgeFalloff = 1.0 - smoothEdge;
      }

      double baseHeight = profileSnapshot.BaseHeight;
      double mainHeight = profileSnapshot.HeightScale * edgeFalloff;

      double continental =
          System.Math.Sin(wx * 0.0065 + wy * 0.0027) * 18.0 +
          System.Math.Cos(wy * 0.0058 - wx * 0.0021) * 16.0 +
          System.Math.Sin((wx + wy) * 0.0042) * 12.0;

      double hills =
          System.Math.Sin(wx * 0.018 + wy * 0.011) * 10.0 +
          System.Math.Cos(wy * 0.021 - wx * 0.009) * 9.0 +
          System.Math.Sin((wx - wy) * 0.016) * 7.0;

      double detail =
          System.Math.Sin(wx * 0.055 + wy * 0.037) * 3.5 +
          System.Math.Cos(wy * 0.061 - wx * 0.024) * 3.0;

      double ridgeBase = System.Math.Abs(
          System.Math.Sin(wx * 0.013 + wy * 0.019) +
          System.Math.Cos(wx * 0.017 - wy * 0.011)
      );

      double ridges =
          System.Math.Pow(Clamp(ridgeBase * 0.5, 0.0, 1.0), 2.0) * 24.0;

      double edgeDrop = profileSnapshot.UseWorldEdgeFalloff
          ? System.Math.Pow(edgeT, 3.0) * 140.0
          : 0.0;

      double valleyMask = Clamp(
          System.Math.Sin(wx * 0.007) * 0.5 +
          System.Math.Cos(wy * 0.006) * 0.5,
          -1.0,
          1.0
      );

      double valleyCut = System.Math.Max(0.0, valleyMask) * 18.0 * edgeFalloff;

      double surfaceHeight =
          baseHeight +
          mainHeight +
          continental * edgeFalloff +
          hills * edgeFalloff +
          detail * edgeFalloff +
          ridges * edgeFalloff -
          edgeDrop -
          valleyCut;

      if (profileSnapshot.UseWorldEdgeFalloff && edgeT > 0.78)
      {
        double outerT = Clamp01((edgeT - 0.78) / 0.22);
        double outerSmooth = SmoothStep(outerT);

        surfaceHeight = Lerp(
            surfaceHeight,
            profileSnapshot.WorldEdgeTargetHeight,
            outerSmooth
        );
      }

      double density = surfaceHeight - wz;
      double caveAmount = 0.0;

      bool canCarveCaves =
          profileSnapshot.CaveStrength > 0.0f &&
          wz < surfaceHeight - profileSnapshot.CaveStartDepth;

      if (canCarveCaves)
      {
        double frequency = System.Math.Max(0.0001, profileSnapshot.CaveFrequency);

        double caveNoiseA =
            System.Math.Sin(wx * frequency + wy * frequency * 0.37 + wz * frequency * 1.71);

        double caveNoiseB =
            System.Math.Cos(wy * frequency * 1.23 - wz * frequency * 0.89 + wx * frequency * 0.53);

        double caveNoiseC =
            System.Math.Sin((wx + wy - wz) * frequency * 0.61);

        double combinedCaveNoise =
            (caveNoiseA + caveNoiseB + caveNoiseC) / 3.0;

        caveAmount =
            System.Math.Max(0.0, combinedCaveNoise) *
            profileSnapshot.CaveStrength;

        density -= caveAmount;
      }

      sample.SurfaceHeight = (float)surfaceHeight;
      sample.CaveAmount = (float)caveAmount;
      sample.Density = (float)density;
      sample.SolidMaterialId = GetSolidMaterial(worldVoxelPosition, sample.Density, sample.SurfaceHeight);
      sample.LiquidMaterialId = 0;
      sample.BiomeId = biomeId;
      sample.IsLiquid = false;

      return sample;
    }

    private int GetSolidMaterial(Vector3 worldVoxelPosition, float density, float surfaceHeight)
    {
      if (density <= 0.0f)
      {
        return 0;
      }

      float depthBelowSurface = surfaceHeight - worldVoxelPosition.y;

      if (depthBelowSurface <= 3.0f)
      {
        return ClampMaterialId(profileSnapshot.SurfaceMaterialId);
      }

      if (depthBelowSurface <= 18.0f)
      {
        return ClampMaterialId(profileSnapshot.SubsurfaceMaterialId);
      }

      return ClampMaterialId(profileSnapshot.StoneMaterialId);
    }

    private static int ClampMaterialId(int value)
    {
      return Mathf.Clamp(value, 1, 65535);
    }

    private static double Clamp01(double value)
    {
      return Clamp(value, 0.0, 1.0);
    }

    private static double Clamp(double value, double min, double max)
    {
      if (value < min)
      {
        return min;
      }

      if (value > max)
      {
        return max;
      }

      return value;
    }

    private static double SmoothStep(double t)
    {
      return t * t * (3.0 - 2.0 * t);
    }

    private static double Lerp(double a, double b, double t)
    {
      return a + (b - a) * t;
    }
  }
}