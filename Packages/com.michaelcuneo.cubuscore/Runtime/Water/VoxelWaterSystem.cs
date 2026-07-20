using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Water
{
  /// <summary>
  /// First-pass static voxel water renderer. Water is generated as a separate,
  /// collider-free surface layer and follows the streamer's desired X/Z chunks.
  /// Terrain density, terrain materials and terrain collision remain untouched.
  /// </summary>
  [DisallowMultipleComponent]
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldStreamer))]
  public sealed class VoxelWaterSystem : MonoBehaviour
  {
    private const float SurfaceInsetInVoxels = 0.06f;
    private const float DepthFadeInVoxels = 7.0f;

    [Header("Static Voxel Water")]
    [SerializeField] private bool generateWater = true;
    [SerializeField] private int seaLevel = 34;
    [SerializeField] private Material waterMaterial;
    [SerializeField] private bool buildAfterInitialTerrainReady = true;
    [SerializeField, Min(1)] private int chunkBuildsPerFrame = 1;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.5f;

    private readonly Dictionary<Vector2Int, WaterChunkView> activeChunks = new();
    private readonly Queue<Vector2Int> pendingBuilds = new();
    private readonly HashSet<Vector2Int> pendingBuildSet = new();
    private readonly HashSet<Vector2Int> desiredColumns = new();
    private readonly List<Vector2Int> staleColumns = new();

    private CubusWorld world;
    private WorldStreamer streamer;
    private Transform waterRoot;
    private Material runtimeMaterial;
    private WorldGenerationSnapshot waterSnapshot;
    private bool hasWaterSnapshot;
    private float nextRefreshTime;
    private int cachedSeaLevel;
    private float cachedVoxelSize;

    public bool GenerateWater
    {
      get => generateWater;
      set
      {
        if (generateWater == value)
        {
          return;
        }

        generateWater = value;
        ForceRebuild();
      }
    }

    public int SeaLevel
    {
      get => seaLevel;
      set
      {
        if (seaLevel == value)
        {
          return;
        }

        seaLevel = value;
        ForceRebuild();
      }
    }

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      streamer = GetComponent<WorldStreamer>();
      EnsureWaterRoot();
      cachedSeaLevel = seaLevel;
      cachedVoxelSize = GetVoxelSize();
      RefreshWaterSnapshot();
    }

    private void OnEnable()
    {
      nextRefreshTime = 0.0f;
    }

    private void Update()
    {
      if (world == null || streamer == null || world.Settings == null)
      {
        return;
      }

      if (!generateWater)
      {
        ClearAllChunks();
        return;
      }

      if (buildAfterInitialTerrainReady && !world.IsInitialTerrainReady)
      {
        return;
      }

      float voxelSize = GetVoxelSize();
      if (cachedSeaLevel != seaLevel || !Mathf.Approximately(cachedVoxelSize, voxelSize))
      {
        cachedSeaLevel = seaLevel;
        cachedVoxelSize = voxelSize;
        ForceRebuild();
      }

      if (Time.unscaledTime >= nextRefreshTime)
      {
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        RefreshDesiredColumns();
      }

      int buildCount = Mathf.Min(Mathf.Max(1, chunkBuildsPerFrame), pendingBuilds.Count);
      for (int i = 0; i < buildCount; i++)
      {
        Vector2Int column = pendingBuilds.Dequeue();
        pendingBuildSet.Remove(column);

        if (desiredColumns.Contains(column))
        {
          BuildOrUpdateChunk(column);
        }
      }
    }

    private void OnDisable()
    {
      ClearAllChunks();
    }

    private void OnDestroy()
    {
      ClearAllChunks();

      if (runtimeMaterial != null)
      {
        DestroyObject(runtimeMaterial);
        runtimeMaterial = null;
      }
    }

    [ContextMenu("Rebuild Voxel Water")]
    public void ForceRebuild()
    {
      ClearAllChunks();
      pendingBuilds.Clear();
      pendingBuildSet.Clear();
      RefreshWaterSnapshot();
      nextRefreshTime = 0.0f;
    }

    private void RefreshWaterSnapshot()
    {
      hasWaterSnapshot = world != null && world.Settings != null;
      if (hasWaterSnapshot)
      {
        waterSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      }
    }

    private void RefreshDesiredColumns()
    {
      desiredColumns.Clear();

      foreach (Vector3Int chunkCoord in streamer.DesiredChunkCoords)
      {
        desiredColumns.Add(new Vector2Int(chunkCoord.x, chunkCoord.z));
      }

      foreach (Vector2Int column in desiredColumns)
      {
        if (!activeChunks.ContainsKey(column) && pendingBuildSet.Add(column))
        {
          pendingBuilds.Enqueue(column);
        }
      }

      staleColumns.Clear();
      foreach (KeyValuePair<Vector2Int, WaterChunkView> pair in activeChunks)
      {
        if (!desiredColumns.Contains(pair.Key))
        {
          staleColumns.Add(pair.Key);
        }
      }

      for (int i = 0; i < staleColumns.Count; i++)
      {
        RemoveChunk(staleColumns[i]);
      }
    }

    private void BuildOrUpdateChunk(Vector2Int column)
    {
      EnsureWaterRoot();

      Mesh mesh = BuildWaterMesh(column);
      if (mesh == null || mesh.vertexCount == 0)
      {
        if (mesh != null)
        {
          DestroyObject(mesh);
        }

        RemoveChunk(column);
        return;
      }

      if (!activeChunks.TryGetValue(column, out WaterChunkView view) || view == null)
      {
        GameObject chunkObject = new GameObject($"Water {column.x}, {column.y}");
        chunkObject.layer = gameObject.layer;
        chunkObject.transform.SetParent(waterRoot, false);
        view = chunkObject.AddComponent<WaterChunkView>();
        activeChunks[column] = view;
      }

      float voxelSize = GetVoxelSize();
      view.transform.localPosition = new Vector3(
          column.x * VoxelConstants.ChunkSize * voxelSize,
          0.0f,
          column.y * VoxelConstants.ChunkSize * voxelSize);
      view.Apply(mesh, ResolveWaterMaterial());
    }

    private Mesh BuildWaterMesh(Vector2Int column)
    {
      const int size = VoxelConstants.ChunkSize;
      float[,] waterDepths = new float[size + 2, size + 2];
      if (!hasWaterSnapshot)
      {
        RefreshWaterSnapshot();
        if (!hasWaterSnapshot)
        {
          return null;
        }
      }

      TerrainColumnSampler columnSampler = new();
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);
      int originX = column.x * size;
      int originZ = column.y * size;

      bool hasWater = false;
      for (int z = -1; z <= size; z++)
      {
        for (int x = -1; x <= size; x++)
        {
          columnSampler.Prepare(
              waterSnapshot,
              originX + x,
              originZ + z,
              densityScale);

          float waterDepth = seaLevel - SurfaceInsetInVoxels - columnSampler.SurfaceHeight;
          waterDepths[x + 1, z + 1] = waterDepth;
          hasWater |= x >= 0 && x < size && z >= 0 && z < size && waterDepth > 0.0f;
        }
      }

      if (!hasWater)
      {
        return null;
      }

      List<Vector3> vertices = new();
      List<Vector3> normals = new();
      List<Vector2> uvs = new();
      List<Vector2> waterData = new();
      List<int> triangles = new();
      float voxelSize = GetVoxelSize();
      float y = (seaLevel - SurfaceInsetInVoxels) * voxelSize;

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          float waterDepth = waterDepths[x + 1, z + 1];
          if (waterDepth <= 0.0f)
          {
            continue;
          }

          float normalizedDepth = Mathf.Clamp01(waterDepth / DepthFadeInVoxels);
          float shore = ComputeShoreMask(waterDepths, x + 1, z + 1, waterDepth);
          AddTopQuad(vertices, normals, uvs, waterData, triangles, x, z, y, voxelSize, normalizedDepth, shore);
        }
      }

      Mesh mesh = new Mesh
      {
        name = $"Voxel Water {column.x}, {column.y}",
        indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
      };

      mesh.SetVertices(vertices);
      mesh.SetNormals(normals);
      mesh.SetUVs(0, uvs);
      mesh.SetUVs(1, waterData);
      mesh.SetTriangles(triangles, 0, true);
      mesh.RecalculateBounds();
      Bounds bounds = mesh.bounds;
      bounds.Expand(new Vector3(0.0f, voxelSize * 0.25f, 0.0f));
      mesh.bounds = bounds;
      return mesh;
    }

    private static float ComputeShoreMask(float[,] waterDepths, int x, int z, float waterDepth)
    {
      float dryNeighborCount = 0.0f;
      dryNeighborCount += waterDepths[x - 1, z] <= 0.0f ? 1.0f : 0.0f;
      dryNeighborCount += waterDepths[x + 1, z] <= 0.0f ? 1.0f : 0.0f;
      dryNeighborCount += waterDepths[x, z - 1] <= 0.0f ? 1.0f : 0.0f;
      dryNeighborCount += waterDepths[x, z + 1] <= 0.0f ? 1.0f : 0.0f;

      float shallow = 1.0f - Mathf.Clamp01(waterDepth / 2.5f);
      return Mathf.Clamp01(dryNeighborCount * 0.25f + shallow * 0.7f);
    }

    private static void AddTopQuad(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Vector2> waterData,
        List<int> triangles,
        int x,
        int z,
        float y,
        float voxelSize,
        float normalizedDepth,
        float shore)
    {
      int start = vertices.Count;
      float x0 = x * voxelSize;
      float x1 = (x + 1) * voxelSize;
      float z0 = z * voxelSize;
      float z1 = (z + 1) * voxelSize;

      vertices.Add(new Vector3(x0, y, z0));
      vertices.Add(new Vector3(x0, y, z1));
      vertices.Add(new Vector3(x1, y, z1));
      vertices.Add(new Vector3(x1, y, z0));

      normals.Add(Vector3.up);
      normals.Add(Vector3.up);
      normals.Add(Vector3.up);
      normals.Add(Vector3.up);

      uvs.Add(new Vector2(x, z));
      uvs.Add(new Vector2(x, z + 1));
      uvs.Add(new Vector2(x + 1, z + 1));
      uvs.Add(new Vector2(x + 1, z));

      Vector2 data = new(normalizedDepth, shore);
      waterData.Add(data);
      waterData.Add(data);
      waterData.Add(data);
      waterData.Add(data);

      triangles.Add(start + 0);
      triangles.Add(start + 1);
      triangles.Add(start + 2);
      triangles.Add(start + 0);
      triangles.Add(start + 2);
      triangles.Add(start + 3);
    }

    private Material ResolveWaterMaterial()
    {
      if (waterMaterial != null)
      {
        ConfigureTransparentMaterial(waterMaterial);
        return waterMaterial;
      }

      if (runtimeMaterial != null)
      {
        return runtimeMaterial;
      }

      Shader shader = Shader.Find("Cubus/Voxel Water URP");
      if (shader == null)
      {
        shader = Shader.Find("Universal Render Pipeline/Lit");
      }

      runtimeMaterial = new Material(shader)
      {
        name = "Cubus Runtime Voxel Water"
      };

      ConfigureTransparentMaterial(runtimeMaterial);

      if (runtimeMaterial.HasProperty("_BaseColor"))
      {
        runtimeMaterial.SetColor("_BaseColor", new Color(0.10f, 0.50f, 0.58f, 0.38f));
      }

      if (runtimeMaterial.HasProperty("_DeepColor"))
      {
        runtimeMaterial.SetColor("_DeepColor", new Color(0.015f, 0.14f, 0.20f, 0.58f));
      }

      if (runtimeMaterial.HasProperty("_FoamColor"))
      {
        runtimeMaterial.SetColor("_FoamColor", new Color(0.78f, 0.92f, 0.86f, 0.55f));
      }

      return runtimeMaterial;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
      if (material == null)
      {
        return;
      }

      material.renderQueue = (int)RenderQueue.Transparent;
      material.SetOverrideTag("RenderType", "Transparent");
      material.SetOverrideTag("Queue", "Transparent");
    }

    private void EnsureWaterRoot()
    {
      if (waterRoot != null)
      {
        return;
      }

      Transform existing = transform.Find("Voxel Water");
      if (existing != null)
      {
        waterRoot = existing;
        return;
      }

      GameObject root = new GameObject("Voxel Water");
      root.layer = gameObject.layer;
      root.transform.SetParent(transform, false);
      waterRoot = root.transform;
    }

    private float GetVoxelSize()
    {
      return world != null && world.Settings != null
          ? Mathf.Max(0.0001f, world.Settings.VoxelSize)
          : VoxelConstants.DefaultVoxelSize;
    }

    private void RemoveChunk(Vector2Int column)
    {
      if (!activeChunks.TryGetValue(column, out WaterChunkView view))
      {
        return;
      }

      activeChunks.Remove(column);
      if (view != null)
      {
        DestroyObject(view.gameObject);
      }
    }

    private void ClearAllChunks()
    {
      foreach (KeyValuePair<Vector2Int, WaterChunkView> pair in activeChunks)
      {
        if (pair.Value != null)
        {
          DestroyObject(pair.Value.gameObject);
        }
      }

      activeChunks.Clear();
      desiredColumns.Clear();
      staleColumns.Clear();
      pendingBuilds.Clear();
      pendingBuildSet.Clear();
    }

    private static void DestroyObject(Object value)
    {
      if (value == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Object.Destroy(value);
      }
      else
      {
        Object.DestroyImmediate(value);
      }
    }
  }

  [DisallowMultipleComponent]
  internal sealed class WaterChunkView : MonoBehaviour
  {
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh currentMesh;

    public void Apply(Mesh mesh, Material material)
    {
      EnsureComponents();

      Mesh oldMesh = currentMesh;
      currentMesh = mesh;
      meshFilter.sharedMesh = currentMesh;
      meshRenderer.sharedMaterial = material;
      meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
      meshRenderer.receiveShadows = false;
      meshRenderer.enabled = currentMesh != null;

      if (oldMesh != null && oldMesh != currentMesh)
      {
        DestroyMesh(oldMesh);
      }
    }

    private void OnDestroy()
    {
      if (currentMesh != null)
      {
        DestroyMesh(currentMesh);
        currentMesh = null;
      }
    }

    private void EnsureComponents()
    {
      meshFilter ??= GetComponent<MeshFilter>();
      if (meshFilter == null)
      {
        meshFilter = gameObject.AddComponent<MeshFilter>();
      }

      meshRenderer ??= GetComponent<MeshRenderer>();
      if (meshRenderer == null)
      {
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
      }
    }

    private static void DestroyMesh(Mesh mesh)
    {
      if (mesh == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Object.Destroy(mesh);
      }
      else
      {
        Object.DestroyImmediate(mesh);
      }
    }
  }
}
