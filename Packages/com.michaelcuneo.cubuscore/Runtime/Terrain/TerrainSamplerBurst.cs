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

      double macroFrequency = 0.0065 * System.Math.Max(0.01, profile.MacroFrequency);
      double hillsFrequency = 0.0180 * System.Math.Max(0.01, profile.HillsFrequency);
      double detailFrequency = 0.0550 * System.Math.Max(0.01, profile.DetailFrequency);
      double ridgeFrequency = 0.0130 * System.Math.Max(0.01, profile.RidgeFrequency);
      double valleyFrequency = 0.0070 * System.Math.Max(0.01, profile.ValleyFrequency);

      double continental =
          System.Math.Sin(wx * macroFrequency + wy * macroFrequency * 0.415) * 18.0 +
          System.Math.Cos(wy * macroFrequency * 0.892 - wx * macroFrequency * 0.323) * 16.0 +
          System.Math.Sin((wx + wy) * macroFrequency * 0.646) * 12.0;
      continental *= System.Math.Max(0.0, profile.MacroStrength);

      double hills =
          System.Math.Sin(wx * hillsFrequency + wy * hillsFrequency * 0.611) * 10.0 +
          System.Math.Cos(wy * hillsFrequency * 1.166 - wx * hillsFrequency * 0.500) * 9.0 +
          System.Math.Sin((wx - wy) * hillsFrequency * 0.888) * 7.0;
      hills *= System.Math.Max(0.0, profile.HillsStrength);

      double detail =
          System.Math.Sin(wx * detailFrequency + wy * detailFrequency * 0.673) * 3.5 +
          System.Math.Cos(wy * detailFrequency * 1.109 - wx * detailFrequency * 0.436) * 3.0;
      detail *= System.Math.Max(0.0, profile.DetailStrength);

      double ridgeBase = System.Math.Abs(
          System.Math.Sin(wx * ridgeFrequency + wy * ridgeFrequency * 1.462) +
          System.Math.Cos(wx * ridgeFrequency * 1.308 - wy * ridgeFrequency * 0.846)
      );

      double ridges =
        System.Math.Pow(
          Clamp(ridgeBase * 0.5, 0.0, 1.0),
          System.Math.Max(0.25, profile.RidgeSharpness)
        ) * 24.0 * System.Math.Max(0.0, profile.RidgeStrength);

      double edgeDrop = profile.UseWorldEdgeFalloff
          ? System.Math.Pow(edgeT, 3.0) * 140.0
          : 0.0;

      double valleyMask = Clamp(
          System.Math.Sin(wx * valleyFrequency) * 0.5 +
          System.Math.Cos(wy * valleyFrequency * 0.857) * 0.5,
          -1.0,
          1.0
      );

      double valleyCut =
        System.Math.Max(0.0, valleyMask) *
        18.0 *
        edgeFalloff *
        System.Math.Max(0.0, profile.ValleyStrength);

      // Keep biome modifiers within a relief budget to avoid extreme cliffs.
      double reliefBudget = System.Math.Max(8.0, profile.HeightScale * 0.85);

      double macroMul = 1.0;
      double hillsMul = 1.0;
      double detailMul = 1.0;
      double ridgeMul = 1.0;
      double valleyMul = 1.0;
      double basinSink = 0.0;
      double dunes = 0.0;
      ApplyLandformPreset(
        profile,
        wx,
        wy,
        edgeFalloff,
        out macroMul,
        out hillsMul,
        out detailMul,
        out ridgeMul,
        out valleyMul,
        out basinSink,
        out dunes
      );

      valleyCut = Clamp(valleyCut, 0.0, reliefBudget * 0.85);
      basinSink = Clamp(basinSink, 0.0, reliefBudget);
      dunes = Clamp(dunes, -reliefBudget * 0.35, reliefBudget * 0.35);

      double surfaceHeight =
          baseHeight +
          mainHeight +
          (continental * macroMul) * edgeFalloff +
          (hills * hillsMul) * edgeFalloff +
          (detail * detailMul) * edgeFalloff +
          (ridges * ridgeMul) * edgeFalloff -
          basinSink -
          edgeDrop -
          (valleyCut * valleyMul) +
          dunes;

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
      solidMaterialId = ClampMat(profile.GetMaterialId(depthBelowSurface));
    }

    private static int ClampMat(int v) => Mathf.Clamp(v, 1, 65535);
    private static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static void ApplyLandformPreset(
      TerrainGenerationProfileSnapshot profile,
      double wx,
      double wy,
      double edgeFalloff,
      out double macroMul,
      out double hillsMul,
      out double detailMul,
      out double ridgeMul,
      out double valleyMul,
      out double basinSink,
      out double dunes)
    {
      macroMul = 1.0;
      hillsMul = 1.0;
      detailMul = 1.0;
      ridgeMul = 1.0;
      valleyMul = 1.0;
      basinSink = 0.0;
      dunes = 0.0;

      switch ((TerrainLandformStyle)profile.LandformStyle)
      {
        case TerrainLandformStyle.Plains:
          macroMul = 0.70;
          hillsMul = 0.35;
          detailMul = 0.65;
          ridgeMul = 0.08;
          valleyMul = 0.25;
          break;

        case TerrainLandformStyle.RollingHills:
          macroMul = 0.90;
          hillsMul = 1.20;
          detailMul = 0.90;
          ridgeMul = 0.35;
          valleyMul = 0.45;
          break;

        case TerrainLandformStyle.Mountains:
          macroMul = 1.10;
          hillsMul = 1.15;
          detailMul = 0.85;
          ridgeMul = 2.10;
          valleyMul = 0.70;
          break;

        case TerrainLandformStyle.Basin:
          macroMul = 0.65;
          hillsMul = 0.55;
          detailMul = 0.75;
          ridgeMul = 0.15;
          valleyMul = 1.00;
          basinSink = (4.0 + System.Math.Max(0.0, profile.BasinDepth) * 0.45) * edgeFalloff;
          break;

        case TerrainLandformStyle.DesertDunes:
          {
            macroMul = 0.60;
            hillsMul = 0.45;
            detailMul = 0.80;
            ridgeMul = 0.20;
            valleyMul = 0.70;
            double duneFrequency = 0.014 * System.Math.Max(0.01, profile.DuneFrequency);
            double duneWave =
                System.Math.Sin(wx * duneFrequency + System.Math.Sin(wy * duneFrequency * 0.60) * 2.2) *
                System.Math.Cos(wy * duneFrequency * 1.40);
            double duneAmp = 2.0 + (System.Math.Max(0.0, profile.DuneStrength) * 5.0);
            dunes = duneWave * duneAmp * edgeFalloff;
            break;
          }

        case TerrainLandformStyle.Badlands:
          macroMul = 0.75;
          hillsMul = 0.90;
          detailMul = 1.10;
          ridgeMul = 1.00;
          valleyMul = 0.90;
          basinSink = (2.0 + System.Math.Max(0.0, profile.BasinDepth) * 0.25) * edgeFalloff;
          break;

        default:
          break;
      }
    }
  }
}