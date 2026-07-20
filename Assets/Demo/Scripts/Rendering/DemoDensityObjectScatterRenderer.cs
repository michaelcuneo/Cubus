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
  public sealed class DemoDensityObjectScatterRenderer : MonoBehaviour
  {
    [Header("Density Scatter")]
    [SerializeField] private bool enabledScatter = true;
    [SerializeField][Min(0.1f)] private float refreshInterval = 0.75f;
    [SerializeField][Range(0f, 1f)] private float minimumUpwardNormal = 0.72f;
    [SerializeField][Min(1)] private int triangleStride = 18;
    [SerializeField][Range(0f, 1f)] private float spawnChance = 0.18f;
    [SerializeField][Min(0)] private int maximumObjectsPerChunk = 10;
    [SerializeField] private Vector2 uniformScaleRange = new(0.55f, 1.35f);
    [SerializeField] private GameObject[] prefabs;

    private readonly Dictionary<Vector3Int, List<GameObject>> spawnedByChunk = new();
    private readonly Stack<GameObject> fallbackPool = new();

    private WorldRenderer worldRenderer;
    private FieldInfo densityViewsField;
    private float nextRefreshAt;

    private void Awake()
    {
      worldRenderer = GetComponent<WorldRenderer>();
      densityViewsField = typeof(WorldRenderer).GetField(
        "activeDensityChunkViews",
        BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void Update()
    {
      if (!enabledScatter || Time.unscaledTime < nextRefreshAt)
      {
        return;
      }

      nextRefreshAt = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
      RefreshScatter();
    }

    private void RefreshScatter()
    {
      if (worldRenderer == null || densityViewsField == null)
      {
        return;
      }

      Dictionary<Vector3Int, ChunkView> densityViews =
        densityViewsField.GetValue(worldRenderer) as Dictionary<Vector3Int, ChunkView>;

      if (densityViews == null)
      {
        return;
      }

      List<Vector3Int> removed = null;
      foreach (Vector3Int chunkCoord in spawnedByChunk.Keys)
      {
        if (!densityViews.ContainsKey(chunkCoord))
        {
          removed ??= new List<Vector3Int>();
          removed.Add(chunkCoord);
        }
      }

      if (removed != null)
      {
        for (int i = 0; i < removed.Count; i++)
        {
          ReleaseChunk(removed[i]);
        }
      }

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in densityViews)
      {
        if (!spawnedByChunk.ContainsKey(pair.Key))
        {
          BuildChunk(pair.Key, pair.Value);
        }
      }
    }

    private void BuildChunk(Vector3Int chunkCoord, ChunkView chunkView)
    {
      Mesh mesh = chunkView != null ? chunkView.CurrentMesh : null;
      if (mesh == null || mesh.vertexCount == 0 || maximumObjectsPerChunk <= 0)
      {
        return;
      }

      Vector3[] vertices = mesh.vertices;
      Vector3[] normals = mesh.normals;
      int[] triangles = mesh.triangles;
      List<GameObject> spawned = new();
      int stride = Mathf.Max(1, triangleStride) * 3;

      for (int index = 0; index + 2 < triangles.Length && spawned.Count < maximumObjectsPerChunk; index += stride)
      {
        int ia = triangles[index];
        int ib = triangles[index + 1];
        int ic = triangles[index + 2];
        Vector3 normal = normals != null && normals.Length == vertices.Length
          ? (normals[ia] + normals[ib] + normals[ic]).normalized
          : Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]).normalized;

        if (normal.y < minimumUpwardNormal)
        {
          continue;
        }

        uint hash = Hash(chunkCoord, index);
        if (ToUnit(hash) > spawnChance)
        {
          continue;
        }

        float u = ToUnit(Hash(chunkCoord, index + 11));
        float v = ToUnit(Hash(chunkCoord, index + 29));
        if (u + v > 1f)
        {
          u = 1f - u;
          v = 1f - v;
        }

        Vector3 localPoint = vertices[ia] + (vertices[ib] - vertices[ia]) * u + (vertices[ic] - vertices[ia]) * v;
        Vector3 worldPoint = chunkView.transform.TransformPoint(localPoint);
        GameObject instance = RentObject(hash);
        instance.transform.SetParent(transform, true);
        instance.transform.position = worldPoint;
        instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) *
          Quaternion.Euler(0f, ToUnit(Hash(chunkCoord, index + 47)) * 360f, 0f);
        float scale = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, ToUnit(Hash(chunkCoord, index + 71)));
        instance.transform.localScale = Vector3.one * scale;
        instance.name = $"Density Scatter {chunkCoord.x},{chunkCoord.y},{chunkCoord.z}";
        instance.SetActive(true);
        spawned.Add(instance);
      }

      spawnedByChunk[chunkCoord] = spawned;
    }

    private GameObject RentObject(uint hash)
    {
      if (prefabs != null && prefabs.Length > 0)
      {
        GameObject prefab = prefabs[hash % (uint)prefabs.Length];
        if (prefab != null)
        {
          return Instantiate(prefab);
        }
      }

      GameObject fallback = fallbackPool.Count > 0
        ? fallbackPool.Pop()
        : GameObject.CreatePrimitive(PrimitiveType.Capsule);
      Collider collider = fallback.GetComponent<Collider>();
      if (collider != null)
      {
        Destroy(collider);
      }
      return fallback;
    }

    private void ReleaseChunk(Vector3Int chunkCoord)
    {
      if (!spawnedByChunk.TryGetValue(chunkCoord, out List<GameObject> objects))
      {
        return;
      }

      spawnedByChunk.Remove(chunkCoord);
      for (int i = 0; i < objects.Count; i++)
      {
        GameObject item = objects[i];
        if (item == null) continue;
        item.SetActive(false);
        fallbackPool.Push(item);
      }
    }

    private void OnDestroy()
    {
      foreach (List<GameObject> objects in spawnedByChunk.Values)
      {
        for (int i = 0; i < objects.Count; i++)
        {
          if (objects[i] != null) Destroy(objects[i]);
        }
      }
      spawnedByChunk.Clear();
      while (fallbackPool.Count > 0)
      {
        GameObject item = fallbackPool.Pop();
        if (item != null) Destroy(item);
      }
    }

    private static uint Hash(Vector3Int chunkCoord, int salt)
    {
      unchecked
      {
        uint value = 2166136261u;
        value = (value ^ (uint)chunkCoord.x) * 16777619u;
        value = (value ^ (uint)chunkCoord.y) * 16777619u;
        value = (value ^ (uint)chunkCoord.z) * 16777619u;
        return (value ^ (uint)salt) * 16777619u;
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
      WorldRenderer[] renderers = FindObjectsByType<WorldRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = 0; i < renderers.Length; i++)
      {
        WorldRenderer renderer = renderers[i];
        if (renderer != null && renderer.GetComponent<DemoDensityObjectScatterRenderer>() == null)
        {
          renderer.gameObject.AddComponent<DemoDensityObjectScatterRenderer>();
        }
      }
    }
  }
}
