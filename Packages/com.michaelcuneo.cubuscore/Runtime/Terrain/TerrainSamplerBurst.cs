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
      double edgeT = 0.0;
      double edgeFalloff = 1.0;

      if (profile.UseWorldEdgeFalloff)
      {
        double distance2D = System.Math.Sqrt(wx * wx + wy * wy);
        double safeWorldEdgeRadius = System.Math.Max(1.0, profile.WorldEdgeRadius);
        edgeT = Clamp01(distance2D / safeWorldEdgeRadius);
        double smoothEdge = SmoothStep(edgeT);
        edgeFalloff = 1.0 - smoothEdge;
      }

      double baseHeight = profile.BaseHeight;
      double mainHeight = profile.HeightScale * edgeFalloff;

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

      double ridges = System.Math.Pow(Clamp(ridgeBase * 0.5, 0.0, 1.0), 2.0) * 24.0;

      double edgeDrop = profile.UseWorldEdgeFalloff
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

      if (profile.UseWorldEdgeFalloff && edgeT > 0.78)
      {
        double outerT = Clamp01((edgeT - 0.78) / 0.22);
        double outerSmooth = SmoothStep(outerT);
        surfaceHeight = Lerp(surfaceHeight, profile.WorldEdgeTargetHeight, outerSmooth);
      }

      double d = surfaceHeight - wz;

      if (profile.CaveStrength > 0.0f && wz < surfaceHeight - profile.CaveStartDepth)
      {
        double frequency = System.Math.Max(0.0001, profile.CaveFrequency);

        double caveA = System.Math.Sin(wx * frequency + wy * frequency * 0.37 + wz * frequency * 1.71);
        double caveB = System.Math.Cos(wy * frequency * 1.23 - wz * frequency * 0.89 + wx * frequency * 0.53);
        double caveC = System.Math.Sin((wx + wy - wz) * frequency * 0.61);
        double combined = (caveA + caveB + caveC) / 3.0;
        double caveAmount = System.Math.Max(0.0, combined) * profile.CaveStrength;
        d -= caveAmount;
      }

      density = (float)d;

      if (density <= 0.0f)
      {
        solidMaterialId = 0;
        return;
      }

      float depthBelowSurface = (float)surfaceHeight - (float)wz;
      if (depthBelowSurface <= 3.0f)
      {
        solidMaterialId = ClampMat(profile.SurfaceMaterialId);
      }
      else if (depthBelowSurface <= 18.0f)
      {
        solidMaterialId = ClampMat(profile.SubsurfaceMaterialId);
      }
      else
      {
        solidMaterialId = ClampMat(profile.StoneMaterialId);
      }
    }

    private static int ClampMat(int v) => Mathf.Clamp(v, 1, 65535);
    private static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
  }
}