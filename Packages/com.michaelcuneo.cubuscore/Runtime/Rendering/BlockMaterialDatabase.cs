using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  public enum BlockRenderCategory : byte
  {
    Opaque = 0,
    Cutout = 1,
    Transparent = 2,
  }

  public enum BlockLightingCategory : byte
  {
    Lit = 0,
    Unlit = 1,
    Emissive = 2,
  }

  [Serializable]
  public sealed class BlockMaterialDefinition
  {
    [Header("Identity")]
    public string DisplayName = "Material";

    [Range(1, 65535)]
    public int MaterialId = 1;

    [Header("Atlas Tile IDs")]
    [Range(1, 65535)]
    public int TopTileId = 1;

    [Range(1, 65535)]
    public int SideTileId = 1;

    [Range(1, 65535)]
    public int BottomTileId = 1;

    [Header("Rendering")]
    public BlockRenderCategory RenderCategory = BlockRenderCategory.Opaque;
    public BlockLightingCategory LightingCategory = BlockLightingCategory.Lit;

    [Range(0.0f, 8.0f)]
    public float EmissionIntensity = 0.0f;
  }

  [CreateAssetMenu(
      fileName = "Block Material Database",
      menuName = "CubusCore/Rendering/Block Material Database"
  )]
  public sealed class BlockMaterialDatabase : ScriptableObject
  {
    [Tooltip("Defines top/side/bottom atlas tile mapping and render/lighting behavior for each material ID.")]
    public List<BlockMaterialDefinition> Definitions = new();

    [ContextMenu("Auto Name Definitions")]
    public void AutoNameDefinitions()
    {
      if (Definitions == null)
      {
        return;
      }

      for (int i = 0; i < Definitions.Count; i++)
      {
        BlockMaterialDefinition def = Definitions[i];
        if (def == null)
        {
          continue;
        }

        if (string.IsNullOrWhiteSpace(def.DisplayName))
        {
          def.DisplayName = $"Material {Mathf.Clamp(def.MaterialId, 1, 65535)}";
        }
      }
    }

    private void OnValidate()
    {
      AutoNameDefinitions();
    }
  }
}
