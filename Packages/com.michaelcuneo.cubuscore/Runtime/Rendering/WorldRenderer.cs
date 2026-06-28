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

    [Header("Surface Detail")]
    [SerializeField] private Texture2D biomeNormalAtlas;
    [SerializeField][Range(0f, 2f)] private float normalStrength = 1.0f;
    [SerializeField][Range(0f, 4f)] private float detailBumpStrength = 0.0f;
    [SerializeField][Range(0f, 1f)] private float surfaceSmoothness = 0.12f;
    [SerializeField][Range(0f, 2f)] private float specularStrength = 0.25f;
    [SerializeField][Range(0f, 2f)] private float fresnelStrength = 0.15f;

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
    private readonly Dictionary<Vector3Int, ChunkView> activeBlockChunkViews = new();
    private readonly Dictionary<Vector3Int, ChunkView> activeDensityChunkViews = new();

    private Material runtimeBlockMaterial;
    private Material runtimeDensityMaterial;
    private Texture2D generatedFallbackAtlas;
    private Texture2D topLookupTexture;
    private Texture2D sideLookupTexture;
    private Texture2D bottomLookupTexture;
    private Texture2D propsLookupTexture;

    private const int LookupTextureSize = 256;

    private enum MaterialAtlasSide
    {
      Top,
      Side,
      Bottom,
      Props
    }

    private CubusWorld world;
    private ChunkPool chunkPool;
    private WorldDetailRenderer detailRenderer;
    private bool hasResolvedDetailRenderer;

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
      foreach (var pair in activeBlockChunkViews)
      {
        if (pair.Value != null)
        {
          ApplyCollisionStateToChunk(pair.Value);
        }
      }

      foreach (var pair in activeDensityChunkViews)
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
        RemoveBlockChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateBlockChunkView(chunkCoord);

      bool shouldGenerateCollision = collisionMode switch
      {
        WorldCollisionMode.None => false,
        WorldCollisionMode.NearViewerOnly => IsChunkNearCollisionViewer(chunkView),
        _ => false
      };
      chunkView.ApplyMesh(meshData, shouldGenerateCollision);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
      ResolveDetailRenderer()?.RefreshChunkDetail(chunkCoord);
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

        RemoveDensityChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateDensityChunkView(chunkCoord);

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

      if (world.Settings.TerrainSystem == TerrainSystem.Block || world.Settings.TerrainSystem == TerrainSystem.Hybrid)
      {
        foreach (KeyValuePair<Vector3Int, BlockChunkData> pair in world.Data.BlockChunks)
        {
          RenderBlockChunk(pair.Key, pair.Value);
        }
      }

      if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity || world.Settings.TerrainSystem == TerrainSystem.Hybrid)
      {
        var snapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
        int rendered = 0;

        foreach (KeyValuePair<Vector3Int, DensityChunkData> pair in world.Data.DensityChunks)
        {
          var chunkCoord = pair.Key;
          var chunkData = pair.Value;

          if (chunkData == null || !chunkData.HasSurfaceCrossing())
          {
            RemoveDensityChunk(chunkCoord);
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
            RemoveDensityChunk(chunkCoord);
            continue;
          }

          RenderDensityChunkMesh(chunkCoord, unityMesh, false);
          rendered++;
        }

        Debug.Log($"Rendered density chunks: {rendered}");
      }

      Debug.Log($"Rendered chunks. Coords={activeChunkViews.Count}, BlockViews={activeBlockChunkViews.Count}, DensityViews={activeDensityChunkViews.Count}");
    }

    public void RenderBlockChunk(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      if (chunkData == null || !chunkData.HasAnySolidVoxel())
      {
        RemoveBlockChunk(chunkCoord);
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
        RemoveBlockChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateBlockChunkView(chunkCoord);
      bool shouldGenerateCollision = collisionMode switch
      {
        WorldCollisionMode.None => false,
        WorldCollisionMode.NearViewerOnly => IsChunkNearCollisionViewer(chunkView),
        _ => false
      };
      chunkView.ApplyMesh(meshData, shouldGenerateCollision);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
      ResolveDetailRenderer()?.RefreshChunkDetail(chunkCoord);
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
          if (RemoveBlockChunk(chunkCoord))
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
      bool removedBlock = RemoveBlockChunk(chunkCoord);
      bool removedDensity = RemoveDensityChunk(chunkCoord);
      return removedBlock || removedDensity;
    }

    private bool RemoveBlockChunk(Vector3Int chunkCoord)
    {
      ResolveDetailRenderer()?.RemoveChunkDetail(chunkCoord);
      return RemoveLayerChunk(chunkCoord, activeBlockChunkViews);
    }

    private bool RemoveDensityChunk(Vector3Int chunkCoord)
    {
      return RemoveLayerChunk(chunkCoord, activeDensityChunkViews);
    }

    private bool RemoveLayerChunk(Vector3Int chunkCoord, Dictionary<Vector3Int, ChunkView> layerViews)
    {
      if (!layerViews.TryGetValue(chunkCoord, out ChunkView chunkView))
      {
        return false;
      }

      layerViews.Remove(chunkCoord);
      UpdateActiveChunkViewIndex(chunkCoord);

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
      ClearLayerViews(activeBlockChunkViews);
      ClearLayerViews(activeDensityChunkViews);
      activeChunkViews.Clear();
      hasLastCollisionViewerChunkCoord = false;
      ResolveDetailRenderer()?.ClearAllDetail();
    }

    private void ClearLayerViews(Dictionary<Vector3Int, ChunkView> layerViews)
    {
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in layerViews)
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

      layerViews.Clear();
    }

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return activeBlockChunkViews.ContainsKey(chunkCoord) || activeDensityChunkViews.ContainsKey(chunkCoord);
    }

    private WorldDetailRenderer ResolveDetailRenderer()
    {
      if (!hasResolvedDetailRenderer)
      {
        detailRenderer = GetComponent<WorldDetailRenderer>();
        hasResolvedDetailRenderer = true;
      }

      return detailRenderer;
    }

    private ChunkView GetOrCreateBlockChunkView(Vector3Int chunkCoord)
    {
      return GetOrCreateLayerChunkView(chunkCoord, activeBlockChunkViews, GetBlockRenderMaterial());
    }

    private ChunkView GetOrCreateDensityChunkView(Vector3Int chunkCoord)
    {
      return GetOrCreateLayerChunkView(chunkCoord, activeDensityChunkViews, GetDensityRenderMaterial());
    }

    private ChunkView GetOrCreateLayerChunkView(Vector3Int chunkCoord, Dictionary<Vector3Int, ChunkView> layerViews, Material material)
    {
      if (layerViews.TryGetValue(chunkCoord, out ChunkView existingView) && existingView != null)
      {
        if (existingView.MeshRenderer != null)
        {
          existingView.MeshRenderer.sharedMaterial = material;
        }

        UpdateActiveChunkViewIndex(chunkCoord);
        return existingView;
      }

      Material safeMaterial = material != null ? material : GetBlockRenderMaterial();

      ChunkView chunkView = chunkPool.Acquire(
          chunkCoord,
          world.Settings.VoxelSize,
          safeMaterial
      );

      layerViews[chunkCoord] = chunkView;
      UpdateActiveChunkViewIndex(chunkCoord);
      ApplyCollisionStateToChunk(chunkView);

      return chunkView;
    }

    private void UpdateActiveChunkViewIndex(Vector3Int chunkCoord)
    {
      if (activeBlockChunkViews.TryGetValue(chunkCoord, out ChunkView blockView) && blockView != null)
      {
        activeChunkViews[chunkCoord] = blockView;
        return;
      }

      if (activeDensityChunkViews.TryGetValue(chunkCoord, out ChunkView densityView) && densityView != null)
      {
        activeChunkViews[chunkCoord] = densityView;
        return;
      }

      activeChunkViews.Remove(chunkCoord);
    }

    private Material GetBlockRenderMaterial()
    {
      EnsureBlockWorldMaterial();
      return runtimeBlockMaterial != null ? runtimeBlockMaterial : blockWorldMaterial;
    }

    private Material GetDensityRenderMaterial()
    {
      EnsureDensityWorldMaterial();
      return runtimeDensityMaterial != null ? runtimeDensityMaterial : densityWorldMaterial;
    }

    private void EnsureBlockWorldMaterial()
    {
      if (runtimeBlockMaterial != null || blockWorldMaterial != null)
      {
        return;
      }

      Shader shader = useBlockBiomeAtlasShader ? Shader.Find(blockBiomeAtlasShaderName) : null;
      if (shader == null)
      {
        shader = Shader.Find("Universal Render Pipeline/Lit");
      }
      if (shader == null)
      {
        shader = Shader.Find("Standard");
      }

      runtimeBlockMaterial = new Material(shader)
      {
        name = "Cubus Runtime Block Material"
      };
      runtimeBlockMaterial.color = Color.white;
      ConfigureBiomeAtlasMaterial(runtimeBlockMaterial);
    }

    private void EnsureDensityWorldMaterial()
    {
      if (runtimeDensityMaterial != null || densityWorldMaterial != null)
      {
        return;
      }

      Shader shader = useDensityBiomeShader ? Shader.Find(densityBiomeShaderName) : null;
      if (shader == null)
      {
        shader = Shader.Find("Universal Render Pipeline/Lit");
      }
      if (shader == null)
      {
        shader = Shader.Find("Standard");
      }

      runtimeDensityMaterial = new Material(shader)
      {
        name = "Cubus Runtime Density Material"
      };
      runtimeDensityMaterial.color = Color.white;
      ConfigureBiomeAtlasMaterial(runtimeDensityMaterial);
    }

    private void ConfigureBiomeAtlasMaterial(Material material)
    {
      if (material == null)
      {
        return;
      }

      if (biomeTextureAtlas != null)
      {
        material.SetTexture("_BiomeAtlas", biomeTextureAtlas);
        material.SetTexture("_BaseMap", biomeTextureAtlas);
        material.mainTexture = biomeTextureAtlas;
      }

      material.SetColor("_BiomeTint", biomeTint);
      material.SetVector("_AtlasGrid", new Vector4(
        Mathf.Max(1, biomeAtlasGrid.x),
        Mathf.Max(1, biomeAtlasGrid.y),
        0,
        0
      ));

      ConfigureMaterialLookupTextures(material);
    }

    private void ConfigureMaterialLookupTextures(Material material)
    {
      if (blockMaterialDatabase == null || material == null)
      {
        return;
      }

      EnsureLookupTextures();

      material.SetTexture("_TopAtlasLookup", topLookupTexture);
      material.SetTexture("_SideAtlasLookup", sideLookupTexture);
      material.SetTexture("_BottomAtlasLookup", bottomLookupTexture);
      material.SetTexture("_PropsAtlasLookup", propsLookupTexture);
      material.SetFloat("_NormalStrength", normalStrength);
      material.SetFloat("_DetailBumpStrength", detailBumpStrength);
      material.SetFloat("_SurfaceSmoothness", surfaceSmoothness);
      material.SetFloat("_SpecularStrength", specularStrength);
      material.SetFloat("_FresnelStrength", fresnelStrength);
    }

    private void EnsureLookupTextures()
    {
      topLookupTexture ??= CreateLookupTexture("Cubus Top Atlas Lookup");
      sideLookupTexture ??= CreateLookupTexture("Cubus Side Atlas Lookup");
      bottomLookupTexture ??= CreateLookupTexture("Cubus Bottom Atlas Lookup");
      propsLookupTexture ??= CreateLookupTexture("Cubus Props Atlas Lookup");
      PopulateLookupTexture(topLookupTexture, MaterialAtlasSide.Top);
      PopulateLookupTexture(sideLookupTexture, MaterialAtlasSide.Side);
      PopulateLookupTexture(bottomLookupTexture, MaterialAtlasSide.Bottom);
      PopulateLookupTexture(propsLookupTexture, MaterialAtlasSide.Props);
    }

    private Texture2D CreateLookupTexture(string textureName)
    {
      Texture2D texture = new(LookupTextureSize, 1, TextureFormat.RGBA32, false, true)
      {
        name = textureName,
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp
      };
      return texture;
    }

    private void PopulateLookupTexture(Texture2D texture, MaterialAtlasSide side)
    {
      if (texture == null || blockMaterialDatabase == null)
      {
        return;
      }

      Color[] pixels = new Color[LookupTextureSize];

      for (int i = 0; i < pixels.Length; i++)
      {
        pixels[i] = new Color(0, 0, 0, 0);
      }

      for (int i = 0; i < blockMaterialDatabase.Materials.Count; i++)
      {
        BlockMaterialDefinition definition = blockMaterialDatabase.Materials[i];
        if (definition == null)
        {
          continue;
        }

        int materialId = Mathf.Clamp(definition.MaterialId, 0, LookupTextureSize - 1);
        Vector2Int atlasCoord = side switch
        {
          MaterialAtlasSide.Top => definition.TopAtlasCoord,
          MaterialAtlasSide.Side => definition.SideAtlasCoord,
          MaterialAtlasSide.Bottom => definition.BottomAtlasCoord,
          MaterialAtlasSide.Props => definition.PropsAtlasCoord,
          _ => Vector2Int.zero
        };

        pixels[materialId] = new Color(
          atlasCoord.x / 255.0f,
          atlasCoord.y / 255.0f,
          0,
          1
        );
      }

      texture.SetPixels(pixels);
      texture.Apply(false, false);
    }

    private void DestroyRuntimeMaterial(ref Material runtimeMaterial, Material assignedMaterial)
    {
      if (runtimeMaterial == null || runtimeMaterial == assignedMaterial)
      {
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

    private void DestroyLookupTexture(ref Texture2D texture)
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
  }
}
