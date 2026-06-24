using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Spawns and recycles per-chunk surface foliage (grass tufts, flowers, shells,
  /// weeds) alongside the block chunk lifecycle. <see cref="WorldRenderer"/> drives
  /// it: a chunk's foliage is (re)built whenever its block mesh is rebuilt and
  /// released when the chunk unloads, so foliage streams exactly with the terrain.
  /// </summary>
  [RequireComponent(typeof(CubusWorld))]
  public sealed class WorldDetailRenderer : MonoBehaviour
  {
    [Header("Foliage")]
    [SerializeField] private bool enableFoliage = true;
    [SerializeField] private DetailScatterDatabase scatterDatabase;
    [SerializeField] private Texture2D foliageAtlas;
    [SerializeField] private string foliageShaderName = "Cubus/FoliageURP";

    [Tooltip("Scales how many clusters each profile scatters (0 = none, 1 = authored amount).")]
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

    private CubusWorld world;
    private Material runtimeMaterial;

    private sealed class DetailView
    {
      public GameObject Go;
      public MeshFilter Filter;
      public MeshRenderer Renderer;
      public Mesh Mesh;
    }

    private readonly Dictionary<Vector3Int, DetailView> activeViews = new();
    private readonly Stack<DetailView> pool = new();
    private Transform root;

    public bool FoliageEnabled => enableFoliage;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      root = transform;
    }

    private void OnValidate()
    {
      if (runtimeMaterial != null)
      {
        ApplyMaterialProperties(runtimeMaterial);
      }
    }

    private bool CanRender()
    {
      return enableFoliage
          && scatterDatabase != null
          && scatterDatabase.HasAnyProfiles
          && foliageAtlas != null;
    }

    /// <summary>
    /// Rebuilds (or removes) the foliage for a single chunk. Safe to call every
    /// time the chunk's block mesh changes.
    /// </summary>
    public void RefreshChunkDetail(Vector3Int chunkCoord)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (!CanRender() || world == null)
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

      Mesh mesh = ChunkDetailMeshBuilder.Build(
          chunkData,
          world.GetBlockMaterialAtWorldVoxel,
          scatterDatabase,
          voxelSize,
          densityMultiplier,
          view.Mesh);

      if (mesh == null)
      {
        // No foliage on this chunk: drop it but keep the (reusable) view + mesh
        // back in the pool so a later chunk can reuse the buffers.
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
      view.Renderer.shadowCastingMode = castShadows
          ? ShadowCastingMode.On
          : ShadowCastingMode.Off;
      view.Renderer.enabled = true;

      view.Go.transform.localPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      view.Go.transform.localRotation = Quaternion.identity;
      view.Go.transform.localScale = Vector3.one;
      view.Go.name = $"Foliage {chunkCoord.x}, {chunkCoord.y}, {chunkCoord.z}";

      activeViews[chunkCoord] = view;
    }

    public void RemoveChunkDetail(Vector3Int chunkCoord)
    {
      if (!activeViews.TryGetValue(chunkCoord, out DetailView view))
      {
        return;
      }

      activeViews.Remove(chunkCoord);
      ReleaseView(view);
    }

    public void ClearAllDetail()
    {
      foreach (KeyValuePair<Vector3Int, DetailView> pair in activeViews)
      {
        ReleaseView(pair.Value);
      }

      activeViews.Clear();
    }

    private DetailView RentView()
    {
      if (pool.Count > 0)
      {
        DetailView pooled = pool.Pop();
        pooled.Go.SetActive(true);
        return pooled;
      }

      GameObject go = new("Foliage");
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

    private Material EnsureMaterial()
    {
      if (runtimeMaterial != null)
      {
        return runtimeMaterial;
      }

      Shader shader = Shader.Find(foliageShaderName);
      if (shader == null)
      {
        Debug.LogError($"WorldDetailRenderer: foliage shader '{foliageShaderName}' not found.");
        shader = Shader.Find("Universal Render Pipeline/Lit");
      }

      runtimeMaterial = new Material(shader) { name = "Cubus Foliage (Runtime)" };
      ApplyMaterialProperties(runtimeMaterial);
      return runtimeMaterial;
    }

    private void ApplyMaterialProperties(Material material)
    {
      if (material == null)
      {
        return;
      }

      if (foliageAtlas != null && material.HasProperty("_FoliageAtlas"))
      {
        material.SetTexture("_FoliageAtlas", foliageAtlas);
      }

      if (material.HasProperty("_Tint"))
      {
        material.SetColor("_Tint", tint);
      }

      if (material.HasProperty("_Cutoff"))
      {
        material.SetFloat("_Cutoff", alphaCutoff);
      }

      if (material.HasProperty("_AmbientBoost"))
      {
        material.SetFloat("_AmbientBoost", ambientBoost);
      }

      if (material.HasProperty("_WindStrength"))
      {
        material.SetFloat("_WindStrength", windStrength);
      }

      if (material.HasProperty("_WindSpeed"))
      {
        material.SetFloat("_WindSpeed", windSpeed);
      }

      if (material.HasProperty("_WindFrequency"))
      {
        material.SetFloat("_WindFrequency", windFrequency);
      }

      if (material.HasProperty("_WindDirX"))
      {
        material.SetFloat("_WindDirX", windDirection.x);
      }

      if (material.HasProperty("_WindDirZ"))
      {
        material.SetFloat("_WindDirZ", windDirection.y);
      }
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
        if (Application.isPlaying)
        {
          Destroy(runtimeMaterial);
        }
        else
        {
          DestroyImmediate(runtimeMaterial);
        }

        runtimeMaterial = null;
      }
    }

    private static void DestroyViewMesh(DetailView view)
    {
      if (view == null || view.Mesh == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Destroy(view.Mesh);
      }
      else
      {
        DestroyImmediate(view.Mesh);
      }

      view.Mesh = null;
    }
  }
}
