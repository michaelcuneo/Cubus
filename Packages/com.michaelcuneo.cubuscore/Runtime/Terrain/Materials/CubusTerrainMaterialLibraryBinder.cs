using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials
{
  [DisallowMultipleComponent]
  public sealed class CubusTerrainMaterialLibraryBinder : MonoBehaviour
  {
    [Header("Library")]
    public CubusTerrainMaterialLibrary MaterialLibrary;

    [Header("Target")]
    public Material TargetMaterial;

    [Header("Generated Texture Arrays")]
    [SerializeField] private Texture2DArray albedoArray;
    [SerializeField] private Texture2DArray normalArray;
    [SerializeField] private Texture2DArray maskArray;

    private static readonly int AlbedoArrayId = Shader.PropertyToID("_TerrainAlbedoArray");
    private static readonly int NormalArrayId = Shader.PropertyToID("_TerrainNormalArray");
    private static readonly int MaskArrayId = Shader.PropertyToID("_TerrainMaskArray");
    private static readonly int MaterialCountId = Shader.PropertyToID("_TerrainMaterialCount");
    private static readonly int MaterialParamsId = Shader.PropertyToID("_TerrainMaterialParams");

    public void Apply()
    {
      if (MaterialLibrary == null || TargetMaterial == null)
      {
        return;
      }

      CubusTerrainTextureArrayBuilder.Build(
        MaterialLibrary,
        out albedoArray,
        out normalArray,
        out maskArray);

      TargetMaterial.SetTexture(AlbedoArrayId, albedoArray);
      TargetMaterial.SetTexture(NormalArrayId, normalArray);
      TargetMaterial.SetTexture(MaskArrayId, maskArray);
      TargetMaterial.SetInt(MaterialCountId, MaterialLibrary.MaterialCount);
      TargetMaterial.SetVectorArray(
        MaterialParamsId,
        BuildMaterialParams(MaterialLibrary));
    }

    private static Vector4[] BuildMaterialParams(CubusTerrainMaterialLibrary library)
    {
      int count = Mathf.Max(1, library.MaterialCount);
      Vector4[] result = new Vector4[count];

      for (int i = 0; i < count; i++)
      {
        CubusTerrainMaterial material = library.Materials[i];

        if (material == null)
        {
          result[i] = new Vector4(1.0f, 1.0f, 0.8f, 0.0f);
          continue;
        }

        result[i] = new Vector4(
          Mathf.Max(0.001f, material.Tiling),
          Mathf.Max(0.0f, material.NormalStrength),
          Mathf.Clamp01(material.RoughnessFallback),
          Mathf.Clamp01(material.HeightStrength));
      }

      return result;
    }

    private void OnEnable()
    {
      Apply();
    }

    private void OnValidate()
    {
      if (Application.isPlaying)
      {
        Apply();
      }
    }
  }
}