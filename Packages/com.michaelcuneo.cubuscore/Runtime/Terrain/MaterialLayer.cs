using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public sealed class MaterialLayer
  {
    [Tooltip("This layer is active when depth below surface is less than or equal to this value. " +
             "Set to a very large number (e.g. 99999) for the deepest/default layer.")]
    [Min(0.0f)]
    public float MaxDepthBelowSurface = 3.0f;

    [Tooltip("Material ID that maps to an atlas tile. Tile index = (MaterialId - 1) % totalAtlasTiles.")]
    [Range(1, 65535)]
    public int MaterialId = 1;

    [Tooltip("Optional label for editor readability.")]
    public string Label = "Layer";
  }
}
