using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public enum TerrainLandformStyle
  {
    Custom = 0,
    Plains = 1,
    RollingHills = 2,
    Mountains = 3,
    Basin = 4,
    DesertDunes = 5,
    Badlands = 6,
  }

  [Serializable]
  public sealed class TerrainGenerationProfile
  {
    [Header("Terrain Shape")]
    public bool UseWorldEdgeFalloff = false;

    [Min(1.0f)]
    public float WorldEdgeRadius = 100000.0f;

    public float WorldEdgeTargetHeight = -55.0f;

    public float BaseHeight = 14.0f;

    [Min(0.0f)]
    public float HeightScale = 78.0f;

    [Header("Landform Style")]
    public TerrainLandformStyle LandformStyle = TerrainLandformStyle.Custom;

    [Tooltip("Scales broad continental frequency. 1.0 keeps default behavior.")]
    [Min(0.01f)]
    public float MacroFrequency = 1.0f;

    [Tooltip("Scales broad continental height contribution.")]
    [Min(0.0f)]
    public float MacroStrength = 1.0f;

    [Tooltip("Scales rolling hill frequency.")]
    [Min(0.01f)]
    public float HillsFrequency = 1.0f;

    [Tooltip("Scales rolling hill height contribution.")]
    [Min(0.0f)]
    public float HillsStrength = 1.0f;

    [Tooltip("Scales high-frequency terrain detail frequency.")]
    [Min(0.01f)]
    public float DetailFrequency = 1.0f;

    [Tooltip("Scales high-frequency terrain detail contribution.")]
    [Min(0.0f)]
    public float DetailStrength = 1.0f;

    [Tooltip("Scales ridge frequency.")]
    [Min(0.01f)]
    public float RidgeFrequency = 1.0f;

    [Tooltip("Scales ridge contribution.")]
    [Min(0.0f)]
    public float RidgeStrength = 1.0f;

    [Tooltip("Ridge sharpness exponent. Higher values produce sharper mountain crests.")]
    [Min(0.25f)]
    public float RidgeSharpness = 2.0f;

    [Tooltip("Scales valley mask frequency.")]
    [Min(0.01f)]
    public float ValleyFrequency = 1.0f;

    [Tooltip("Scales valley carving amount.")]
    [Min(0.0f)]
    public float ValleyStrength = 1.0f;

    [Tooltip("Additional basin sink amount used by Basin and Badlands styles.")]
    [Min(0.0f)]
    public float BasinDepth = 0.0f;

    [Tooltip("Scales dune frequency used by Desert Dunes style.")]
    [Min(0.01f)]
    public float DuneFrequency = 1.0f;

    [Tooltip("Additional dune amplitude used by Desert Dunes style.")]
    [Min(0.0f)]
    public float DuneStrength = 0.0f;

    [Header("Caves")]
    [Min(0.0f)]
    public float CaveStrength = 0.0f;

    [Min(0.0001f)]
    public float CaveFrequency = 0.035f;

    [Min(0.0f)]
    public float CaveStartDepth = 18.0f;

    [Header("Material Layers")]
    [Tooltip("Depth-driven material layers. Material IDs are game-defined. " +
             "The engine only blends them by depth, weight, and noise.")]
    public List<MaterialLayer> MaterialLayers = new()
    {
      new MaterialLayer
      {
        MaxDepthBelowSurface = 2.0f,
        MaterialId = 1,
        BlendWidth = 2.5f,
        Weight = 1.25f,
        NoiseScale = 0.045f,
        NoiseStrength = 0.12f,
        Label = "Surface"
      },
      new MaterialLayer
      {
        MaxDepthBelowSurface = 8.0f,
        MaterialId = 2,
        BlendWidth = 4.0f,
        Weight = 1.0f,
        NoiseScale = 0.04f,
        NoiseStrength = 0.25f,
        Label = "Shallow Subsurface"
      },
      new MaterialLayer
      {
        MaxDepthBelowSurface = 20.0f,
        MaterialId = 3,
        BlendWidth = 6.0f,
        Weight = 0.95f,
        NoiseScale = 0.032f,
        NoiseStrength = 0.35f,
        Label = "Mid Layer"
      },
      new MaterialLayer
      {
        MaxDepthBelowSurface = 45.0f,
        MaterialId = 4,
        BlendWidth = 8.0f,
        Weight = 0.9f,
        NoiseScale = 0.07f,
        NoiseStrength = 0.45f,
        Label = "Deep Broken Layer"
      },
      new MaterialLayer
      {
        MaxDepthBelowSurface = 99999.0f,
        MaterialId = 5,
        BlendWidth = 12.0f,
        Weight = 1.1f,
        NoiseScale = 0.025f,
        NoiseStrength = 0.18f,
        Label = "Base Layer"
      },
    };

    [Header("Material Veins")]
    [Tooltip("Optional 3D material injections. Game-defined material IDs can represent ores, crystals, coal, clay pockets, fossil bands, etc.")]
    public List<MaterialVeinRule> MaterialVeins = new()
    {
    };
  }
}