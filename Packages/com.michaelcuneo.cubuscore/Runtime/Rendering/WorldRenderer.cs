using System.Collections.Generic;
using System.Text;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  [RequireComponent(typeof(CubusWorld))]
  public sealed class WorldRenderer : MonoBehaviour
  {
    private enum DensityTerrainDebugView
    {
      Lit = 0,
      RawSplatWeights = 1,
      BlendedSplatWeights = 2,
      MaterialIds = 3,
      AlbedoUnlit = 4,
      GeometryNormal = 5,
      TextureNormal = 6,
      Roughness = 7,
      LitGeometryNormal = 8,
      LitTextureNormal = 9,
      LitNoShadows = 10,
      ShadowAttenuation = 11,
      DirectLight = 12,
      LitGeometryNoShadows = 13
    }

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
    [SerializeField][Range(0f, 1f)] private float densityTerrainNormalMapStrength = 1.0f;
    [SerializeField][Range(0f, 4f)] private float detailBumpStrength = 0.0f;
    [SerializeField][Range(0f, 1f)] private float surfaceSmoothness = 0.12f;
    [SerializeField][Range(0f, 2f)] private float specularStrength = 0.25f;
    [SerializeField][Range(0f, 2f)] private float fresnelStrength = 0.15f;
    [SerializeField][Range(0f, 1f)] private float densityTerrainShadowStrength = 1.0f;
    [SerializeField][Range(0f, 1f)] private float densityTerrainLightingNormalMapStrength = 1.0f;
    [SerializeField][Range(0.0f, 1.0f)] private float terrainTextureDetileStrength = 0.42f;
    [SerializeField][Min(0.0001f)] private float terrainTextureNoiseScale = 0.23f;
    [SerializeField][Range(0.0f, 0.35f)] private float terrainTextureNoiseStrength = 0.025f;

    [Header("Distance Texture Detail")]
    [SerializeField][Min(0.0f)] private float distanceTextureStart = 80.0f;
    [SerializeField][Min(0.0f)] private float distanceTextureEnd = 240.0f;
    [SerializeField][Range(0.02f, 1.0f)] private float distanceTextureScaleMultiplier = 0.18f;
    [SerializeField][Range(0.0f, 1.0f)] private float distanceNormalFade = 0.85f;
    [SerializeField][Min(0.0001f)] private float distanceTextureWarpScale = 0.012f;
    [SerializeField][Range(0.0f, 64.0f)] private float distanceTextureWarpStrength = 22.0f;
    [SerializeField][Range(0.0f, 0.35f)] private float distanceTextureVariationStrength = 0.12f;

    [Header("Terrain Diagnostics")]
    [SerializeField] private bool enableDensityTerrainDebugView;
    [SerializeField] private DensityTerrainDebugView densityTerrainDebugView = DensityTerrainDebugView.Lit;
    [SerializeField] private bool logDensitySeamAuditDetails;

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

    private Texture2DArray densityTerrainAlbedoArray;
    private Texture2DArray densityTerrainNormalArray;
    private Texture2DArray densityTerrainMaskArray;
    private Texture2D densityTerrainSliceLookup;
    private CubusTerrainMaterialLibrary appliedDensityTerrainMaterialLibrary;

    private static readonly int TerrainAlbedoArrayId = Shader.PropertyToID("_TerrainAlbedoArray");
    private static readonly int TerrainNormalArrayId = Shader.PropertyToID("_TerrainNormalArray");
    private static readonly int TerrainMaskArrayId = Shader.PropertyToID("_TerrainMaskArray");
    private static readonly int TerrainMaterialSliceLookupId = Shader.PropertyToID("_TerrainMaterialSliceLookup");
    private static readonly int TerrainMaterialCountId = Shader.PropertyToID("_TerrainMaterialCount");
    private static readonly int TerrainMaterialParamsId = Shader.PropertyToID("_TerrainMaterialParams");

    private CubusWorld world;
    private ChunkPool chunkPool;
    private WorldDetailRenderer detailRenderer;
    private bool hasResolvedDetailRenderer;
    private Vector3Int lastCollisionViewerChunkCoord;
    private bool hasLastCollisionViewerChunkCoord;
    private float timeSinceLastCollisionUpdate;
    private readonly ChunkCollisionBaker collisionBaker = new();

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
      EnsureDensityTerrainVisualDefaults();
      NormalizeCollisionMode();
      world = GetComponent<CubusWorld>();
      chunkPool = new ChunkPool(transform);
      collisionBaker.Configure(Mathf.Max(2, SystemInfo.processorCount / 2));
      EnsureBlockWorldMaterial();
      EnsureDensityWorldMaterial();
    }

    private void OnValidate()
    {
      EnsureDensityTerrainVisualDefaults();
      NormalizeCollisionMode();
    }

    private void EnsureDensityTerrainVisualDefaults()
    {
      if (densityTerrainNormalMapStrength < 0.999f)
      {
        densityTerrainNormalMapStrength = 1.0f;
      }

      if (densityTerrainLightingNormalMapStrength < 0.999f)
      {
        densityTerrainLightingNormalMapStrength = 1.0f;
      }

      if (densityTerrainShadowStrength < 0.999f)
      {
        densityTerrainShadowStrength = 1.0f;
      }

    }

    private void Update()
    {
      ApplyLiveDensityDiagnosticProperties();

      // Apply any collision meshes that finished cooking on a worker thread. Pumped
      // every frame regardless of collision mode; it is a no-op when nothing is baking.
      collisionBaker.Update();

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

    private void ApplyLiveDensityDiagnosticProperties()
    {
      Material material = runtimeDensityMaterial != null ? runtimeDensityMaterial : densityWorldMaterial;
      if (material == null)
      {
        return;
      }

      SetFloatIfPropertyExists(material, "_DensityDebugView", enableDensityTerrainDebugView ? (float)densityTerrainDebugView : 0.0f);
      SetFloatIfPropertyExists(material, "_DensityTerrainNormalMapStrength", densityTerrainNormalMapStrength);
      SetFloatIfPropertyExists(material, "_DensityTerrainLightingNormalMapStrength", densityTerrainLightingNormalMapStrength);
      SetFloatIfPropertyExists(material, "_DensityTerrainShadowStrength", densityTerrainShadowStrength);
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
            WorldSettings.NormalizeDensityMeshStep(world.Settings.DensityMeshStep),
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
      collisionBaker.Clear();
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

    public bool HasBlockChunkCollider(Vector3Int chunkCoord)
    {
      return activeBlockChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView) &&
             chunkView != null &&
             chunkView.HasActiveCollisionMesh;
    }

    public bool HasDensityChunkCollider(Vector3Int chunkCoord)
    {
      return activeDensityChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView) &&
             chunkView != null &&
             chunkView.HasActiveCollisionMesh;
    }

    public void EnsureBlockChunkCollision(Vector3Int chunkCoord)
    {
      if (activeBlockChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView) && chunkView != null)
      {
        chunkView.SetCollisionEnabled(true);
      }
    }

    public void EnsureDensityChunkCollision(Vector3Int chunkCoord)
    {
      if (activeDensityChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView) && chunkView != null)
      {
        chunkView.SetCollisionEnabled(true);
      }
    }

    [ContextMenu("Audit Density Chunk Seams")]
    private void AuditDensityChunkSeams()
    {
      if (world == null) world = GetComponent<CubusWorld>();
      if (world == null || world.Settings == null)
      {
        Debug.LogWarning("Cannot audit density seams without a CubusWorld and WorldSettings.", this);
        return;
      }

      float chunkWorldSize = world.Settings.VoxelSize * VoxelConstants.ChunkSize;
      float epsilon = Mathf.Max(0.0001f, world.Settings.VoxelSize * 0.001f);
      StringBuilder details = logDensitySeamAuditDetails ? new StringBuilder(2048) : null;
      DensitySeamAuditStats stats = default;
      int effectiveDensityMeshStep = WorldSettings.NormalizeDensityMeshStep(world.Settings.DensityMeshStep);
      float cellWorldSize = world.Settings.VoxelSize * effectiveDensityMeshStep;

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeDensityChunkViews)
      {
        AuditDensitySeamPair(pair.Key, pair.Value, Vector3Int.right, 0, chunkWorldSize, cellWorldSize, effectiveDensityMeshStep, epsilon, details, ref stats);
        AuditDensitySeamPair(pair.Key, pair.Value, Vector3Int.up, 1, chunkWorldSize, cellWorldSize, effectiveDensityMeshStep, epsilon, details, ref stats);
        AuditDensitySeamPair(pair.Key, pair.Value, new Vector3Int(0, 0, 1), 2, chunkWorldSize, cellWorldSize, effectiveDensityMeshStep, epsilon, details, ref stats);
      }

      string message =
        $"Density seam audit. Pairs={stats.PairCount}, SharedVertices={stats.SharedVertexCount}, " +
        $"PlaneSharedVertices={stats.PlaneSharedVertexCount}, PlaneNormalMismatches={stats.PlaneNormalMismatchCount}, " +
        $"MissingOpposite={stats.MissingOppositeCount}, NormalMismatches={stats.NormalMismatchCount}, " +
        $"SplatMismatches={stats.SplatMismatchCount}, MaterialIdMismatches={stats.MaterialIdMismatchCount}, " +
        $"IntraFaceVariants={stats.IntraFaceVariantCount}, MissingInterior={stats.MissingInteriorFaceCount}, " +
        $"MissingChunkEdge={stats.MissingChunkEdgeCount}, MissingChunkCorner={stats.MissingChunkCornerCount}, " +
        $"MissingX={stats.MissingAxisXCount}, MissingY={stats.MissingAxisYCount}, MissingZ={stats.MissingAxisZCount}, " +
        $"BoundaryPlaneOwned={stats.BoundaryPlaneOwnedCount}, BoundaryLineOwned={stats.BoundaryLineOwnedCount}, " +
        $"MissingAmbiguousFace={stats.MissingAmbiguousFaceCount}, " +
        $"InternalSeamPlaneEdges={stats.InternalSeamPlaneEdgeCount}, MissingConnectivityOnly={stats.MissingConnectivityOnlyCount}, " +
        $"OppositeEdgeSegmentCovered={stats.OppositeEdgeSegmentCoveredCount}, PartialOppositeEdgeCovered={stats.PartialOppositeEdgeCoveredCount}, " +
        $"MissingOneEndpoint={stats.MissingOneEndpointCount}, " +
        $"MissingBothEndpoints={stats.MissingBothEndpointsCount}";

      if (details != null && details.Length > 0)
      {
        message += "\n" + details;
      }

      Debug.Log(message, this);
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
      chunkView.SetCollisionBaker(collisionBaker);
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

    private void AuditDensitySeamPair(
      Vector3Int chunkCoord,
      ChunkView chunkView,
      Vector3Int neighbourOffset,
      int axis,
      float chunkWorldSize,
      float cellWorldSize,
      int effectiveDensityMeshStep,
      float epsilon,
      StringBuilder details,
      ref DensitySeamAuditStats stats)
    {
      if (chunkView == null || chunkView.CurrentMesh == null)
      {
        return;
      }

      Vector3Int neighbourCoord = chunkCoord + neighbourOffset;
      if (!activeDensityChunkViews.TryGetValue(neighbourCoord, out ChunkView neighbourView) || neighbourView == null || neighbourView.CurrentMesh == null)
      {
        return;
      }

      DensitySeamFaceMap positiveFace = BuildDensitySeamFaceMap(chunkCoord, chunkView, axis, chunkWorldSize, chunkWorldSize, epsilon, ref stats);
      DensitySeamFaceMap negativeFace = BuildDensitySeamFaceMap(neighbourCoord, neighbourView, axis, 0.0f, chunkWorldSize, epsilon, ref stats);
      stats.PairCount++;

      CompareDensitySeamPlaneVertexSamples(positiveFace, negativeFace, chunkCoord, neighbourCoord, details, ref stats);

      foreach (KeyValuePair<DensitySeamEdgeKey, DensitySeamEdgeSample> entry in positiveFace.Edges)
      {
        if (!negativeFace.Edges.TryGetValue(entry.Key, out DensitySeamEdgeSample other))
        {
          if (negativeFace.PlaneVertices.ContainsKey(entry.Key.A) && negativeFace.PlaneVertices.ContainsKey(entry.Key.B))
          {
            stats.MissingConnectivityOnlyCount++;
            AppendDensitySeamAuditDetail(details, $"Missing negative-face edge connectivity at {entry.Key} between {chunkCoord} and {neighbourCoord}");
            continue;
          }

          if (IsDensitySeamEdgeCoveredByOppositeSegments(entry.Key, negativeFace.PlaneEdges))
          {
            stats.OppositeEdgeSegmentCoveredCount++;
            AppendDensitySeamAuditDetail(details, $"Negative-face edge segment covered at {entry.Key} between {chunkCoord} and {neighbourCoord}");
            continue;
          }

          if (IsDensitySeamEdgePartiallyCoveredByOppositeSegments(entry.Key, negativeFace))
          {
            stats.PartialOppositeEdgeCoveredCount++;
            AppendDensitySeamAuditDetail(details, $"Negative-face edge partially covered at {entry.Key} between {chunkCoord} and {neighbourCoord}");
            continue;
          }

          if (IsDensityBoundaryPlaneOwnedSample(entry.Value, axis))
          {
            stats.BoundaryPlaneOwnedCount++;
            AppendDensitySeamAuditDetail(details, $"Boundary-plane-owned positive-face vertex at {entry.Key} between {chunkCoord} and {neighbourCoord}");
            continue;
          }

          if (IsDensityBoundaryLineOwnedSample(entry.Value))
          {
            stats.BoundaryLineOwnedCount++;
            AppendDensitySeamAuditDetail(details, $"Boundary-line-owned positive-face vertex at {entry.Key} between {chunkCoord} and {neighbourCoord}");
            continue;
          }

          stats.MissingOppositeCount++;
          RecordMissingDensitySeamEndpointPresence(entry.Key, negativeFace, ref stats);
          RecordMissingDensitySeamOpposite(entry.Value, ref stats);
          RecordMissingDensitySeamAxis(axis, ref stats);
          RecordAmbiguousMissingDensitySeamFace(entry.Value, axis, cellWorldSize, effectiveDensityMeshStep, ref stats);
          AppendDensitySeamAuditDetail(details, $"Missing negative-face boundary edge at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        CompareDensitySeamEdgeSamples(entry.Key, entry.Value, other, chunkCoord, neighbourCoord, details, ref stats);
      }

      foreach (KeyValuePair<DensitySeamEdgeKey, DensitySeamEdgeSample> entry in negativeFace.Edges)
      {
        if (positiveFace.Edges.ContainsKey(entry.Key))
        {
          continue;
        }

        if (positiveFace.PlaneVertices.ContainsKey(entry.Key.A) && positiveFace.PlaneVertices.ContainsKey(entry.Key.B))
        {
          stats.MissingConnectivityOnlyCount++;
          AppendDensitySeamAuditDetail(details, $"Missing positive-face edge connectivity at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        if (IsDensitySeamEdgeCoveredByOppositeSegments(entry.Key, positiveFace.PlaneEdges))
        {
          stats.OppositeEdgeSegmentCoveredCount++;
          AppendDensitySeamAuditDetail(details, $"Positive-face edge segment covered at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        if (IsDensitySeamEdgePartiallyCoveredByOppositeSegments(entry.Key, positiveFace))
        {
          stats.PartialOppositeEdgeCoveredCount++;
          AppendDensitySeamAuditDetail(details, $"Positive-face edge partially covered at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        if (IsDensityBoundaryPlaneOwnedSample(entry.Value, axis))
        {
          stats.BoundaryPlaneOwnedCount++;
          AppendDensitySeamAuditDetail(details, $"Boundary-plane-owned negative-face vertex at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        if (IsDensityBoundaryLineOwnedSample(entry.Value))
        {
          stats.BoundaryLineOwnedCount++;
          AppendDensitySeamAuditDetail(details, $"Boundary-line-owned negative-face vertex at {entry.Key} between {chunkCoord} and {neighbourCoord}");
          continue;
        }

        stats.MissingOppositeCount++;
        RecordMissingDensitySeamEndpointPresence(entry.Key, positiveFace, ref stats);
        RecordMissingDensitySeamOpposite(entry.Value, ref stats);
        RecordMissingDensitySeamAxis(axis, ref stats);
        RecordAmbiguousMissingDensitySeamFace(entry.Value, axis, cellWorldSize, effectiveDensityMeshStep, ref stats);
        AppendDensitySeamAuditDetail(details, $"Missing positive-face boundary edge at {entry.Key} between {chunkCoord} and {neighbourCoord}");
      }
    }

    private static DensitySeamFaceMap BuildDensitySeamFaceMap(
      Vector3Int chunkCoord,
      ChunkView chunkView,
      int axis,
      float facePosition,
      float chunkWorldSize,
      float epsilon,
      ref DensitySeamAuditStats stats)
    {
      Mesh mesh = chunkView.CurrentMesh;
      Vector3[] vertices = mesh.vertices;
      Vector3[] normals = mesh.normals;
      Color[] colors = mesh.colors;
      Vector2[] uv0 = mesh.uv;
      Vector2[] uv1 = mesh.uv2;
      int[] triangles = mesh.triangles;
      Dictionary<DensitySeamEdgeKey, int> seamEdgeUseCounts = new();
      HashSet<DensitySeamEdgeKey> reportedInternalEdges = new();
      bool[] seamPlaneVertices = new bool[vertices.Length];
      bool[] seamEdgeVertices = new bool[vertices.Length];
      Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> planeVertexResult = new();
      Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> vertexResult = new();
      Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> planeEdgeResult = new();
      Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> edgeResult = new();

      for (int i = 0; i + 2 < triangles.Length; i += 3)
      {
        CountDensitySeamEdgeUse(triangles[i], triangles[i + 1], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, seamPlaneVertices);
        CountDensitySeamEdgeUse(triangles[i + 1], triangles[i + 2], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, seamPlaneVertices);
        CountDensitySeamEdgeUse(triangles[i + 2], triangles[i], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, seamPlaneVertices);
      }

      for (int i = 0; i + 2 < triangles.Length; i += 3)
      {
        MarkOpenDensitySeamEdgeVertex(triangles[i], triangles[i + 1], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, reportedInternalEdges, seamEdgeVertices, ref stats);
        MarkOpenDensitySeamEdgeVertex(triangles[i + 1], triangles[i + 2], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, reportedInternalEdges, seamEdgeVertices, ref stats);
        MarkOpenDensitySeamEdgeVertex(triangles[i + 2], triangles[i], chunkView, vertices, axis, facePosition, epsilon, seamEdgeUseCounts, reportedInternalEdges, seamEdgeVertices, ref stats);
      }

      for (int i = 0; i < vertices.Length; i++)
      {
        if (!seamPlaneVertices[i])
        {
          continue;
        }

        DensitySeamVertexSample sample = new(
          normals != null && i < normals.Length ? normals[i].normalized : Vector3.up,
          colors != null && i < colors.Length ? colors[i] : Color.clear,
          uv0 != null && i < uv0.Length ? uv0[i] : Vector2.zero,
          uv1 != null && i < uv1.Length ? uv1[i] : Vector2.zero,
          CountNonFaceChunkBoundaries(vertices[i], axis, chunkWorldSize, epsilon),
          chunkCoord,
          vertices[i]);

        DensitySeamVertexKey key = DensitySeamVertexKey.From(chunkView.transform.TransformPoint(vertices[i]), epsilon);

        if (!planeVertexResult.ContainsKey(key))
        {
          planeVertexResult.Add(key, sample);
        }

        if (!seamEdgeVertices[i])
        {
          continue;
        }

        if (vertexResult.TryGetValue(key, out DensitySeamVertexSample existing))
        {
          if (Vector3.Dot(existing.Normal, sample.Normal) < 0.995f ||
              !ApproximatelyEqual(existing.Color, sample.Color, 0.01f) ||
              (existing.Uv0 - sample.Uv0).sqrMagnitude > 0.01f ||
              (existing.Uv1 - sample.Uv1).sqrMagnitude > 0.01f)
          {
            stats.IntraFaceVariantCount++;
          }

          continue;
        }

        vertexResult.Add(key, sample);
      }

      foreach (KeyValuePair<DensitySeamEdgeKey, int> entry in seamEdgeUseCounts)
      {
        if (planeVertexResult.TryGetValue(entry.Key.A, out DensitySeamVertexSample planeA) && planeVertexResult.TryGetValue(entry.Key.B, out DensitySeamVertexSample planeB))
        {
          planeEdgeResult[entry.Key] = new DensitySeamEdgeSample(planeA, planeB);
        }

        if (entry.Value != 1 || !vertexResult.TryGetValue(entry.Key.A, out DensitySeamVertexSample a) || !vertexResult.TryGetValue(entry.Key.B, out DensitySeamVertexSample b))
        {
          continue;
        }

        edgeResult.Add(entry.Key, new DensitySeamEdgeSample(a, b));
      }

      return new DensitySeamFaceMap(vertexResult, edgeResult, planeVertexResult, planeEdgeResult);
    }

    private static void CountDensitySeamEdgeUse(
      int a,
      int b,
      ChunkView chunkView,
      Vector3[] vertices,
      int axis,
      float facePosition,
      float epsilon,
      Dictionary<DensitySeamEdgeKey, int> seamEdgeUseCounts,
      bool[] seamPlaneVertices)
    {
      if (!TryGetDensitySeamEdgeKey(a, b, chunkView, vertices, axis, facePosition, epsilon, out DensitySeamEdgeKey edgeKey))
      {
        return;
      }

      seamEdgeUseCounts.TryGetValue(edgeKey, out int count);
      seamEdgeUseCounts[edgeKey] = count + 1;
      seamPlaneVertices[a] = true;
      seamPlaneVertices[b] = true;
    }

    private static void MarkOpenDensitySeamEdgeVertex(
      int a,
      int b,
      ChunkView chunkView,
      Vector3[] vertices,
      int axis,
      float facePosition,
      float epsilon,
      Dictionary<DensitySeamEdgeKey, int> seamEdgeUseCounts,
      HashSet<DensitySeamEdgeKey> reportedInternalEdges,
      bool[] seamEdgeVertices,
      ref DensitySeamAuditStats stats)
    {
      if (!TryGetDensitySeamEdgeKey(a, b, chunkView, vertices, axis, facePosition, epsilon, out DensitySeamEdgeKey edgeKey))
      {
        return;
      }

      if (!seamEdgeUseCounts.TryGetValue(edgeKey, out int count) || count <= 0)
      {
        return;
      }

      if (count > 1)
      {
        if (reportedInternalEdges.Add(edgeKey))
        {
          stats.InternalSeamPlaneEdgeCount++;
        }

        return;
      }

      seamEdgeVertices[a] = true;
      seamEdgeVertices[b] = true;
    }

    private static bool TryGetDensitySeamEdgeKey(
      int a,
      int b,
      ChunkView chunkView,
      Vector3[] vertices,
      int axis,
      float facePosition,
      float epsilon,
      out DensitySeamEdgeKey edgeKey)
    {
      edgeKey = default;

      if (a < 0 || b < 0 || a >= vertices.Length || b >= vertices.Length)
      {
        return false;
      }

      if (Mathf.Abs(vertices[a][axis] - facePosition) > epsilon || Mathf.Abs(vertices[b][axis] - facePosition) > epsilon)
      {
        return false;
      }

      DensitySeamVertexKey keyA = DensitySeamVertexKey.From(chunkView.transform.TransformPoint(vertices[a]), epsilon);
      DensitySeamVertexKey keyB = DensitySeamVertexKey.From(chunkView.transform.TransformPoint(vertices[b]), epsilon);
      if (keyA.CompareTo(keyB) == 0)
      {
        return false;
      }

      edgeKey = new DensitySeamEdgeKey(keyA, keyB);
      return true;
    }

    private static int CountNonFaceChunkBoundaries(Vector3 localPosition, int faceAxis, float chunkWorldSize, float epsilon)
    {
      int count = 0;
      for (int axis = 0; axis < 3; axis++)
      {
        if (axis == faceAxis)
        {
          continue;
        }

        float coordinate = localPosition[axis];
        if (Mathf.Abs(coordinate) <= epsilon || Mathf.Abs(coordinate - chunkWorldSize) <= epsilon)
        {
          count++;
        }
      }

      return count;
    }

    private static bool IsDensityBoundaryPlaneOwnedSample(DensitySeamVertexSample sample, int seamAxis)
    {
      return Mathf.Abs(sample.Normal[seamAxis]) >= 0.9f;
    }

    private static bool IsDensityBoundaryPlaneOwnedSample(DensitySeamEdgeSample sample, int seamAxis)
    {
      return IsDensityBoundaryPlaneOwnedSample(sample.A, seamAxis) || IsDensityBoundaryPlaneOwnedSample(sample.B, seamAxis);
    }

    private static bool IsDensityBoundaryLineOwnedSample(DensitySeamVertexSample sample)
    {
      return sample.NonFaceBoundaryAxisCount > 0;
    }

    private static bool IsDensityBoundaryLineOwnedSample(DensitySeamEdgeSample sample)
    {
      return IsDensityBoundaryLineOwnedSample(sample.A) || IsDensityBoundaryLineOwnedSample(sample.B);
    }

    private static void RecordMissingDensitySeamEndpointPresence(DensitySeamEdgeKey edge, DensitySeamFaceMap oppositeFace, ref DensitySeamAuditStats stats)
    {
      bool hasA = oppositeFace.PlaneVertices.ContainsKey(edge.A);
      bool hasB = oppositeFace.PlaneVertices.ContainsKey(edge.B);

      if (hasA || hasB)
      {
        stats.MissingOneEndpointCount++;
      }
      else
      {
        stats.MissingBothEndpointsCount++;
      }
    }

    private static bool IsDensitySeamEdgeCoveredByOppositeSegments(DensitySeamEdgeKey edge, Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> oppositeEdges)
    {
      int axis = DominantAxis(edge.A, edge.B);
      int targetStart = Mathf.Min(edge.A[axis], edge.B[axis]);
      int targetEnd = Mathf.Max(edge.A[axis], edge.B[axis]);
      if (targetStart == targetEnd)
      {
        return false;
      }

      List<Vector2Int> intervals = new();

      foreach (DensitySeamEdgeKey other in oppositeEdges.Keys)
      {
        if (!AreCollinear(edge.A, edge.B, other.A) || !AreCollinear(edge.A, edge.B, other.B))
        {
          continue;
        }

        int otherStart = Mathf.Max(targetStart, Mathf.Min(other.A[axis], other.B[axis]));
        int otherEnd = Mathf.Min(targetEnd, Mathf.Max(other.A[axis], other.B[axis]));
        if (otherEnd > otherStart)
        {
          intervals.Add(new Vector2Int(otherStart, otherEnd));
        }
      }

      if (intervals.Count == 0)
      {
        return false;
      }

      intervals.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
      int coveredUntil = targetStart;

      for (int i = 0; i < intervals.Count; i++)
      {
        if (intervals[i].x > coveredUntil)
        {
          return false;
        }

        coveredUntil = Mathf.Max(coveredUntil, intervals[i].y);
        if (coveredUntil >= targetEnd)
        {
          return true;
        }
      }

      return false;
    }

    private static bool IsDensitySeamEdgePartiallyCoveredByOppositeSegments(DensitySeamEdgeKey edge, DensitySeamFaceMap oppositeFace)
    {
      bool hasA = oppositeFace.PlaneVertices.ContainsKey(edge.A);
      bool hasB = oppositeFace.PlaneVertices.ContainsKey(edge.B);
      bool coveredA = hasA || IsDensitySeamVertexOnAnyEdge(edge.A, oppositeFace.PlaneEdges);
      bool coveredB = hasB || IsDensitySeamVertexOnAnyEdge(edge.B, oppositeFace.PlaneEdges);

      return coveredA && coveredB && (!hasA || !hasB);
    }

    private static bool IsDensitySeamVertexOnAnyEdge(DensitySeamVertexKey vertex, Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> edges)
    {
      foreach (DensitySeamEdgeKey edge in edges.Keys)
      {
        if (IsPointOnSegment(vertex, edge.A, edge.B))
        {
          return true;
        }
      }

      return false;
    }

    private static int DominantAxis(DensitySeamVertexKey a, DensitySeamVertexKey b)
    {
      int dx = Mathf.Abs(b.X - a.X);
      int dy = Mathf.Abs(b.Y - a.Y);
      int dz = Mathf.Abs(b.Z - a.Z);

      if (dx >= dy && dx >= dz) return 0;
      return dy >= dz ? 1 : 2;
    }

    private static bool AreCollinear(DensitySeamVertexKey a, DensitySeamVertexKey b, DensitySeamVertexKey p)
    {
      long abx = b.X - a.X;
      long aby = b.Y - a.Y;
      long abz = b.Z - a.Z;
      long apx = p.X - a.X;
      long apy = p.Y - a.Y;
      long apz = p.Z - a.Z;

      return aby * apz - abz * apy == 0 &&
             abz * apx - abx * apz == 0 &&
             abx * apy - aby * apx == 0;
    }

    private static bool IsPointOnSegment(DensitySeamVertexKey p, DensitySeamVertexKey a, DensitySeamVertexKey b)
    {
      if (!AreCollinear(a, b, p))
      {
        return false;
      }

      return p.X >= Mathf.Min(a.X, b.X) && p.X <= Mathf.Max(a.X, b.X) &&
             p.Y >= Mathf.Min(a.Y, b.Y) && p.Y <= Mathf.Max(a.Y, b.Y) &&
             p.Z >= Mathf.Min(a.Z, b.Z) && p.Z <= Mathf.Max(a.Z, b.Z);
    }

    private static void RecordMissingDensitySeamOpposite(DensitySeamVertexSample sample, ref DensitySeamAuditStats stats)
    {
      if (sample.NonFaceBoundaryAxisCount >= 2)
      {
        stats.MissingChunkCornerCount++;
      }
      else if (sample.NonFaceBoundaryAxisCount == 1)
      {
        stats.MissingChunkEdgeCount++;
      }
      else
      {
        stats.MissingInteriorFaceCount++;
      }
    }

    private static void RecordMissingDensitySeamOpposite(DensitySeamEdgeSample sample, ref DensitySeamAuditStats stats)
    {
      if (sample.A.NonFaceBoundaryAxisCount >= 2 || sample.B.NonFaceBoundaryAxisCount >= 2)
      {
        stats.MissingChunkCornerCount++;
      }
      else if (sample.A.NonFaceBoundaryAxisCount == 1 || sample.B.NonFaceBoundaryAxisCount == 1)
      {
        stats.MissingChunkEdgeCount++;
      }
      else
      {
        stats.MissingInteriorFaceCount++;
      }
    }

    private static void RecordMissingDensitySeamAxis(int axis, ref DensitySeamAuditStats stats)
    {
      switch (axis)
      {
        case 0:
          stats.MissingAxisXCount++;
          break;
        case 1:
          stats.MissingAxisYCount++;
          break;
        case 2:
          stats.MissingAxisZCount++;
          break;
      }
    }

    private void RecordAmbiguousMissingDensitySeamFace(DensitySeamVertexSample sample, int seamAxis, float cellWorldSize, int effectiveDensityMeshStep, ref DensitySeamAuditStats stats)
    {
      if (world == null || world.Settings == null || cellWorldSize <= 0.0f || effectiveDensityMeshStep <= 0)
      {
        return;
      }

      int axisA = seamAxis == 0 ? 1 : 0;
      int axisB = seamAxis == 2 ? 1 : 2;
      if (seamAxis == 1)
      {
        axisA = 0;
        axisB = 2;
      }

      int[] local = { 0, 0, 0 };
      local[seamAxis] = Mathf.RoundToInt(sample.LocalPosition[seamAxis] / world.Settings.VoxelSize);
      local[axisA] = Mathf.Clamp(Mathf.FloorToInt(sample.LocalPosition[axisA] / cellWorldSize) * effectiveDensityMeshStep, 0, VoxelConstants.ChunkSize - effectiveDensityMeshStep);
      local[axisB] = Mathf.Clamp(Mathf.FloorToInt(sample.LocalPosition[axisB] / cellWorldSize) * effectiveDensityMeshStep, 0, VoxelConstants.ChunkSize - effectiveDensityMeshStep);

      Vector3Int baseWorldVoxel = sample.ChunkCoord * VoxelConstants.ChunkSize;
      Vector3Int p00 = baseWorldVoxel + new Vector3Int(local[0], local[1], local[2]);
      Vector3Int p10 = p00;
      Vector3Int p01 = p00;
      Vector3Int p11 = p00;
      p10[axisA] += effectiveDensityMeshStep;
      p01[axisB] += effectiveDensityMeshStep;
      p11[axisA] += effectiveDensityMeshStep;
      p11[axisB] += effectiveDensityMeshStep;

      bool s00 = world.GetDensityVoxelAtWorldVoxel(p00).Density > 0.0f;
      bool s10 = world.GetDensityVoxelAtWorldVoxel(p10).Density > 0.0f;
      bool s01 = world.GetDensityVoxelAtWorldVoxel(p01).Density > 0.0f;
      bool s11 = world.GetDensityVoxelAtWorldVoxel(p11).Density > 0.0f;

      if (s00 == s11 && s10 == s01 && s00 != s10)
      {
        stats.MissingAmbiguousFaceCount++;
      }
    }

    private void RecordAmbiguousMissingDensitySeamFace(DensitySeamEdgeSample sample, int seamAxis, float cellWorldSize, int effectiveDensityMeshStep, ref DensitySeamAuditStats stats)
    {
      int before = stats.MissingAmbiguousFaceCount;
      RecordAmbiguousMissingDensitySeamFace(sample.A, seamAxis, cellWorldSize, effectiveDensityMeshStep, ref stats);
      if (stats.MissingAmbiguousFaceCount == before)
      {
        RecordAmbiguousMissingDensitySeamFace(sample.B, seamAxis, cellWorldSize, effectiveDensityMeshStep, ref stats);
      }
    }

    private static void CompareDensitySeamEdgeSamples(
      DensitySeamEdgeKey key,
      DensitySeamEdgeSample sample,
      DensitySeamEdgeSample other,
      Vector3Int chunkCoord,
      Vector3Int neighbourCoord,
      StringBuilder details,
      ref DensitySeamAuditStats stats)
    {
      CompareDensitySeamVertexSamples(key.A, sample.A, other.A, chunkCoord, neighbourCoord, details, ref stats);
      CompareDensitySeamVertexSamples(key.B, sample.B, other.B, chunkCoord, neighbourCoord, details, ref stats);
    }

    private static void CompareDensitySeamPlaneVertexSamples(
      DensitySeamFaceMap positiveFace,
      DensitySeamFaceMap negativeFace,
      Vector3Int chunkCoord,
      Vector3Int neighbourCoord,
      StringBuilder details,
      ref DensitySeamAuditStats stats)
    {
      foreach (KeyValuePair<DensitySeamVertexKey, DensitySeamVertexSample> entry in positiveFace.PlaneVertices)
      {
        if (!negativeFace.PlaneVertices.TryGetValue(entry.Key, out DensitySeamVertexSample other))
        {
          continue;
        }

        stats.PlaneSharedVertexCount++;

        if (Vector3.Dot(entry.Value.Normal, other.Normal) < 0.995f)
        {
          stats.PlaneNormalMismatchCount++;
          AppendDensitySeamAuditDetail(details, $"Plane normal mismatch at {entry.Key} between {chunkCoord} and {neighbourCoord}: {entry.Value.Normal} vs {other.Normal}");
        }
      }
    }

    private static void CompareDensitySeamVertexSamples(
      DensitySeamVertexKey key,
      DensitySeamVertexSample sample,
      DensitySeamVertexSample other,
      Vector3Int chunkCoord,
      Vector3Int neighbourCoord,
      StringBuilder details,
      ref DensitySeamAuditStats stats)
    {
      stats.SharedVertexCount++;

      if (Vector3.Dot(sample.Normal, other.Normal) < 0.995f)
      {
        stats.NormalMismatchCount++;
        AppendDensitySeamAuditDetail(details, $"Normal mismatch at {key} between {chunkCoord} and {neighbourCoord}: {sample.Normal} vs {other.Normal}");
      }

      if (!ApproximatelyEqual(sample.Color, other.Color, 0.01f))
      {
        stats.SplatMismatchCount++;
        AppendDensitySeamAuditDetail(details, $"Splat mismatch at {key} between {chunkCoord} and {neighbourCoord}: {sample.Color} vs {other.Color}");
      }

      if ((sample.Uv0 - other.Uv0).sqrMagnitude > 0.01f || (sample.Uv1 - other.Uv1).sqrMagnitude > 0.01f)
      {
        stats.MaterialIdMismatchCount++;
        AppendDensitySeamAuditDetail(details, $"Material id mismatch at {key} between {chunkCoord} and {neighbourCoord}: {sample.Uv0}/{sample.Uv1} vs {other.Uv0}/{other.Uv1}");
      }
    }

    private static bool ApproximatelyEqual(Color a, Color b, float tolerance)
    {
      return Mathf.Abs(a.r - b.r) <= tolerance &&
             Mathf.Abs(a.g - b.g) <= tolerance &&
             Mathf.Abs(a.b - b.b) <= tolerance &&
             Mathf.Abs(a.a - b.a) <= tolerance;
    }

    private static void AppendDensitySeamAuditDetail(StringBuilder details, string line)
    {
      if (details == null || details.Length > 8192)
      {
        return;
      }

      details.AppendLine(line);
    }

    private struct DensitySeamAuditStats
    {
      public int PairCount;
      public int SharedVertexCount;
      public int PlaneSharedVertexCount;
      public int PlaneNormalMismatchCount;
      public int MissingOppositeCount;
      public int NormalMismatchCount;
      public int SplatMismatchCount;
      public int MaterialIdMismatchCount;
      public int IntraFaceVariantCount;
      public int MissingInteriorFaceCount;
      public int MissingChunkEdgeCount;
      public int MissingChunkCornerCount;
      public int MissingAxisXCount;
      public int MissingAxisYCount;
      public int MissingAxisZCount;
      public int BoundaryPlaneOwnedCount;
      public int BoundaryLineOwnedCount;
      public int MissingAmbiguousFaceCount;
      public int InternalSeamPlaneEdgeCount;
      public int MissingConnectivityOnlyCount;
      public int OppositeEdgeSegmentCoveredCount;
      public int PartialOppositeEdgeCoveredCount;
      public int MissingOneEndpointCount;
      public int MissingBothEndpointsCount;
    }

    private readonly struct DensitySeamFaceMap
    {
      public readonly Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> Vertices;
      public readonly Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> Edges;
      public readonly Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> PlaneVertices;
      public readonly Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> PlaneEdges;

      public DensitySeamFaceMap(
        Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> vertices,
        Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> edges,
        Dictionary<DensitySeamVertexKey, DensitySeamVertexSample> planeVertices,
        Dictionary<DensitySeamEdgeKey, DensitySeamEdgeSample> planeEdges)
      {
        Vertices = vertices;
        Edges = edges;
        PlaneVertices = planeVertices;
        PlaneEdges = planeEdges;
      }
    }

    private readonly struct DensitySeamEdgeSample
    {
      public readonly DensitySeamVertexSample A;
      public readonly DensitySeamVertexSample B;

      public DensitySeamEdgeSample(DensitySeamVertexSample a, DensitySeamVertexSample b)
      {
        A = a;
        B = b;
      }
    }

    private readonly struct DensitySeamVertexSample
    {
      public readonly Vector3 Normal;
      public readonly Color Color;
      public readonly Vector2 Uv0;
      public readonly Vector2 Uv1;
      public readonly int NonFaceBoundaryAxisCount;
      public readonly Vector3Int ChunkCoord;
      public readonly Vector3 LocalPosition;

      public DensitySeamVertexSample(Vector3 normal, Color color, Vector2 uv0, Vector2 uv1, int nonFaceBoundaryAxisCount, Vector3Int chunkCoord, Vector3 localPosition)
      {
        Normal = normal;
        Color = color;
        Uv0 = uv0;
        Uv1 = uv1;
        NonFaceBoundaryAxisCount = nonFaceBoundaryAxisCount;
        ChunkCoord = chunkCoord;
        LocalPosition = localPosition;
      }
    }

    private readonly struct DensitySeamEdgeKey : System.IEquatable<DensitySeamEdgeKey>
    {
      private readonly DensitySeamVertexKey a;
      private readonly DensitySeamVertexKey b;

      public DensitySeamVertexKey A => a;
      public DensitySeamVertexKey B => b;

      public DensitySeamEdgeKey(DensitySeamVertexKey first, DensitySeamVertexKey second)
      {
        if (first.CompareTo(second) <= 0)
        {
          a = first;
          b = second;
        }
        else
        {
          a = second;
          b = first;
        }
      }

      public bool Equals(DensitySeamEdgeKey other)
      {
        return a.Equals(other.a) && b.Equals(other.b);
      }

      public override bool Equals(object obj)
      {
        return obj is DensitySeamEdgeKey other && Equals(other);
      }

      public override int GetHashCode()
      {
        return System.HashCode.Combine(a, b);
      }

      public override string ToString()
      {
        return $"{a}->{b}";
      }
    }

    private readonly struct DensitySeamVertexKey : System.IComparable<DensitySeamVertexKey>
    {
      private readonly int x;
      private readonly int y;
      private readonly int z;

      public int X => x;
      public int Y => y;
      public int Z => z;

      public int this[int axis] => axis switch
      {
        0 => x,
        1 => y,
        _ => z
      };

      private DensitySeamVertexKey(int x, int y, int z)
      {
        this.x = x;
        this.y = y;
        this.z = z;
      }

      public static DensitySeamVertexKey From(Vector3 position, float epsilon)
      {
        float scale = 1.0f / Mathf.Max(0.000001f, epsilon);
        return new DensitySeamVertexKey(
          Mathf.RoundToInt(position.x * scale),
          Mathf.RoundToInt(position.y * scale),
          Mathf.RoundToInt(position.z * scale));
      }

      public int CompareTo(DensitySeamVertexKey other)
      {
        int xCompare = x.CompareTo(other.x);
        if (xCompare != 0) return xCompare;

        int yCompare = y.CompareTo(other.y);
        if (yCompare != 0) return yCompare;

        return z.CompareTo(other.z);
      }

      public override string ToString()
      {
        return $"({x}, {y}, {z})";
      }
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

      // Legacy atlas lookup path. Safe to leave for older shaders.
      ApplyDensityMaterialLookups(material);

      // New density terrain texture-array path.
      ApplyDensityTerrainMaterialLibrary(material);
    }

    private void ApplyDensityTerrainMaterialLibrary(Material material)
    {
      if (material == null || world == null || world.Settings == null)
      {
        return;
      }

      CubusTerrainMaterialLibrary library = world.Settings.TerrainMaterialLibrary;

      if (library == null)
      {
        Debug.LogWarning(
          "WorldSettings.TerrainMaterialLibrary is not assigned. Density terrain texture arrays will not be bound.",
          this);
        return;
      }

      if (densityTerrainAlbedoArray == null ||
          densityTerrainNormalArray == null ||
          densityTerrainMaskArray == null ||
          densityTerrainSliceLookup == null ||
          appliedDensityTerrainMaterialLibrary != library)
      {
        DestroyTextureArray(ref densityTerrainAlbedoArray);
        DestroyTextureArray(ref densityTerrainNormalArray);
        DestroyTextureArray(ref densityTerrainMaskArray);
        DestroyTexture(ref densityTerrainSliceLookup);

        CubusTerrainTextureArrayBuilder.Build(
          library,
          out densityTerrainAlbedoArray,
          out densityTerrainNormalArray,
          out densityTerrainMaskArray);

        densityTerrainSliceLookup = CubusTerrainMaterialLibraryBinder.BuildMaterialSliceLookup(library);

        appliedDensityTerrainMaterialLibrary = library;

        Debug.Log(
          $"Built density terrain texture arrays. Materials={library.MaterialCount}, Size={library.TextureSize}",
          this);
      }

      material.SetTexture(TerrainAlbedoArrayId, densityTerrainAlbedoArray);
      material.SetTexture(TerrainNormalArrayId, densityTerrainNormalArray);
      material.SetTexture(TerrainMaskArrayId, densityTerrainMaskArray);
      material.SetTexture(TerrainMaterialSliceLookupId, densityTerrainSliceLookup);
      material.SetInt(TerrainMaterialCountId, library.MaterialCount);
      material.SetVectorArray(
        TerrainMaterialParamsId,
        BuildTerrainMaterialParams(library));
    }

    private static Vector4[] BuildTerrainMaterialParams(CubusTerrainMaterialLibrary library)
    {
      const int MaxMaterialParams = 128;

      Vector4[] result = new Vector4[MaxMaterialParams];

      for (int i = 0; i < MaxMaterialParams; i++)
      {
        result[i] = new Vector4(1.0f, 1.0f, 0.8f, 0.0f);
      }

      if (library == null || library.Materials == null)
      {
        return result;
      }

      int count = Mathf.Min(library.Materials.Count, MaxMaterialParams);

      for (int i = 0; i < count; i++)
      {
        CubusTerrainMaterial terrainMaterial = library.Materials[i];

        if (terrainMaterial == null)
        {
          continue;
        }

        result[i] = new Vector4(
          Mathf.Max(0.001f, terrainMaterial.Tiling),
          Mathf.Max(0.0f, terrainMaterial.NormalStrength),
          Mathf.Clamp01(terrainMaterial.RoughnessFallback),
          Mathf.Clamp01(terrainMaterial.HeightStrength));
      }

      return result;
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
      SetFloatIfPropertyExists(material, "_DensityTerrainNormalMapStrength", densityTerrainNormalMapStrength);
      SetFloatIfPropertyExists(material, "_DensityTerrainLightingNormalMapStrength", densityTerrainLightingNormalMapStrength);
      SetFloatIfPropertyExists(material, "_DensityTerrainShadowStrength", densityTerrainShadowStrength);
      SetFloatIfPropertyExists(material, "_DetailBumpStrength", detailBumpStrength);
      SetFloatIfPropertyExists(material, "_Smoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_SurfaceSmoothness", surfaceSmoothness);
      SetFloatIfPropertyExists(material, "_SpecularStrength", specularStrength);
      SetFloatIfPropertyExists(material, "_FresnelStrength", fresnelStrength);
      SetFloatIfPropertyExists(material, "_TerrainTextureDetileStrength", terrainTextureDetileStrength);
      SetFloatIfPropertyExists(material, "_TerrainTextureNoiseScale", terrainTextureNoiseScale);
      SetFloatIfPropertyExists(material, "_TerrainTextureNoiseStrength", terrainTextureNoiseStrength);
      SetFloatIfPropertyExists(material, "_DistanceTextureStart", distanceTextureStart);
      SetFloatIfPropertyExists(material, "_DistanceTextureEnd", Mathf.Max(distanceTextureStart + 0.001f, distanceTextureEnd));
      SetFloatIfPropertyExists(material, "_DistanceTextureScaleMultiplier", distanceTextureScaleMultiplier);
      SetFloatIfPropertyExists(material, "_DistanceNormalFade", distanceNormalFade);
      SetFloatIfPropertyExists(material, "_DistanceTextureWarpScale", distanceTextureWarpScale);
      SetFloatIfPropertyExists(material, "_DistanceTextureWarpStrength", distanceTextureWarpStrength);
      SetFloatIfPropertyExists(material, "_DistanceTextureVariationStrength", distanceTextureVariationStrength);
      SetFloatIfPropertyExists(material, "_DensityDebugView", enableDensityTerrainDebugView ? (float)densityTerrainDebugView : 0.0f);
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
      DestroyTextureArray(ref densityTerrainAlbedoArray);
      DestroyTextureArray(ref densityTerrainNormalArray);
      DestroyTextureArray(ref densityTerrainMaskArray);
      DestroyTexture(ref densityTerrainSliceLookup);

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

    private static void DestroyTextureArray(ref Texture2DArray textureArray)
    {
      if (textureArray == null) return;
      if (Application.isPlaying) Destroy(textureArray);
      else DestroyImmediate(textureArray);
      textureArray = null;
    }
  }
}
