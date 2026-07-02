using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials
{
  [DefaultExecutionOrder(1000)]
  [DisallowMultipleComponent]
  public sealed class CubusTerrainMaterialLibraryBinder : MonoBehaviour
  {
    private const int MaxMaterialParams = 128;
    private const int LookupTextureSize = 256;

    [Header("Library")]
    public CubusTerrainMaterialLibrary MaterialLibrary;

    [Header("Target")]
    public Material TargetMaterial;
    public WorldRenderer WorldRenderer;

    [Header("Generated Texture Arrays")]
    [SerializeField] private Texture2DArray albedoArray;
    [SerializeField] private Texture2DArray normalArray;
    [SerializeField] private Texture2DArray maskArray;

    [Header("Generated Lookups")]
    [SerializeField] private Texture2D materialSliceLookup;

    private static readonly int AlbedoArrayId = Shader.PropertyToID("_TerrainAlbedoArray");
    private static readonly int NormalArrayId = Shader.PropertyToID("_TerrainNormalArray");
    private static readonly int MaskArrayId = Shader.PropertyToID("_TerrainMaskArray");
    private static readonly int MaterialCountId = Shader.PropertyToID("_TerrainMaterialCount");
    private static readonly int MaterialParamsId = Shader.PropertyToID("_TerrainMaterialParams");
    private static readonly int MaterialSliceLookupId = Shader.PropertyToID("_TerrainMaterialSliceLookup");

    public void Apply()
    {
      ResolveDefaults();

      if (MaterialLibrary == null || TargetMaterial == null)
      {
        return;
      }

      CubusTerrainTextureArrayBuilder.Build(
        MaterialLibrary,
        out albedoArray,
        out normalArray,
        out maskArray);

      materialSliceLookup = BuildMaterialSliceLookup(MaterialLibrary);

      TargetMaterial.SetTexture(AlbedoArrayId, albedoArray);
      TargetMaterial.SetTexture(NormalArrayId, normalArray);
      TargetMaterial.SetTexture(MaskArrayId, maskArray);
      TargetMaterial.SetTexture(MaterialSliceLookupId, materialSliceLookup);
      TargetMaterial.SetInt(MaterialCountId, MaterialLibrary.MaterialCount);
      TargetMaterial.SetVectorArray(
        MaterialParamsId,
        BuildMaterialParams(MaterialLibrary));

      Debug.Log(
        $"Applied terrain material library. Materials={MaterialLibrary.MaterialCount}, Target={TargetMaterial.name}",
        this);
    }

    private void ResolveDefaults()
    {
      if (WorldRenderer == null)
      {
        WorldRenderer = GetComponent<WorldRenderer>();
      }

      if (MaterialLibrary == null && WorldRenderer != null)
      {
        CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World.CubusWorld world =
          WorldRenderer.GetComponent<CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World.CubusWorld>();

        if (world != null && world.Settings != null)
        {
          MaterialLibrary = world.Settings.TerrainMaterialLibrary;
        }
      }

      if (WorldRenderer != null)
      {
        TargetMaterial = WorldRenderer.EnsureWorldMaterialAndGet();
      }
    }

    private static Vector4[] BuildMaterialParams(CubusTerrainMaterialLibrary library)
    {
      Vector4[] result = new Vector4[MaxMaterialParams];

      for (int i = 0; i < MaxMaterialParams; i++)
      {
        result[i] = new Vector4(1.0f, 1.0f, 0.8f, 0.0f);
      }

      if (library == null || library.Materials == null)
      {
        return result;
      }

      int count = Mathf.Min(library.Materials.Count, MaxMaterialParams);

      for (int i = 0; i < count; i++)
      {
        CubusTerrainMaterial material = library.Materials[i];

        if (material == null)
        {
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

    private static Texture2D BuildMaterialSliceLookup(CubusTerrainMaterialLibrary library)
    {
      Texture2D lookup = new(
        LookupTextureSize,
        LookupTextureSize,
        TextureFormat.RGBA32,
        false,
        true)
      {
        name = "Cubus Terrain Material Slice Lookup",
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp
      };

      Color32[] pixels = new Color32[LookupTextureSize * LookupTextureSize];

      for (int i = 0; i < pixels.Length; i++)
      {
        pixels[i] = new Color32(0, 0, 0, 255);
      }

      if (library != null && library.Materials != null)
      {
        int count = Mathf.Min(library.Materials.Count, 65535);

        for (int slice = 0; slice < count; slice++)
        {
          CubusTerrainMaterial material = library.Materials[slice];

          if (material == null)
          {
            continue;
          }

          int materialId = Mathf.Clamp(material.MaterialId, 1, 65535);
          int encodedSlice = Mathf.Clamp(slice + 1, 1, 65535);

          pixels[materialId] = new Color32(
            (byte)(encodedSlice & 0xFF),
            (byte)((encodedSlice >> 8) & 0xFF),
            0,
            255);
        }
      }

      lookup.SetPixels32(pixels);
      lookup.Apply(false, false);
      return lookup;
    }

    private void OnEnable()
    {
      Apply();
    }

    private void Start()
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
