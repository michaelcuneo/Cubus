using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  [RequireComponent(typeof(CubusWorld))]
  public sealed class WorldRenderer : MonoBehaviour
  {
    [Header("Rendering")]
    [SerializeField] private Material worldMaterial;
    [SerializeField] private bool useBiomeAtlasShader = true;
    [SerializeField] private string biomeAtlasShaderName = "Cubus/BiomeAtlasURP";
    [SerializeField] private Texture2D biomeTextureAtlas;
    [SerializeField] private Vector2Int biomeAtlasGrid = new(4, 4);
    [SerializeField] private BlockMaterialDatabase blockMaterialDatabase;
    [SerializeField] private Color biomeTint = Color.white;
    [SerializeField] private bool logRenderedChunkMeshes;

    [Header("Collision")]
    [SerializeField] private WorldCollisionMode collisionMode = WorldCollisionMode.NearViewerOnly;
    [SerializeField] private Transform collisionViewer;
    [SerializeField] private float collisionActivationRadius = 96.0f;
    [SerializeField] private float collisionUpdateInterval = 0.05f;

    public WorldCollisionMode CollisionMode
    {
      get => collisionMode;
      set
      {
        collisionMode = value;
        RefreshChunkCollision();
      }
    }

    private float timeSinceLastCollisionUpdate;
    private readonly Dictionary<Vector3Int, ChunkView> activeChunkViews = new();
    private Material runtimeWorldMaterial;
    private Texture2D generatedFallbackAtlas;
    private Texture2D topLookupTexture;
    private Texture2D sideLookupTexture;
    private Texture2D bottomLookupTexture;
    private Texture2D propsLookupTexture;

    private const int LookupTextureSize = 256;

    private CubusWorld world;
    private ChunkPool chunkPool;

    public IReadOnlyDictionary<Vector3Int, ChunkView> ActiveChunkViews => activeChunkViews;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      chunkPool = new ChunkPool(transform);
      EnsureWorldMaterial();
    }

    private void Update()
    {
      if (collisionMode != WorldCollisionMode.NearViewerOnly)
      {
        return;
      }

      timeSinceLastCollisionUpdate += Time.deltaTime;

      if (timeSinceLastCollisionUpdate < collisionUpdateInterval)
      {
        return;
      }

      timeSinceLastCollisionUpdate = 0.0f;
      RefreshChunkCollision();
    }

    public void SetCollisionViewer(Transform viewer)
    {
      collisionViewer = viewer;
      RefreshChunkCollision();
    }

    public void RefreshChunkCollision()
    {
      foreach (var pair in activeChunkViews)
      {
        if (pair.Value != null)
        {
          ApplyCollisionStateToChunk(pair.Value);
        }
      }
    }

    public void RenderBlockChunkMesh(Vector3Int chunkCoord, MeshData meshData)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (meshData == null || meshData.IsEmpty)
      {
        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);
      chunkView.ApplyMesh(meshData, collisionMode != WorldCollisionMode.None);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
    }

    public void RenderUnityMesh(Vector3Int chunkCoord, Mesh unityMesh, bool generateCollision = false)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (unityMesh == null || unityMesh.vertexCount == 0 || unityMesh.GetIndexCount(0) == 0)
      {
        if (logRenderedChunkMeshes)
        {
          Debug.LogWarning(
              $"Skipping Unity mesh chunk {chunkCoord}. " +
              $"MeshNull={unityMesh == null}, " +
              $"Verts={(unityMesh != null ? unityMesh.vertexCount : 0)}, " +
              $"Indices={(unityMesh != null && unityMesh.subMeshCount > 0 ? unityMesh.GetIndexCount(0) : 0)}"
          );
        }

        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);

      bool shouldGenerateCollision =
          generateCollision &&
          collisionMode != WorldCollisionMode.None;

      chunkView.ApplyMesh(unityMesh, shouldGenerateCollision);

      if (logRenderedChunkMeshes)
      {
        Debug.Log(
            $"Rendered Unity mesh chunk {chunkCoord}. " +
            $"Verts={unityMesh.vertexCount}, " +
            $"Indices={unityMesh.GetIndexCount(0)}, " +
            $"Bounds={unityMesh.bounds}, " +
            $"Material={(chunkView.MeshRenderer != null ? chunkView.MeshRenderer.sharedMaterial : null)}, " +
            $"RendererEnabled={(chunkView.MeshRenderer != null && chunkView.MeshRenderer.enabled)}"
        );
      }

      ApplyCollisionStateToChunk(chunkView);
    }

    private void ApplyCollisionStateToChunk(ChunkView chunkView)
    {
      if (chunkView == null)
      {
        return;
      }

      switch (collisionMode)
      {
        case WorldCollisionMode.None:
          chunkView.SetCollisionEnabled(false);
          break;

        case WorldCollisionMode.AllChunks:
          chunkView.SetCollisionEnabled(true);
          break;

        case WorldCollisionMode.NearViewerOnly:
          chunkView.SetCollisionEnabled(IsChunkNearCollisionViewer(chunkView));
          break;
      }
    }

    private bool IsChunkNearCollisionViewer(ChunkView chunkView)
    {
      if (collisionViewer == null)
      {
        return true;
      }

      Vector3 chunkCenter =
          chunkView.transform.position +
          Vector3.one * world.Settings.VoxelSize * VoxelConstants.ChunkSize * 0.5f;

      float distanceSquared = (chunkCenter - collisionViewer.position).sqrMagnitude;
      float radiusSquared = collisionActivationRadius * collisionActivationRadius;

      return distanceSquared <= radiusSquared;
    }

    private void OnDestroy()
    {
      ClearAll();

      if (runtimeWorldMaterial != null)
      {
        if (Application.isPlaying)
        {
          Destroy(runtimeWorldMaterial);
        }
        else
        {
          DestroyImmediate(runtimeWorldMaterial);
        }

        runtimeWorldMaterial = null;
      }

      if (generatedFallbackAtlas != null)
      {
        if (Application.isPlaying)
        {
          Destroy(generatedFallbackAtlas);
        }
        else
        {
          DestroyImmediate(generatedFallbackAtlas);
        }

        generatedFallbackAtlas = null;
      }

      DestroyLookupTexture(ref topLookupTexture);
      DestroyLookupTexture(ref sideLookupTexture);
      DestroyLookupTexture(ref bottomLookupTexture);
      DestroyLookupTexture(ref propsLookupTexture);

      if (chunkPool != null)
      {
        chunkPool.DestroyAll();
        chunkPool = null;
      }
    }

    [ContextMenu("Rebuild All")]
    public void RebuildAll()
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (!world.IsWorldReady)
      {
        world.GenerateWorld();
      }

      ClearAll();

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        foreach (KeyValuePair<Vector3Int, BlockChunkData> pair in world.Data.BlockChunks)
        {
          RenderBlockChunk(pair.Key, pair.Value);
        }

        Debug.Log($"Rendered block chunks: {activeChunkViews.Count}");
        return;
      }

      if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity)
      {
        var snapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
        int rendered = 0;

        foreach (KeyValuePair<Vector3Int, DensityChunkData> pair in world.Data.DensityChunks)
        {
          var chunkCoord = pair.Key;
          var chunkData = pair.Value;

          if (chunkData == null ||
              !chunkData.HasAnySolidVoxel() ||
              !chunkData.HasSurfaceCrossing())
          {
            RemoveChunk(chunkCoord);
            continue;
          }

          int cellStep = Mathf.Max(1, world.Settings.DensityMeshStep);
          var unityMesh = MarchingCubesMesher.GenerateMeshDirect(
            chunkCoord,
            snapshot,
            cellStep,
            true,
            world.GetDensityVoxelAtWorldVoxel
        );

          if (unityMesh == null || unityMesh.vertexCount == 0)
          {
            RemoveChunk(chunkCoord);
            continue;
          }

          ChunkView cv = GetOrCreateChunkView(chunkCoord);

          // Smooth-density RebuildAll is visual-only for now.
          // Do not cook full marching-cubes MeshColliders for every chunk.
          cv.ApplyMesh(unityMesh, false);

          ApplyCollisionStateToChunk(cv);
          rendered++;
        }

        Debug.Log($"Rendered density chunks: {rendered}");
        return;
      }
    }

    public void RenderBlockChunk(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      if (chunkData == null || !chunkData.HasAnySolidVoxel())
      {
        RemoveChunk(chunkCoord);
        return;
      }

      MeshData meshData = MeshDataPool.Rent(4096, 6144);
      BlockGreedyMesher.GenerateNeighbourAware(
        chunkData,
        world.GetBlockMaterialAtWorldVoxel,
        world.Settings.VoxelSize,
        meshData
      );

      if (meshData.IsEmpty)
      {
        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);
      chunkView.ApplyMesh(meshData, collisionMode != WorldCollisionMode.None);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
    }

    public void RebuildBlockChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      int rebuiltCount = 0;
      int removedCount = 0;

      foreach (Vector3Int chunkCoord in dirtyChunks)
      {
        bool hasData = world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData);

        if (!hasData || chunkData == null || !chunkData.HasAnySolidVoxel())
        {
          if (RemoveChunk(chunkCoord))
          {
            removedCount++;
          }

          continue;
        }

        RenderBlockChunk(chunkCoord, chunkData);
        rebuiltCount++;
      }

      Debug.Log($"Rebuilt block chunks. Rebuilt={rebuiltCount}, Removed={removedCount}");
    }

    public bool RemoveChunk(Vector3Int chunkCoord)
    {
      if (!activeChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView))
      {
        return false;
      }

      activeChunkViews.Remove(chunkCoord);

      if (chunkPool != null)
      {
        chunkPool.Release(chunkView);
      }
      else if (chunkView != null)
      {
        if (Application.isPlaying)
        {
          Destroy(chunkView.gameObject);
        }
        else
        {
          DestroyImmediate(chunkView.gameObject);
        }
      }

      return true;
    }

    [ContextMenu("Clear Rendered Chunks")]
    public void ClearAll()
    {
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeChunkViews)
      {
        if (pair.Value != null)
        {
          if (chunkPool != null)
          {
            chunkPool.Release(pair.Value);
          }
          else
          {
            if (Application.isPlaying)
            {
              Destroy(pair.Value.gameObject);
            }
            else
            {
              DestroyImmediate(pair.Value.gameObject);
            }
          }
        }
      }

      activeChunkViews.Clear();
    }

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return activeChunkViews.ContainsKey(chunkCoord);
    }

    private ChunkView GetOrCreateChunkView(Vector3Int chunkCoord)
    {
      if (activeChunkViews.TryGetValue(chunkCoord, out ChunkView existingView) && existingView != null)
      {
        return existingView;
      }

      EnsureWorldMaterial();

      ChunkView chunkView = chunkPool.Acquire(
          chunkCoord,
          world.Settings.VoxelSize,
          runtimeWorldMaterial != null ? runtimeWorldMaterial : worldMaterial
      );

      activeChunkViews[chunkCoord] = chunkView;
      ApplyCollisionStateToChunk(chunkView);

      return chunkView;
    }

    private void EnsureWorldMaterial()
    {
      if (!useBiomeAtlasShader)
      {
        runtimeWorldMaterial = worldMaterial;
        return;
      }

      Shader shader = Shader.Find(biomeAtlasShaderName);
      if (!IsUsableShader(shader))
      {
        runtimeWorldMaterial = worldMaterial;
        return;
      }

      if (runtimeWorldMaterial == null || runtimeWorldMaterial.shader != shader)
      {
        if (runtimeWorldMaterial != null && runtimeWorldMaterial != worldMaterial)
        {
          if (Application.isPlaying)
          {
            Destroy(runtimeWorldMaterial);
          }
          else
          {
            DestroyImmediate(runtimeWorldMaterial);
          }
        }

        runtimeWorldMaterial = new Material(shader)
        {
          name = "Cubus Runtime Biome Atlas Material"
        };
      }

      Texture2D atlas = biomeTextureAtlas != null ? biomeTextureAtlas : GetOrCreateFallbackAtlas();
      runtimeWorldMaterial.SetTexture("_Atlas", atlas);
      runtimeWorldMaterial.SetVector(
          "_AtlasGrid",
          new Vector4(
              Mathf.Max(1, biomeAtlasGrid.x),
              Mathf.Max(1, biomeAtlasGrid.y),
              0.0f,
              0.0f
          )
      );
      runtimeWorldMaterial.SetColor("_Tint", biomeTint);
      ApplyBlockMaterialLookups();

      if (runtimeWorldMaterial == null || !IsUsableShader(runtimeWorldMaterial.shader))
      {
        runtimeWorldMaterial = worldMaterial;
      }
    }

    private static bool IsUsableShader(Shader shader)
    {
      if (shader == null || !shader.isSupported)
      {
        return false;
      }

      string shaderName = shader.name;
      if (string.IsNullOrEmpty(shaderName))
      {
        return false;
      }

      if (shaderName.Contains("InternalErrorShader") || shaderName.Contains("FallbackError"))
      {
        return false;
      }

      return shader.passCount > 0;
    }

    private void ApplyBlockMaterialLookups()
    {
      if (runtimeWorldMaterial == null)
      {
        return;
      }

      EnsureLookupTextures();

      int texelCount = LookupTextureSize * LookupTextureSize;
      Color32[] topPixels = new Color32[texelCount];
      Color32[] sidePixels = new Color32[texelCount];
      Color32[] bottomPixels = new Color32[texelCount];
      Color32[] propsPixels = new Color32[texelCount];

      for (int id = 0; id < texelCount; id++)
      {
        ushort safeId = id == 0 ? (ushort)1 : (ushort)id;
        Color32 encodedId = EncodeU16(safeId);
        topPixels[id] = encodedId;
        sidePixels[id] = encodedId;
        bottomPixels[id] = encodedId;
        propsPixels[id] = new Color32(
            (byte)BlockRenderCategory.Opaque,
            (byte)BlockLightingCategory.Lit,
            0,
            255
        );
      }

      if (blockMaterialDatabase != null && blockMaterialDatabase.Definitions != null)
      {
        for (int i = 0; i < blockMaterialDatabase.Definitions.Count; i++)
        {
          BlockMaterialDefinition def = blockMaterialDatabase.Definitions[i];
          if (def == null)
          {
            continue;
          }

          int materialId = Mathf.Clamp(def.MaterialId, 1, 65535);
          topPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.TopTileId, 1, 65535));
          sidePixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.SideTileId, 1, 65535));
          bottomPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.BottomTileId, 1, 65535));
          propsPixels[materialId] = new Color32(
              (byte)def.RenderCategory,
              (byte)def.LightingCategory,
              (byte)Mathf.Clamp(Mathf.RoundToInt(def.EmissionIntensity * 31.875f), 0, 255),
              255
          );
        }
      }

      topLookupTexture.SetPixels32(topPixels);
      sideLookupTexture.SetPixels32(sidePixels);
      bottomLookupTexture.SetPixels32(bottomPixels);
      propsLookupTexture.SetPixels32(propsPixels);

      topLookupTexture.Apply(false, false);
      sideLookupTexture.Apply(false, false);
      bottomLookupTexture.Apply(false, false);
      propsLookupTexture.Apply(false, false);

      runtimeWorldMaterial.SetTexture("_TopLookup", topLookupTexture);
      runtimeWorldMaterial.SetTexture("_SideLookup", sideLookupTexture);
      runtimeWorldMaterial.SetTexture("_BottomLookup", bottomLookupTexture);
      runtimeWorldMaterial.SetTexture("_PropsLookup", propsLookupTexture);
    }

    private void EnsureLookupTextures()
    {
      if (topLookupTexture == null)
      {
        topLookupTexture = CreateLookupTexture("Cubus Top Lookup");
      }

      if (sideLookupTexture == null)
      {
        sideLookupTexture = CreateLookupTexture("Cubus Side Lookup");
      }

      if (bottomLookupTexture == null)
      {
        bottomLookupTexture = CreateLookupTexture("Cubus Bottom Lookup");
      }

      if (propsLookupTexture == null)
      {
        propsLookupTexture = CreateLookupTexture("Cubus Props Lookup");
      }
    }

    private static Texture2D CreateLookupTexture(string name)
    {
      return new Texture2D(LookupTextureSize, LookupTextureSize, TextureFormat.RGBA32, false)
      {
        name = name,
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Point,
      };
    }

    private static void DestroyLookupTexture(ref Texture2D texture)
    {
      if (texture == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Destroy(texture);
      }
      else
      {
        DestroyImmediate(texture);
      }

      texture = null;
    }

    private static Color32 EncodeU16(ushort value)
    {
      return new Color32(
          (byte)(value & 0xFF),
          (byte)((value >> 8) & 0xFF),
          0,
          255
      );
    }

    private Texture2D GetOrCreateFallbackAtlas()
    {
      if (generatedFallbackAtlas != null)
      {
        return generatedFallbackAtlas;
      }

      const int tilesPerAxis = 4;
      const int tileSize = 32;
      int size = tilesPerAxis * tileSize;

      generatedFallbackAtlas = new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = "Cubus Fallback Material Atlas",
        wrapMode = TextureWrapMode.Repeat,
        filterMode = FilterMode.Point
      };

      for (int y = 0; y < size; y++)
      {
        for (int x = 0; x < size; x++)
        {
          int tileX = x / tileSize;
          int tileY = y / tileSize;
          int tileIndex = tileY * tilesPerAxis + tileX;

          float hue = (tileIndex % (tilesPerAxis * tilesPerAxis)) / (float)(tilesPerAxis * tilesPerAxis);
          Color baseColor = Color.HSVToRGB(hue, 0.65f, 0.9f);

          bool checker = ((x + y) & 4) == 0;
          Color shaded = checker ? baseColor : Color.Lerp(baseColor, Color.black, 0.2f);
          generatedFallbackAtlas.SetPixel(x, y, shaded);
        }
      }

      generatedFallbackAtlas.Apply(false, false);
      return generatedFallbackAtlas;
    }
  }
}