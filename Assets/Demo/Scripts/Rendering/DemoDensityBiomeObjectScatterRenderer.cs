using System.Collections.Generic;
using System.Reflection;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Rendering
{
  [DisallowMultipleComponent]
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  public sealed class DemoDensityBiomeObjectScatterRenderer : MonoBehaviour
  {
    private const string DefaultSettingsResourcePath = "Demo Density Object Scatter Settings";

    [SerializeField] private bool enableScatter = true;
    [SerializeField] private DemoDensityObjectScatterSettings settings;
    [SerializeField][Min(0.1f)] private float refreshInterval = 0.75f;

    private sealed class SpawnedObject
    {
      public GameObject Instance;
      public GameObject Prefab;
    }

    private readonly Dictionary<Vector3Int, List<SpawnedObject>> spawnedByChunk = new();
    private readonly Dictionary<GameObject, Stack<GameObject>> poolsByPrefab = new();
    private readonly Stack<GameObject> fallbackPool = new();

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private FieldInfo densityViewsField;
    private float nextRefreshAt;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      worldRenderer = GetComponent<WorldRenderer>();
      densityViewsField = typeof(WorldRenderer).GetField(
        "activeDensityChunkViews",
        BindingFlags.Instance | BindingFlags.NonPublic);

      if (settings == null)
      {
        settings = Resources.Load<DemoDensityObjectScatterSettings>(DefaultSettingsResourcePath);
      }
    }

    private void Update()
    {
      if (!enableScatter || Time.unscaledTime < nextRefreshAt)
      {
        return;
      }

      nextRefreshAt = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
      RefreshScatter();
    }

    private void RefreshScatter()
    {
      Dictionary<Vector3Int, ChunkView> densityViews = GetDensityViews();
      if (densityViews == null)
      {
        return;
      }

      List<Vector3Int> removed = null;
      foreach (Vector3Int coord in spawnedByChunk.Keys)
      {
        if (!densityViews.ContainsKey(coord))
        {
          removed ??= new List<Vector3Int>();
          removed.Add(coord);
        }
      }

      if (removed != null)
      {
        for (int i = 0; i < removed.Count; i++) ReleaseChunk(removed[i]);
      }

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in densityViews)
      {
        if (!spawnedByChunk.ContainsKey(pair.Key)) BuildChunk(pair.Key, pair.Value);
      }
    }

    private Dictionary<Vector3Int, ChunkView> GetDensityViews()
    {
      if (worldRenderer == null || densityViewsField == null)
      {
        return null;
      }

      return densityViewsField.GetValue(worldRenderer) as Dictionary<Vector3Int, ChunkView>;
    }

    private void BuildChunk(Vector3Int chunkCoord, ChunkView chunkView)
    {
      Mesh mesh = chunkView != null ? chunkView.CurrentMesh : null;
      if (mesh == null || mesh.vertexCount == 0 || world == null || world.Settings == null)
      {
        return;
      }

      Vector3[] vertices = mesh.vertices;
      Vector3[] normals = mesh.normals;
      int[] triangles = mesh.triangles;
      List<SpawnedObject> spawned = new();
      Dictionary<DemoDensityObjectScatterGroup, List<Vector3>> acceptedByGroup = new();

      for (int index = 0; index + 2 < triangles.Length; index += 3)
      {
        int ia = triangles[index];
        int ib = triangles[index + 1];
        int ic = triangles[index + 2];

        Vector3 normal = normals != null && normals.Length == vertices.Length
          ? (normals[ia] + normals[ib] + normals[ic]).normalized
          : Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]).normalized;

        Vector3 centroid = (vertices[ia] + vertices[ib] + vertices[ic]) / 3f;
        Vector3 centroidWorld = chunkView.transform.TransformPoint(centroid);
        byte biomeId = ResolveBiomeId(centroidWorld);
        IReadOnlyList<DemoDensityObjectScatterGroup> groups = settings != null
          ? settings.GetGroups(biomeId)
          : null;

        if (groups == null) continue;

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
          DemoDensityObjectScatterGroup group = groups[groupIndex];
          if (!CanUseGroup(group, normal, centroidWorld.y)) continue;

          int stride = Mathf.Max(1, group.TriangleStride) * 3;
          if (index % stride != 0) continue;

          uint hash = Hash(chunkCoord, index, groupIndex);
          if (ToUnit(hash) > group.SpawnChance) continue;

          if (!acceptedByGroup.TryGetValue(group, out List<Vector3> accepted))
          {
            accepted = new List<Vector3>();
            acceptedByGroup[group] = accepted;
          }

          if (accepted.Count >= Mathf.Max(0, group.MaximumPerChunk)) continue;

          Vector3 worldPoint = SampleTrianglePoint(
            chunkView,
            vertices[ia],
            vertices[ib],
            vertices[ic],
            hash);

          if (!HasSpacing(worldPoint, accepted, group.MinimumSpacing)) continue;

          GameObject prefab = ChoosePrefab(group, hash);
          GameObject instance = Rent(prefab, group.DisplayName);
          ApplyTransform(instance.transform, worldPoint, normal, group, hash);
          instance.SetActive(true);

          accepted.Add(worldPoint);
          spawned.Add(new SpawnedObject { Instance = instance, Prefab = prefab });
        }
      }

      spawnedByChunk[chunkCoord] = spawned;
    }

    private byte ResolveBiomeId(Vector3 worldPoint)
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      Vector3 local = transform.InverseTransformPoint(worldPoint) / voxelSize;
      int voxelX = Mathf.FloorToInt(local.x);
      int voxelZ = Mathf.FloorToInt(local.z);
      world.Settings.ResolveBiomeAtWorldXZ(voxelX, voxelZ, out _, out byte biomeId);
      return biomeId;
    }

    private static bool CanUseGroup(
      DemoDensityObjectScatterGroup group,
      Vector3 normal,
      float worldHeight)
    {
      return group != null &&
             group.Enabled &&
             group.MaximumPerChunk > 0 &&
             normal.y >= group.MinimumUpwardNormal &&
             worldHeight >= group.MinimumWorldHeight &&
             worldHeight <= group.MaximumWorldHeight;
    }

    private static Vector3 SampleTrianglePoint(
      ChunkView view,
      Vector3 a,
      Vector3 b,
      Vector3 c,
      uint hash)
    {
      float u = ToUnit(Mix(hash, 11));
      float v = ToUnit(Mix(hash, 29));
      if (u + v > 1f)
      {
        u = 1f - u;
        v = 1f - v;
      }

      return view.transform.TransformPoint(a + (b - a) * u + (c - a) * v);
    }

    private static bool HasSpacing(Vector3 point, List<Vector3> accepted, float spacing)
    {
      float minDistanceSquared = Mathf.Max(0f, spacing) * Mathf.Max(0f, spacing);
      for (int i = 0; i < accepted.Count; i++)
      {
        if ((accepted[i] - point).sqrMagnitude < minDistanceSquared) return false;
      }

      return true;
    }

    private static GameObject ChoosePrefab(DemoDensityObjectScatterGroup group, uint hash)
    {
      if (group.Prefabs == null || group.Prefabs.Length == 0) return null;
      return group.Prefabs[hash % (uint)group.Prefabs.Length];
    }

    private GameObject Rent(GameObject prefab, string label)
    {
      GameObject instance;
      if (prefab == null)
      {
        instance = fallbackPool.Count > 0
          ? fallbackPool.Pop()
          : GameObject.CreatePrimitive(PrimitiveType.Capsule);

        Collider collider = instance.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
      }
      else
      {
        if (!poolsByPrefab.TryGetValue(prefab, out Stack<GameObject> pool))
        {
          pool = new Stack<GameObject>();
          poolsByPrefab[prefab] = pool;
        }

        instance = pool.Count > 0 ? pool.Pop() : Instantiate(prefab);
      }

      instance.name = string.IsNullOrWhiteSpace(label) ? "Density Scatter" : label;
      instance.transform.SetParent(transform, true);
      return instance;
    }

    private static void ApplyTransform(
      Transform target,
      Vector3 point,
      Vector3 normal,
      DemoDensityObjectScatterGroup group,
      uint hash)
    {
      target.position = point + group.PositionOffset;
      Quaternion surfaceRotation = group.AlignToSurface
        ? Quaternion.FromToRotation(Vector3.up, normal)
        : Quaternion.identity;
      Quaternion yaw = group.RandomYaw
        ? Quaternion.Euler(0f, ToUnit(Mix(hash, 47)) * 360f, 0f)
        : Quaternion.identity;
      target.rotation = surfaceRotation * yaw;

      float minScale = Mathf.Min(group.UniformScaleRange.x, group.UniformScaleRange.y);
      float maxScale = Mathf.Max(group.UniformScaleRange.x, group.UniformScaleRange.y);
      float scale = Mathf.Lerp(minScale, maxScale, ToUnit(Mix(hash, 71)));
      target.localScale = Vector3.one * scale;
    }

    private void ReleaseChunk(Vector3Int chunkCoord)
    {
      if (!spawnedByChunk.TryGetValue(chunkCoord, out List<SpawnedObject> objects)) return;
      spawnedByChunk.Remove(chunkCoord);

      for (int i = 0; i < objects.Count; i++)
      {
        SpawnedObject spawned = objects[i];
        if (spawned?.Instance == null) continue;
        spawned.Instance.SetActive(false);

        if (spawned.Prefab == null)
        {
          fallbackPool.Push(spawned.Instance);
        }
        else
        {
          poolsByPrefab[spawned.Prefab].Push(spawned.Instance);
        }
      }
    }

    private void OnDestroy()
    {
      List<Vector3Int> chunks = new(spawnedByChunk.Keys);
      for (int i = 0; i < chunks.Count; i++) ReleaseChunk(chunks[i]);

      while (fallbackPool.Count > 0) Destroy(fallbackPool.Pop());
      foreach (Stack<GameObject> pool in poolsByPrefab.Values)
      {
        while (pool.Count > 0) Destroy(pool.Pop());
      }
      poolsByPrefab.Clear();
    }

    private static uint Hash(Vector3Int coord, int triangleIndex, int groupIndex)
    {
      unchecked
      {
        uint value = 2166136261u;
        value = (value ^ (uint)coord.x) * 16777619u;
        value = (value ^ (uint)coord.y) * 16777619u;
        value = (value ^ (uint)coord.z) * 16777619u;
        value = (value ^ (uint)triangleIndex) * 16777619u;
        return (value ^ (uint)groupIndex) * 16777619u;
      }
    }

    private static uint Mix(uint value, uint salt)
    {
      unchecked
      {
        value ^= salt + 0x9e3779b9u + (value << 6) + (value >> 2);
        return value * 16777619u;
      }
    }

    private static float ToUnit(uint value)
    {
      return (value & 0x00FFFFFFu) / 16777215f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
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
      WorldRenderer[] renderers = FindObjectsByType<WorldRenderer>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);

      for (int i = 0; i < renderers.Length; i++)
      {
        WorldRenderer renderer = renderers[i];
        if (renderer != null &&
            renderer.GetComponent<DemoDensityBiomeObjectScatterRenderer>() == null)
        {
          renderer.gameObject.AddComponent<DemoDensityBiomeObjectScatterRenderer>();
        }
      }
    }
  }
}
