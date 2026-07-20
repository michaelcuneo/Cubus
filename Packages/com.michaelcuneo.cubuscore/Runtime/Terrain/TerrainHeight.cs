using Unity.Mathematics;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  // Single shared, Burst-safe surface-height function. This is the one place the
  // terrain landform equations live; TerrainSampler, TerrainSamplerBurst and
  // MarchingCubesMesher all call it so the three paths can never drift apart.
  public static class TerrainHeight
  {
    private const double MacroBaseFrequency = 0.0040;
    private const double HillsBaseFrequency = 0.0150;
    private const double DetailBaseFrequency = 0.0480;
    private const double RidgeBaseFrequency = 0.0100;
    private const double ValleyBaseFrequency = 0.0055;

    private const int SeedMacro = 0;
    private const int SeedHills = 1000;
    private const int SeedDetail = 2000;
    private const int SeedRidge = 3000;
    private const int SeedValley = 4000;
    private const int SeedRange = 5000;
    private const int SeedTributary = 6000;
    private const int SeedValleyWarp = 7000;

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

      // Broad continental shape. This remains the large-scale elevation field,
      // but it now also controls where ranges, foothills and drainage can form.
      double contRaw = SampleLayer(
          NoiseAlgorithm.Perlin,
          FractalMode.Fbm,
          wx,
          wy,
          macroFrequency,
          5,
          SeedMacro,
          seed,
          0.55f,
          2.0f);

      double cont01 = Clamp01(contRaw * 0.5 + 0.5);
      double contShaped = (System.Math.Pow(cont01, 1.25) * 2.0) - 1.0;
      double continental = contShaped * 48.0 * System.Math.Max(0.0, profile.MacroStrength);

      // Broad range backbone. A low-frequency ridged field creates connected
      // mountain belts instead of allowing every fine ridge sample to become an
      // isolated mountain. Fine ridges are then nested inside this backbone.
      double rangeNoise = SampleLayer(
          NoiseAlgorithm.Simplex,
          FractalMode.Ridged,
          wx,
          wy,
          ridgeFrequency * 0.28,
          3,
          SeedRange,
          seed,
          1.15f,
          1.35f);

      double rangeBackbone = SmoothStep(Clamp01((rangeNoise - 0.24) / 0.62));
      double highCountryMask = SmoothStep(Clamp01((cont01 - 0.48) / 0.34));
      double mountainMask = rangeBackbone * highCountryMask;
      double foothillMask = SmoothStep(Clamp01((rangeNoise - 0.08) / 0.58)) * highCountryMask;

      // Rolling terrain follows broad geography and fades in valley floors.
      double hillsNoise = SampleLayer(
          NoiseAlgorithm.Perlin,
          FractalMode.Fbm,
          wx,
          wy,
          hillsFrequency,
          4,
          SeedHills,
          seed,
          0.40f,
          2.0f);

      double hills =
          hillsNoise *
          19.0 *
          System.Math.Max(0.0, profile.HillsStrength) *
          (0.42 + 0.42 * cont01 + 0.16 * foothillMask);

      // Fine surface detail. It is deliberately restrained in the broadest low
      // areas so floodplains and valley bottoms do not become noisy corrugations.
      double detailNoise = SampleLayer(
          NoiseAlgorithm.Perlin,
          FractalMode.Fbm,
          wx,
          wy,
          detailFrequency,
          3,
          SeedDetail,
          seed,
          0.0f,
          0.0f);

      double detail =
          detailNoise *
          5.0 *
          System.Math.Max(0.0, profile.DetailStrength);

      // Fine ridges only gain significant height inside the connected range
      // backbone. Foothills use a softer copy of the same structure.
      double ridgeNoise = SampleLayer(
          NoiseAlgorithm.Perlin,
          FractalMode.Ridged,
          wx,
          wy,
          ridgeFrequency,
          4,
          SeedRidge,
          seed,
          0.30f,
          2.0f);

      double sharpRidges = System.Math.Pow(
          Clamp01(ridgeNoise),
          System.Math.Max(0.25, profile.RidgeSharpness));

      double ridges =
          sharpRidges *
          43.0 *
          System.Math.Max(0.0, profile.RidgeStrength) *
          mountainMask;

      double foothills =
          hillsNoise *
          10.0 *
          System.Math.Max(0.0, profile.RidgeStrength) *
          foothillMask *
          (1.0 - mountainMask * 0.55);

      double edgeDrop = profile.UseWorldEdgeFalloff
          ? System.Math.Pow(edgeT, 3.0) * 140.0
          : 0.0;

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

      double preValleyHeight =
          baseHeight +
          mainHeight +
          (continental * macroMul) * edgeFalloff +
          ((hills + foothills) * hillsMul) * edgeFalloff +
          (detail * detailMul) * edgeFalloff +
          (ridges * ridgeMul) * edgeFalloff -
          basinSink -
          edgeDrop +
          dunes;

      // Connected drainage approximation. Zero-crossings from two differently
      // scaled, domain-warped fields form long trunk channels and branching
      // tributaries. The network is widened in lower terrain and cut deeper in
      // high relief, producing mountain gullies, V valleys and broader lowland
      // corridors instead of unrelated positive-noise depressions.
      double drainage = ComputeDrainageCarve(
          profile,
          wx,
          wy,
          valleyFrequency,
          seed,
          cont01,
          mountainMask,
          preValleyHeight,
          edgeFalloff);

      double reliefBudget = System.Math.Max(8.0, profile.HeightScale * 0.85);
      drainage = Clamp(drainage * valleyMul, 0.0, reliefBudget * 0.92);
      basinSink = Clamp(basinSink, 0.0, reliefBudget);
      dunes = Clamp(dunes, -reliefBudget * 0.35, reliefBudget * 0.35);

      double surfaceHeight = preValleyHeight - drainage;

      if (profile.UseWorldEdgeFalloff && edgeT > 0.78)
      {
        double outerT = Clamp01((edgeT - 0.78) / 0.22);
        double outerSmooth = SmoothStep(outerT);
        surfaceHeight = Lerp(surfaceHeight, profile.WorldEdgeTargetHeight, outerSmooth);
      }

      return surfaceHeight;
    }

    private static double ComputeDrainageCarve(
        in TerrainGenerationProfileSnapshot profile,
        double wx,
        double wy,
        double valleyFrequency,
        int seed,
        double continent01,
        double mountainMask,
        double preValleyHeight,
        double edgeFalloff)
    {
      double strength = System.Math.Max(0.0, profile.ValleyStrength);
      if (strength <= 0.0)
      {
        return 0.0;
      }

      // Separate low-frequency warp keeps the channel network coherent while
      // removing obvious straight or grid-like zero contours.
      double warpX = SampleLayer(
          NoiseAlgorithm.Simplex,
          FractalMode.Fbm,
          wx,
          wy,
          valleyFrequency * 0.45,
          2,
          SeedValleyWarp,
          seed,
          0.0f,
          0.0f);

      double warpY = SampleLayer(
          NoiseAlgorithm.Simplex,
          FractalMode.Fbm,
          wx + 917.0,
          wy - 613.0,
          valleyFrequency * 0.45,
          2,
          SeedValleyWarp + 71,
          seed,
          0.0f,
          0.0f);

      double warpedX = wx + warpX * 34.0;
      double warpedY = wy + warpY * 34.0;

      double trunkField = SampleLayer(
          NoiseAlgorithm.Simplex,
          FractalMode.Fbm,
          warpedX,
          warpedY,
          valleyFrequency * 0.62,
          3,
          SeedValley,
          seed,
          0.0f,
          0.0f);

      double tributaryField = SampleLayer(
          NoiseAlgorithm.Simplex,
          FractalMode.Fbm,
          warpedX + 251.0,
          warpedY - 137.0,
          valleyFrequency * 1.55,
          2,
          SeedTributary,
          seed,
          0.0f,
          0.0f);

      double trunkWidth = Lerp(0.105, 0.205, 1.0 - continent01);
      double tributaryWidth = Lerp(0.060, 0.105, mountainMask);

      double trunk = 1.0 - SmoothStep(Clamp01(System.Math.Abs(trunkField) / trunkWidth));
      double tributary = 1.0 - SmoothStep(Clamp01(System.Math.Abs(tributaryField) / tributaryWidth));

      // Tributaries mostly appear near trunk drainage or in mountain country,
      // avoiding a uniform web of equally strong channels across plains.
      double tributaryAccess = Clamp01(trunk * 0.65 + mountainMask * 0.85);
      double channelNetwork = Clamp01(trunk + tributary * tributaryAccess * 0.72);

      // High terrain receives narrow, deeper incision. Lower terrain receives a
      // wider but gentler floodplain depression around the trunk channel.
      double highRelief = Clamp01((preValleyHeight - profile.BaseHeight) /
          System.Math.Max(24.0, profile.HeightScale * 0.95));

      double channelDepth =
          channelNetwork *
          (7.0 + 17.0 * highRelief + 8.0 * mountainMask) *
          strength;

      double floodplainWidth = Lerp(0.28, 0.14, highRelief);
      double floodplain = 1.0 - SmoothStep(
          Clamp01(System.Math.Abs(trunkField) / floodplainWidth));

      double floodplainDepth =
          floodplain *
          (1.5 + 5.5 * (1.0 - highRelief)) *
          strength;

      return (channelDepth + floodplainDepth) * edgeFalloff;
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
          detailMul = 0.55;
          ridgeMul = 0.08;
          valleyMul = 0.42;
          break;

        case TerrainLandformStyle.RollingHills:
          macroMul = 0.90;
          hillsMul = 1.20;
          detailMul = 0.82;
          ridgeMul = 0.35;
          valleyMul = 0.68;
          break;

        case TerrainLandformStyle.Mountains:
          macroMul = 1.10;
          hillsMul = 1.15;
          detailMul = 0.72;
          ridgeMul = 2.10;
          valleyMul = 1.12;
          break;

        case TerrainLandformStyle.Basin:
          macroMul = 0.65;
          hillsMul = 0.55;
          detailMul = 0.65;
          ridgeMul = 0.15;
          valleyMul = 0.82;
          basinSink = (4.0 + System.Math.Max(0.0, profile.BasinDepth) * 0.45) * edgeFalloff;
          break;

        case TerrainLandformStyle.DesertDunes:
          {
            macroMul = 0.60;
            hillsMul = 0.45;
            detailMul = 0.80;
            ridgeMul = 0.20;
            valleyMul = 0.48;
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
          detailMul = 1.05;
          ridgeMul = 1.00;
          valleyMul = 1.25;
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
