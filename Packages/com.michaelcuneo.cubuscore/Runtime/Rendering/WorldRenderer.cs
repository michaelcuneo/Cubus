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
    [SerializeField] private Material blockWorldMaterial;
    [SerializeField] private Material densityWorldMaterial;

    [SerializeField] private bool useBlockBiomeAtlasShader = true;
    [SerializeField] private bool useDensityBiomeShader = true;

    [SerializeField] private string blockBiomeAtlasShaderName = "Cubus/BiomeAtlasURP";
    [SerializeField] private string densityBiomeShaderName = "Cubus/DensityBiomeURP";
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
        NormalizeCollisionMode();
        RefreshChunkCollision();
      }
    }

    private float timeSinceLastCollisionUpdate;
    private readonly Dictionary<Vector3Int, ChunkView> activeChunkViews = new();
    private Material runtimeBlockMaterial;
    private Material runtimeDensityMaterial;
    private Texture2D generatedFallbackAtlas;
    private Texture2D topLookupTexture;
    private Texture2D sideLookupTexture;
    private Texture2D bottomLookupTexture;
    private Texture2D propsLookupTexture;

    private const int LookupTextureSize = 256;

    private CubusWorld world;
    private ChunkPool chunkPool;

    private Vector3Int lastCollisionViewerChunkCoord;
    private bool hasLastCollisionViewerChunkCoord;

    public IReadOnlyDictionary<Vector3Int, ChunkView> ActiveChunkViews => activeChunkViews;

    private void Awake()
    {
      NormalizeCollisionMode();
      world = GetComponent<CubusWorld>();
      chunkPool = new ChunkPool(transform);
      EnsureBlockWorldMaterial();
      EnsureDensityWorldMaterial();
    }

    private void OnValidate()
    {
      NormalizeCollisionMode();
    }

    private void Update()
    {
      if (collisionMode != WorldCollisionMode.NearViewerOnly)
      {
        return;
      }

      if (collisionViewer == null || world == null)
      {
        return;
      }

      timeSinceLastCollisionUpdate += Time.deltaTime;
      float updateInterval = Mathf.Max(0.01f, collisionUpdateInterval);
      if (timeSinceLastCollisionUpdate < updateInterval)
      {
        return;
      }

      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      Vector3 localViewerPosition = transform.InverseTransformPoint(collisionViewer.position);

      Vector3Int viewerChunkCoord = new(
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.x / voxelSize), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.y / voxelSize), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.z / voxelSize), VoxelConstants.ChunkSize)
      );

      if (hasLastCollisionViewerChunkCoord && viewerChunkCoord == lastCollisionViewerChunkCoord)
      {
        return;
      }

      timeSinceLastCollisionUpdate = 0.0f;
      lastCollisionViewerChunkCoord = viewerChunkCoord;
      hasLastCollisionViewerChunkCoord = true;

      RefreshChunkCollision();
    }

    public void SetCollisionViewer(Transform viewer)
    {
      collisionViewer = viewer;
      hasLastCollisionViewerChunkCoord = false;
      timeSinceLastCollisionUpdate = 0.0f;
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

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord, GetBlockRenderMaterial());

      bool shouldGenerateCollision = collisionMode switch
      {
        WorldCollisionMode.None => false,
        WorldCollisionMode.NearViewerOnly => IsChunkNearCollisionViewer(chunkView),
        _ => false
      };
      chunkView.ApplyMesh(meshData, shouldGenerateCollision);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
    }

    public void RenderUnityMesh(Vector3Int chunkCoord, Mesh unityMesh, bool generateCollision = false)
    {
      RenderDensityChunkMesh(chunkCoord, unityMesh, generateCollision);
    }

    public Material EnsureWorldMaterialAndGet()
    {
      return GetDensityRenderMaterial();
    }

    public void RenderDensityChunkMesh(Vector3Int chunkCoord, Mesh unityMesh, bool generateCollision = false)
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
              $"Skipping density mesh chunk {chunkCoord}. " +
              $"MeshNull={unityMesh == null}, " +
              $"Verts={(unityMesh != null ? unityMesh.vertexCount : 0)}, " +
              $"Indices={(unityMesh != null && unityMesh.subMeshCount > 0 ? unityMesh.GetIndexCount(0) : 0)}"
          );
        }

        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord, GetDensityRenderMaterial());

      bool shouldGenerateCollision =
          generateCollision &&
          collisionMode != WorldCollisionMode.None;

      chunkView.ApplyMesh(unityMesh, shouldGenerateCollision);

      if (logRenderedChunkMeshes)
      {
        Debug.Log(
            $"Rendered density mesh chunk {chunkCoord}. " +
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

        case WorldCollisionMode.NearViewerOnly:
        default:
          chunkView.SetCollisionEnabled(IsChunkNearCollisionViewer(chunkView));
          break;
      }
    }

    private void NormalizeCollisionMode()
    {
      if (collisionMode == WorldCollisionMode.None)
      {
        return;
      }

      collisionMode = WorldCollisionMode.NearViewerOnly;
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

      DestroyRuntimeMaterial(ref runtimeBlockMaterial, blockWorldMaterial);
      DestroyRuntimeMaterial(ref runtimeDensityMaterial, densityWorldMaterial);

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

          if (chunkData == null || !chunkData.HasSurfaceCrossing())
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

          RenderDensityChunkMesh(chunkCoord, unityMesh, false);
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

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord, GetBlockRenderMaterial());
      bool shouldGenerateCollision = collisionMode switch
      {
        WorldCollisionMode.None => false,
        WorldCollisionMode.NearViewerOnly => IsChunkNearCollisionViewer(chunkView),
        _ => false
      };
      chunkView.ApplyMesh(meshData, shouldGenerateCollision);
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
      hasLastCollisionViewerChunkCoord = false;
    }

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return activeChunkViews.ContainsKey(chunkCoord);
    }

    private ChunkView GetOrCreateChunkView(Vector3Int chunkCoord, Material material)
    {
      if (activeChunkViews.TryGetValue(chunkCoord, out ChunkView existingView) && existingView != null)
      {
        if (existingView.MeshRenderer != null)
        {
          existingView.MeshRenderer.sharedMaterial = material;
        }

        return existingView;
      }

      Material safeMaterial = material != null ? material : GetBlockRenderMaterial();

      ChunkView chunkView = chunkPool.Acquire(
          chunkCoord,
          world.Settings.VoxelSize,
          safeMaterial
      );

      activeChunkViews[chunkCoord] = chunkView;
      ApplyCollisionStateToChunk(chunkView);

      return chunkView;
    }

    private Material GetBlockRenderMaterial()
    {
      EnsureBlockWorldMaterial();
      return runtimeBlockMaterial != null ? runtimeBlockMaterial : blockWorldMaterial;
    }

    private Material GetDensityRenderMaterial()
    {
      EnsureDensityWorldMaterial();

      if (runtimeDensityMaterial != null)
      {
        return runtimeDensityMaterial;
      }

      if (densityWorldMaterial != null)
      {
        return densityWorldMaterial;
      }

      return GetBlockRenderMaterial();
    }

    private void EnsureBlockWorldMaterial()
    {
      if (!useBlockBiomeAtlasShader)
      {
        runtimeBlockMaterial = blockWorldMaterial;
        return;
      }

      Shader shader = Shader.Find(blockBiomeAtlasShaderName);
      if (!IsUsableShader(shader))
      {
        runtimeBlockMaterial = blockWorldMaterial;
        return;
      }

      if (runtimeBlockMaterial == null || runtimeBlockMaterial.shader != shader)
      {
        DestroyRuntimeMaterial(ref runtimeBlockMaterial, blockWorldMaterial);

        runtimeBlockMaterial = new Material(shader)
        {
          name = "Cubus Runtime Block Biome Atlas Material"
        };
      }

      ApplyCommonAtlasProperties(runtimeBlockMaterial);
      ApplyBlockMaterialLookups(runtimeBlockMaterial);

      if (runtimeBlockMaterial == null || !IsUsableShader(runtimeBlockMaterial.shader))
      {
        runtimeBlockMaterial = blockWorldMaterial;
      }
    }

    private void EnsureDensityWorldMaterial()
    {
      if (!useDensityBiomeShader)
      {
        runtimeDensityMaterial = densityWorldMaterial;
        return;
      }

      Shader shader = Shader.Find(densityBiomeShaderName);
      if (!IsUsableShader(shader))
      {
        runtimeDensityMaterial = densityWorldMaterial;
        return;
      }

      if (runtimeDensityMaterial == null || runtimeDensityMaterial.shader != shader)
      {
        DestroyRuntimeMaterial(ref runtimeDensityMaterial, densityWorldMaterial);

        runtimeDensityMaterial = new Material(shader)
        {
          name = "Cubus Runtime Density Biome Material"
        };
      }

      ApplyCommonAtlasProperties(runtimeDensityMaterial);
      ApplyDensityMaterialLookups(runtimeDensityMaterial);

      if (runtimeDensityMaterial == null || !IsUsableShader(runtimeDensityMaterial.shader))
      {
        runtimeDensityMaterial = densityWorldMaterial;
      }
    }

    private void ApplyCommonAtlasProperties(Material material)
    {
      if (material == null)
      {
        return;
      }

      Texture2D atlas = biomeTextureAtlas != null ? biomeTextureAtlas : GetOrCreateFallbackAtlas();

      material.SetTexture("_Atlas", atlas);
      material.SetVector(
          "_AtlasGrid",
          new Vector4(
              Mathf.Max(1, biomeAtlasGrid.x),
              Mathf.Max(1, biomeAtlasGrid.y),
              0.0f,
              0.0f
          )
      );
      material.SetColor("_Tint", biomeTint);

      if (material.HasProperty("_TextureScale"))
      {
        material.SetFloat("_TextureScale", 0.25f);
      }

      if (material.HasProperty("_TriplanarSharpness"))
      {
        material.SetFloat("_TriplanarSharpness", 4.0f);
      }

    }

    private void DestroyRuntimeMaterial(ref Material runtimeMaterial, Material serializedMaterial)
    {
      if (runtimeMaterial == null || runtimeMaterial == serializedMaterial)
      {
        runtimeMaterial = null;
        return;
      }

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

    private void ApplyBlockMaterialLookups(Material material)
    {
      if (material == null)
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
        ushort fallbackTileId = id == 0 ? (ushort)1 : (ushort)Mathf.Clamp(id, 1, 64);
        Color32 encodedFallbackTile = EncodeU16(fallbackTileId);

        topPixels[id] = encodedFallbackTile;
        sidePixels[id] = encodedFallbackTile;
        bottomPixels[id] = encodedFallbackTile;

        propsPixels[id] = new Color32(
            (byte)BlockRenderCategory.Opaque,
            (byte)BlockLightingCategory.Lit,
            0,
            255
        );
      }

      ApplyDefaultMaterialMapping(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels
      );

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

          topPixels[materialId] = EncodeU16(
              (ushort)Mathf.Clamp(def.TopTileId, 1, 64)
          );

          sidePixels[materialId] = EncodeU16(
              (ushort)Mathf.Clamp(def.SideTileId, 1, 64)
          );

          bottomPixels[materialId] = EncodeU16(
              (ushort)Mathf.Clamp(def.BottomTileId, 1, 64)
          );

          propsPixels[materialId] = new Color32(
              (byte)def.RenderCategory,
              (byte)def.LightingCategory,
              (byte)Mathf.Clamp(
                  Mathf.RoundToInt(def.EmissionIntensity * 31.875f),
                  0,
                  255
              ),
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

      material.SetTexture("_TopLookup", topLookupTexture);
      material.SetTexture("_SideLookup", sideLookupTexture);
      material.SetTexture("_BottomLookup", bottomLookupTexture);
      material.SetTexture("_PropsLookup", propsLookupTexture);
    }

    private void ApplyDensityMaterialLookups(Material material)
    {
      if (material == null)
      {
        return;
      }

      EnsureLookupTextures();

      int texelCount = LookupTextureSize * LookupTextureSize;
      Color32[] densityPixels = new Color32[texelCount];
      Color32[] propsPixels = new Color32[texelCount];

      for (int id = 0; id < texelCount; id++)
      {
        ushort fallbackTileId = id == 0 ? (ushort)1 : (ushort)Mathf.Clamp(id, 1, 64);

        densityPixels[id] = EncodeU16(fallbackTileId);

        propsPixels[id] = new Color32(
            (byte)BlockRenderCategory.Opaque,
            (byte)BlockLightingCategory.Lit,
            0,
            255
        );
      }

      ApplyDefaultDensityMaterialMapping(densityPixels, propsPixels);

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

          // For density terrain we need one smooth material tile, not top/side/bottom.
          // Use SideTileId as the first pass because side textures usually look best
          // on slopes and caves. Later this should become def.DensityTileId.
          densityPixels[materialId] = EncodeU16(
              (ushort)Mathf.Clamp(def.SideTileId, 1, 64)
          );

          propsPixels[materialId] = new Color32(
              (byte)def.RenderCategory,
              (byte)def.LightingCategory,
              (byte)Mathf.Clamp(
                  Mathf.RoundToInt(def.EmissionIntensity * 31.875f),
                  0,
                  255
              ),
              255
          );
        }
      }

      topLookupTexture.SetPixels32(densityPixels);
      propsLookupTexture.SetPixels32(propsPixels);

      topLookupTexture.Apply(false, false);
      propsLookupTexture.Apply(false, false);

      // Use both names so your new shader can call it either _DensityLookup or
      // _TopLookup while you are wiring it up.
      material.SetTexture("_DensityLookup", topLookupTexture);
      material.SetTexture("_TopLookup", topLookupTexture);
      material.SetTexture("_PropsLookup", propsLookupTexture);
    }

    private static void ApplyDefaultDensityMaterialMapping(
    Color32[] densityPixels,
    Color32[] propsPixels)
    {
      for (int materialId = 1; materialId <= 20; materialId++)
      {
        int tileId = materialId;

        densityPixels[materialId] = EncodeU16(
            (ushort)Mathf.Clamp(tileId, 1, 64)
        );

        propsPixels[materialId] = new Color32(
            (byte)BlockRenderCategory.Opaque,
            (byte)BlockLightingCategory.Lit,
            0,
            255
        );
      }
    }

    private static void ApplyDefaultMaterialMapping(
    Color32[] topPixels,
    Color32[] sidePixels,
    Color32[] bottomPixels,
    Color32[] propsPixels)
    {
      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 1,
          topTileId: 1,
          sideTileId: 2,
          bottomTileId: 3,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 2,
          topTileId: 2,
          sideTileId: 2,
          bottomTileId: 3,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 3,
          topTileId: 3,
          sideTileId: 3,
          bottomTileId: 3,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 4,
          topTileId: 4,
          sideTileId: 4,
          bottomTileId: 4,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 5,
          topTileId: 5,
          sideTileId: 5,
          bottomTileId: 5,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 6,
          topTileId: 6,
          sideTileId: 6,
          bottomTileId: 6,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 7,
          topTileId: 7,
          sideTileId: 7,
          bottomTileId: 7,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 8,
          topTileId: 8,
          sideTileId: 8,
          bottomTileId: 8,
          renderCategory: BlockRenderCategory.Transparent,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 9,
          topTileId: 9,
          sideTileId: 9,
          bottomTileId: 9,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 10,
          topTileId: 10,
          sideTileId: 10,
          bottomTileId: 10,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 11,
          topTileId: 11,
          sideTileId: 11,
          bottomTileId: 11,
          renderCategory: BlockRenderCategory.Transparent,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 12,
          topTileId: 12,
          sideTileId: 12,
          bottomTileId: 12,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Emissive,
          emissionIntensity: 4.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 13,
          topTileId: 13,
          sideTileId: 14,
          bottomTileId: 13,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 14,
          topTileId: 14,
          sideTileId: 14,
          bottomTileId: 14,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 15,
          topTileId: 15,
          sideTileId: 15,
          bottomTileId: 15,
          renderCategory: BlockRenderCategory.Cutout,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 16,
          topTileId: 16,
          sideTileId: 16,
          bottomTileId: 16,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 17,
          topTileId: 17,
          sideTileId: 17,
          bottomTileId: 17,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 18,
          topTileId: 18,
          sideTileId: 18,
          bottomTileId: 18,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 19,
          topTileId: 19,
          sideTileId: 19,
          bottomTileId: 19,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );

      SetMaterialLookup(
          topPixels,
          sidePixels,
          bottomPixels,
          propsPixels,
          materialId: 20,
          topTileId: 20,
          sideTileId: 20,
          bottomTileId: 20,
          renderCategory: BlockRenderCategory.Opaque,
          lightingCategory: BlockLightingCategory.Lit,
          emissionIntensity: 0.0f
      );
    }

    private static void SetMaterialLookup(
    Color32[] topPixels,
    Color32[] sidePixels,
    Color32[] bottomPixels,
    Color32[] propsPixels,
    int materialId,
    int topTileId,
    int sideTileId,
    int bottomTileId,
    BlockRenderCategory renderCategory,
    BlockLightingCategory lightingCategory,
    float emissionIntensity)
    {
      int safeMaterialId = Mathf.Clamp(materialId, 1, 65535);

      topPixels[safeMaterialId] = EncodeU16(
          (ushort)Mathf.Clamp(topTileId, 1, 64)
      );

      sidePixels[safeMaterialId] = EncodeU16(
          (ushort)Mathf.Clamp(sideTileId, 1, 64)
      );

      bottomPixels[safeMaterialId] = EncodeU16(
          (ushort)Mathf.Clamp(bottomTileId, 1, 64)
      );

      propsPixels[safeMaterialId] = new Color32(
          (byte)renderCategory,
          (byte)lightingCategory,
          (byte)Mathf.Clamp(
              Mathf.RoundToInt(emissionIntensity * 31.875f),
              0,
              255
          ),
          255
      );
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