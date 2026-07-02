using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials
{
  [CreateAssetMenu(
    fileName = "Cubus Terrain Material Library",
    menuName = "CubusCore/Terrain/Terrain Material Library"
  )]
  public sealed class CubusTerrainMaterialLibrary : ScriptableObject
  {
    [Header("Texture Array Settings")]
    [Min(16)]
    public int TextureSize = 1024;

    public TextureFormat AlbedoFormat = TextureFormat.RGBA32;
    public TextureFormat NormalFormat = TextureFormat.RGBA32;
    public TextureFormat MaskFormat = TextureFormat.RGBA32;

    public bool GenerateMipMaps = true;

    [Header("Materials")]
    public List<CubusTerrainMaterial> Materials = new();

    public bool TryGetMaterial(int materialId, out CubusTerrainMaterial material)
    {
      if (Materials != null)
      {
        for (int i = 0; i < Materials.Count; i++)
        {
          CubusTerrainMaterial candidate = Materials[i];

          if (candidate != null && candidate.MaterialId == materialId)
          {
            material = candidate;
            return true;
          }
        }
      }

      material = null;
      return false;
    }

    public int GetSliceIndex(int materialId)
    {
      if (Materials != null)
      {
        for (int i = 0; i < Materials.Count; i++)
        {
          CubusTerrainMaterial candidate = Materials[i];

          if (candidate != null && candidate.MaterialId == materialId)
          {
            return i;
          }
        }
      }

      return 0;
    }

    public int MaterialCount => Materials != null ? Materials.Count : 0;
  }
}