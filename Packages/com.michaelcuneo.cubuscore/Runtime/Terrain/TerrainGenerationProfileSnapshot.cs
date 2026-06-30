using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public readonly struct MaterialLayerSnapshotEntry
  {
    public readonly float MaxDepthBelowSurface;
    public readonly int MaterialId;
    public readonly float BlendWidth;
    public readonly float Weight;
    public readonly float NoiseScale;
    public readonly float NoiseStrength;

    public MaterialLayerSnapshotEntry(
      float maxDepthBelowSurface,
      int materialId,
      float blendWidth,
      float weight,
      float noiseScale,
      float noiseStrength)
    {
      MaxDepthBelowSurface = maxDepthBelowSurface;
      MaterialId = materialId;
      BlendWidth = blendWidth;
      Weight = weight;
      NoiseScale = noiseScale;
      NoiseStrength = noiseStrength;
    }
  }

  public readonly struct MaterialVeinSnapshotEntry
  {
    public readonly int MaterialId;
    public readonly float MinDepthBelowSurface;
    public readonly float MaxDepthBelowSurface;
    public readonly float NoiseScale;
    public readonly float Threshold;
    public readonly float BlendWidth;
    public readonly float Weight;
    public readonly float VerticalScale;

    public MaterialVeinSnapshotEntry(
      int materialId,
      float minDepthBelowSurface,
      float maxDepthBelowSurface,
      float noiseScale,
      float threshold,
      float blendWidth,
      float weight,
      float verticalScale)
    {
      MaterialId = materialId;
      MinDepthBelowSurface = minDepthBelowSurface;
      MaxDepthBelowSurface = maxDepthBelowSurface;
      NoiseScale = noiseScale;
      Threshold = threshold;
      BlendWidth = blendWidth;
      Weight = weight;
      VerticalScale = verticalScale;
    }
  }

  // Burst-compatible fixed-capacity snapshot of material layers.
  // Uses a large fixed-list buffer to support "a lot" of layers while staying unmanaged.
  [Serializable]
  public readonly struct TerrainGenerationProfileSnapshot
  {
    public const int MaxLayers = 512;

    public readonly bool UseWorldEdgeFalloff;
    public readonly float WorldEdgeRadius;
    public readonly float WorldEdgeTargetHeight;
    public readonly float BaseHeight;
    public readonly float HeightScale;
    public readonly int LandformStyle;
    public readonly float MacroFrequency;
    public readonly float MacroStrength;
    public readonly float HillsFrequency;
    public readonly float HillsStrength;
    public readonly float DetailFrequency;
    public readonly float DetailStrength;
    public readonly float RidgeFrequency;
    public readonly float RidgeStrength;
    public readonly float RidgeSharpness;
    public readonly float ValleyFrequency;
    public readonly float ValleyStrength;
    public readonly float BasinDepth;
    public readonly float DuneFrequency;
    public readonly float DuneStrength;

    public readonly float CaveStrength;
    public readonly float CaveFrequency;
    public readonly float CaveStartDepth;

    public readonly int WorldSeed;

    // Fixed-list storage — Burst-safe, no heap allocation.
    public readonly int LayerCount;
    public readonly FixedList4096Bytes<MaterialLayerSnapshotEntry> MaterialLayers;

    public const int MaxVeins = 128;

    public readonly int VeinCount;
    public readonly FixedList4096Bytes<MaterialVeinSnapshotEntry> MaterialVeins;

    private TerrainGenerationProfileSnapshot(
        bool useWorldEdgeFalloff,
        float worldEdgeRadius,
        float worldEdgeTargetHeight,
        float baseHeight,
        float heightScale,
        int landformStyle,
        float macroFrequency,
        float macroStrength,
        float hillsFrequency,
        float hillsStrength,
        float detailFrequency,
        float detailStrength,
        float ridgeFrequency,
        float ridgeStrength,
        float ridgeSharpness,
        float valleyFrequency,
        float valleyStrength,
        float basinDepth,
        float duneFrequency,
        float duneStrength,
        float caveStrength,
        float caveFrequency,
        float caveStartDepth,
        int worldSeed,
        FixedList4096Bytes<MaterialLayerSnapshotEntry> materialLayers,
        FixedList4096Bytes<MaterialVeinSnapshotEntry> materialVeins)
    {
      UseWorldEdgeFalloff = useWorldEdgeFalloff;
      WorldEdgeRadius = worldEdgeRadius;
      WorldEdgeTargetHeight = worldEdgeTargetHeight;
      BaseHeight = baseHeight;
      HeightScale = heightScale;
      LandformStyle = landformStyle;
      MacroFrequency = macroFrequency;
      MacroStrength = macroStrength;
      HillsFrequency = hillsFrequency;
      HillsStrength = hillsStrength;
      DetailFrequency = detailFrequency;
      DetailStrength = detailStrength;
      RidgeFrequency = ridgeFrequency;
      RidgeStrength = ridgeStrength;
      RidgeSharpness = ridgeSharpness;
      ValleyFrequency = valleyFrequency;
      ValleyStrength = valleyStrength;
      BasinDepth = basinDepth;
      DuneFrequency = duneFrequency;
      DuneStrength = duneStrength;
      CaveStrength = caveStrength;
      CaveFrequency = caveFrequency;
      CaveStartDepth = caveStartDepth;
      WorldSeed = worldSeed;
      MaterialLayers = materialLayers;
      LayerCount = MaterialLayers.Length;
      MaterialVeins = materialVeins;
      VeinCount = MaterialVeins.Length;
    }

    public TerrainGenerationProfileSnapshot(TerrainGenerationProfile profile, int worldSeed = 0)
    {
      if (profile == null)
      {
        profile = new TerrainGenerationProfile();
      }

      UseWorldEdgeFalloff = profile.UseWorldEdgeFalloff;
      WorldEdgeRadius = profile.WorldEdgeRadius;
      WorldEdgeTargetHeight = profile.WorldEdgeTargetHeight;
      BaseHeight = profile.BaseHeight;
      HeightScale = profile.HeightScale;
      LandformStyle = (int)profile.LandformStyle;
      MacroFrequency = Mathf.Max(0.01f, profile.MacroFrequency);
      MacroStrength = Mathf.Max(0.0f, profile.MacroStrength);
      HillsFrequency = Mathf.Max(0.01f, profile.HillsFrequency);
      HillsStrength = Mathf.Max(0.0f, profile.HillsStrength);
      DetailFrequency = Mathf.Max(0.01f, profile.DetailFrequency);
      DetailStrength = Mathf.Max(0.0f, profile.DetailStrength);
      RidgeFrequency = Mathf.Max(0.01f, profile.RidgeFrequency);
      RidgeStrength = Mathf.Max(0.0f, profile.RidgeStrength);
      RidgeSharpness = Mathf.Max(0.25f, profile.RidgeSharpness);
      ValleyFrequency = Mathf.Max(0.01f, profile.ValleyFrequency);
      ValleyStrength = Mathf.Max(0.0f, profile.ValleyStrength);
      BasinDepth = Mathf.Max(0.0f, profile.BasinDepth);
      DuneFrequency = Mathf.Max(0.01f, profile.DuneFrequency);
      DuneStrength = Mathf.Max(0.0f, profile.DuneStrength);
      CaveStrength = profile.CaveStrength;
      CaveFrequency = profile.CaveFrequency;
      CaveStartDepth = profile.CaveStartDepth;
      WorldSeed = worldSeed;

      FixedList4096Bytes<MaterialLayerSnapshotEntry> layers = new();
      List<MaterialLayer> src = profile.MaterialLayers;

      if (src != null)
      {
        int desiredCount = Mathf.Min(src.Count, MaxLayers);
        for (int i = 0; i < desiredCount; i++)
        {
          if (layers.Length >= layers.Capacity)
          {
            break;
          }

          MaterialLayer layer = src[i];
          layers.Add(new MaterialLayerSnapshotEntry(
              layer.MaxDepthBelowSurface,
              Mathf.Clamp(layer.MaterialId, 1, 65535),
              Mathf.Max(0.01f, layer.BlendWidth),
              Mathf.Max(0.0f, layer.Weight),
              Mathf.Max(0.0001f, layer.NoiseScale),
              Mathf.Clamp01(layer.NoiseStrength)
          ));
        }
      }

      if (layers.Length == 0)
      {
        layers.Add(new MaterialLayerSnapshotEntry(
            99999.0f,
            1,
            8.0f,
            1.0f,
            0.037f,
            0.25f
        ));
      }

      MaterialLayers = layers;
      LayerCount = MaterialLayers.Length;

      FixedList4096Bytes<MaterialVeinSnapshotEntry> veins = new();

      if (profile.MaterialVeins != null)
      {
        int desiredVeinCount = Mathf.Min(profile.MaterialVeins.Count, MaxVeins);

        for (int i = 0; i < desiredVeinCount; i++)
        {
          if (veins.Length >= veins.Capacity)
          {
            break;
          }

          MaterialVeinRule vein = profile.MaterialVeins[i];

          float minDepth = Mathf.Max(0.0f, vein.MinDepthBelowSurface);
          float maxDepth = Mathf.Max(minDepth + 0.001f, vein.MaxDepthBelowSurface);

          veins.Add(new MaterialVeinSnapshotEntry(
            Mathf.Clamp(vein.MaterialId, 1, 65535),
            minDepth,
            maxDepth,
            Mathf.Max(0.0001f, vein.NoiseScale),
            Mathf.Clamp01(vein.Threshold),
            Mathf.Max(0.001f, vein.BlendWidth),
            Mathf.Max(0.0f, vein.Weight),
            Mathf.Max(0.01f, vein.VerticalScale)
          ));
        }
      }

      MaterialVeins = veins;
      VeinCount = MaterialVeins.Length;
    }

    // Returns the material ID for a given depth below the terrain surface.
    // Iterates layers in order; the last layer catches all remaining depth.
    public int GetMaterialId(float depthBelowSurface)
    {
      if (MaterialLayers.Length == 0)
      {
        return 1;
      }

      for (int i = 0; i < MaterialLayers.Length; i++)
      {
        MaterialLayerSnapshotEntry layer = MaterialLayers[i];
        if (depthBelowSurface <= layer.MaxDepthBelowSurface)
        {
          return layer.MaterialId;
        }
      }

      return MaterialLayers[MaterialLayers.Length - 1].MaterialId;
    }

    public static TerrainGenerationProfileSnapshot Blend(
        in TerrainGenerationProfileSnapshot a,
        in TerrainGenerationProfileSnapshot b,
        float t)
    {
      float clampedT = Mathf.Clamp01(t);
      // Keep a stable layer set during profile blending. Selecting A/B layers by
      // a hard 0.5 threshold creates visible seam lines across biome transitions.
      FixedList4096Bytes<MaterialLayerSnapshotEntry> layers = a.MaterialLayers;
      FixedList4096Bytes<MaterialVeinSnapshotEntry> veins = a.MaterialVeins;

      // Preserve biome character away from the center of the transition.
      // Only switch to Custom near the middle band to avoid hard preset snaps.
      int blendedLandformStyle;
      if (a.LandformStyle == b.LandformStyle)
      {
        blendedLandformStyle = a.LandformStyle;
      }
      else if (clampedT <= 0.25f)
      {
        blendedLandformStyle = a.LandformStyle;
      }
      else if (clampedT >= 0.75f)
      {
        blendedLandformStyle = b.LandformStyle;
      }
      else
      {
        blendedLandformStyle = (int)TerrainLandformStyle.Custom;
      }

      bool useWorldEdgeFalloff = a.UseWorldEdgeFalloff || b.UseWorldEdgeFalloff;

      return new TerrainGenerationProfileSnapshot(
        useWorldEdgeFalloff,
          Mathf.Lerp(a.WorldEdgeRadius, b.WorldEdgeRadius, clampedT),
          Mathf.Lerp(a.WorldEdgeTargetHeight, b.WorldEdgeTargetHeight, clampedT),
          Mathf.Lerp(a.BaseHeight, b.BaseHeight, clampedT),
          Mathf.Lerp(a.HeightScale, b.HeightScale, clampedT),
        blendedLandformStyle,
          Mathf.Lerp(a.MacroFrequency, b.MacroFrequency, clampedT),
          Mathf.Lerp(a.MacroStrength, b.MacroStrength, clampedT),
          Mathf.Lerp(a.HillsFrequency, b.HillsFrequency, clampedT),
          Mathf.Lerp(a.HillsStrength, b.HillsStrength, clampedT),
          Mathf.Lerp(a.DetailFrequency, b.DetailFrequency, clampedT),
          Mathf.Lerp(a.DetailStrength, b.DetailStrength, clampedT),
          Mathf.Lerp(a.RidgeFrequency, b.RidgeFrequency, clampedT),
          Mathf.Lerp(a.RidgeStrength, b.RidgeStrength, clampedT),
          Mathf.Lerp(a.RidgeSharpness, b.RidgeSharpness, clampedT),
          Mathf.Lerp(a.ValleyFrequency, b.ValleyFrequency, clampedT),
          Mathf.Lerp(a.ValleyStrength, b.ValleyStrength, clampedT),
          Mathf.Lerp(a.BasinDepth, b.BasinDepth, clampedT),
          Mathf.Lerp(a.DuneFrequency, b.DuneFrequency, clampedT),
          Mathf.Lerp(a.DuneStrength, b.DuneStrength, clampedT),
          Mathf.Lerp(a.CaveStrength, b.CaveStrength, clampedT),
          Mathf.Lerp(a.CaveFrequency, b.CaveFrequency, clampedT),
          Mathf.Lerp(a.CaveStartDepth, b.CaveStartDepth, clampedT),
          a.WorldSeed,
          layers,
          veins
      );
    }
  }
}