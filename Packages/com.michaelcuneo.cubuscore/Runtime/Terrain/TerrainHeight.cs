using Unity.Mathematics;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  // Single shared, Burst-safe surface-height function. This is the one place the
  // terrain landform equations live; TerrainSampler, TerrainSamplerBurst and
  // MarchingCubesMesher all call it so the three paths can never drift apart.
  //
  // Height layers are built on CubusNoise (Perlin fbm + ridged), seeded by
  // profile.WorldSeed, replacing the old non-seeded sin/cos pattern noise.
  public static class TerrainHeight
  {
    // Base layer frequencies (multiplied by the per-profile frequency knobs).
    private const double MacroBaseFrequency = 0.0040;
    private const double HillsBaseFrequency = 0.0150;
    private const double DetailBaseFrequency = 0.0480;
    private const double RidgeBaseFrequency = 0.0100;
    private const double ValleyBaseFrequency = 0.0055;

    // Per-layer seed offsets so the layers decorrelate.
    private const int SeedMacro = 0;
    private const int SeedHills = 1000;
    private const int SeedDetail = 2000;
    private const int SeedRidge = 3000;
    private const int SeedValley = 4000;

    // Column-invariant: pure function of world X/Z. Hoist out of per-voxel loops.
    public static double ComputeSurfaceHeight(
        in TerrainGenerationProfileSnapshot profile,
        double wx,
        double wy)
    {
      int seed = profile.WorldSeed;

      double edgeT = 0.0;
      double edgeFalloff = 1.0;

      if (profile.UseWorldEdgeFalloff)
      {
        double distance2D = System.Math.Sqrt(wx * wx + wy * wy);
        double safeWorldEdgeRadius = System.Math.Max(1.0, profile.WorldEdgeRadius);
        edgeT = Clamp01(distance2D / safeWorldEdgeRadius);
        edgeFalloff = 1.0 - SmoothStep(edgeT);
      }

      double baseHeight = profile.BaseHeight;
      double mainHeight = profile.HeightScale * edgeFalloff;

      double macroFrequency = MacroBaseFrequency * System.Math.Max(0.01, profile.MacroFrequency);
      double hillsFrequency = HillsBaseFrequency * System.Math.Max(0.01, profile.HillsFrequency);
      double detailFrequency = DetailBaseFrequency * System.Math.Max(0.01, profile.DetailFrequency);
      double ridgeFrequency = RidgeBaseFrequency * System.Math.Max(0.01, profile.RidgeFrequency);
      double valleyFrequency = ValleyBaseFrequency * System.Math.Max(0.01, profile.ValleyFrequency);

      // Broad continental shape: low frequency, domain-warped for natural
      // meandering coastlines, then redistributed (hypsometry) so most of the
      // world is gentle lowland with occasional high country.
      double contRaw = SampleLayer(NoiseAlgorithm.Perlin, FractalMode.Fbm, wx, wy, macroFrequency, 5, SeedMacro, seed, 0.55f, 2.0f);
      double cont01 = Clamp01(contRaw * 0.5 + 0.5);
      double contShaped = (System.Math.Pow(cont01, 1.25) * 2.0) - 1.0;
      double continental = contShaped * 48.0 * System.Math.Max(0.0, profile.MacroStrength);

      // Rolling hills (domain-warped), stronger on higher ground than in basins.
      double hills =
          SampleLayer(NoiseAlgorithm.Perlin, FractalMode.Fbm, wx, wy, hillsFrequency, 4, SeedHills, seed, 0.40f, 2.0f)
          * 22.0 * System.Math.Max(0.0, profile.HillsStrength) * (0.45 + 0.55 * cont01);

      // Fine surface detail.
      double detail =
          SampleLayer(NoiseAlgorithm.Perlin, FractalMode.Fbm, wx, wy, detailFrequency, 3, SeedDetail, seed, 0.0f, 0.0f)
          * 6.0 * System.Math.Max(0.0, profile.DetailStrength);

      // Mountain ridges (ridged fractal, sharpened) that only build up in
      // high-continent regions, so mountains form ranges instead of random
      // spikes scattered across lowlands.
      double ridgeNoise = SampleLayer(NoiseAlgorithm.Perlin, FractalMode.Ridged, wx, wy, ridgeFrequency, 4, SeedRidge, seed, 0.0f, 0.0f);
      double mountainMask = SmoothStep(Clamp01((cont01 - 0.55) / 0.30));
      double ridges =
          System.Math.Pow(
              Clamp01(ridgeNoise),
              System.Math.Max(0.25, profile.RidgeSharpness))
          * 38.0 * System.Math.Max(0.0, profile.RidgeStrength) * mountainMask;

      double edgeDrop = profile.UseWorldEdgeFalloff
          ? System.Math.Pow(edgeT, 3.0) * 140.0
          : 0.0;

      // Valley / erosion carving.
      double valleyMask = SampleLayer(NoiseAlgorithm.Perlin, FractalMode.Fbm, wx, wy, valleyFrequency, 2, SeedValley, seed, 0.0f, 0.0f);
      double valleyCut =
          System.Math.Max(0.0, valleyMask) *
          16.0 *
          edgeFalloff *
          System.Math.Max(0.0, profile.ValleyStrength);

      double reliefBudget = System.Math.Max(8.0, profile.HeightScale * 0.85);

      ApplyLandformPreset(
          profile,
          wx,
          wy,
          edgeFalloff,
          out double macroMul,
          out double hillsMul,
          out double detailMul,
          out double ridgeMul,
          out double valleyMul,
          out double basinSink,
          out double dunes);

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

      return surfaceHeight;
    }

    private static double SampleLayer(
        NoiseAlgorithm algorithm,
        FractalMode fractal,
        double wx,
        double wy,
        double frequency,
        int octaves,
        int seedOffset,
        int worldSeed,
        float warpAmplitude = 0.0f,
        float warpFrequency = 0.0f)
    {
      NoiseSettings settings = new NoiseSettings
      {
        Algorithm = algorithm,
        Fractal = fractal,
        Frequency = 1.0f,
        Octaves = octaves,
        Lacunarity = 2.0f,
        Gain = 0.5f,
        SeedOffset = seedOffset,
        WarpAmplitude = warpAmplitude,
        WarpFrequency = warpFrequency,
      };

      // Pre-scale by frequency in double precision, then cast, so large world
      // coordinates keep precision before the float noise evaluation.
      float2 p = new float2((float)(wx * frequency), (float)(wy * frequency));
      return CubusNoise.Sample(settings, p, worldSeed);
    }

    private static void ApplyLandformPreset(
        in TerrainGenerationProfileSnapshot profile,
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

    private static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

    private static double Clamp(double v, double min, double max)
    {
      if (v < min)
      {
        return min;
      }

      return v > max ? max : v;
    }

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
  }
}
