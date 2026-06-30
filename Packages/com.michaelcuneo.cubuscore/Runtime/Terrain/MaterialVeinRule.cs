using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public sealed class MaterialVeinRule
  {
    [Tooltip("Game-defined material ID injected by this rule. The engine does not know whether this is ore, crystal, clay, coal, etc.")]
    [Range(1, 65535)]
    public int MaterialId = 1;

    [Tooltip("Minimum depth below surface where this vein can appear.")]
    [Min(0.0f)]
    public float MinDepthBelowSurface = 8.0f;

    [Tooltip("Maximum depth below surface where this vein can appear.")]
    [Min(0.0f)]
    public float MaxDepthBelowSurface = 64.0f;

    [Tooltip("World-space noise scale. Smaller values create broader veins.")]
    [Min(0.0001f)]
    public float NoiseScale = 0.045f;

    [Tooltip("Noise threshold required for this vein to appear. Higher values make it rarer.")]
    [Range(0.0f, 1.0f)]
    public float Threshold = 0.72f;

    [Tooltip("Softness around the threshold. Larger values create broader blended vein edges.")]
    [Min(0.001f)]
    public float BlendWidth = 0.08f;

    [Tooltip("Maximum contribution when the vein is fully active.")]
    [Min(0.0f)]
    public float Weight = 1.0f;

    [Tooltip("Extra vertical stretching. Larger values make flatter/sedimentary bands. 1 keeps cubic noise.")]
    [Min(0.01f)]
    public float VerticalScale = 0.35f;

    [Tooltip("Editor-only label for readability.")]
    public string Label = "Material Vein";
  }
}