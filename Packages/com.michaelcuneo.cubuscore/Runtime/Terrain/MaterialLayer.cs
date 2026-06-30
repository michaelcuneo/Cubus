using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public sealed class MaterialLayer
  {
    [Tooltip("This layer is strongest around this depth range. Layers are evaluated by depth below terrain surface.")]
    [Min(0.0f)]
    public float MaxDepthBelowSurface = 3.0f;

    [Tooltip("Game-defined material ID. The engine does not know whether this is grass, dirt, clay, basalt, copper, etc.")]
    [Range(1, 65535)]
    public int MaterialId = 1;

    [Tooltip("How far this layer blends into neighbouring layers. Larger values produce softer geological transitions.")]
    [Min(0.01f)]
    public float BlendWidth = 3.0f;

    [Tooltip("Relative strength when this layer competes with nearby layers.")]
    [Min(0.0f)]
    public float Weight = 1.0f;

    [Tooltip("World-space noise scale used to break up this layer. Smaller values create broader patches.")]
    [Min(0.0001f)]
    public float NoiseScale = 0.037f;

    [Tooltip("How strongly noise affects this layer. 0 = clean band, 1 = strong breakup.")]
    [Range(0.0f, 1.0f)]
    public float NoiseStrength = 0.25f;

    [Tooltip("Editor-only label for readability. The engine does not use this at runtime.")]
    public string Label = "Layer";
  }
}