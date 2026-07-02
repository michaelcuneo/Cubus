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
    private Texture2D generatedFallbackAtlas;
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

    public bool HasBlockChunkView(Vector3Int chunkCoord)
    {
      return activeBlockChunkViews.ContainsKey(chunkCoord);
    }

    public bool HasDensityChunkView(Vector3Int chunkCoord)
    {
      return activeDensityChunkViews.ContainsKey(chunkCoord);
    }

    /// <summary>
    /// Shows or hides the renderers for a chunk (both block and density layers) for
    /// view-frustum culling. The chunk keeps its mesh, collider, and active state so
    /// it can be shown again instantly with no re-meshing.
    /// </summary>
    public void SetChunkRenderVisible(Vector3Int chunkCoord, bool visible)
    {
      if (activeBlockChunkViews.TryGetValue(chunkCoord, out ChunkView blockView) && blockView != null)
      {
        blockView.SetRenderVisible(visible);
      }

      if (activeDensityChunkViews.TryGetValue(chunkCoord, out ChunkView densityView) && densityView != null)
      {
        densityView.SetRenderVisible(visible);
      }
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
        ConfigureBlockMaterial(runtimeBlockMaterial);
        return;
      }

      runtimeBlockMaterial = CreateRuntimeTerrainMaterial(
        blockWorldMaterial,
        useBlockBiomeAtlasShader ? blockBiomeAtlasShaderName : null,
        "Cubus Runtime Block Material");
      ConfigureBlockMaterial(runtimeBlockMaterial);
    }

    private void EnsureDensityWorldMaterial()
    {
      if (runtimeDensityMaterial != null)
      {
        ConfigureDensityMaterial(runtimeDensityMaterial);
        return;
      }

      runtimeDensityMaterial = CreateRuntimeTerrainMaterial(
        densityWorldMaterial,
        useDensityBiomeShader ? densityBiomeShaderName : null,
        "Cubus Runtime Density Material");
      ConfigureDensityMaterial(runtimeDensityMaterial);
    }

    private static Material CreateRuntimeTerrainMaterial(Material assignedMaterial, string preferredShaderName, string runtimeName)
    {
      if (assignedMaterial != null)
      {
        return new Material(assignedMaterial) { name = runtimeName + " (Instance)" };
      }

      Shader shader = !string.IsNullOrWhiteSpace(preferredShaderName) ? Shader.Find(preferredShaderName) : null;
      shader = IsUsableShader(shader) ? shader : Shader.Find("Universal Render Pipeline/Lit");
      shader = IsUsableShader(shader) ? shader : Shader.Find("Standard");
      return new Material(shader) { name = runtimeName };
    }

    private static bool IsUsableShader(Shader shader)
    {
      if (shader == null || !shader.isSupported || shader.passCount <= 0) return false;
      string shaderName = shader.name;
      return !string.IsNullOrEmpty(shaderName) && !shaderName.Contains("InternalErrorShader") && !shaderName.Contains("FallbackError");
    }

    private void ConfigureBlockMaterial(Material material)
    {
      ApplyCommonAtlasProperties(material);
      ApplyBlockMaterialLookups(material);
    }

    private void ConfigureDensityMaterial(Material material)
    {
      ApplyCommonAtlasProperties(material);
      ApplyDensityMaterialLookups(material);
    }

    private void ApplyCommonAtlasProperties(Material material)
    {
      if (material == null) return;

      Texture2D atlas = biomeTextureAtlas != null ? biomeTextureAtlas : GetOrCreateFallbackAtlas();

      SetTextureIfPropertyExists(material, "_Atlas", atlas);
      SetTextureIfPropertyExists(material, "_BiomeAtlas", atlas);
      SetTextureIfPropertyExists(material, "_BaseMap", atlas);
      SetTextureIfPropertyExists(material, "_MainTex", atlas);
      material.mainTexture = atlas;

      SetColorIfPropertyExists(material, "_Tint", biomeTint);
      SetColorIfPropertyExists(material, "_BiomeTint", biomeTint);
      SetColorIfPropertyExists(material, "_BaseColor", biomeTint);
      SetColorIfPropertyExists(material, "_Color", biomeTint);

      SetVectorIfPropertyExists(material, "_AtlasGrid", new Vector4(Mathf.Max(1, biomeAtlasGrid.x), Mathf.Max(1, biomeAtlasGrid.y), 0, 0));
      SetFloatIfPropertyExists(material, "_TextureScale", 0.25f);
      SetFloatIfPropertyExists(material, "_TriplanarSharpness", 4.0f);

      if (biomeNormalAtlas != null)
      {
        SetTextureIfPropertyExists(material, "_NormalAtlas", biomeNormalAtlas);
        SetTextureIfPropertyExists(material, "_BiomeNormalAtlas", biomeNormalAtlas);
        SetTextureIfPropertyExists(material, "_BumpMap", biomeNormalAtlas);
        material.EnableKeyword("_NORMALMAP");
      }

      SetFloatIfPropertyExists(material, "_NormalStrength", normalStrength);
      SetFloatIfPropertyExists(material, "_DetailBumpStrength", detailBumpStrength);
      SetFloatIfPropertyExists(material, "_Smoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_SurfaceSmoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_SpecularStrength", specularStrength);
      SetFloatIfPropertyExists(material, "_FresnelStrength", fresnelStrength);
    }

    private void ApplyBlockMaterialLookups(Material material)
    {
      if (material == null) return;
      EnsureLookupTextures();

      int texelCount = LookupTextureSize * LookupTextureSize;
      Color32[] topPixels = new Color32[texelCount];
      Color32[] sidePixels = new Color32[texelCount];
      Color32[] bottomPixels = new Color32[texelCount];
      Color32[] propsPixels = new Color32[texelCount];

      for (int id = 0; id < texelCount; id++)
      {
        ushort fallbackTileId = (ushort)Mathf.Clamp(id == 0 ? 1 : id, 1, 64);
        Color32 encodedFallbackTile = EncodeU16(fallbackTileId);
        topPixels[id] = encodedFallbackTile;
        sidePixels[id] = encodedFallbackTile;
        bottomPixels[id] = encodedFallbackTile;
        propsPixels[id] = new Color32((byte)BlockRenderCategory.Opaque, (byte)BlockLightingCategory.Lit, 0, 255);
      }

      ApplyDefaultMaterialMapping(topPixels, sidePixels, bottomPixels, propsPixels);

      if (blockMaterialDatabase != null && blockMaterialDatabase.Definitions != null)
      {
        for (int i = 0; i < blockMaterialDatabase.Definitions.Count; i++)
        {
          BlockMaterialDefinition def = blockMaterialDatabase.Definitions[i];
          if (def == null) continue;

          int materialId = Mathf.Clamp(def.MaterialId, 1, 65535);
          topPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.TopTileId, 1, 64));
          sidePixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.SideTileId, 1, 64));
          bottomPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.BottomTileId, 1, 64));
          propsPixels[materialId] = new Color32(
            (byte)def.RenderCategory,
            (byte)def.LightingCategory,
            (byte)Mathf.Clamp(Mathf.RoundToInt(def.EmissionIntensity * 31.875f), 0, 255),
            255);
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

      SetTextureIfPropertyExists(material, "_TopLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_SideLookup", sideLookupTexture);
      SetTextureIfPropertyExists(material, "_BottomLookup", bottomLookupTexture);
      SetTextureIfPropertyExists(material, "_PropsLookup", propsLookupTexture);
      SetTextureIfPropertyExists(material, "_TopAtlasLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_SideAtlasLookup", sideLookupTexture);
      SetTextureIfPropertyExists(material, "_BottomAtlasLookup", bottomLookupTexture);
      SetTextureIfPropertyExists(material, "_PropsAtlasLookup", propsLookupTexture);
    }

    private void ApplyDensityMaterialLookups(Material material)
    {
      if (material == null) return;
      EnsureLookupTextures();

      int texelCount = LookupTextureSize * LookupTextureSize;
      Color32[] densityPixels = new Color32[texelCount];
      Color32[] propsPixels = new Color32[texelCount];

      for (int id = 0; id < texelCount; id++)
      {
        ushort fallbackTileId = (ushort)Mathf.Clamp(id == 0 ? 1 : id, 1, 64);
        densityPixels[id] = EncodeU16(fallbackTileId);
        propsPixels[id] = new Color32((byte)BlockRenderCategory.Opaque, (byte)BlockLightingCategory.Lit, 0, 255);
      }

      ApplyDefaultDensityMaterialMapping(densityPixels, propsPixels);

      if (blockMaterialDatabase != null && blockMaterialDatabase.Definitions != null)
      {
        for (int i = 0; i < blockMaterialDatabase.Definitions.Count; i++)
        {
          BlockMaterialDefinition def = blockMaterialDatabase.Definitions[i];
          if (def == null) continue;

          int materialId = Mathf.Clamp(def.MaterialId, 1, 65535);
          densityPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(def.SideTileId, 1, 64));
          propsPixels[materialId] = new Color32(
            (byte)def.RenderCategory,
            (byte)def.LightingCategory,
            (byte)Mathf.Clamp(Mathf.RoundToInt(def.EmissionIntensity * 31.875f), 0, 255),
            255);
        }
      }

      topLookupTexture.SetPixels32(densityPixels);
      propsLookupTexture.SetPixels32(propsPixels);
      topLookupTexture.Apply(false, false);
      propsLookupTexture.Apply(false, false);

      SetTextureIfPropertyExists(material, "_DensityLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_TopLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_PropsLookup", propsLookupTexture);
      SetTextureIfPropertyExists(material, "_TopAtlasLookup", topLookupTexture);
      SetTextureIfPropertyExists(material, "_PropsAtlasLookup", propsLookupTexture);
    }

    private static void ApplyDefaultDensityMaterialMapping(Color32[] densityPixels, Color32[] propsPixels)
    {
      for (int materialId = 1; materialId <= 20; materialId++)
      {
        densityPixels[materialId] = EncodeU16((ushort)Mathf.Clamp(materialId, 1, 64));
        propsPixels[materialId] = new Color32((byte)BlockRenderCategory.Opaque, (byte)BlockLightingCategory.Lit, 0, 255);
      }
    }

    private static void ApplyDefaultMaterialMapping(Color32[] topPixels, Color32[] sidePixels, Color32[] bottomPixels, Color32[] propsPixels)
    {
      SetMaterialLookup(topPixels, sidePixels, bottomPixels, propsPixels, 1, 1, 2, 3, BlockRenderCategory.Opaque, BlockLightingCategory.Lit, 0.0f);
      SetMaterialLookup(topPixels, sidePixels, bottomPixels, propsPixels, 2, 2, 2, 3, BlockRenderCategory.Opaque, BlockLightingCategory.Lit, 0.0f);
      for (int materialId = 3; materialId <= 20; materialId++)
      {
        SetMaterialLookup(topPixels, sidePixels, bottomPixels, propsPixels, materialId, materialId, materialId, materialId, BlockRenderCategory.Opaque, BlockLightingCategory.Lit, 0.0f);
      }
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
      topPixels[safeMaterialId] = EncodeU16((ushort)Mathf.Clamp(topTileId, 1, 64));
      sidePixels[safeMaterialId] = EncodeU16((ushort)Mathf.Clamp(sideTileId, 1, 64));
      bottomPixels[safeMaterialId] = EncodeU16((ushort)Mathf.Clamp(bottomTileId, 1, 64));
      propsPixels[safeMaterialId] = new Color32(
        (byte)renderCategory,
        (byte)lightingCategory,
        (byte)Mathf.Clamp(Mathf.RoundToInt(emissionIntensity * 31.875f), 0, 255),
        255);
    }

    private void EnsureLookupTextures()
    {
      topLookupTexture ??= CreateLookupTexture("Cubus Top Lookup");
      sideLookupTexture ??= CreateLookupTexture("Cubus Side Lookup");
      bottomLookupTexture ??= CreateLookupTexture("Cubus Bottom Lookup");
      propsLookupTexture ??= CreateLookupTexture("Cubus Props Lookup");
    }

    private static Texture2D CreateLookupTexture(string textureName)
    {
      return new Texture2D(LookupTextureSize, LookupTextureSize, TextureFormat.RGBA32, false)
      {
        name = textureName,
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Point
      };
    }

    private static Color32 EncodeU16(ushort value)
    {
      return new Color32((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), 0, 255);
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
      DestroyTexture(ref generatedFallbackAtlas);
      DestroyTexture(ref topLookupTexture);
      DestroyTexture(ref sideLookupTexture);
      DestroyTexture(ref bottomLookupTexture);
      DestroyTexture(ref propsLookupTexture);

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
        runtimeMaterial = null;
        return;
      }

      if (Application.isPlaying) Destroy(runtimeMaterial);
      else DestroyImmediate(runtimeMaterial);
      runtimeMaterial = null;
    }

    private static void DestroyTexture(ref Texture2D texture)
    {
      if (texture == null) return;
      if (Application.isPlaying) Destroy(texture);
      else DestroyImmediate(texture);
      texture = null;
    }
  }
}
