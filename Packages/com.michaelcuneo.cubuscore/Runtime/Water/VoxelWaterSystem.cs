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
    private const float DefaultRefreshInterval = 0.25f;
    private const float SurfaceInsetInVoxels = 0.06f;

    [Header("Static Voxel Water")]
    [SerializeField] private bool generateWater = true;
    [SerializeField] private int seaLevel = 34;
    [SerializeField] private Material waterMaterial;
    [SerializeField, Min(1)] private int chunkBuildsPerFrame = 2;
    [SerializeField, Min(0.05f)] private float refreshInterval = DefaultRefreshInterval;

    private readonly Dictionary<Vector2Int, WaterChunkView> activeChunks = new();
    private readonly Queue<Vector2Int> pendingBuilds = new();
    private readonly HashSet<Vector2Int> pendingBuildSet = new();
    private readonly HashSet<Vector2Int> desiredColumns = new();
    private readonly List<Vector2Int> staleColumns = new();

    private CubusWorld world;
    private WorldStreamer streamer;
    private Transform waterRoot;
    private Material runtimeMaterial;
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
      nextRefreshTime = 0.0f;
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
      bool[,] wet = new bool[size, size];
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);
      int originX = column.x * size;
      int originZ = column.y * size;

      bool hasWater = false;
      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          TerrainSample sample = BiomeTerrainSampler.Sample(
              world.Settings,
              new Vector3Int(originX + x, seaLevel, originZ + z),
              densityScale);

          bool isWet = sample.SurfaceHeight < seaLevel - SurfaceInsetInVoxels;
          wet[x, z] = isWet;
          hasWater |= isWet;
        }
      }

      if (!hasWater)
      {
        return null;
      }

      bool[,] consumed = new bool[size, size];
      List<Vector3> vertices = new();
      List<Vector3> normals = new();
      List<Vector2> uvs = new();
      List<int> triangles = new();
      float voxelSize = GetVoxelSize();
      float y = (seaLevel - SurfaceInsetInVoxels) * voxelSize;

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          if (!wet[x, z] || consumed[x, z])
          {
            continue;
          }

          int width = 1;
          while (x + width < size && wet[x + width, z] && !consumed[x + width, z])
          {
            width++;
          }

          int depth = 1;
          bool canGrow = true;
          while (z + depth < size && canGrow)
          {
            for (int dx = 0; dx < width; dx++)
            {
              if (!wet[x + dx, z + depth] || consumed[x + dx, z + depth])
              {
                canGrow = false;
                break;
              }
            }

            if (canGrow)
            {
              depth++;
            }
          }

          for (int dz = 0; dz < depth; dz++)
          {
            for (int dx = 0; dx < width; dx++)
            {
              consumed[x + dx, z + dz] = true;
            }
          }

          AddTopQuad(vertices, normals, uvs, triangles, x, z, width, depth, y, voxelSize);
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
      mesh.SetTriangles(triangles, 0, true);
      mesh.RecalculateBounds();
      return mesh;
    }

    private static void AddTopQuad(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        int x,
        int z,
        int width,
        int depth,
        float y,
        float voxelSize)
    {
      int start = vertices.Count;
      float x0 = x * voxelSize;
      float x1 = (x + width) * voxelSize;
      float z0 = z * voxelSize;
      float z1 = (z + depth) * voxelSize;

      vertices.Add(new Vector3(x0, y, z0));
      vertices.Add(new Vector3(x0, y, z1));
      vertices.Add(new Vector3(x1, y, z1));
      vertices.Add(new Vector3(x1, y, z0));

      normals.Add(Vector3.up);
      normals.Add(Vector3.up);
      normals.Add(Vector3.up);
      normals.Add(Vector3.up);

      uvs.Add(new Vector2(x, z));
      uvs.Add(new Vector2(x, z + depth));
      uvs.Add(new Vector2(x + width, z + depth));
      uvs.Add(new Vector2(x + width, z));

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

      if (runtimeMaterial.HasProperty("_BaseColor"))
      {
        runtimeMaterial.SetColor("_BaseColor", new Color(0.055f, 0.32f, 0.48f, 0.72f));
      }

      return runtimeMaterial;
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
