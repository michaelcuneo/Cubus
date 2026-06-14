using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [CreateAssetMenu(
      fileName = "Biome Material Set",
      menuName = "CubusCore/Terrain/Biome Material Set"
  )]
  public sealed class BiomeMaterialSet : ScriptableObject
  {
    [Header("Identity")]
    public string SetName = "Material Set";

    [Header("Material Layers")]
    [Tooltip("Each layer maps a depth range to a unique material ID. " +
             "Layers are evaluated top-to-bottom; the last layer covers all remaining depth. " +
             "Material IDs map to atlas tiles: tile = (id - 1) % total.")]
    public List<MaterialLayer> MaterialLayers = new()
    {
      new MaterialLayer { MaxDepthBelowSurface = 3.0f,     MaterialId = 1, Label = "Surface" },
      new MaterialLayer { MaxDepthBelowSurface = 18.0f,    MaterialId = 2, Label = "Subsurface" },
      new MaterialLayer { MaxDepthBelowSurface = 99999.0f, MaterialId = 3, Label = "Stone" },
    };
  }
}