using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  // Hardcoded set of realistic, real-world biome variables.
  //
  // These are GAMEPLAY mechanics (ambient temperature, terrain hardness, etc.),
  // NOT terrain placement inputs. Placement is driven only by Elevation + Rise.
  //
  // To add or remove a variable, edit this enum AND the matching entry in
  // BiomeVariableCatalog.All. A biome only specifies the variables it wants to
  // change; everything else falls back to the world default.
  public enum BiomeVariableType
  {
    Temperature = 0,
    Humidity = 1,
    Precipitation = 2,
    WindStrength = 3,
    Erosion = 4,
    Hardness = 5,
    Fertility = 6,
    Toxicity = 7,
    Radiation = 8,
    Salinity = 9,
  }

  // Metadata for a single biome variable: how it is labelled, its unit, the
  // value range the inspector clamps to, and the built-in default used when
  // neither the biome nor the world overrides it.
  public readonly struct BiomeVariableDescriptor
  {
    public readonly BiomeVariableType Type;
    public readonly string DisplayName;
    public readonly string Unit;
    public readonly float DefaultValue;
    public readonly float MinValue;
    public readonly float MaxValue;

    public BiomeVariableDescriptor(
        BiomeVariableType type,
        string displayName,
        string unit,
        float defaultValue,
        float minValue,
        float maxValue)
    {
      Type = type;
      DisplayName = displayName;
      Unit = unit;
      DefaultValue = defaultValue;
      MinValue = minValue;
      MaxValue = maxValue;
    }

    public float Clamp(float value)
    {
      return Mathf.Clamp(value, Mathf.Min(MinValue, MaxValue), Mathf.Max(MinValue, MaxValue));
    }
  }

  public static class BiomeVariableCatalog
  {
    // Edit this table to add/remove realistic biome variables. Units and ranges
    // are advisory (used for inspector display / clamping); values are not
    // forced to be normalized.
    public static readonly BiomeVariableDescriptor[] All =
    {
      new BiomeVariableDescriptor(BiomeVariableType.Temperature,   "Temperature",   "\u00B0C",    15.0f, -60.0f, 60.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Humidity,      "Humidity",      "%",          50.0f,   0.0f, 100.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Precipitation, "Precipitation", "mm/yr",     800.0f,   0.0f, 8000.0f),
      new BiomeVariableDescriptor(BiomeVariableType.WindStrength,  "Wind Strength", "m/s",         5.0f,   0.0f, 60.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Erosion,       "Erosion",       "0..1",        0.5f,   0.0f, 1.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Hardness,      "Hardness",      "0..1",        0.5f,   0.0f, 1.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Fertility,     "Fertility",     "0..1",        0.5f,   0.0f, 1.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Toxicity,      "Toxicity",      "0..1",        0.0f,   0.0f, 1.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Radiation,     "Radiation",     "0..1",        0.0f,   0.0f, 1.0f),
      new BiomeVariableDescriptor(BiomeVariableType.Salinity,      "Salinity",      "0..1",        0.0f,   0.0f, 1.0f),
    };

    public static int Count => All.Length;

    public static BiomeVariableDescriptor Get(BiomeVariableType type)
    {
      for (int i = 0; i < All.Length; i++)
      {
        if (All[i].Type == type)
        {
          return All[i];
        }
      }

      // Unknown type: return a neutral descriptor so callers never crash.
      return new BiomeVariableDescriptor(type, type.ToString(), string.Empty, 0.0f, 0.0f, 1.0f);
    }

    public static float DefaultValue(BiomeVariableType type)
    {
      return Get(type).DefaultValue;
    }
  }

  // A single per-biome (or per-world) override for a biome variable. Stored in
  // lists so only the variables that differ from the default need to be set.
  [System.Serializable]
  public struct BiomeVariableOverride
  {
    public BiomeVariableType Type;
    public float Value;

    public BiomeVariableOverride(BiomeVariableType type, float value)
    {
      Type = type;
      Value = value;
    }
  }
}
