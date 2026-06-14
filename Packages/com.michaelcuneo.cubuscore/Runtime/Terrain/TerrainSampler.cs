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

      double macroFrequency = 0.0065 * System.Math.Max(0.01, profileSnapshot.MacroFrequency);
      double hillsFrequency = 0.0180 * System.Math.Max(0.01, profileSnapshot.HillsFrequency);
      double detailFrequency = 0.0550 * System.Math.Max(0.01, profileSnapshot.DetailFrequency);
      double ridgeFrequency = 0.0130 * System.Math.Max(0.01, profileSnapshot.RidgeFrequency);
      double valleyFrequency = 0.0070 * System.Math.Max(0.01, profileSnapshot.ValleyFrequency);

      double continental =
          System.Math.Sin(wx * macroFrequency + wy * macroFrequency * 0.415) * 18.0 +
          System.Math.Cos(wy * macroFrequency * 0.892 - wx * macroFrequency * 0.323) * 16.0 +
          System.Math.Sin((wx + wy) * macroFrequency * 0.646) * 12.0;
      continental *= System.Math.Max(0.0, profileSnapshot.MacroStrength);

      double hills =
          System.Math.Sin(wx * hillsFrequency + wy * hillsFrequency * 0.611) * 10.0 +
          System.Math.Cos(wy * hillsFrequency * 1.166 - wx * hillsFrequency * 0.500) * 9.0 +
          System.Math.Sin((wx - wy) * hillsFrequency * 0.888) * 7.0;
      hills *= System.Math.Max(0.0, profileSnapshot.HillsStrength);

      double detail =
          System.Math.Sin(wx * detailFrequency + wy * detailFrequency * 0.673) * 3.5 +
          System.Math.Cos(wy * detailFrequency * 1.109 - wx * detailFrequency * 0.436) * 3.0;
      detail *= System.Math.Max(0.0, profileSnapshot.DetailStrength);

      double ridgeBase = System.Math.Abs(
          System.Math.Sin(wx * ridgeFrequency + wy * ridgeFrequency * 1.462) +
          System.Math.Cos(wx * ridgeFrequency * 1.308 - wy * ridgeFrequency * 0.846)
      );

      double ridges =
        System.Math.Pow(
          Clamp(ridgeBase * 0.5, 0.0, 1.0),
          System.Math.Max(0.25, profileSnapshot.RidgeSharpness)
        ) * 24.0 * System.Math.Max(0.0, profileSnapshot.RidgeStrength);

      double edgeDrop = profileSnapshot.UseWorldEdgeFalloff
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
        System.Math.Max(0.0, profileSnapshot.ValleyStrength);

      // Keep procedural modifiers within a reasonable relief budget so
      // per-biome tuning cannot create pathological vertical cliffs.
      double reliefBudget = System.Math.Max(8.0, profileSnapshot.HeightScale * 0.85);

      double macroMul = 1.0;
      double hillsMul = 1.0;
      double detailMul = 1.0;
      double ridgeMul = 1.0;
      double valleyMul = 1.0;
      double basinSink = 0.0;
      double dunes = 0.0;
      ApplyLandformPreset(
        profileSnapshot,
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
      if (density <= 0.0f)
      {
        return 0;
      }

      float depthBelowSurface = surfaceHeight - worldVoxelPosition.y;
      return ClampMaterialId(profileSnapshot.GetMaterialId(depthBelowSurface));
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

    private static void ApplyLandformPreset(
      TerrainGenerationProfileSnapshot profileSnapshot,
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

      switch ((TerrainLandformStyle)profileSnapshot.LandformStyle)
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
          basinSink = (4.0 + System.Math.Max(0.0, profileSnapshot.BasinDepth) * 0.45) * edgeFalloff;
          break;

        case TerrainLandformStyle.DesertDunes:
          {
            macroMul = 0.60;
            hillsMul = 0.45;
            detailMul = 0.80;
            ridgeMul = 0.20;
            valleyMul = 0.70;
            double duneFrequency = 0.014 * System.Math.Max(0.01, profileSnapshot.DuneFrequency);
            double duneWave =
                System.Math.Sin(wx * duneFrequency + System.Math.Sin(wy * duneFrequency * 0.60) * 2.2) *
                System.Math.Cos(wy * duneFrequency * 1.40);
            double duneAmp = 2.0 + (System.Math.Max(0.0, profileSnapshot.DuneStrength) * 5.0);
            dunes = duneWave * duneAmp * edgeFalloff;
            break;
          }

        case TerrainLandformStyle.Badlands:
          macroMul = 0.75;
          hillsMul = 0.90;
          detailMul = 1.10;
          ridgeMul = 1.00;
          valleyMul = 0.90;
          basinSink = (2.0 + System.Math.Max(0.0, profileSnapshot.BasinDepth) * 0.25) * edgeFalloff;
          break;

        default:
          break;
      }
    }
  }
}