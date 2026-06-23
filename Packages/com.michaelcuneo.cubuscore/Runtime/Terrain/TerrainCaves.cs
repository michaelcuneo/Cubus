using Unity.Mathematics;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  // Burst-safe 3D cave carving built on CubusNoise.
  //
  // Produces a density-subtraction amount (same units/scale as the legacy
  // CaveStrength carve) so it can drop straight into the existing density
  // samplers (TerrainSampler, TerrainSamplerBurst, MarchingCubesMesher).
  //
  // Two cave systems are combined:
  //   * Spaghetti tunnels - the intersection of two domain-warped noise fields
  //     (a tunnel exists where BOTH fields are near zero), giving long winding
  //     1D worm caves.
  //   * Cheese caverns - low-frequency Worley pockets that open up larger rooms.
  //
  // Caves are gated to start below CaveStartDepth and fade in smoothly so there
  // is no hard cave ceiling. worldSeed is accepted so caves can be reshuffled by
  // the global world seed once it is threaded through the samplers; it defaults
  // to 0 today.
  public static class TerrainCaves
  {
    // Decorrelated seed offsets for the independent noise fields.
    private const int SeedTunnelA = 1300;
    private const int SeedTunnelB = 5210;
    private const int SeedCheese = 9001;

    // Tunnel thickness. Larger = wider worm caves.
    private const float TunnelRadius = 0.09f;

    // Depth (in world units) over which caves fade in below CaveStartDepth.
    private const float DepthFade = 6.0f;

    public static float CarveAmount(
        in TerrainGenerationProfileSnapshot profile,
        double worldX,
        double worldZ,
        double worldYVertical,
        double surfaceHeight)
    {
      if (profile.CaveStrength <= 0.0f)
      {
        return 0.0f;
      }

      int worldSeed = profile.WorldSeed;

      double depthBelowSurface = surfaceHeight - worldYVertical;

      if (depthBelowSurface <= profile.CaveStartDepth)
      {
        return 0.0f;
      }

      float freq = math.max(0.0001f, profile.CaveFrequency);
      float3 p = new float3((float)worldX, (float)worldYVertical, (float)worldZ) * freq;

      // --- Spaghetti tunnels -------------------------------------------------
      NoiseSettings tunnel = new NoiseSettings
      {
        Algorithm = NoiseAlgorithm.Perlin,
        Fractal = FractalMode.Fbm,
        Frequency = 1.0f,
        Octaves = 2,
        Lacunarity = 2.0f,
        Gain = 0.5f,
        SeedOffset = SeedTunnelA,
        WarpAmplitude = 0.6f,
        WarpFrequency = 0.5f,
      };

      float na = CubusNoise.Sample(tunnel, p, worldSeed);

      tunnel.SeedOffset = SeedTunnelB;
      float nb = CubusNoise.Sample(tunnel, p, worldSeed);

      float tube = na * na + nb * nb; // ~0 along tunnel centrelines
      float spaghetti = math.saturate(1.0f - tube / TunnelRadius);

      // --- Cheese caverns ----------------------------------------------------
      NoiseSettings cheeseNoise = new NoiseSettings
      {
        Algorithm = NoiseAlgorithm.Worley,
        Fractal = FractalMode.None,
        Frequency = 0.5f,
        Octaves = 1,
        Lacunarity = 2.0f,
        Gain = 0.5f,
        SeedOffset = SeedCheese,
        WarpAmplitude = 0.0f,
        WarpFrequency = 0.0f,
      };

      float cheeseRaw = CubusNoise.Sample01(cheeseNoise, p, worldSeed);
      float cheese = math.saturate((cheeseRaw - 0.86f) / 0.14f);

      float open = math.max(spaghetti, cheese);

      if (open <= 0.0f)
      {
        return 0.0f;
      }

      float fade = math.saturate((float)(depthBelowSurface - profile.CaveStartDepth) / DepthFade);

      return open * fade * profile.CaveStrength;
    }
  }
}
