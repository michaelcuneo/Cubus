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

    private readonly Dictionary<Vector3Int, ChunkView> activeChunkViews = new();
    private readonly Dictionary<Vector3Int, ChunkView> activeBlockChunkViews = new();
    private readonly Dictionary<Vector3Int, ChunkView> activeDensityChunkViews = new();

    private Material runtimeBlockMaterial;
    private Material runtimeDensityMaterial;
    private Texture2D topLookupTexture;
    private Texture2D sideLookupTexture;
    private Texture2D bottomLookupTexture;
    private Texture2D propsLookupTexture;

    private CubusWorld world;
    private ChunkPool chunkPool;
    private WorldDetailRenderer detailRenderer;
    private bool hasResolvedDetailRenderer;
    private Vector3Int lastCollisionViewerChunkCoord;
    private bool hasLastCollisionViewerChunkCoord;
    private float timeSinceLastCollisionUpdate;

    private const int LookupTextureSize = 256;

    private enum MaterialAtlasSide
    {
      Top,
      Side,
      Bottom,
      Props
    }

    public IReadOnlyDictionary<Vector3Int, ChunkView> ActiveChunkViews => activeChunkViews;

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
      if (collisionMode != WorldCollisionMode.NearViewerOnly || collisionViewer == null || world == null)
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
        VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.z / voxelSize), VoxelConstants.ChunkSize));

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
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeBlockChunkViews)
      {
        ApplyCollisionStateToChunk(pair.Value);
      }

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeDensityChunkViews)
      {
        ApplyCollisionStateToChunk(pair.Value);
      }
    }

    public Material EnsureWorldMaterialAndGet()
    {
      return GetDensityRenderMaterial();
    }

    public void RenderUnityMesh(Vector3Int chunkCoord, Mesh unityMesh, bool generateCollision = false)
    {
      RenderDensityChunkMesh(chunkCoord, unityMesh, generateCollision);
    }

    public void RenderBlockChunkMesh(Vector3Int chunkCoord, MeshData meshData)
    {
      EnsureRuntimeReferences();

      if (meshData == null || meshData.IsEmpty)
      {
        RemoveBlockChunkMesh(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateBlockChunkView(chunkCoord);
      chunkView.ApplyMesh(meshData, ShouldGenerateCollisionForChunk(chunkView));
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
      ResolveDetailRenderer()?.RefreshChunkDetail(chunkCoord);
    }

    public void RenderDensityChunkMesh(Vector3Int chunkCoord, Mesh unityMesh, bool generateCollision = false)
    {
      EnsureRuntimeReferences();

      if (unityMesh == null || unityMesh.vertexCount == 0 || unityMesh.subMeshCount == 0 || unityMesh.GetIndexCount(0) == 0)
      {
        if (logRenderedChunkMeshes)
        {
          Debug.LogWarning($"Skipping density mesh chunk {chunkCoord}. MeshNull={unityMesh == null}, Verts={(unityMesh != null ? unityMesh.vertexCount : 0)}");
        }

        RemoveDensityChunkMesh(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateDensityChunkView(chunkCoord);
      chunkView.ApplyMesh(unityMesh, generateCollision && collisionMode != WorldCollisionMode.None);
      ApplyCollisionStateToChunk(chunkView);

      if (logRenderedChunkMeshes)
      {
        Debug.Log($"Rendered density mesh chunk {chunkCoord}. Verts={unityMesh.vertexCount}, Indices={unityMesh.GetIndexCount(0)}, Material={(chunkView.MeshRenderer != null ? chunkView.MeshRenderer.sharedMaterial : null)}");
      }
    }

    [ContextMenu("Rebuild All")]
    public void RebuildAll()
    {
      EnsureRuntimeReferences();

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
        WorldGenerationSnapshot snapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
        foreach (KeyValuePair<Vector3Int, DensityChunkData> pair in world.Data.DensityChunks)
        {
          if (pair.Value == null || !pair.Value.HasSurfaceCrossing())
          {
            RemoveDensityChunkMesh(pair.Key);
            continue;
          }

          Mesh mesh = MarchingCubesMesher.GenerateMeshDirect(
            pair.Key,
            snapshot,
            Mathf.Max(1, world.Settings.DensityMeshStep),
            true,
            world.GetDensityVoxelAtWorldVoxel);

          RenderDensityChunkMesh(pair.Key, mesh, false);
        }
      }

      Debug.Log($"Rendered chunks. Coords={activeChunkViews.Count}, BlockViews={activeBlockChunkViews.Count}, DensityViews={activeDensityChunkViews.Count}");
    }

    public void RenderBlockChunk(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      EnsureRuntimeReferences();

      if (chunkData == null || !chunkData.HasAnySolidVoxel())
      {
        RemoveBlockChunkMesh(chunkCoord);
        return;
      }

      MeshData meshData = MeshDataPool.Rent(4096, 6144);
      BlockGreedyMesher.GenerateNeighbourAware(
        chunkData,
        world.GetBlockMaterialAtWorldVoxel,
        world.Settings.VoxelSize,
        meshData);

      if (meshData.IsEmpty)
      {
        MeshDataPool.Return(meshData);
        RemoveBlockChunkMesh(chunkCoord);
        return;
      }

      RenderBlockChunkMesh(chunkCoord, meshData);
    }

    public void RebuildBlockChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      EnsureRuntimeReferences();
      int rebuiltCount = 0;
      int removedCount = 0;

      foreach (Vector3Int chunkCoord in dirtyChunks)
      {
        if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData) || chunkData == null || !chunkData.HasAnySolidVoxel())
        {
          if (RemoveBlockChunkMesh(chunkCoord)) removedCount++;
          continue;
        }

        RenderBlockChunk(chunkCoord, chunkData);
        rebuiltCount++;
      }

      Debug.Log($"Rebuilt block chunks. Rebuilt={rebuiltCount}, Removed={removedCount}");
    }

    public bool RemoveChunk(Vector3Int chunkCoord)
    {
      bool removedBlock = RemoveBlockChunkMesh(chunkCoord);
      bool removedDensity = RemoveDensityChunkMesh(chunkCoord);
      return removedBlock || removedDensity;
    }

    public bool RemoveBlockChunkMesh(Vector3Int chunkCoord)
    {
      ResolveDetailRenderer()?.RemoveChunkDetail(chunkCoord);
      return RemoveLayerChunk(chunkCoord, activeBlockChunkViews);
    }

    public bool RemoveDensityChunkMesh(Vector3Int chunkCoord)
    {
      return RemoveLayerChunk(chunkCoord, activeDensityChunkViews);
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

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return activeBlockChunkViews.ContainsKey(chunkCoord) || activeDensityChunkViews.ContainsKey(chunkCoord);
    }

    private void EnsureRuntimeReferences()
    {
      if (world == null) world = GetComponent<CubusWorld>();
      chunkPool ??= new ChunkPool(transform);
      EnsureBlockWorldMaterial();
      EnsureDensityWorldMaterial();
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

      ChunkView chunkView = chunkPool.Acquire(chunkCoord, world.Settings.VoxelSize, material);
      layerViews[chunkCoord] = chunkView;
      UpdateActiveChunkViewIndex(chunkCoord);
      ApplyCollisionStateToChunk(chunkView);
      return chunkView;
    }

    private bool RemoveLayerChunk(Vector3Int chunkCoord, Dictionary<Vector3Int, ChunkView> layerViews)
    {
      if (!layerViews.TryGetValue(chunkCoord, out ChunkView chunkView))
      {
        return false;
      }

      layerViews.Remove(chunkCoord);
      UpdateActiveChunkViewIndex(chunkCoord);

      if (chunkPool != null) chunkPool.Release(chunkView);
      else if (chunkView != null)
      {
        if (Application.isPlaying) Destroy(chunkView.gameObject);
        else DestroyImmediate(chunkView.gameObject);
      }

      return true;
    }

    private void ClearLayerViews(Dictionary<Vector3Int, ChunkView> layerViews)
    {
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in layerViews)
      {
        if (pair.Value == null) continue;
        if (chunkPool != null) chunkPool.Release(pair.Value);
        else if (Application.isPlaying) Destroy(pair.Value.gameObject);
        else DestroyImmediate(pair.Value.gameObject);
      }

      layerViews.Clear();
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

    private WorldDetailRenderer ResolveDetailRenderer()
    {
      if (!hasResolvedDetailRenderer)
      {
        detailRenderer = GetComponent<WorldDetailRenderer>();
        hasResolvedDetailRenderer = true;
      }

      return detailRenderer;
    }

    private void NormalizeCollisionMode()
    {
      if (collisionMode != WorldCollisionMode.None)
      {
        collisionMode = WorldCollisionMode.NearViewerOnly;
      }
    }

    private bool ShouldGenerateCollisionForChunk(ChunkView chunkView)
    {
      return collisionMode switch
      {
        WorldCollisionMode.None => false,
        WorldCollisionMode.NearViewerOnly => IsChunkNearCollisionViewer(chunkView),
        _ => false
      };
    }

    private void ApplyCollisionStateToChunk(ChunkView chunkView)
    {
      if (chunkView == null) return;

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

    private bool IsChunkNearCollisionViewer(ChunkView chunkView)
    {
      if (chunkView == null || collisionViewer == null || world == null)
      {
        return true;
      }

      Vector3 chunkCenter = chunkView.transform.position + Vector3.one * world.Settings.VoxelSize * VoxelConstants.ChunkSize * 0.5f;
      return (chunkCenter - collisionViewer.position).sqrMagnitude <= collisionActivationRadius * collisionActivationRadius;
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
      if (runtimeBlockMaterial != null)
      {
        ConfigureTerrainMaterial(runtimeBlockMaterial);
        return;
      }

      runtimeBlockMaterial = CreateRuntimeTerrainMaterial(
        blockWorldMaterial,
        useBlockBiomeAtlasShader ? blockBiomeAtlasShaderName : null,
        "Cubus Runtime Block Material");
      ConfigureTerrainMaterial(runtimeBlockMaterial);
    }

    private void EnsureDensityWorldMaterial()
    {
      if (runtimeDensityMaterial != null)
      {
        ConfigureTerrainMaterial(runtimeDensityMaterial);
        return;
      }

      runtimeDensityMaterial = CreateRuntimeTerrainMaterial(
        densityWorldMaterial,
        useDensityBiomeShader ? densityBiomeShaderName : null,
        "Cubus Runtime Density Material");
      ConfigureTerrainMaterial(runtimeDensityMaterial);
    }

    private static Material CreateRuntimeTerrainMaterial(Material assignedMaterial, string preferredShaderName, string runtimeName)
    {
      if (assignedMaterial != null)
      {
        Material clone = new(assignedMaterial)
        {
          name = runtimeName + " (Instance)"
        };
        return clone;
      }

      Shader shader = !string.IsNullOrWhiteSpace(preferredShaderName) ? Shader.Find(preferredShaderName) : null;
      shader ??= Shader.Find("Universal Render Pipeline/Lit");
      shader ??= Shader.Find("Standard");
      return new Material(shader) { name = runtimeName };
    }

    private void ConfigureTerrainMaterial(Material material)
    {
      if (material == null)
      {
        return;
      }

      Texture mainTexture = biomeTextureAtlas != null ? biomeTextureAtlas : material.mainTexture;
      if (mainTexture != null)
      {
        SetTextureIfPropertyExists(material, "_BiomeAtlas", mainTexture);
        SetTextureIfPropertyExists(material, "_BaseMap", mainTexture);
        SetTextureIfPropertyExists(material, "_MainTex", mainTexture);
        material.mainTexture = mainTexture;
      }

      if (biomeNormalAtlas != null)
      {
        SetTextureIfPropertyExists(material, "_BiomeNormalAtlas", biomeNormalAtlas);
        SetTextureIfPropertyExists(material, "_NormalAtlas", biomeNormalAtlas);
        SetTextureIfPropertyExists(material, "_BumpMap", biomeNormalAtlas);
        material.EnableKeyword("_NORMALMAP");
      }

      SetColorIfPropertyExists(material, "_BiomeTint", biomeTint);
      SetColorIfPropertyExists(material, "_BaseColor", biomeTint);
      SetColorIfPropertyExists(material, "_Color", biomeTint);

      SetVectorIfPropertyExists(material, "_AtlasGrid", new Vector4(Mathf.Max(1, biomeAtlasGrid.x), Mathf.Max(1, biomeAtlasGrid.y), 0, 0));
      SetFloatIfPropertyExists(material, "_NormalStrength", normalStrength);
      SetFloatIfPropertyExists(material, "_DetailBumpStrength", detailBumpStrength);
      SetFloatIfPropertyExists(material, "_SurfaceSmoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_Smoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_SpecularStrength", specularStrength);
      SetFloatIfPropertyExists(material, "_FresnelStrength", fresnelStrength);

      ConfigureMaterialLookupTextures(material);
    }

    private void ConfigureMaterialLookupTextures(Material material)
    {
      if (material == null || blockMaterialDatabase == null || blockMaterialDatabase.Definitions == null)
      {
        return;
      }

      EnsureLookupTextures();
      SetTextureIfPropertyExists(material, "_TopAtlasLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_SideAtlasLookup", sideLookupTexture);
      SetTextureIfPropertyExists(material, "_BottomAtlasLookup", bottomLookupTexture);
      SetTextureIfPropertyExists(material, "_PropsAtlasLookup", propsLookupTexture);
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

    private static Texture2D CreateLookupTexture(string textureName)
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
      if (texture == null || blockMaterialDatabase == null || blockMaterialDatabase.Definitions == null)
      {
        return;
      }

      Color[] pixels = new Color[LookupTextureSize];
      int atlasColumns = Mathf.Max(1, biomeAtlasGrid.x);
      int atlasRows = Mathf.Max(1, biomeAtlasGrid.y);
      int atlasTileCount = Mathf.Max(1, atlasColumns * atlasRows);

      for (int i = 0; i < blockMaterialDatabase.Definitions.Count; i++)
      {
        BlockMaterialDefinition definition = blockMaterialDatabase.Definitions[i];
        if (definition == null) continue;

        int materialId = Mathf.Clamp(definition.MaterialId, 0, LookupTextureSize - 1);
        int tileId = side switch
        {
          MaterialAtlasSide.Top => definition.TopTileId,
          MaterialAtlasSide.Side => definition.SideTileId,
          MaterialAtlasSide.Bottom => definition.BottomTileId,
          MaterialAtlasSide.Props => definition.SideTileId,
          _ => definition.SideTileId
        };

        int zeroBasedTileId = Mathf.Clamp(tileId - 1, 0, atlasTileCount - 1);
        int atlasX = zeroBasedTileId % atlasColumns;
        int atlasY = zeroBasedTileId / atlasColumns;
        pixels[materialId] = new Color(atlasX / 255.0f, atlasY / 255.0f, 0, 1);
      }

      texture.SetPixels(pixels);
      texture.Apply(false, false);
    }

    private static void SetTextureIfPropertyExists(Material material, string propertyName, Texture texture)
    {
      if (material != null && texture != null && material.HasProperty(propertyName))
      {
        material.SetTexture(propertyName, texture);
      }
    }

    private static void SetColorIfPropertyExists(Material material, string propertyName, Color color)
    {
      if (material != null && material.HasProperty(propertyName))
      {
        material.SetColor(propertyName, color);
      }
    }

    private static void SetVectorIfPropertyExists(Material material, string propertyName, Vector4 value)
    {
      if (material != null && material.HasProperty(propertyName))
      {
        material.SetVector(propertyName, value);
      }
    }

    private static void SetFloatIfPropertyExists(Material material, string propertyName, float value)
    {
      if (material != null && material.HasProperty(propertyName))
      {
        material.SetFloat(propertyName, value);
      }
    }

    private void OnDestroy()
    {
      ClearAll();
      DestroyRuntimeMaterial(ref runtimeBlockMaterial, blockWorldMaterial);
      DestroyRuntimeMaterial(ref runtimeDensityMaterial, densityWorldMaterial);
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

    private static void DestroyRuntimeMaterial(ref Material runtimeMaterial, Material assignedMaterial)
    {
      if (runtimeMaterial == null || runtimeMaterial == assignedMaterial)
      {
        return;
      }

      if (Application.isPlaying) Destroy(runtimeMaterial);
      else DestroyImmediate(runtimeMaterial);
      runtimeMaterial = null;
    }

    private static void DestroyLookupTexture(ref Texture2D texture)
    {
      if (texture == null) return;
      if (Application.isPlaying) Destroy(texture);
      else DestroyImmediate(texture);
      texture = null;
    }
  }
}
