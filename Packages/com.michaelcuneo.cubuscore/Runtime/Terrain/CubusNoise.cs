using Unity.Mathematics;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  // Base noise primitive used by a layer.
  public enum NoiseAlgorithm
  {
    Value = 0,
    Perlin = 1,
    Simplex = 2,
    Worley = 3,
  }

  // How octaves are combined.
  public enum FractalMode
  {
    None = 0,
    Fbm = 1,
    Ridged = 2,
    Billow = 3,
  }

  // Blittable, Burst-friendly configuration for a single noise layer. Plain
  // value type so it can live inside Burst jobs and NativeArrays.
  [System.Serializable]
  public struct NoiseSettings
  {
    public NoiseAlgorithm Algorithm;
    public FractalMode Fractal;

    [UnityEngine.Min(0.0f)]
    public float Frequency;

    [UnityEngine.Min(1)]
    public int Octaves;

    [UnityEngine.Min(1.0f)]
    public float Lacunarity;

    [UnityEngine.Range(0.0f, 1.0f)]
    public float Gain;

    // Per-layer seed offset, mixed with the world seed so different layers
    // (continents, hills, caves) decorrelate.
    public int SeedOffset;

    // Domain warp: before sampling, the position is displaced by another noise
    // field. 0 amplitude disables it.
    public float WarpAmplitude;
    public float WarpFrequency;

    public static NoiseSettings Default(NoiseAlgorithm algorithm = NoiseAlgorithm.Perlin)
    {
      return new NoiseSettings
      {
        Algorithm = algorithm,
        Fractal = FractalMode.Fbm,
        Frequency = 0.01f,
        Octaves = 4,
        Lacunarity = 2.0f,
        Gain = 0.5f,
        SeedOffset = 0,
        WarpAmplitude = 0.0f,
        WarpFrequency = 0.01f,
      };
    }
  }

  // Burst-safe, allocation-free coherent noise library.
  //
  // All gradients/permutations are derived by integer hashing (no managed
  // lookup tables), so every method is safe to call from inside [BurstCompile]
  // jobs. Base noises are signed and roughly in [-1, 1]; *01 helpers remap to
  // [0, 1]. This is intended to become the single noise source shared by
  // TerrainSampler, TerrainSamplerBurst and the generation jobs.
  public static class CubusNoise
  {
    private const float Inv01 = 1.0f / 4294967296.0f; // 1 / 2^32

    // ---- Integer hashing -------------------------------------------------

    private static uint HashUInt(uint x)
    {
      x ^= x >> 16;
      x *= 0x7feb352du;
      x ^= x >> 15;
      x *= 0x846ca68bu;
      x ^= x >> 16;
      return x;
    }

    private static uint Hash(int x, int y, int seed)
    {
      uint h = (uint)seed * 0x9e3779b1u;
      h = HashUInt(h ^ ((uint)x * 0x85ebca77u));
      h = HashUInt(h ^ ((uint)y * 0xc2b2ae3du));
      return h;
    }

    private static uint Hash(int x, int y, int z, int seed)
    {
      uint h = (uint)seed * 0x9e3779b1u;
      h = HashUInt(h ^ ((uint)x * 0x85ebca77u));
      h = HashUInt(h ^ ((uint)y * 0xc2b2ae3du));
      h = HashUInt(h ^ ((uint)z * 0x27d4eb2fu));
      return h;
    }

    private static float HashFloat01(uint h)
    {
      return h * Inv01;
    }

    // ---- Fade / interpolation -------------------------------------------

    private static float Fade(float t)
    {
      // Quintic smootherstep.
      return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    // ---- Value noise -----------------------------------------------------

    private static float ValueNoise(float2 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);
      float xf = p.x - xi;
      float yf = p.y - yi;

      float u = Fade(xf);
      float v = Fade(yf);

      float c00 = HashFloat01(Hash(xi, yi, seed));
      float c10 = HashFloat01(Hash(xi + 1, yi, seed));
      float c01 = HashFloat01(Hash(xi, yi + 1, seed));
      float c11 = HashFloat01(Hash(xi + 1, yi + 1, seed));

      float x0 = math.lerp(c00, c10, u);
      float x1 = math.lerp(c01, c11, u);
      return math.lerp(x0, x1, v) * 2.0f - 1.0f;
    }

    private static float ValueNoise(float3 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);
      int zi = (int)math.floor(p.z);
      float xf = p.x - xi;
      float yf = p.y - yi;
      float zf = p.z - zi;

      float u = Fade(xf);
      float v = Fade(yf);
      float w = Fade(zf);

      float c000 = HashFloat01(Hash(xi, yi, zi, seed));
      float c100 = HashFloat01(Hash(xi + 1, yi, zi, seed));
      float c010 = HashFloat01(Hash(xi, yi + 1, zi, seed));
      float c110 = HashFloat01(Hash(xi + 1, yi + 1, zi, seed));
      float c001 = HashFloat01(Hash(xi, yi, zi + 1, seed));
      float c101 = HashFloat01(Hash(xi + 1, yi, zi + 1, seed));
      float c011 = HashFloat01(Hash(xi, yi + 1, zi + 1, seed));
      float c111 = HashFloat01(Hash(xi + 1, yi + 1, zi + 1, seed));

      float x00 = math.lerp(c000, c100, u);
      float x10 = math.lerp(c010, c110, u);
      float x01 = math.lerp(c001, c101, u);
      float x11 = math.lerp(c011, c111, u);

      float y0 = math.lerp(x00, x10, v);
      float y1 = math.lerp(x01, x11, v);
      return math.lerp(y0, y1, w) * 2.0f - 1.0f;
    }

    // ---- Perlin (gradient) noise ----------------------------------------

    private static float Grad(uint hash, float x, float y)
    {
      // 8 gradient directions from the low bits.
      uint h = hash & 7u;
      float u = h < 4u ? x : y;
      float v = h < 4u ? y : x;
      return ((h & 1u) == 0u ? u : -u) + ((h & 2u) == 0u ? 2.0f * v : -2.0f * v);
    }

    private static float Grad(uint hash, float x, float y, float z)
    {
      // 16-direction gradient set (Perlin "improved noise"), table-free.
      uint h = hash & 15u;
      float u = h < 8u ? x : y;
      float v = h < 4u ? y : (h == 12u || h == 14u ? x : z);
      return ((h & 1u) == 0u ? u : -u) + ((h & 2u) == 0u ? v : -v);
    }

    private static float PerlinNoise(float2 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);
      float xf = p.x - xi;
      float yf = p.y - yi;

      float u = Fade(xf);
      float v = Fade(yf);

      float n00 = Grad(Hash(xi, yi, seed), xf, yf);
      float n10 = Grad(Hash(xi + 1, yi, seed), xf - 1.0f, yf);
      float n01 = Grad(Hash(xi, yi + 1, seed), xf, yf - 1.0f);
      float n11 = Grad(Hash(xi + 1, yi + 1, seed), xf - 1.0f, yf - 1.0f);

      float x0 = math.lerp(n00, n10, u);
      float x1 = math.lerp(n01, n11, u);
      // Scale to ~[-1, 1].
      return math.lerp(x0, x1, v) * 0.7071f;
    }

    private static float PerlinNoise(float3 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);
      int zi = (int)math.floor(p.z);
      float xf = p.x - xi;
      float yf = p.y - yi;
      float zf = p.z - zi;

      float u = Fade(xf);
      float v = Fade(yf);
      float w = Fade(zf);

      float n000 = Grad(Hash(xi, yi, zi, seed), xf, yf, zf);
      float n100 = Grad(Hash(xi + 1, yi, zi, seed), xf - 1.0f, yf, zf);
      float n010 = Grad(Hash(xi, yi + 1, zi, seed), xf, yf - 1.0f, zf);
      float n110 = Grad(Hash(xi + 1, yi + 1, zi, seed), xf - 1.0f, yf - 1.0f, zf);
      float n001 = Grad(Hash(xi, yi, zi + 1, seed), xf, yf, zf - 1.0f);
      float n101 = Grad(Hash(xi + 1, yi, zi + 1, seed), xf - 1.0f, yf, zf - 1.0f);
      float n011 = Grad(Hash(xi, yi + 1, zi + 1, seed), xf, yf - 1.0f, zf - 1.0f);
      float n111 = Grad(Hash(xi + 1, yi + 1, zi + 1, seed), xf - 1.0f, yf - 1.0f, zf - 1.0f);

      float x00 = math.lerp(n000, n100, u);
      float x10 = math.lerp(n010, n110, u);
      float x01 = math.lerp(n001, n101, u);
      float x11 = math.lerp(n011, n111, u);

      float y0 = math.lerp(x00, x10, v);
      float y1 = math.lerp(x01, x11, v);
      return math.lerp(y0, y1, w);
    }

    // ---- Simplex noise (Gustavson-style, hashless gradients) ------------

    private static float2 Grad2(uint hash)
    {
      // 8 evenly spaced 2D directions.
      uint h = hash & 7u;
      float angle = h * 0.7853982f; // 2*pi/8
      math.sincos(angle, out float s, out float c);
      return new float2(c, s);
    }

    private static float3 Grad3(uint hash)
    {
      // 12 edge-midpoint directions of a cube (classic simplex gradient set).
      uint h = hash % 12u;
      switch (h)
      {
        case 0u: return new float3(1, 1, 0);
        case 1u: return new float3(-1, 1, 0);
        case 2u: return new float3(1, -1, 0);
        case 3u: return new float3(-1, -1, 0);
        case 4u: return new float3(1, 0, 1);
        case 5u: return new float3(-1, 0, 1);
        case 6u: return new float3(1, 0, -1);
        case 7u: return new float3(-1, 0, -1);
        case 8u: return new float3(0, 1, 1);
        case 9u: return new float3(0, -1, 1);
        case 10u: return new float3(0, 1, -1);
        default: return new float3(0, -1, -1);
      }
    }

    private static float SimplexNoise(float2 p, int seed)
    {
      const float F2 = 0.3660254f; // (sqrt(3)-1)/2
      const float G2 = 0.2113249f; // (3-sqrt(3))/6

      float s = (p.x + p.y) * F2;
      int i = (int)math.floor(p.x + s);
      int j = (int)math.floor(p.y + s);

      float t = (i + j) * G2;
      float x0 = p.x - (i - t);
      float y0 = p.y - (j - t);

      int i1 = x0 > y0 ? 1 : 0;
      int j1 = x0 > y0 ? 0 : 1;

      float x1 = x0 - i1 + G2;
      float y1 = y0 - j1 + G2;
      float x2 = x0 - 1.0f + 2.0f * G2;
      float y2 = y0 - 1.0f + 2.0f * G2;

      float n = 0.0f;

      float t0 = 0.5f - x0 * x0 - y0 * y0;
      if (t0 > 0.0f)
      {
        t0 *= t0;
        n += t0 * t0 * math.dot(Grad2(Hash(i, j, seed)), new float2(x0, y0));
      }

      float t1 = 0.5f - x1 * x1 - y1 * y1;
      if (t1 > 0.0f)
      {
        t1 *= t1;
        n += t1 * t1 * math.dot(Grad2(Hash(i + i1, j + j1, seed)), new float2(x1, y1));
      }

      float t2 = 0.5f - x2 * x2 - y2 * y2;
      if (t2 > 0.0f)
      {
        t2 *= t2;
        n += t2 * t2 * math.dot(Grad2(Hash(i + 1, j + 1, seed)), new float2(x2, y2));
      }

      return 70.0f * n;
    }

    private static float SimplexNoise(float3 p, int seed)
    {
      const float F3 = 1.0f / 3.0f;
      const float G3 = 1.0f / 6.0f;

      float s = (p.x + p.y + p.z) * F3;
      int i = (int)math.floor(p.x + s);
      int j = (int)math.floor(p.y + s);
      int k = (int)math.floor(p.z + s);

      float t = (i + j + k) * G3;
      float x0 = p.x - (i - t);
      float y0 = p.y - (j - t);
      float z0 = p.z - (k - t);

      int i1, j1, k1;
      int i2, j2, k2;

      if (x0 >= y0)
      {
        if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
        else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
      }
      else
      {
        if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
        else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
        else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
      }

      float x1 = x0 - i1 + G3;
      float y1 = y0 - j1 + G3;
      float z1 = z0 - k1 + G3;
      float x2 = x0 - i2 + 2.0f * G3;
      float y2 = y0 - j2 + 2.0f * G3;
      float z2 = z0 - k2 + 2.0f * G3;
      float x3 = x0 - 1.0f + 3.0f * G3;
      float y3 = y0 - 1.0f + 3.0f * G3;
      float z3 = z0 - 1.0f + 3.0f * G3;

      float n = 0.0f;

      float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
      if (t0 > 0.0f)
      {
        t0 *= t0;
        n += t0 * t0 * math.dot(Grad3(Hash(i, j, k, seed)), new float3(x0, y0, z0));
      }

      float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
      if (t1 > 0.0f)
      {
        t1 *= t1;
        n += t1 * t1 * math.dot(Grad3(Hash(i + i1, j + j1, k + k1, seed)), new float3(x1, y1, z1));
      }

      float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
      if (t2 > 0.0f)
      {
        t2 *= t2;
        n += t2 * t2 * math.dot(Grad3(Hash(i + i2, j + j2, k + k2, seed)), new float3(x2, y2, z2));
      }

      float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
      if (t3 > 0.0f)
      {
        t3 *= t3;
        n += t3 * t3 * math.dot(Grad3(Hash(i + 1, j + 1, k + 1, seed)), new float3(x3, y3, z3));
      }

      return 32.0f * n;
    }

    // ---- Worley / cellular noise ----------------------------------------

    private static float2 CellPoint(int cx, int cy, int seed)
    {
      uint hx = Hash(cx, cy, seed);
      uint hy = Hash(cx, cy, seed + 0x68bc21eb);
      return new float2(HashFloat01(hx), HashFloat01(hy));
    }

    private static float WorleyNoise(float2 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);

      float f1 = float.MaxValue;

      for (int oy = -1; oy <= 1; oy++)
      {
        for (int ox = -1; ox <= 1; ox++)
        {
          int cx = xi + ox;
          int cy = yi + oy;
          float2 feature = new float2(cx, cy) + CellPoint(cx, cy, seed);
          float d = math.distance(p, feature);
          if (d < f1)
          {
            f1 = d;
          }
        }
      }

      // Remap roughly to [-1, 1] (F1 distance is ~[0, 1.4]).
      return 1.0f - 2.0f * math.saturate(f1);
    }

    private static float3 CellPoint(int cx, int cy, int cz, int seed)
    {
      uint hx = Hash(cx, cy, cz, seed);
      uint hy = Hash(cx, cy, cz, seed + 0x1b56c4f5);
      uint hz = Hash(cx, cy, cz, seed + 0x632be59b);
      return new float3(HashFloat01(hx), HashFloat01(hy), HashFloat01(hz));
    }

    private static float WorleyNoise(float3 p, int seed)
    {
      int xi = (int)math.floor(p.x);
      int yi = (int)math.floor(p.y);
      int zi = (int)math.floor(p.z);

      float f1 = float.MaxValue;

      for (int oz = -1; oz <= 1; oz++)
      {
        for (int oy = -1; oy <= 1; oy++)
        {
          for (int ox = -1; ox <= 1; ox++)
          {
            int cx = xi + ox;
            int cy = yi + oy;
            int cz = zi + oz;
            float3 feature = new float3(cx, cy, cz) + CellPoint(cx, cy, cz, seed);
            float d = math.distance(p, feature);
            if (d < f1)
            {
              f1 = d;
            }
          }
        }
      }

      return 1.0f - 2.0f * math.saturate(f1);
    }

    // ---- Base dispatch ---------------------------------------------------

    private static float Base(NoiseAlgorithm algorithm, float2 p, int seed)
    {
      switch (algorithm)
      {
        case NoiseAlgorithm.Value: return ValueNoise(p, seed);
        case NoiseAlgorithm.Simplex: return SimplexNoise(p, seed);
        case NoiseAlgorithm.Worley: return WorleyNoise(p, seed);
        default: return PerlinNoise(p, seed);
      }
    }

    private static float Base(NoiseAlgorithm algorithm, float3 p, int seed)
    {
      switch (algorithm)
      {
        case NoiseAlgorithm.Value: return ValueNoise(p, seed);
        case NoiseAlgorithm.Simplex: return SimplexNoise(p, seed);
        case NoiseAlgorithm.Worley: return WorleyNoise(p, seed);
        default: return PerlinNoise(p, seed);
      }
    }

    private static float ApplyFractalSample(FractalMode mode, float sample)
    {
      switch (mode)
      {
        case FractalMode.Ridged:
          {
            float r = 1.0f - math.abs(sample);
            return r * r;
          }
        case FractalMode.Billow:
          return math.abs(sample) * 2.0f - 1.0f;
        default:
          return sample;
      }
    }

    // ---- Public fractal sampling ----------------------------------------

    public static float Sample(in NoiseSettings settings, float2 position, int worldSeed)
    {
      int seed = worldSeed + settings.SeedOffset;
      float2 p = position * settings.Frequency;

      if (settings.WarpAmplitude > 0.0f)
      {
        float2 w = new float2(
            Base(settings.Algorithm, p * settings.WarpFrequency + new float2(13.7f, 0.0f), seed + 101),
            Base(settings.Algorithm, p * settings.WarpFrequency + new float2(0.0f, 71.3f), seed + 211));
        p += w * settings.WarpAmplitude;
      }

      int octaves = settings.Octaves < 1 ? 1 : settings.Octaves;
      float lacunarity = settings.Lacunarity < 1.0f ? 2.0f : settings.Lacunarity;
      float gain = settings.Gain <= 0.0f ? 0.5f : settings.Gain;

      float amplitude = 1.0f;
      float sum = 0.0f;
      float norm = 0.0f;

      for (int o = 0; o < octaves; o++)
      {
        float raw = Base(settings.Algorithm, p, seed + o * 1013);
        sum += ApplyFractalSample(settings.Fractal, raw) * amplitude;
        norm += amplitude;
        amplitude *= gain;
        p *= lacunarity;
      }

      float result = norm > 0.0f ? sum / norm : sum;

      // Ridged/Billow already produce roughly [0,1] / [-1,1]; keep signed.
      return result;
    }

    public static float Sample(in NoiseSettings settings, float3 position, int worldSeed)
    {
      int seed = worldSeed + settings.SeedOffset;
      float3 p = position * settings.Frequency;

      if (settings.WarpAmplitude > 0.0f)
      {
        float3 w = new float3(
            Base(settings.Algorithm, p * settings.WarpFrequency + new float3(13.7f, 0.0f, 5.1f), seed + 101),
            Base(settings.Algorithm, p * settings.WarpFrequency + new float3(0.0f, 71.3f, 19.2f), seed + 211),
            Base(settings.Algorithm, p * settings.WarpFrequency + new float3(41.9f, 2.4f, 0.0f), seed + 331));
        p += w * settings.WarpAmplitude;
      }

      int octaves = settings.Octaves < 1 ? 1 : settings.Octaves;
      float lacunarity = settings.Lacunarity < 1.0f ? 2.0f : settings.Lacunarity;
      float gain = settings.Gain <= 0.0f ? 0.5f : settings.Gain;

      float amplitude = 1.0f;
      float sum = 0.0f;
      float norm = 0.0f;

      for (int o = 0; o < octaves; o++)
      {
        float raw = Base(settings.Algorithm, p, seed + o * 1013);
        sum += ApplyFractalSample(settings.Fractal, raw) * amplitude;
        norm += amplitude;
        amplitude *= gain;
        p *= lacunarity;
      }

      return norm > 0.0f ? sum / norm : sum;
    }

    // [0, 1] convenience wrappers.
    public static float Sample01(in NoiseSettings settings, float2 position, int worldSeed)
    {
      return math.saturate(Sample(settings, position, worldSeed) * 0.5f + 0.5f);
    }

    public static float Sample01(in NoiseSettings settings, float3 position, int worldSeed)
    {
      return math.saturate(Sample(settings, position, worldSeed) * 0.5f + 0.5f);
    }
  }
}
