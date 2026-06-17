using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public static class TerrainMaterialResolver
  {
    private const int IronstoneOreMaterialId = 15;
    private const int NovaOreMaterialId = 31;

    public static int ResolveSolidMaterial(
        TerrainGenerationProfileSnapshot profile,
        double horizontalX,
        double horizontalZ,
        double verticalY,
        float density,
        float surfaceHeight)
    {
      if (density <= 0.0f)
      {
        return 0;
      }

      double depthBelowSurface = surfaceHeight - verticalY;
      if (depthBelowSurface < 0.0)
      {
        return 0;
      }

      int veinMaterialId = ResolveVeinMaterial(
          horizontalX,
          horizontalZ,
          verticalY,
          depthBelowSurface
      );

      if (veinMaterialId > 0)
      {
        return ClampMaterialId(veinMaterialId);
      }

      double effectiveDepthBelowSurface = ResolveEffectiveDepthBelowSurface(
          horizontalX,
          horizontalZ,
          verticalY,
          depthBelowSurface
      );

      return ClampMaterialId(profile.GetMaterialId((float)effectiveDepthBelowSurface));
    }

    private static double ResolveEffectiveDepthBelowSurface(
        double horizontalX,
        double horizontalZ,
        double verticalY,
        double depthBelowSurface)
    {
      double effectiveDepth = depthBelowSurface;

      if (depthBelowSurface <= 2.25)
      {
        double surfacePatchNoise = FractalNoise01(
            horizontalX * 0.045,
            horizontalZ * 0.045,
            17.31
        );

        // Near-surface terrain used to resolve almost every visible voxel to the
        // first material layer, which made both block and density terrain render
        // as one material. Push deterministic surface patches into deeper layers
        // so dirt, stone/clay/sandstone, and biome-specific strata are actually
        // visible on generated terrain instead of only inside cuts.
        if (surfacePatchNoise > 0.82)
        {
          effectiveDepth = 32.0 + depthBelowSurface;
        }
        else if (surfacePatchNoise > 0.52)
        {
          effectiveDepth = 8.0 + depthBelowSurface;
        }
      }
      else if (depthBelowSurface <= 5.0)
      {
        double exposedSubsurfaceNoise = FractalNoise01(
            horizontalX * 0.075,
            horizontalZ * 0.075,
            verticalY * 0.025 + 19.17
        );

        if (exposedSubsurfaceNoise > 0.48)
        {
          double t = Clamp01((exposedSubsurfaceNoise - 0.48) / 0.52);
          effectiveDepth += 6.0 + SmoothStep(t) * 22.0;
        }
      }

      return effectiveDepth;
    }

    private static int ResolveVeinMaterial(
        double horizontalX,
        double horizontalZ,
        double verticalY,
        double depthBelowSurface)
    {
      if (depthBelowSurface < 7.0)
      {
        return 0;
      }

      double ironVein = FractalNoise01(
          horizontalX * 0.052,
          verticalY * 0.052,
          horizontalZ * 0.052 + 83.31
      );

      if (verticalY < 128.0 && ironVein > 0.835)
      {
        return IronstoneOreMaterialId;
      }

      double novaVein = FractalNoise01(
          horizontalX * 0.037 + 41.0,
          verticalY * 0.037,
          horizontalZ * 0.037 - 27.0
      );

      if (verticalY < 64.0 && novaVein > 0.90)
      {
        return NovaOreMaterialId;
      }

      return 0;
    }

    private static double FractalNoise01(double x, double y, double z)
    {
      double value = 0.0;
      double amplitude = 0.5;
      double frequency = 1.0;
      double amplitudeSum = 0.0;

      for (int octave = 0; octave < 3; octave++)
      {
        value += ValueNoise01(x * frequency, y * frequency, z * frequency) * amplitude;
        amplitudeSum += amplitude;
        amplitude *= 0.5;
        frequency *= 2.03;
      }

      if (amplitudeSum <= 0.0)
      {
        return 0.0;
      }

      return Clamp01(value / amplitudeSum);
    }

    private static double ValueNoise01(double x, double y, double z)
    {
      int ix = FastFloor(x);
      int iy = FastFloor(y);
      int iz = FastFloor(z);

      double fx = x - ix;
      double fy = y - iy;
      double fz = z - iz;

      double sx = SmoothStep(fx);
      double sy = SmoothStep(fy);
      double sz = SmoothStep(fz);

      double x00 = Lerp(Hash01(ix, iy, iz), Hash01(ix + 1, iy, iz), sx);
      double x10 = Lerp(Hash01(ix, iy + 1, iz), Hash01(ix + 1, iy + 1, iz), sx);
      double x01 = Lerp(Hash01(ix, iy, iz + 1), Hash01(ix + 1, iy, iz + 1), sx);
      double x11 = Lerp(Hash01(ix, iy + 1, iz + 1), Hash01(ix + 1, iy + 1, iz + 1), sx);

      double y0 = Lerp(x00, x10, sy);
      double y1 = Lerp(x01, x11, sy);

      return Lerp(y0, y1, sz);
    }

    private static double Hash01(int x, int y, int z)
    {
      unchecked
      {
        uint h = 2166136261u;
        h = (h ^ (uint)x) * 16777619u;
        h = (h ^ (uint)y) * 16777619u;
        h = (h ^ (uint)z) * 16777619u;
        h ^= h >> 13;
        h *= 1274126177u;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / 16777215.0;
      }
    }

    private static int FastFloor(double value)
    {
      int i = (int)value;
      return value < i ? i - 1 : i;
    }

    private static int ClampMaterialId(int value)
    {
      return Mathf.Clamp(value, 1, 65535);
    }

    private static double Clamp01(double value)
    {
      return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
    }

    private static double SmoothStep(double value)
    {
      return value * value * (3.0 - 2.0 * value);
    }

    private static double Lerp(double a, double b, double t)
    {
      return a + (b - a) * t;
    }
  }
}
