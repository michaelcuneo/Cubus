using System.Collections.Generic;
using System.Reflection;
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
    private const string SettingsPath = "Demo Density Object Scatter Settings";

    [SerializeField] private bool enableScatter = true;
    [SerializeField] private DemoDensityObjectScatterSettings settings;
    [SerializeField] private bool logDiagnostics = true;

    private sealed class Spawned
    {
      public GameObject Instance;
      public GameObject Prefab;
    }

    private readonly Dictionary<Vector3Int, List<Spawned>> spawnedByChunk = new();
    private readonly Dictionary<GameObject, Stack<GameObject>> pools = new();
    private readonly Stack<GameObject> fallbackPool = new();

    private CubusWorld world;
    private WorldRenderer renderer;
    private FieldInfo densityViewsField;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      renderer = GetComponent<WorldRenderer>();
      densityViewsField = typeof(WorldRenderer).GetField(
        "activeDensityChunkViews",
        BindingFlags.Instance | BindingFlags.NonPublic);
      settings ??= Resources.Load<DemoDensityObjectScatterSettings>(SettingsPath);
    }

    private void OnEnable()
    {
      if (renderer == null) renderer = GetComponent<WorldRenderer>();
      if (renderer != null)
      {
        renderer.DensityChunkRendered -= RebuildChunk;
        renderer.DensityChunkRemoved -= RemoveChunk;
        renderer.ChunksCleared -= ClearAll;
        renderer.DensityChunkRendered += RebuildChunk;
        renderer.DensityChunkRemoved += RemoveChunk;
        renderer.ChunksCleared += ClearAll;
      }

      RefreshExisting();
      if (logDiagnostics)
      {
        Debug.Log($"Density scatter enabled. Settings={(settings != null ? settings.name : "MISSING")}", this);
      }
    }

    private void OnDisable()
    {
      if (renderer == null) return;
      renderer.DensityChunkRendered -= RebuildChunk;
      renderer.DensityChunkRemoved -= RemoveChunk;
      renderer.ChunksCleared -= ClearAll;
    }

    private Dictionary<Vector3Int, ChunkView> GetDensityViews()
    {
      return densityViewsField?.GetValue(renderer) as Dictionary<Vector3Int, ChunkView>;
    }

    private void RefreshExisting()
    {
      Dictionary<Vector3Int, ChunkView> views = GetDensityViews();
      if (views == null) return;
      foreach (Vector3Int coord in views.Keys) RebuildChunk(coord);
    }

    private void RebuildChunk(Vector3Int coord)
    {
      RemoveChunk(coord);
      if (!enableScatter || settings == null || world == null || world.Settings == null) return;

      Dictionary<Vector3Int, ChunkView> views = GetDensityViews();
      if (views == null || !views.TryGetValue(coord, out ChunkView view) || view == null) return;

      Mesh mesh = view.CurrentMesh;
      if (mesh == null || mesh.vertexCount == 0) return;

      Vector3[] vertices = mesh.vertices;
      Vector3[] normals = mesh.normals;
      int[] triangles = mesh.triangles;
      List<Spawned> chunkObjects = new();
      Dictionary<DemoDensityObjectScatterGroup, List<Vector3>> accepted = new();
      int candidates = 0;

      for (int index = 0; index + 2 < triangles.Length; index += 3)
      {
        int ia = triangles[index];
        int ib = triangles[index + 1];
        int ic = triangles[index + 2];
        Vector3 localNormal = normals != null && normals.Length == vertices.Length
          ? (normals[ia] + normals[ib] + normals[ic]).normalized
          : Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]).normalized;
        Vector3 worldNormal = view.transform.TransformDirection(localNormal).normalized;
        if (worldNormal.y < 0f) worldNormal = -worldNormal;

        Vector3 centroid = (vertices[ia] + vertices[ib] + vertices[ic]) / 3f;
        Vector3 centroidWorld = view.transform.TransformPoint(centroid);
        byte biomeId = ResolveBiome(centroidWorld);
        IReadOnlyList<DemoDensityObjectScatterGroup> groups = settings.GetGroups(biomeId);
        if (groups == null) continue;

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
          DemoDensityObjectScatterGroup group = groups[groupIndex];
          if (!Valid(group, worldNormal.y, centroidWorld.y)) continue;
          if ((index / 3) % Mathf.Max(1, group.TriangleStride) != 0) continue;
          candidates++;

          uint hash = Hash(coord, index, groupIndex);
          if (ToUnit(hash) > group.SpawnChance) continue;

          if (!accepted.TryGetValue(group, out List<Vector3> positions))
          {
            positions = new List<Vector3>();
            accepted[group] = positions;
          }
          if (positions.Count >= group.MaximumPerChunk) continue;

          Vector3 point = SamplePoint(view, vertices[ia], vertices[ib], vertices[ic], hash);
          if (!HasSpacing(point, positions, group.MinimumSpacing)) continue;

          GameObject prefab = ChoosePrefab(group, hash);
          GameObject instance = Rent(prefab, group.DisplayName);
          ApplyTransform(instance.transform, point, worldNormal, group, hash);
          instance.SetActive(true);
          positions.Add(point);
          chunkObjects.Add(new Spawned { Instance = instance, Prefab = prefab });
        }
      }

      spawnedByChunk[coord] = chunkObjects;
      if (logDiagnostics)
      {
        Debug.Log($"Density scatter chunk {coord}: candidates={candidates}, spawned={chunkObjects.Count}", this);
      }
    }

    private byte ResolveBiome(Vector3 point)
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      Vector3 local = transform.InverseTransformPoint(point) / voxelSize;
      world.Settings.ResolveBiomeAtWorldXZ(
        Mathf.FloorToInt(local.x),
        Mathf.FloorToInt(local.z),
        out _,
        out byte biomeId);
      return biomeId;
    }

    private static bool Valid(DemoDensityObjectScatterGroup group, float up, float height)
    {
      return group != null && group.Enabled && group.MaximumPerChunk > 0 &&
             up >= group.MinimumUpwardNormal &&
             height >= group.MinimumWorldHeight &&
             height <= group.MaximumWorldHeight;
    }

    private static Vector3 SamplePoint(ChunkView view, Vector3 a, Vector3 b, Vector3 c, uint hash)
    {
      float u = ToUnit(Mix(hash, 11));
      float v = ToUnit(Mix(hash, 29));
      if (u + v > 1f) { u = 1f - u; v = 1f - v; }
      return view.transform.TransformPoint(a + (b - a) * u + (c - a) * v);
    }

    private static bool HasSpacing(Vector3 point, List<Vector3> positions, float spacing)
    {
      float squared = Mathf.Max(0f, spacing) * Mathf.Max(0f, spacing);
      for (int i = 0; i < positions.Count; i++)
      {
        if ((positions[i] - point).sqrMagnitude < squared) return false;
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
        instance = fallbackPool.Count > 0 ? fallbackPool.Pop() : GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Collider collider = instance.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
      }
      else
      {
        if (!pools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
          pool = new Stack<GameObject>();
          pools[prefab] = pool;
        }
        instance = pool.Count > 0 ? pool.Pop() : Instantiate(prefab);
      }

      instance.name = string.IsNullOrWhiteSpace(label) ? "Density Scatter" : label;
      instance.transform.SetParent(transform, true);
      return instance;
    }

    private static void ApplyTransform(Transform target, Vector3 point, Vector3 normal, DemoDensityObjectScatterGroup group, uint hash)
    {
      target.position = point + group.PositionOffset;
      Quaternion surface = group.AlignToSurface ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
      Quaternion yaw = group.RandomYaw ? Quaternion.Euler(0f, ToUnit(Mix(hash, 47)) * 360f, 0f) : Quaternion.identity;
      target.rotation = surface * yaw;
      float min = Mathf.Min(group.UniformScaleRange.x, group.UniformScaleRange.y);
      float max = Mathf.Max(group.UniformScaleRange.x, group.UniformScaleRange.y);
      target.localScale = Vector3.one * Mathf.Lerp(min, max, ToUnit(Mix(hash, 71)));
    }

    private void RemoveChunk(Vector3Int coord)
    {
      if (!spawnedByChunk.TryGetValue(coord, out List<Spawned> objects)) return;
      spawnedByChunk.Remove(coord);
      for (int i = 0; i < objects.Count; i++)
      {
        Spawned item = objects[i];
        if (item?.Instance == null) continue;
        item.Instance.SetActive(false);
        if (item.Prefab == null) fallbackPool.Push(item.Instance);
        else
        {
          if (!pools.TryGetValue(item.Prefab, out Stack<GameObject> pool))
          {
            pool = new Stack<GameObject>();
            pools[item.Prefab] = pool;
          }
          pool.Push(item.Instance);
        }
      }
    }

    private void ClearAll()
    {
      List<Vector3Int> coords = new(spawnedByChunk.Keys);
      for (int i = 0; i < coords.Count; i++) RemoveChunk(coords[i]);
    }

    private void OnDestroy()
    {
      ClearAll();
      while (fallbackPool.Count > 0) Destroy(fallbackPool.Pop());
      foreach (Stack<GameObject> pool in pools.Values)
      {
        while (pool.Count > 0) Destroy(pool.Pop());
      }
      pools.Clear();
    }

    private static uint Hash(Vector3Int coord, int triangle, int group)
    {
      unchecked
      {
        uint value = 2166136261u;
        value = (value ^ (uint)coord.x) * 16777619u;
        value = (value ^ (uint)coord.y) * 16777619u;
        value = (value ^ (uint)coord.z) * 16777619u;
        value = (value ^ (uint)triangle) * 16777619u;
        return (value ^ (uint)group) * 16777619u;
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

    private static float ToUnit(uint value) => (value & 0x00FFFFFFu) / 16777215f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
      Install();
      SceneManager.sceneLoaded -= OnSceneLoaded;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    private static void Install()
    {
      WorldRenderer[] renderers = FindObjectsByType<WorldRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = 0; i < renderers.Length; i++)
      {
        WorldRenderer item = renderers[i];
        if (item != null && item.GetComponent<DemoDensityBiomeObjectScatterRenderer>() == null)
        {
          item.gameObject.AddComponent<DemoDensityBiomeObjectScatterRenderer>();
        }
      }
    }
  }
}
