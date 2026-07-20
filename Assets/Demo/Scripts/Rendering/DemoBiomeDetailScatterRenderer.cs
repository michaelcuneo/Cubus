using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Rendering
{
  [DisallowMultipleComponent]
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  public sealed class DemoBiomeDetailScatterRenderer : MonoBehaviour
  {
    private const string DefaultSettingsResourcePath = "Demo Biome Detail Scatter Settings";
    private const string FoliageShaderName = "Cubus/FoliageURP";

    [SerializeField] private bool enableFoliage = true;
    [SerializeField] private DemoBiomeDetailScatterSettings settings;
    [SerializeField][Range(0f, 3f)] private float densityMultiplier = 1f;
    [SerializeField] private bool castShadows = true;

    [Header("Appearance")]
    [SerializeField] private Color tint = Color.white;
    [SerializeField][Range(0f, 1f)] private float alphaCutoff = 0.4f;
    [SerializeField][Range(0f, 2f)] private float ambientBoost = 1f;

    [Header("Wind")]
    [SerializeField][Range(0f, 1f)] private float windStrength = 0.12f;
    [SerializeField][Range(0f, 10f)] private float windSpeed = 1.5f;
    [SerializeField][Range(0f, 2f)] private float windFrequency = 0.15f;
    [SerializeField] private Vector2 windDirection = new(1f, 0.35f);

    private readonly Dictionary<Vector3Int, DetailView> activeViews = new();
    private readonly Stack<DetailView> pool = new();
    private readonly Dictionary<ulong, DemoDetailScatterProfile> generatedProfiles = new();

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private Material runtimeMaterial;
    private Transform root;

    private sealed class DetailView
    {
      public GameObject Go;
      public MeshFilter Filter;
      public MeshRenderer Renderer;
      public Mesh Mesh;
    }

    private void Awake()
    {
      ResolveReferences();
      LoadDefaultSettingsIfNeeded();
    }

    private void OnEnable()
    {
      ResolveReferences();
      LoadDefaultSettingsIfNeeded();

      if (worldRenderer != null)
      {
        worldRenderer.BlockChunkRendered -= RefreshChunkDetail;
        worldRenderer.BlockChunkRemoved -= RemoveChunkDetail;
        worldRenderer.ChunksCleared -= ClearAllDetail;
        worldRenderer.BlockChunkRendered += RefreshChunkDetail;
        worldRenderer.BlockChunkRemoved += RemoveChunkDetail;
        worldRenderer.ChunksCleared += ClearAllDetail;
      }

      RefreshActiveBlockChunks();
    }

    private void OnDisable()
    {
      if (worldRenderer != null)
      {
        worldRenderer.BlockChunkRendered -= RefreshChunkDetail;
        worldRenderer.BlockChunkRemoved -= RemoveChunkDetail;
        worldRenderer.ChunksCleared -= ClearAllDetail;
      }
    }

    private void OnValidate()
    {
      if (runtimeMaterial != null)
      {
        ApplyMaterialProperties(runtimeMaterial);
      }
    }

    private void ResolveReferences()
    {
      world ??= GetComponent<CubusWorld>();
      worldRenderer ??= GetComponent<WorldRenderer>();
      root ??= transform;
    }

    private void LoadDefaultSettingsIfNeeded()
    {
      if (settings == null)
      {
        settings = Resources.Load<DemoBiomeDetailScatterSettings>(DefaultSettingsResourcePath);
      }
    }

    private bool CanRender()
    {
      return enableFoliage && world != null && world.Settings != null && settings != null && settings.FoliageAtlas != null;
    }

    private void RefreshActiveBlockChunks()
    {
      if (!CanRender() || worldRenderer == null || world == null)
      {
        return;
      }

      foreach (Vector3Int chunkCoord in worldRenderer.ActiveChunkViews.Keys)
      {
        if (world.Data.BlockChunks.ContainsKey(chunkCoord))
        {
          RefreshChunkDetail(chunkCoord);
        }
      }
    }

    private void RefreshChunkDetail(Vector3Int chunkCoord)
    {
      if (!CanRender())
      {
        RemoveChunkDetail(chunkCoord);
        return;
      }

      if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData) || chunkData == null)
      {
        RemoveChunkDetail(chunkCoord);
        return;
      }

      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      activeViews.TryGetValue(chunkCoord, out DetailView existing);
      DetailView view = existing ?? RentView();

      Mesh mesh = DemoBiomeDetailMeshBuilder.Build(
          chunkData,
          world.GetBlockMaterialAtWorldVoxel,
          ResolveScatterProfile,
          Mathf.Max(1, settings.AtlasColumns),
          Mathf.Max(1, settings.AtlasRows),
          voxelSize,
          densityMultiplier,
          view.Mesh);

      if (mesh == null)
      {
        if (existing != null)
        {
          activeViews.Remove(chunkCoord);
        }

        ReleaseView(view);
        return;
      }

      view.Mesh = mesh;
      view.Filter.sharedMesh = mesh;
      view.Renderer.sharedMaterial = EnsureMaterial();
      view.Renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
      view.Renderer.enabled = true;

      view.Go.transform.localPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      view.Go.transform.localRotation = Quaternion.identity;
      view.Go.transform.localScale = Vector3.one;
      view.Go.name = $"Biome Detail {chunkCoord.x}, {chunkCoord.y}, {chunkCoord.z}";

      activeViews[chunkCoord] = view;
    }

    private DemoDetailScatterProfile ResolveScatterProfile(Vector3Int worldVoxel, ushort materialId)
    {
      if (materialId == 0 || world == null || world.Settings == null)
      {
        return null;
      }

      world.Settings.ResolveBiomeAtWorldXZ(worldVoxel.x, worldVoxel.z, out _, out byte biomeId);

      DemoDetailScatterProfile authored = settings != null ? settings.GetAuthoredProfile(biomeId, materialId) : null;
      if (authored != null)
      {
        return authored;
      }

      ulong key = ((ulong)biomeId << 32) | materialId;
      if (generatedProfiles.TryGetValue(key, out DemoDetailScatterProfile generated))
      {
        return generated;
      }

      generated = CreateDefaultProfileForBiomeMaterial(biomeId, materialId);
      generatedProfiles[key] = generated;
      return generated;
    }

    private DemoDetailScatterProfile CreateDefaultProfileForBiomeMaterial(byte biomeId, ushort materialId)
    {
      BiomeDefinition biome = FindBiomeDefinition(biomeId);
      string biomeName = biome != null ? biome.BiomeName : $"Biome {biomeId}";
      string label = FindMaterialLabel(biome, materialId);
      string lowerBiome = biomeName.ToLowerInvariant();
      string lowerLabel = label.ToLowerInvariant();

      DemoDetailScatterProfile profile = new()
      {
        DisplayName = $"{biomeName} {label}",
        MaterialId = materialId,
        Coverage = 0.16f,
        MaxClustersPerVoxel = 1,
        MinHeight = 0.25f,
        MaxHeight = 0.45f,
        MinWidth = 0.35f,
        MaxWidth = 0.65f,
        AtlasTiles = new[] { 10 },
        TintVariation = 0.10f,
        SwayStrength = 0.0f,
      };

      if (ContainsAny(lowerLabel, "grass", "surface") && !lowerBiome.Contains("snow"))
      {
        profile.AtlasTiles = lowerBiome.Contains("brown") || lowerBiome.Contains("desert")
            ? new[] { 3, 7, 3, 2 }
            : new[] { 0, 0, 0, 1, 1, 2, 4, 5, 12, 13 };
        profile.Coverage = lowerBiome.Contains("rocky") ? 0.28f : 0.55f;
        profile.MaxClustersPerVoxel = 3;
        profile.MinHeight = 0.45f;
        profile.MaxHeight = 1.0f;
        profile.MinWidth = 0.5f;
        profile.MaxWidth = 0.9f;
        profile.SwayStrength = 1.0f;
      }
      else if (ContainsAny(lowerLabel, "dirt", "topsoil", "clay"))
      {
        profile.AtlasTiles = new[] { 11, 2, 0, 4, 5, 12, 13 };
        profile.Coverage = 0.32f;
        profile.MaxClustersPerVoxel = 2;
        profile.MinHeight = 0.4f;
        profile.MaxHeight = 0.85f;
        profile.MinWidth = 0.45f;
        profile.MaxWidth = 0.8f;
        profile.SwayStrength = 0.85f;
      }
      else if (ContainsAny(lowerLabel, "sand", "basin"))
      {
        profile.AtlasTiles = new[] { 8, 10, 3, 8, 9, 10 };
        profile.Coverage = 0.24f;
        profile.MaxClustersPerVoxel = 2;
        profile.MinHeight = 0.25f;
        profile.MaxHeight = 0.65f;
        profile.MinWidth = 0.35f;
        profile.MaxWidth = 0.75f;
        profile.SwayStrength = 0.45f;
      }
      else if (ContainsAny(lowerLabel, "snow", "frozen"))
      {
        profile.AtlasTiles = new[] { 10, 14, 3 };
        profile.Coverage = lowerLabel.Contains("snow") ? 0.08f : 0.14f;
        profile.MaxClustersPerVoxel = 1;
        profile.MinHeight = 0.22f;
        profile.MaxHeight = 0.48f;
        profile.MinWidth = 0.3f;
        profile.MaxWidth = 0.65f;
        profile.Tint = new Color(0.88f, 0.94f, 1.0f, 1.0f);
        profile.SwayStrength = 0.25f;
      }
      else if (ContainsAny(lowerLabel, "gravel", "rock", "stone", "granite", "limestone", "bedrock"))
      {
        profile.AtlasTiles = ContainsAny(lowerBiome, "green", "valley")
            ? new[] { 10, 10, 0, 2 }
            : new[] { 10, 10, 3 };
        profile.Coverage = ContainsAny(lowerBiome, "rocky", "mountain") ? 0.18f : 0.10f;
        profile.MaxClustersPerVoxel = 1;
        profile.MinHeight = 0.22f;
        profile.MaxHeight = 0.45f;
        profile.MinWidth = 0.35f;
        profile.MaxWidth = 0.7f;
        profile.SwayStrength = ContainsAny(lowerBiome, "green", "valley") ? 0.45f : 0.0f;
      }

      return profile;
    }

    private BiomeDefinition FindBiomeDefinition(byte biomeId)
    {
      if (world == null || world.Settings == null || world.Settings.BiomeWorldRules == null)
      {
        return null;
      }

      for (int i = 0; i < world.Settings.BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = world.Settings.BiomeWorldRules[i];
        if (rule != null && rule.Biome != null && Mathf.Clamp(rule.Biome.BiomeId, 0, 255) == biomeId)
        {
          return rule.Biome;
        }
      }

      return null;
    }

    private static string FindMaterialLabel(BiomeDefinition biome, ushort materialId)
    {
      if (biome != null && biome.MaterialSet != null && biome.MaterialSet.MaterialLayers != null)
      {
        for (int i = 0; i < biome.MaterialSet.MaterialLayers.Count; i++)
        {
          MaterialLayer layer = biome.MaterialSet.MaterialLayers[i];
          if (layer != null && Mathf.Clamp(layer.MaterialId, 1, 65535) == materialId)
          {
            return string.IsNullOrWhiteSpace(layer.Label) ? $"Material {materialId}" : layer.Label;
          }
        }
      }

      return $"Material {materialId}";
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
      for (int i = 0; i < needles.Length; i++)
      {
        if (value.Contains(needles[i]))
        {
          return true;
        }
      }

      return false;
    }

    private DetailView RentView()
    {
      if (pool.Count > 0)
      {
        DetailView pooled = pool.Pop();
        pooled.Go.SetActive(true);
        return pooled;
      }

      GameObject go = new("Biome Detail");
      go.transform.SetParent(root, false);
      go.layer = gameObject.layer;

      DetailView view = new()
      {
        Go = go,
        Filter = go.AddComponent<MeshFilter>(),
        Renderer = go.AddComponent<MeshRenderer>(),
      };

      view.Renderer.shadowCastingMode = ShadowCastingMode.On;
      view.Renderer.receiveShadows = true;
      view.Renderer.enabled = false;
      return view;
    }

    private void ReleaseView(DetailView view)
    {
      if (view == null)
      {
        return;
      }

      if (view.Renderer != null)
      {
        view.Renderer.enabled = false;
      }

      if (view.Filter != null)
      {
        view.Filter.sharedMesh = null;
      }

      if (view.Go != null)
      {
        view.Go.SetActive(false);
        pool.Push(view);
      }
    }

    private void RemoveChunkDetail(Vector3Int chunkCoord)
    {
      if (!activeViews.TryGetValue(chunkCoord, out DetailView view))
      {
        return;
      }

      activeViews.Remove(chunkCoord);
      ReleaseView(view);
    }

    private void ClearAllDetail()
    {
      foreach (KeyValuePair<Vector3Int, DetailView> pair in activeViews)
      {
        ReleaseView(pair.Value);
      }

      activeViews.Clear();
    }

    private Material EnsureMaterial()
    {
      if (runtimeMaterial != null)
      {
        return runtimeMaterial;
      }

      Shader shader = Shader.Find(FoliageShaderName);
      if (shader == null)
      {
        Debug.LogError($"DemoBiomeDetailScatterRenderer: foliage shader '{FoliageShaderName}' not found.");
        shader = Shader.Find("Universal Render Pipeline/Lit");
      }

      runtimeMaterial = new Material(shader) { name = "Demo Biome Detail Scatter (Runtime)" };
      ApplyMaterialProperties(runtimeMaterial);
      return runtimeMaterial;
    }

    private void ApplyMaterialProperties(Material material)
    {
      if (material == null)
      {
        return;
      }

      if (settings != null && settings.FoliageAtlas != null && material.HasProperty("_FoliageAtlas"))
      {
        material.SetTexture("_FoliageAtlas", settings.FoliageAtlas);
      }

      if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
      if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", alphaCutoff);
      if (material.HasProperty("_AmbientBoost")) material.SetFloat("_AmbientBoost", ambientBoost);
      if (material.HasProperty("_WindStrength")) material.SetFloat("_WindStrength", windStrength);
      if (material.HasProperty("_WindSpeed")) material.SetFloat("_WindSpeed", windSpeed);
      if (material.HasProperty("_WindFrequency")) material.SetFloat("_WindFrequency", windFrequency);
      if (material.HasProperty("_WindDirX")) material.SetFloat("_WindDirX", windDirection.x);
      if (material.HasProperty("_WindDirZ")) material.SetFloat("_WindDirZ", windDirection.y);
    }

    private void OnDestroy()
    {
      ClearAllDetail();

      foreach (DetailView view in pool)
      {
        DestroyViewMesh(view);
      }

      pool.Clear();

      if (runtimeMaterial != null)
      {
        if (Application.isPlaying) Destroy(runtimeMaterial);
        else DestroyImmediate(runtimeMaterial);
        runtimeMaterial = null;
      }
    }

    private static void DestroyViewMesh(DetailView view)
    {
      if (view == null || view.Mesh == null)
      {
        return;
      }

      if (Application.isPlaying) Destroy(view.Mesh);
      else DestroyImmediate(view.Mesh);
      view.Mesh = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterInitialSceneLoad()
    {
      InstallInLoadedScenes();
      SceneManager.sceneLoaded -= OnSceneLoaded;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
      InstallInLoadedScenes();
    }

    private static void InstallInLoadedScenes()
    {
      WorldRenderer[] renderers = FindObjectsByType<WorldRenderer>(FindObjectsInactive.Include);
      for (int i = 0; i < renderers.Length; i++)
      {
        WorldRenderer renderer = renderers[i];
        if (renderer != null && renderer.GetComponent<DemoBiomeDetailScatterRenderer>() == null)
        {
          renderer.gameObject.AddComponent<DemoBiomeDetailScatterRenderer>();
        }
      }
    }
  }

  internal static class DemoBiomeDetailMeshBuilder
  {
    private const int Size = VoxelConstants.ChunkSize;
    private const int MaxVertices = 60000;

    private static readonly List<Vector3> Positions = new(8192);
    private static readonly List<Vector3> Normals = new(8192);
    private static readonly List<Vector2> Uvs = new(8192);
    private static readonly List<Vector2> Uv2 = new(8192);
    private static readonly List<Color32> Colors = new(8192);
    private static readonly List<int> Triangles = new(12288);

    public static Mesh Build(
        BlockChunkData chunkData,
        Func<Vector3Int, ushort> getMaterialAtWorldVoxel,
        Func<Vector3Int, ushort, DemoDetailScatterProfile> resolveProfile,
        int atlasColumns,
        int atlasRows,
        float voxelSize,
        float densityMultiplier,
        Mesh reuseMesh)
    {
      if (chunkData == null || resolveProfile == null)
      {
        return null;
      }

      Positions.Clear();
      Normals.Clear();
      Uvs.Clear();
      Uv2.Clear();
      Colors.Clear();
      Triangles.Clear();

      int cols = Mathf.Max(1, atlasColumns);
      int rows = Mathf.Max(1, atlasRows);
      float insetU = 0.5f / (cols * 256f);
      float insetV = 0.5f / (rows * 256f);

      for (int x = 0; x < Size; x++)
      {
        for (int z = 0; z < Size; z++)
        {
          for (int y = 0; y < Size; y++)
          {
            Voxel voxel = chunkData.GetVoxel(x, y, z);
            if (!voxel.IsSolid)
            {
              continue;
            }

            bool aboveSolid = y < Size - 1
                ? chunkData.GetVoxel(x, y + 1, z).IsSolid
                : getMaterialAtWorldVoxel(chunkData.LocalToWorldVoxel(x, y + 1, z)) != 0;

            if (aboveSolid)
            {
              continue;
            }

            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);
            DemoDetailScatterProfile profile = resolveProfile(worldVoxel, voxel.MaterialId);
            if (profile == null || profile.AtlasTiles == null || profile.AtlasTiles.Length == 0)
            {
              continue;
            }

            uint state = Hash(worldVoxel.x, worldVoxel.y, worldVoxel.z);
            if (Rand01(ref state) > Mathf.Clamp01(profile.Coverage))
            {
              continue;
            }

            int maxClusters = Mathf.Max(0, Mathf.RoundToInt(profile.MaxClustersPerVoxel * Mathf.Max(0f, densityMultiplier)));
            if (maxClusters <= 0)
            {
              continue;
            }

            int clusters = Mathf.Clamp(1 + Mathf.FloorToInt(Rand01(ref state) * maxClusters), 1, maxClusters);
            float baseX = x * voxelSize;
            float baseZ = z * voxelSize;
            float topY = (y + 1) * voxelSize;

            for (int cluster = 0; cluster < clusters; cluster++)
            {
              if (Positions.Count >= MaxVertices)
              {
                break;
              }

              float jx = baseX + (0.15f + Rand01(ref state) * 0.7f) * voxelSize;
              float jz = baseZ + (0.15f + Rand01(ref state) * 0.7f) * voxelSize;
              Vector3 basePos = new(jx, topY - 0.02f * voxelSize, jz);

              float height = Mathf.Lerp(profile.MinHeight, profile.MaxHeight, Rand01(ref state)) * voxelSize;
              float width = Mathf.Lerp(profile.MinWidth, profile.MaxWidth, Rand01(ref state)) * voxelSize;
              float yaw = Rand01(ref state) * Mathf.PI;
              float phase = Rand01(ref state);
              int tile = profile.AtlasTiles[Mathf.Min(profile.AtlasTiles.Length - 1,
                  Mathf.FloorToInt(Rand01(ref state) * profile.AtlasTiles.Length))];

              Color32 clusterTint = JitterTint(profile.Tint, profile.TintVariation, ref state);
              float swayTop = Mathf.Clamp01(profile.SwayStrength * 0.5f) * 2f;

              ComputeTileUv(tile, cols, rows, insetU, insetV, out float u0, out float u1, out float vBottom, out float vTop);
              Vector3 dirA = new(Mathf.Cos(yaw) * width * 0.5f, 0f, Mathf.Sin(yaw) * width * 0.5f);
              Vector3 dirB = new(-Mathf.Sin(yaw) * width * 0.5f, 0f, Mathf.Cos(yaw) * width * 0.5f);

              AddQuad(basePos, dirA, height, u0, u1, vBottom, vTop, swayTop, phase, clusterTint);
              AddQuad(basePos, dirB, height, u0, u1, vBottom, vTop, swayTop, phase, clusterTint);
            }
          }
        }
      }

      if (Positions.Count == 0)
      {
        return null;
      }

      Mesh mesh = reuseMesh;
      if (mesh == null)
      {
        mesh = new Mesh { name = "Demo Biome Detail" };
      }
      else
      {
        mesh.Clear();
      }

      mesh.indexFormat = Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
      mesh.SetVertices(Positions);
      mesh.SetNormals(Normals);
      mesh.SetUVs(0, Uvs);
      mesh.SetUVs(1, Uv2);
      mesh.SetColors(Colors);
      mesh.SetTriangles(Triangles, 0, true);
      mesh.bounds = new Bounds(
          new Vector3(Size * voxelSize * 0.5f, Size * voxelSize * 0.5f, Size * voxelSize * 0.5f),
          new Vector3(Size * voxelSize, Size * voxelSize + 4f, Size * voxelSize));

      return mesh;
    }

    private static void AddQuad(Vector3 basePos, Vector3 dir, float height, float u0, float u1, float vBottom, float vTop, float swayTop, float phase, Color32 tint)
    {
      int i0 = Positions.Count;
      Vector3 up = new(0f, height, 0f);
      Vector3 bl = basePos - dir;
      Vector3 br = basePos + dir;
      Vector3 tr = br + up;
      Vector3 tl = bl + up;
      Color32 baseCol = Scale(tint, 0.72f);

      Positions.Add(bl); Positions.Add(br); Positions.Add(tr); Positions.Add(tl);
      Normals.Add(Vector3.up); Normals.Add(Vector3.up); Normals.Add(Vector3.up); Normals.Add(Vector3.up);
      Uvs.Add(new Vector2(u0, vBottom)); Uvs.Add(new Vector2(u1, vBottom)); Uvs.Add(new Vector2(u1, vTop)); Uvs.Add(new Vector2(u0, vTop));
      Uv2.Add(new Vector2(0f, phase)); Uv2.Add(new Vector2(0f, phase)); Uv2.Add(new Vector2(swayTop, phase)); Uv2.Add(new Vector2(swayTop, phase));
      Colors.Add(baseCol); Colors.Add(baseCol); Colors.Add(tint); Colors.Add(tint);
      Triangles.Add(i0); Triangles.Add(i0 + 1); Triangles.Add(i0 + 2);
      Triangles.Add(i0); Triangles.Add(i0 + 2); Triangles.Add(i0 + 3);
    }

    private static void ComputeTileUv(int tile, int cols, int rows, float insetU, float insetV, out float u0, out float u1, out float vBottom, out float vTop)
    {
      tile = Mathf.Clamp(tile, 0, cols * rows - 1);
      int col = tile % cols;
      int row = tile / cols;
      u0 = (float)col / cols + insetU;
      u1 = (float)(col + 1) / cols - insetU;
      vTop = 1f - (float)row / rows - insetV;
      vBottom = 1f - (float)(row + 1) / rows + insetV;
    }

    private static Color32 JitterTint(Color tint, float variation, ref uint state)
    {
      if (variation <= 0f)
      {
        return tint;
      }

      float brightness = 1f - variation * 0.5f + Rand01(ref state) * variation;
      return new Color(
          Mathf.Clamp01(tint.r * brightness),
          Mathf.Clamp01(tint.g * brightness),
          Mathf.Clamp01(tint.b * brightness),
          tint.a);
    }

    private static Color32 Scale(Color32 color, float factor)
    {
      return new Color32((byte)(color.r * factor), (byte)(color.g * factor), (byte)(color.b * factor), color.a);
    }

    private static uint Hash(int x, int y, int z)
    {
      unchecked
      {
        uint hash = 2166136261u;
        hash = (hash ^ (uint)x) * 16777619u;
        hash = (hash ^ (uint)y) * 16777619u;
        hash = (hash ^ (uint)z) * 16777619u;
        hash ^= hash >> 15;
        hash *= 0x2c1b3c6du;
        hash ^= hash >> 12;
        hash *= 0x297a2d39u;
        hash ^= hash >> 15;
        return hash == 0u ? 0x9e3779b9u : hash;
      }
    }

    private static float Rand01(ref uint state)
    {
      unchecked
      {
        state = state * 747796405u + 2891336453u;
        uint r = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
        r = (r >> 22) ^ r;
        return (r & 0xFFFFFFu) / 16777216f;
      }
    }
  }
}