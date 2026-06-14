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

      if (UseTemperature)
      {
        float s = ScoreRange(sample.Temperature, TemperatureRange, safeFeather);
        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      if (UseElevation)
      {
        float s = ScoreRange(sample.Elevation, ElevationRange, safeFeather);
        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      if (UseRise)
      {
        float s = ScoreRange(sample.Rise, RiseRange, safeFeather);
        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      if (UseHarshness)
      {
        float s = ScoreRange(sample.Harshness, HarshnessRange, safeFeather);
        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      if (UseErosion)
      {
        float s = ScoreRange(sample.Erosion, ErosionRange, safeFeather);
        if (s <= 0.0f)
        {
          return 0.0f;
        }

        score *= s;
      }

      return Mathf.Clamp01(score);
    }

    private static bool InRange(float value, Vector2 range)
    {
      float min = Mathf.Min(range.x, range.y);
      float max = Mathf.Max(range.x, range.y);
      return value >= min && value <= max;
    }

    private static float ScoreRange(float value, Vector2 range, float feather)
    {
      float min = Mathf.Min(range.x, range.y);
      float max = Mathf.Max(range.x, range.y);

      float distance = 0.0f;

      if (value < min)
      {
        distance = min - value;
      }
      else if (value > max)
      {
        distance = value - max;
      }

      if (distance <= 0.0f)
      {
        return 1.0f;
      }

      // Use a smooth gaussian-like tail outside the rule range so
      // nearby biomes can still contribute and blend naturally.
      float sigma = Mathf.Max(0.0001f, feather);
      float normalized = distance / sigma;
      float score = Mathf.Exp(-normalized * normalized);

      return Mathf.Clamp01(score);
    }
  }

  public readonly struct BiomeClimateSample
  {
    public readonly float Temperature;
    public readonly float Elevation;
    public readonly float Rise;
    public readonly float Harshness;
    public readonly float Erosion;

    public BiomeClimateSample(
        float temperature,
        float elevation,
        float rise,
        float harshness,
        float erosion)
    {
      Temperature = temperature;
      Elevation = elevation;
      Rise = rise;
      Harshness = harshness;
      Erosion = erosion;
    }
  }
}