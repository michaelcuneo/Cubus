using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public sealed class BiomeWorldRule
  {
    public string RuleName = "Biome Rule";

    [Tooltip("Used as the default biome when no rule matches. If multiple are marked, highest Priority wins.")]
    public bool IsFallback = false;

    [Min(0)]
    public int Priority = 0;

    public BiomeDefinition Biome;

    public bool UseTemperature = true;
    public Vector2 TemperatureRange = new(0.0f, 1.0f);

    public bool UseElevation = true;
    public Vector2 ElevationRange = new(0.0f, 1.0f);

    public bool UseRise = false;
    public Vector2 RiseRange = new(0.0f, 1.0f);

    public bool UseHarshness = false;
    public Vector2 HarshnessRange = new(0.0f, 1.0f);

    public bool UseErosion = false;
    public Vector2 ErosionRange = new(0.0f, 1.0f);

    public bool Matches(in BiomeClimateSample sample)
    {
      return GetMatchScore(sample, 0.0001f) > 0.0f;
    }

    public float GetMatchScore(in BiomeClimateSample sample, float feather)
    {
      if (Biome == null)
      {
        return 0.0f;
      }

      float safeFeather = Mathf.Max(0.0001f, feather);
      float score = 1.0f;

      // Placement selector: terrain elevation band.
      if (UseElevation)
      {
        float s = ScoreRange(sample.Elevation, ElevationRange, safeFeather);

        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      // Placement selector: terrain rise/slope/mountain band.
      if (UseRise)
      {
        float s = ScoreRange(sample.Rise, RiseRange, safeFeather);

        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      return Mathf.Clamp01(score);
    }

    private static float ScoreRange(float value, Vector2 range, float feather)
    {
      float min = Mathf.Min(range.x, range.y);
      float max = Mathf.Max(range.x, range.y);

      if (value >= min && value <= max)
      {
        return 1.0f;
      }

      float distance = value < min
          ? min - value
          : value - max;

      float t = Mathf.Clamp01(
          1.0f - distance / Mathf.Max(0.0001f, feather)
      );

      return t * t * (3.0f - 2.0f * t);
    }
  }
}