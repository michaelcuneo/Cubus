using System.Collections;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  [DefaultExecutionOrder(-10)]
  public sealed class WorldStreamer : MonoBehaviour
  {
    [SerializeField] private Transform viewer;
    [SerializeField] private StreamingSettings settings = new();

    [SerializeField][Min(1)] private int initialSpawnRequiredRenderedChunks = 9;

    private readonly HashSet<Vector3Int> desiredChunkCoords = new();
    private readonly HashSet<Vector3Int> keepChunkCoords = new();
    private readonly Queue<Vector3Int> pendingGenerateQueue = new();
    private readonly HashSet<Vector3Int> pendingGenerateSet = new();
    private readonly Queue<Vector3Int> pendingRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingRenderSet = new();
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly HashSet<Vector3Int> forcedDensityRebuildChunks = new();
    private readonly Dictionary<Vector2Int, int> surfaceChunkYCache = new();

    private readonly BlockChunkBuildQueue buildQueue = new();
    private readonly DensityChunkBuildQueue densityBuildQueue = new();
    private WorldGenerationSnapshot worldSnapshot;

    private CubusWorld world;
    private CubusWorldStorage storage;
    private WorldRenderer worldRenderer;
    private WorldGenerator generator;
    private BlockEditTool editTool;

    private Vector3Int lastViewerChunkCoord;
    private bool hasLastViewerChunkCoord;

    public StreamingSettings Settings => settings;

    private int UnloadPaddingInChunks =>
      Mathf.Max(2, settings != null ? settings.UnloadPaddingInChunks : 4);

    private int ChunksBelowSurface =>
        Mathf.Max(24, settings != null ? settings.ChunksBelowSurface : 32);

    private int ChunksAboveSurface =>
        Mathf.Max(6, settings != null ? settings.ChunksAboveSurface : 8);

    private int ChunksGeneratedPerFrame =>
        Mathf.Clamp(settings != null ? settings.ChunksGeneratedPerFrame : 8, 4, 64);

    private int InitialChunksGeneratedPerFrame =>
        Mathf.Clamp(settings != null ? settings.InitialChunksGeneratedPerFrame : 32, 8, 128);

    private int ChunksRenderedPerFrame =>
        Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame : 16, 4, 64);

    private int MeshAppliesPerFrame =>
        Mathf.Clamp(settings != null ? settings.MeshAppliesPerFrame : 16, 4, 64);

    private bool UseAsyncGeneration =>
        settings == null || settings.UseAsyncGeneration;

    private int MaxAsyncChunkTasks =>
        Mathf.Clamp(settings != null ? settings.MaxAsyncChunkTasks : 8, 2, 32);

    public void SetViewer(Transform newViewer)
    {
      viewer = newViewer;

      if (worldRenderer != null)
      {
        worldRenderer.SetCollisionViewer(newViewer);
      }

      ForceRefreshStreamingSet();
    }

    // LOGS
    [SerializeField] private bool logStreamingStats;
    private float timeSinceLastStreamingLog;

    [Header("Pre-Generation")]
    [SerializeField] private bool useIncrementalPreGeneration = false;
    [SerializeField][Min(1)] private int preGenerationChunksPerFrame = 1;
    [SerializeField][Min(0)] private int spawnFirstRadiusInChunks = 0;
    [SerializeField] private bool continuePreGenerationInBackground = false;
    [SerializeField][Min(1)] private int maxAutoPreGenerationChunks = 1;
    [SerializeField] private bool allowVeryLargePreGeneration = false;

    private Coroutine bootstrapCoroutine;
    private bool backgroundPreGenerationActive;
    private int backgroundMinChunkX;
    private int backgroundMaxChunkX;
    private int backgroundMinChunkY;
    private int backgroundMaxChunkY;
    private int backgroundMinChunkZ;
    private int backgroundMaxChunkZ;
    private int backgroundCursorX;
    private int backgroundCursorY;
    private int backgroundCursorZ;
    private int backgroundGeneratedChunks;
    private int backgroundEstimatedChunks;

    [SerializeField] private bool evictCachedChunkDataOutsideKeepSet = true;

    [Header("Initial Terrain Ready")]
    [SerializeField] private Vector3 desiredInitialSpawnLocation = Vector3.zero;
    [SerializeField] private float initialSpawnSearchRadius = 128.0f;
    [SerializeField] private int initialSpawnSearchBelowVoxels = 512;
    [SerializeField] private int initialSpawnSearchAboveVoxels = 512;
    [SerializeField] private float initialSpawnClearance = 2.0f;

    private bool hasBroadcastInitialTerrainReady;
    private Vector3Int spawnTargetChunkCoord;
    private Vector3 GetSpawnReferencePosition()
    {
      if (viewer != null)
      {
        return viewer.position;
      }
      return desiredInitialSpawnLocation;
    }

    private Vector3Int WorldToChunkCoord(Vector3 worldPos)
    {
      Vector3 local = transform.InverseTransformPoint(worldPos);
      Vector3 voxel = local / Mathf.Max(0.0001f, world.Settings.VoxelSize);
      return new Vector3Int(
          VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.x), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.y), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.z), VoxelConstants.ChunkSize)
      );
    }

    private Vector3Int WorldToSurfaceChunkCoord(Vector3 worldPos)
    {
      Vector3 local = transform.InverseTransformPoint(worldPos);
      Vector3 voxel = local / Mathf.Max(0.0001f, world.Settings.VoxelSize);

      int chunkX = VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.x), VoxelConstants.ChunkSize);
      int chunkZ = VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.z), VoxelConstants.ChunkSize);
      int chunkY = GetSurfaceChunkYForColumn(chunkX, chunkZ, Mathf.FloorToInt(voxel.y));

      return new Vector3Int(chunkX, chunkY, chunkZ);
    }

    private int GetSurfaceChunkYForColumn(int chunkX, int chunkZ, int fallbackVoxelY)
    {
      if (generator == null)
      {
        return VoxelMath.FloorDiv(fallbackVoxelY, VoxelConstants.ChunkSize);
      }

      Vector2Int column = new(chunkX, chunkZ);

      if (surfaceChunkYCache.TryGetValue(column, out int cachedChunkY))
      {
        return cachedChunkY;
      }

      int surfaceChunkY = generator.GetSurfaceChunkYForChunkColumn(column);
      surfaceChunkYCache[column] = surfaceChunkY;
      return surfaceChunkY;
    }

    private int GetDensityCellStepForChunk(Vector3Int chunkCoord)
    {
      return 1;
    }

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      worldRenderer = GetComponent<WorldRenderer>();
      editTool = GetComponent<BlockEditTool>();
      storage = GetComponent<CubusWorldStorage>();
    }

    private void OnEnable()
    {
      if (editTool == null)
      {
        editTool = GetComponent<BlockEditTool>();
      }

      if (editTool != null)
      {
        editTool.BlockChunksEdited += HandleBlockChunksEdited;
      }
    }

    private void OnDisable()
    {
      if (editTool != null)
      {
        editTool.BlockChunksEdited -= HandleBlockChunksEdited;
      }
    }

    private void Start()
    {
      if (viewer == null && Camera.main != null)
      {
        viewer = Camera.main.transform;
      }

      if (!world.Settings.TryValidateConfiguration(out string configError))
      {
        Debug.LogError($"WorldStreamer disabled due to invalid world settings: {configError}");
        enabled = false;
        return;
      }

      StartBootstrap(logRegenerateMessage: false);
    }

    [ContextMenu("Regenerate Streamed World")]
    public void RegenerateStreamedWorld()
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (worldRenderer == null)
      {
        worldRenderer = GetComponent<WorldRenderer>();
      }

      if (world == null || worldRenderer == null)
      {
        Debug.LogError("WorldStreamer regeneration failed: missing CubusWorld or WorldRenderer.");
        return;
      }

      if (!world.Settings.TryValidateConfiguration(out string configError))
      {
        Debug.LogError($"WorldStreamer regeneration aborted due to invalid settings: {configError}");
        return;
      }

      world.SyncBiomeMaterialLayersFromRules();

      StartBootstrap(logRegenerateMessage: true);
    }

    private void StartBootstrap(bool logRegenerateMessage)
    {
      if (bootstrapCoroutine != null)
      {
        StopCoroutine(bootstrapCoroutine);
      }

      bootstrapCoroutine = StartCoroutine(BootstrapRoutine(logRegenerateMessage));
    }

    private IEnumerator BootstrapRoutine(bool logRegenerateMessage)
    {
      if (!world.IsWorldReady)
      {
        if (storage != null && storage.LoadWorldManifestOnly())
        {
          Debug.Log("WorldStreamer loaded generated world manifest from storage.");
        }
        else
        {
          Debug.LogWarning(
              "No generated world database manifest exists. Generating now before streaming starts."
          );

          yield return world.GenerateWorldAsync();

          if (!world.IsWorldReady)
          {
            Debug.LogError("WorldStreamer cannot start because world database generation failed.");
            bootstrapCoroutine = null;
            enabled = false;
            yield break;
          }

          if (storage != null)
          {
            storage.LoadWorldManifestOnly();
          }
        }
      }

      generator = new WorldGenerator(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);

      spawnTargetChunkCoord = WorldToSurfaceChunkCoord(GetSpawnReferencePosition());

      worldRenderer.ClearAll();
      ClearStreamingState();

      ForceRefreshStreamingSet();
      TryBroadcastInitialTerrainReady();

      if (logRegenerateMessage)
      {
        Debug.Log(
            $"WorldStreamer refreshed streamed views from generated world database. " +
            $"Mode={world.Settings.TerrainSystem}, " +
            $"BlockChunks={world.Data.BlockChunks.Count}, " +
            $"DensityChunks={world.Data.DensityChunks.Count}, " +
            $"BiomeRuleWorldScale={world.Settings.BiomeRuleWorldScale}, " +
            $"DensitySampleScale={world.Settings.DensitySampleScale}"
        );
      }

      bootstrapCoroutine = null;
    }
    private void Update()
    {
      if (bootstrapCoroutine != null)
      {
        return;
      }

      if (
          world.Settings.TerrainSystem != TerrainSystem.Block &&
          world.Settings.TerrainSystem != TerrainSystem.SmoothDensity)
      {
        return;
      }

      UpdateStreamingSetIfNeeded();
      ProcessRenderQueue();
      TryBroadcastInitialTerrainReady();

      if (logStreamingStats)
      {
        timeSinceLastStreamingLog += Time.deltaTime;

        if (timeSinceLastStreamingLog >= 1.0f)
        {
          timeSinceLastStreamingLog = 0.0f;

          Debug.Log(
              $"Streaming: ActiveViews={worldRenderer.ActiveChunkViews.Count}, " +
              $"BlockDataChunks={world.Data.BlockChunks.Count}, " +
              $"DensityDataChunks={world.Data.DensityChunks.Count}, " +
              $"PendingRender={pendingRenderQueue.Count}, " +
              $"KnownEmpty={knownEmptyChunks.Count}"
          );
        }
      }
    }

    [ContextMenu("Force Refresh Streaming Set")]
    public void ForceRefreshStreamingSet()
    {
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      surfaceChunkYCache.Clear();

      buildQueue.IncrementGeneration();
      densityBuildQueue.IncrementGeneration();

      hasLastViewerChunkCoord = false;
      UpdateStreamingSetIfNeeded(force: true);
    }

    private bool EnsureChunkDataAvailable(Vector3Int chunkCoord)
    {
      if (HasChunkData(chunkCoord))
      {
        return true;
      }

      if (storage == null)
      {
        return false;
      }

      if (!storage.TryLoadChunk(chunkCoord))
      {
        return false;
      }

      return HasChunkData(chunkCoord);
    }

    private void UpdateStreamingSetIfNeeded(bool force = false)
    {
      Vector3 viewerWorldPosition = viewer != null
          ? viewer.position
          : transform.TransformPoint(Vector3.zero);

      Vector3 localViewerPosition = transform.InverseTransformPoint(viewerWorldPosition);

      Vector3Int viewerChunkCoord = new(
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.x / world.Settings.VoxelSize), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.y / world.Settings.VoxelSize), VoxelConstants.ChunkSize),
          VoxelMath.FloorDiv(Mathf.FloorToInt(localViewerPosition.z / world.Settings.VoxelSize), VoxelConstants.ChunkSize)
      );

      if (!force && hasLastViewerChunkCoord && viewerChunkCoord == lastViewerChunkCoord)
      {
        return;
      }

      lastViewerChunkCoord = viewerChunkCoord;
      hasLastViewerChunkCoord = true;

      if (logStreamingStats)
      {
        Debug.Log(
            $"Streaming target. " +
            $"ViewerWorld={viewerWorldPosition}, " +
            $"ViewerChunk={viewerChunkCoord}, " +
            $"SpawnTargetChunk={spawnTargetChunkCoord}"
        );
      }

      BuildChunkSet(
        viewerChunkCoord,
        Mathf.Max(1, world.Settings.ViewDistanceInChunks),
        desiredChunkCoords
      );

      QueueGeneratedChunksForRender();

      BuildChunkSet(
        viewerChunkCoord,
        Mathf.Max(1, world.Settings.ViewDistanceInChunks) + UnloadPaddingInChunks,
        keepChunkCoords
      );

      UnloadOutsideKeepSet();
    }

    public void ClearStreamingState()
    {
      desiredChunkCoords.Clear();
      keepChunkCoords.Clear();

      pendingGenerateQueue.Clear();
      pendingGenerateSet.Clear();

      pendingRenderQueue.Clear();
      pendingRenderSet.Clear();

      knownEmptyChunks.Clear();
      forcedDensityRebuildChunks.Clear();

      buildQueue.IncrementGeneration();
      densityBuildQueue.IncrementGeneration();

      backgroundPreGenerationActive = false;
      backgroundGeneratedChunks = 0;
      backgroundEstimatedChunks = 0;

      hasLastViewerChunkCoord = false;
      hasBroadcastInitialTerrainReady = false;
    }

    private void BuildChunkSet(
        Vector3Int viewerChunkCoord,
        int horizontalRadius,
        HashSet<Vector3Int> targetSet)
    {
      targetSet.Clear();

      int safeHorizontalRadius = Mathf.Max(1, horizontalRadius);

      for (int z = -safeHorizontalRadius; z <= safeHorizontalRadius; z++)
      {
        for (int x = -safeHorizontalRadius; x <= safeHorizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;

          int surfaceChunkY = GetSurfaceChunkYForColumn(
              chunkX,
              chunkZ,
              viewerChunkCoord.y * VoxelConstants.ChunkSize
          );

          int minChunkY = surfaceChunkY - ChunksBelowSurface;
          int maxChunkY = surfaceChunkY + ChunksAboveSurface;

          for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
          {
            targetSet.Add(new Vector3Int(chunkX, chunkY, chunkZ));
          }
        }
      }
    }

    private void AddNearbyEditedChunks(
        Vector3Int viewerChunkCoord,
        int radius,
        HashSet<Vector3Int> output)
    {
      int radiusWithPadding = radius + UnloadPaddingInChunks;

      void AddIfNearby(Vector3Int editedChunkCoord)
      {
        int dx = Mathf.Abs(editedChunkCoord.x - viewerChunkCoord.x);
        int dz = Mathf.Abs(editedChunkCoord.z - viewerChunkCoord.z);

        if (dx > radiusWithPadding || dz > radiusWithPadding)
        {
          return;
        }

        output.Add(editedChunkCoord);
      }

      foreach (Vector3Int editedChunkCoord in world.Data.BlockVoxelOverridesByChunk.Keys)
      {
        AddIfNearby(editedChunkCoord);
      }

      foreach (Vector3Int editedChunkCoord in world.Data.DensityVoxelOverridesByChunk.Keys)
      {
        AddIfNearby(editedChunkCoord);
      }
    }

    private bool HasChunkData(Vector3Int chunkCoord)
    {
      return world.Settings.TerrainSystem switch
      {
        TerrainSystem.Block => world.Data.BlockChunks.ContainsKey(chunkCoord),
        TerrainSystem.SmoothDensity => world.Data.DensityChunks.ContainsKey(chunkCoord),
        _ => false
      };
    }

    private void QueueMissingChunks(Vector3Int viewerChunkCoord)
    {
      List<Vector3Int> missingChunkCoords = new();

      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (HasChunkData(chunkCoord))
        {
          continue;
        }

        if (worldRenderer.HasChunkView(chunkCoord))
        {
          continue;
        }

        if (pendingGenerateSet.Contains(chunkCoord))
        {
          continue;
        }

        if (pendingRenderSet.Contains(chunkCoord))
        {
          continue;
        }

        if (knownEmptyChunks.Contains(chunkCoord))
        {
          continue;
        }

        missingChunkCoords.Add(chunkCoord);
      }

      // Always prioritize the spawn target chunk if it is missing
      if (missingChunkCoords.Remove(spawnTargetChunkCoord))
      {
        pendingGenerateQueue.Enqueue(spawnTargetChunkCoord);
        pendingGenerateSet.Add(spawnTargetChunkCoord);
      }

      missingChunkCoords.Sort(
          (a, b) =>
          {
            int adx = a.x - viewerChunkCoord.x;
            int ady = a.y - viewerChunkCoord.y;
            int adz = a.z - viewerChunkCoord.z;

            int bdx = b.x - viewerChunkCoord.x;
            int bdy = b.y - viewerChunkCoord.y;
            int bdz = b.z - viewerChunkCoord.z;

            int aDistanceSquared = adx * adx + ady * ady + adz * adz;
            int bDistanceSquared = bdx * bdx + bdy * bdy + bdz * bdz;

            if (aDistanceSquared != bDistanceSquared)
            {
              return aDistanceSquared.CompareTo(bDistanceSquared);
            }

            int aAbsY = Mathf.Abs(a.y);
            int bAbsY = Mathf.Abs(b.y);

            if (aAbsY != bAbsY)
            {
              return aAbsY.CompareTo(bAbsY);
            }

            return a.y.CompareTo(b.y);
          }
      );

      foreach (Vector3Int chunkCoord in missingChunkCoords)
      {
        pendingGenerateQueue.Enqueue(chunkCoord);
        pendingGenerateSet.Add(chunkCoord);
      }
    }

    private void ProcessGenerateQueue()
    {
      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          if (UseAsyncGeneration)
          {
            ProcessBlockGenerateQueueAsync();
          }
          else
          {
            ProcessGenerateQueueSynchronous();
          }
          break;

        case TerrainSystem.SmoothDensity:
          ProcessDensityGenerateQueueAsync();
          break;
      }
    }

    private void ProcessGenerateQueueSynchronous()
    {
      int generatedThisFrame = 0;

      int generateBudget = hasBroadcastInitialTerrainReady
          ? ChunksGeneratedPerFrame
          : InitialChunksGeneratedPerFrame;

      while (
          pendingGenerateQueue.Count > 0 &&
          generatedThisFrame < generateBudget)
      {
        Vector3Int chunkCoord = pendingGenerateQueue.Dequeue();
        pendingGenerateSet.Remove(chunkCoord);
        bool forceRebuild = forcedDensityRebuildChunks.Remove(chunkCoord);

        if (!forceRebuild && !desiredChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        if (!forceRebuild && worldRenderer.HasChunkView(chunkCoord))
        {
          continue;
        }

        BlockChunkData chunkData = new(chunkCoord);

        if (world.TryGetBlockOverrides(chunkCoord, out var overrides))
        {
          generator.GenerateBlockChunkDataWithOverrides(chunkData, overrides);
        }
        else
        {
          generator.GenerateBlockChunkData(chunkData);
        }

        if (!chunkData.HasAnySolidVoxel())
        {
          knownEmptyChunks.Add(chunkCoord);
          world.Data.BlockChunks.Remove(chunkCoord);
          generatedThisFrame++;
          continue;
        }

        knownEmptyChunks.Remove(chunkCoord);
        world.Data.BlockChunks[chunkCoord] = chunkData;

        QueueBlockChunkAndNeighboursForRender(chunkCoord);

        generatedThisFrame++;
      }
    }

    private void ProcessBlockGenerateQueueAsync()
    {
      int startedThisFrame = 0;

      int generateBudget = hasBroadcastInitialTerrainReady
          ? ChunksGeneratedPerFrame
          : InitialChunksGeneratedPerFrame;

      while (
          pendingGenerateQueue.Count > 0 &&
          startedThisFrame < generateBudget &&
          buildQueue.ActiveTaskCount < MaxAsyncChunkTasks)
      {
        Vector3Int chunkCoord = pendingGenerateQueue.Dequeue();
        pendingGenerateSet.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        if (worldRenderer.HasChunkView(chunkCoord))
        {
          continue;
        }

        if (buildQueue.IsInFlight(chunkCoord))
        {
          continue;
        }

        BlockChunkBuildRequest request = new()
        {
          ChunkCoord = chunkCoord,
          GenerationId = buildQueue.GenerationId,
          WorldSnapshot = worldSnapshot,
          OverrideSnapshot = world.CreateBlockOverrideSnapshot(chunkCoord)
        };

        if (buildQueue.TryStartBuild(request, MaxAsyncChunkTasks))
        {
          startedThisFrame++;
        }
      }
    }

    private void ProcessDensityGenerateQueueAsync()
    {
      int startedThisFrame = 0;

      int generateBudget = hasBroadcastInitialTerrainReady
          ? ChunksGeneratedPerFrame
          : InitialChunksGeneratedPerFrame;

      while (
          pendingGenerateQueue.Count > 0 &&
          startedThisFrame < generateBudget &&
          densityBuildQueue.ActiveTaskCount < MaxAsyncChunkTasks)
      {
        Vector3Int chunkCoord = pendingGenerateQueue.Dequeue();
        pendingGenerateSet.Remove(chunkCoord);

        bool forceRebuild = forcedDensityRebuildChunks.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        if (!forceRebuild && worldRenderer.HasChunkView(chunkCoord))
        {
          continue;
        }

        if (densityBuildQueue.IsInFlight(chunkCoord))
        {
          continue;
        }

        // For forced rebuilds (post-edit), pass a snapshot of the existing chunk data
        // so the background build can skip expensive terrain noise re-generation.
        DensityChunkData existingSnapshot = null;
        if (forceRebuild && world.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData existing))
        {
          existingSnapshot = existing.Clone();
        }

        int densityCellStep = GetDensityCellStepForChunk(chunkCoord);

        DensityChunkBuildRequest request = new()
        {
          ChunkCoord = chunkCoord,
          GenerationId = densityBuildQueue.GenerationId,
          WorldSnapshot = worldSnapshot,
          CellStep = densityCellStep,
          FlipWinding = true,
          OverrideSnapshot = world.CreateDensityOverrideSnapshot(chunkCoord),
          ChunkDataSnapshot = existingSnapshot
        };

        if (densityBuildQueue.TryStartBuild(
           request,
           MaxAsyncChunkTasks))
        {
          startedThisFrame++;
        }
      }
    }

    private void ProcessCompletedBuildResults()
    {
      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          ProcessCompletedBlockBuildResults();
          break;

        case TerrainSystem.SmoothDensity:
          ProcessCompletedDensityBuildResults();
          break;
      }
    }

    private void ProcessCompletedBlockBuildResults()
    {
      int appliedThisFrame = 0;

      while (
          appliedThisFrame < MeshAppliesPerFrame &&
          buildQueue.TryDequeueCompleted(out BlockChunkBuildResult result))
      {
        if (result.GenerationId != buildQueue.GenerationId)
        {
          appliedThisFrame++;
          continue;
        }

        if (!desiredChunkCoords.Contains(result.ChunkCoord))
        {
          appliedThisFrame++;
          continue;
        }

        if (result.IsEmpty || result.ChunkData == null)
        {
          knownEmptyChunks.Add(result.ChunkCoord);
          world.Data.BlockChunks.Remove(result.ChunkCoord);
          worldRenderer.RemoveChunk(result.ChunkCoord);

          appliedThisFrame++;
          continue;
        }

        knownEmptyChunks.Remove(result.ChunkCoord);
        world.Data.BlockChunks[result.ChunkCoord] = result.ChunkData;

        if (result.MeshData != null)
        {
          MeshDataPool.Return(result.MeshData);
        }

        QueueBlockChunkAndNeighboursForRender(result.ChunkCoord);

        appliedThisFrame++;
      }
    }

    public void RebuildDensityChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (world.Settings.TerrainSystem != TerrainSystem.SmoothDensity)
      {
        return;
      }

      foreach (Vector3Int chunkCoord in dirtyChunks)
      {
        knownEmptyChunks.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          desiredChunkCoords.Add(chunkCoord);
        }

        // Re-render from existing generated/edited density data.
        // Do not regenerate terrain data here.
        worldRenderer.RemoveChunk(chunkCoord);
        QueueRender(chunkCoord);
      }
    }

    private void ProcessCompletedDensityBuildResults()
    {
      int appliedThisFrame = 0;

      while (
          appliedThisFrame < MeshAppliesPerFrame &&
          densityBuildQueue.TryDequeueCompleted(out DensityChunkBuildResult result))
      {
        if (result == null)
        {
          appliedThisFrame++;
          continue;
        }

        if (result.GenerationId != densityBuildQueue.GenerationId)
        {
          if (logStreamingStats)
          {
            Debug.Log(
                $"Density result stale. Chunk={result.ChunkCoord}, ResultGen={result.GenerationId}, CurrentGen={densityBuildQueue.GenerationId}");
          }

          appliedThisFrame++;
          continue;
        }

        if (!desiredChunkCoords.Contains(result.ChunkCoord))
        {
          if (logStreamingStats)
          {
            Debug.Log($"Density result not desired anymore. Chunk={result.ChunkCoord}");
          }

          appliedThisFrame++;
          continue;
        }

        int vertexCount = result.MeshData != null ? result.MeshData.VertexCount : 0;
        int triangleCount = result.MeshData != null ? result.MeshData.TriangleCount : 0;

        if (logStreamingStats)
        {
          Debug.Log(
            $"Density result. Chunk={result.ChunkCoord}, " +
            $"ChunkDataNull={result.ChunkData == null}, " +
            $"IsEmpty={result.IsEmpty}, " +
            $"HasSurfaceCrossing={result.HasSurfaceCrossing}, " +
            $"Verts={vertexCount}, Tris={triangleCount}");
        }

        if (result.ChunkData == null ||
            result.IsEmpty ||
            !result.HasSurfaceCrossing ||
            !result.ChunkData.HasAnySolidVoxel() ||
            !result.ChunkData.HasSurfaceCrossing())
        {
          knownEmptyChunks.Add(result.ChunkCoord);
          world.Data.DensityChunks.Remove(result.ChunkCoord);
          worldRenderer.RemoveChunk(result.ChunkCoord);

          appliedThisFrame++;
          continue;
        }

        knownEmptyChunks.Remove(result.ChunkCoord);
        world.Data.DensityChunks[result.ChunkCoord] = result.ChunkData;

        int cellStep = GetDensityCellStepForChunk(result.ChunkCoord);
        float densityScale = Mathf.Max(0.001f, worldSnapshot.DensitySampleScale);

        Mesh mesh = MarchingCubesMesher.GenerateMeshDirect(
            result.ChunkCoord,
            worldSnapshot,
            cellStep,
            true,
            worldVoxel => SampleDensityForMeshing(
                worldVoxel,
                result.ChunkCoord,
                result.ChunkData,
                densityScale
            )
        );

        if (mesh == null ||
            mesh.vertexCount == 0 ||
            mesh.subMeshCount == 0 ||
            mesh.GetIndexCount(0) == 0)
        {
          knownEmptyChunks.Add(result.ChunkCoord);
          world.Data.DensityChunks.Remove(result.ChunkCoord);

          if (logStreamingStats)
          {
            Debug.LogWarning($"Density chunk has no mesh. Chunk={result.ChunkCoord}");
          }

          worldRenderer.RemoveChunk(result.ChunkCoord);

          appliedThisFrame++;
          continue;
        }

        knownEmptyChunks.Remove(result.ChunkCoord);

        if (logStreamingStats)
        {
          Debug.Log(
              $"Rendering density mesh. Chunk={result.ChunkCoord}, " +
              $"Verts={mesh.vertexCount}, Indices={mesh.GetIndexCount(0)}, CellStep={cellStep}");
        }

        worldRenderer.RenderUnityMesh(
            result.ChunkCoord,
            mesh,
            ShouldGenerateDensityCollision(result.ChunkCoord)
        );

        TryBroadcastInitialTerrainReady();

        appliedThisFrame++;
      }
    }

    private DensityVoxel SampleDensityForMeshing(
        Vector3Int worldVoxelCoord,
        Vector3Int rootChunkCoord,
        DensityChunkData rootChunkData,
        float densityScale)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      DensityChunkData sourceChunk = null;

      if (chunkCoord == rootChunkCoord)
      {
        sourceChunk = rootChunkData;
      }
      else
      {
        world.Data.DensityChunks.TryGetValue(chunkCoord, out sourceChunk);
      }

      if (sourceChunk != null)
      {
        return sourceChunk.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      // Outside the generated database is empty space.
      // The streamer must not generate terrain while rendering.
      return new DensityVoxel(0.0f, 0);
    }

    private void QueueRender(Vector3Int chunkCoord)
    {
      if (pendingRenderSet.Contains(chunkCoord))
      {
        return;
      }

      pendingRenderQueue.Enqueue(chunkCoord);
      pendingRenderSet.Add(chunkCoord);
    }

    private void ProcessRenderQueue()
    {
      int renderedThisFrame = 0;

      while (
          pendingRenderQueue.Count > 0 &&
          renderedThisFrame < ChunksRenderedPerFrame)
      {
        Vector3Int chunkCoord = pendingRenderQueue.Dequeue();
        pendingRenderSet.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData) ||
              chunkData == null ||
              !chunkData.HasAnySolidVoxel())
          {
            continue;
          }

          if (world.TryGetBlockOverrides(chunkCoord, out var blockOverrides) &&
              blockOverrides != null)
          {
            BlockChunkBuilder.ApplyOverrides(chunkData, blockOverrides);
          }

          worldRenderer.RenderBlockChunk(chunkCoord, chunkData);
          TryBroadcastInitialTerrainReady();
          renderedThisFrame++;
          continue;
        }

        if (!world.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData densityChunkData) ||
            densityChunkData == null ||
            !densityChunkData.HasSurfaceCrossing())
        {
          knownEmptyChunks.Add(chunkCoord);
          worldRenderer.RemoveChunk(chunkCoord);
          continue;
        }

        RenderDensityChunk(chunkCoord, densityChunkData);
        renderedThisFrame++;
      }
    }

    private void QueueGeneratedChunksForRender()
    {
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (worldRenderer.HasChunkView(chunkCoord))
        {
          continue;
        }

        if (pendingRenderSet.Contains(chunkCoord))
        {
          continue;
        }

        if (knownEmptyChunks.Contains(chunkCoord))
        {
          continue;
        }

        if (!EnsureChunkDataAvailable(chunkCoord))
        {
          knownEmptyChunks.Add(chunkCoord);
          continue;
        }

        QueueRender(chunkCoord);
      }
    }

    private IEnumerator PreGenerateSpawnRegionIfNeededAsync()
    {
      if (!world.Settings.UseFixedGenerationBounds)
      {
        yield break;
      }

      int estimatedChunks = GetEstimatedPreGenerationChunkCount();

      if (!allowVeryLargePreGeneration && estimatedChunks > Mathf.Max(1, maxAutoPreGenerationChunks))
      {
        Debug.LogWarning(
            $"Fixed-bounds estimate is large, using spawn-first generation. " +
            $"EstimatedChunks={estimatedChunks}, Limit={maxAutoPreGenerationChunks}."
        );
      }

      world.Settings.GetGenerationChunkBoundsXZ(
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        world.Settings.GetEffectiveBlockChunkYRange(out int minChunkY, out int maxChunkY);

        int clampedMinX = Mathf.Max(minChunkX, spawnTargetChunkCoord.x - spawnFirstRadiusInChunks);
        int clampedMaxX = Mathf.Min(maxChunkX, spawnTargetChunkCoord.x + spawnFirstRadiusInChunks);
        int clampedMinZ = Mathf.Max(minChunkZ, spawnTargetChunkCoord.z - spawnFirstRadiusInChunks);
        int clampedMaxZ = Mathf.Min(maxChunkZ, spawnTargetChunkCoord.z + spawnFirstRadiusInChunks);

        int processed = 0;
        int budget = Mathf.Max(1, preGenerationChunksPerFrame);

        Debug.Log(
            $"Spawn-first pre-generation... Mode={world.Settings.TerrainSystem}, Radius={spawnFirstRadiusInChunks}, EstimatedTotal={estimatedChunks}"
        );

        for (int y = minChunkY; y <= maxChunkY; y++)
        {
          for (int z = clampedMinZ; z <= clampedMaxZ; z++)
          {
            for (int x = clampedMinX; x <= clampedMaxX; x++)
            {
              GenerateAndStoreChunk(new Vector3Int(x, y, z));

              processed++;
              if (useIncrementalPreGeneration && processed % budget == 0)
              {
                yield return null;
              }
            }
          }
        }

        Debug.Log(
            $"Spawn-first pre-generation complete. ProcessedChunks={processed}, BlockChunks={world.Data.BlockChunks.Count}"
        );

        yield break;
      }

      world.Settings.GetEffectiveDensityChunkYRange(out int densityMinChunkY, out int densityMaxChunkY);

      int densityClampedMinX = Mathf.Max(minChunkX, spawnTargetChunkCoord.x - spawnFirstRadiusInChunks);
      int densityClampedMaxX = Mathf.Min(maxChunkX, spawnTargetChunkCoord.x + spawnFirstRadiusInChunks);
      int densityClampedMinZ = Mathf.Max(minChunkZ, spawnTargetChunkCoord.z - spawnFirstRadiusInChunks);
      int densityClampedMaxZ = Mathf.Min(maxChunkZ, spawnTargetChunkCoord.z + spawnFirstRadiusInChunks);

      int densityProcessed = 0;
      int densityBudget = Mathf.Max(1, preGenerationChunksPerFrame);

      Debug.Log(
          $"Spawn-first pre-generation... Mode={world.Settings.TerrainSystem}, Radius={spawnFirstRadiusInChunks}, EstimatedTotal={estimatedChunks}"
      );

      for (int y = densityMinChunkY; y <= densityMaxChunkY; y++)
      {
        for (int z = densityClampedMinZ; z <= densityClampedMaxZ; z++)
        {
          for (int x = densityClampedMinX; x <= densityClampedMaxX; x++)
          {
            GenerateAndStoreChunk(new Vector3Int(x, y, z));

            densityProcessed++;
            if (useIncrementalPreGeneration && densityProcessed % densityBudget == 0)
            {
              yield return null;
            }
          }
        }
      }

      Debug.Log(
          $"Spawn-first pre-generation complete. ProcessedChunks={densityProcessed}, DensityChunks={world.Data.DensityChunks.Count}"
      );
    }

    private void ConfigureBackgroundPreGenerationIfNeeded()
    {
      backgroundPreGenerationActive = false;

      if (!world.Settings.UseFixedGenerationBounds || !continuePreGenerationInBackground)
      {
        return;
      }

      world.Settings.GetGenerationChunkBoundsXZ(
          out backgroundMinChunkX,
          out backgroundMaxChunkX,
          out backgroundMinChunkZ,
          out backgroundMaxChunkZ
      );

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        world.Settings.GetEffectiveBlockChunkYRange(out backgroundMinChunkY, out backgroundMaxChunkY);
      }
      else
      {
        world.Settings.GetEffectiveDensityChunkYRange(out backgroundMinChunkY, out backgroundMaxChunkY);
      }

      backgroundCursorX = backgroundMinChunkX;
      backgroundCursorY = backgroundMinChunkY;
      backgroundCursorZ = backgroundMinChunkZ;
      backgroundGeneratedChunks = 0;
      backgroundEstimatedChunks = GetEstimatedPreGenerationChunkCount();
      backgroundPreGenerationActive = true;

      Debug.Log(
          $"Background pre-generation started. EstimatedChunks={backgroundEstimatedChunks}, BudgetPerFrame={Mathf.Max(1, preGenerationChunksPerFrame)}"
      );
    }

    private void ProcessBackgroundPreGeneration()
    {
      if (!backgroundPreGenerationActive)
      {
        return;
      }

      if (!hasBroadcastInitialTerrainReady)
      {
        return;
      }

      // Prioritize visible streaming work first so background filling does not
      // steal frame time while chunks around the player are still loading.
      if (pendingGenerateQueue.Count > 0 ||
          pendingRenderQueue.Count > 0 ||
          buildQueue.ActiveTaskCount > 0 ||
          densityBuildQueue.ActiveTaskCount > 0)
      {
        return;
      }

      int budget = Mathf.Max(1, preGenerationChunksPerFrame);

      for (int i = 0; i < budget; i++)
      {
        if (!TryDequeueBackgroundChunk(out Vector3Int chunkCoord))
        {
          backgroundPreGenerationActive = false;
          Debug.Log(
              $"Background pre-generation complete. GeneratedOrTouched={backgroundGeneratedChunks}, BlockChunks={world.Data.BlockChunks.Count}, DensityChunks={world.Data.DensityChunks.Count}"
          );
          return;
        }

        GenerateAndStoreChunk(chunkCoord);
        backgroundGeneratedChunks++;

        if (desiredChunkCoords.Contains(chunkCoord) && !worldRenderer.HasChunkView(chunkCoord))
        {
          QueueRender(chunkCoord);
        }
      }
    }

    private bool TryDequeueBackgroundChunk(out Vector3Int chunkCoord)
    {
      if (!backgroundPreGenerationActive)
      {
        chunkCoord = default;
        return false;
      }

      if (backgroundCursorY > backgroundMaxChunkY)
      {
        chunkCoord = default;
        return false;
      }

      chunkCoord = new Vector3Int(backgroundCursorX, backgroundCursorY, backgroundCursorZ);

      backgroundCursorX++;
      if (backgroundCursorX > backgroundMaxChunkX)
      {
        backgroundCursorX = backgroundMinChunkX;
        backgroundCursorZ++;

        if (backgroundCursorZ > backgroundMaxChunkZ)
        {
          backgroundCursorZ = backgroundMinChunkZ;
          backgroundCursorY++;
        }
      }

      return true;
    }

    private void GenerateAndStoreChunk(Vector3Int chunkCoord)
    {
      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          {
            if (world.Data.BlockChunks.ContainsKey(chunkCoord))
            {
              knownEmptyChunks.Remove(chunkCoord);
              return;
            }

            BlockChunkData chunkData = new(chunkCoord);

            if (world.TryGetBlockOverrides(chunkCoord, out var blockOverrides))
            {
              generator.GenerateBlockChunkDataWithOverrides(chunkData, blockOverrides);
            }
            else
            {
              generator.GenerateBlockChunkData(chunkData);
            }

            if (chunkData.HasAnySolidVoxel())
            {
              knownEmptyChunks.Remove(chunkCoord);
              world.Data.BlockChunks[chunkCoord] = chunkData;
            }
            else
            {
              knownEmptyChunks.Add(chunkCoord);
              world.Data.BlockChunks.Remove(chunkCoord);
            }

            return;
          }

        case TerrainSystem.SmoothDensity:
          {
            if (world.Data.DensityChunks.ContainsKey(chunkCoord))
            {
              knownEmptyChunks.Remove(chunkCoord);
              return;
            }

            DensityChunkData chunkData = DensityChunkBuilder.GenerateChunkData(
                chunkCoord,
                worldSnapshot,
                world.CreateDensityOverrideSnapshot(chunkCoord)
            );

            if (chunkData.HasAnySolidVoxel())
            {
              knownEmptyChunks.Remove(chunkCoord);
              world.Data.DensityChunks[chunkCoord] = chunkData;
            }
            else
            {
              knownEmptyChunks.Add(chunkCoord);
              world.Data.DensityChunks.Remove(chunkCoord);
            }

            return;
          }
      }
    }

    private int GetEstimatedPreGenerationChunkCount()
    {
      world.Settings.GetGenerationChunkBoundsXZ(
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      int xCount = Mathf.Max(0, maxChunkX - minChunkX + 1);
      int zCount = Mathf.Max(0, maxChunkZ - minChunkZ + 1);

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        world.Settings.GetEffectiveBlockChunkYRange(out int minChunkY, out int maxChunkY);
        int yCount = Mathf.Max(0, maxChunkY - minChunkY + 1);
        return xCount * yCount * zCount;
      }

      world.Settings.GetEffectiveDensityChunkYRange(out int densityMinY, out int densityMaxY);
      int densityYCount = Mathf.Max(0, densityMaxY - densityMinY + 1);
      return xCount * densityYCount * zCount;
    }

    private void RenderDensityChunk(Vector3Int chunkCoord, DensityChunkData chunkData)
    {
      if (chunkData == null || !chunkData.HasSurfaceCrossing())
      {
        knownEmptyChunks.Add(chunkCoord);
        worldRenderer.RemoveChunk(chunkCoord);
        return;
      }

      int cellStep = GetDensityCellStepForChunk(chunkCoord);
      float densityScale = Mathf.Max(0.001f, worldSnapshot.DensitySampleScale);

      Mesh mesh = MarchingCubesMesher.GenerateMeshDirect(
          chunkCoord,
          worldSnapshot,
          cellStep,
          true,
          worldVoxel => SampleDensityForMeshing(
              worldVoxel,
              chunkCoord,
              chunkData,
              densityScale
          )
      );

      if (mesh == null ||
          mesh.vertexCount == 0 ||
          mesh.subMeshCount == 0 ||
          mesh.GetIndexCount(0) == 0)
      {
        knownEmptyChunks.Add(chunkCoord);
        worldRenderer.RemoveChunk(chunkCoord);
        return;
      }

      knownEmptyChunks.Remove(chunkCoord);

      if (logStreamingStats)
      {
        Debug.Log(
            $"RenderDensityChunk. " +
            $"Chunk={chunkCoord}, " +
            $"Verts={mesh.vertexCount}, " +
            $"Indices={mesh.GetIndexCount(0)}, " +
            $"Collision={ShouldGenerateDensityCollision(chunkCoord)}"
        );
      }

      worldRenderer.RenderUnityMesh(
          chunkCoord,
          mesh,
          ShouldGenerateDensityCollision(chunkCoord)
      );

      worldRenderer.RefreshChunkCollision();
    }

    private bool ShouldGenerateDensityCollision(Vector3Int chunkCoord)
    {
      if (!hasBroadcastInitialTerrainReady && chunkCoord == spawnTargetChunkCoord)
      {
        return true;
      }

      Vector3 referencePosition = GetSpawnReferencePosition();
      Vector3Int viewerChunkCoord = WorldToChunkCoord(referencePosition);

      int dx = Mathf.Abs(chunkCoord.x - viewerChunkCoord.x);
      int dy = Mathf.Abs(chunkCoord.y - viewerChunkCoord.y);
      int dz = Mathf.Abs(chunkCoord.z - viewerChunkCoord.z);

      // Cook collision only for the local movement bubble.
      // This prevents falling through when stepping into the next chunk,
      // without cooking colliders for the entire streamed terrain.
      return dx <= 1 && dy <= 1 && dz <= 1;
    }


    private void UnloadOutsideKeepSet()
    {
      List<Vector3Int> chunksToUnload = new();

      foreach (Vector3Int chunkCoord in worldRenderer.ActiveChunkViews.Keys)
      {
        if (!keepChunkCoords.Contains(chunkCoord))
        {
          chunksToUnload.Add(chunkCoord);
        }
      }

      for (int i = 0; i < chunksToUnload.Count; i++)
      {
        Vector3Int chunkCoord = chunksToUnload[i];

        worldRenderer.RemoveChunk(chunkCoord);

        if (evictCachedChunkDataOutsideKeepSet && storage != null)
        {
          RemoveChunkData(chunkCoord);
        }
      }
    }

    private void RemoveChunkData(Vector3Int chunkCoord)
    {
      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          world.Data.BlockChunks.Remove(chunkCoord);
          break;

        case TerrainSystem.SmoothDensity:
          world.Data.DensityChunks.Remove(chunkCoord);
          break;
      }
    }

    private void HandleBlockChunksEdited(IReadOnlyCollection<Vector3Int> dirtyChunks)
    {
      foreach (Vector3Int chunkCoord in dirtyChunks)
      {
        knownEmptyChunks.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          desiredChunkCoords.Add(chunkCoord);
        }

        QueueBlockChunkAndNeighboursForRender(chunkCoord);
      }
    }

    private void TryBroadcastInitialTerrainReady()
    {
      if (hasBroadcastInitialTerrainReady)
      {
        return;
      }

      if (world == null || worldRenderer == null)
      {
        return;
      }

      if (!worldRenderer.ActiveChunkViews.ContainsKey(spawnTargetChunkCoord))
      {
        return;
      }

      if (!TryFindRenderedSpawnPosition(out Vector3 spawnPosition))
      {
        return;
      }

      Debug.Log(
          $"Initial terrain ready. " +
          $"SpawnPosition={spawnPosition}, " +
          $"SpawnTargetChunk={spawnTargetChunkCoord}, " +
          $"ActiveViews={worldRenderer.ActiveChunkViews.Count}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, " +
          $"DensityChunks={world.Data.DensityChunks.Count}"
      );

      world.BroadcastInitialTerrainReady(spawnPosition);
      hasBroadcastInitialTerrainReady = true;
    }

    private bool TryFindRenderedSpawnPosition(out Vector3 spawnPosition)
    {
      spawnPosition = Vector3.zero;

      Vector3 referenceWorldPosition = GetSpawnReferencePosition();
      Vector3 localReference = transform.InverseTransformPoint(referenceWorldPosition);

      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);

      int referenceVoxelX = Mathf.FloorToInt(localReference.x / voxelSize);
      int referenceVoxelZ = Mathf.FloorToInt(localReference.z / voxelSize);

      int searchRadiusVoxels = Mathf.CeilToInt(initialSpawnSearchRadius / voxelSize);
      int step = Mathf.Max(1, VoxelConstants.ChunkSize / 2);

      float bestDistanceSq = float.PositiveInfinity;
      Vector3 bestSpawn = Vector3.zero;
      bool found = false;

      for (int dz = -searchRadiusVoxels; dz <= searchRadiusVoxels; dz += step)
      {
        for (int dx = -searchRadiusVoxels; dx <= searchRadiusVoxels; dx += step)
        {
          int voxelX = referenceVoxelX + dx;
          int voxelZ = referenceVoxelZ + dz;

          if (!TryFindHighestRenderedSolidVoxelInColumn(
                  voxelX,
                  voxelZ,
                  out int surfaceVoxelY))
          {
            continue;
          }

          Vector3Int surfaceChunkCoord = new(
            VoxelMath.FloorDiv(voxelX, VoxelConstants.ChunkSize),
            VoxelMath.FloorDiv(surfaceVoxelY, VoxelConstants.ChunkSize),
            VoxelMath.FloorDiv(voxelZ, VoxelConstants.ChunkSize)
        );

          if (!worldRenderer.HasChunkView(surfaceChunkCoord))
          {
            continue;
          }

          Vector3 candidateLocal = new(
              (voxelX + 0.5f) * voxelSize,
              (surfaceVoxelY + initialSpawnClearance) * voxelSize,
              (voxelZ + 0.5f) * voxelSize
          );

          float distanceSq = (candidateLocal - localReference).sqrMagnitude;

          if (distanceSq < bestDistanceSq)
          {
            bestDistanceSq = distanceSq;
            bestSpawn = transform.TransformPoint(candidateLocal);
            found = true;
          }
        }
      }

      if (!found)
      {
        return false;
      }

      spawnPosition = bestSpawn;
      return true;
    }

    private bool TryFindHighestRenderedSolidVoxelInColumn(
        int voxelX,
        int voxelZ,
        out int surfaceVoxelY)
    {
      surfaceVoxelY = 0;

      bool found = false;
      int bestY = int.MinValue;

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in worldRenderer.ActiveChunkViews)
      {
        Vector3Int chunkCoord = pair.Key;

        int minX = chunkCoord.x * VoxelConstants.ChunkSize;
        int maxX = minX + VoxelConstants.ChunkSize - 1;

        int minZ = chunkCoord.z * VoxelConstants.ChunkSize;
        int maxZ = minZ + VoxelConstants.ChunkSize - 1;

        if (voxelX < minX || voxelX > maxX || voxelZ < minZ || voxelZ > maxZ)
        {
          continue;
        }

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData blockChunk) ||
              blockChunk == null)
          {
            continue;
          }

          int localX = voxelX - minX;
          int localZ = voxelZ - minZ;

          for (int localY = VoxelConstants.ChunkSize - 1; localY >= 0; localY--)
          {
            if (!blockChunk.IsSolid(localX, localY, localZ))
            {
              continue;
            }

            int worldY = chunkCoord.y * VoxelConstants.ChunkSize + localY;

            if (worldY > bestY)
            {
              bestY = worldY;
              found = true;
            }

            break;
          }
        }
        else if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity)
        {
          if (!world.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData densityChunk) ||
              densityChunk == null)
          {
            continue;
          }

          int localX = voxelX - minX;
          int localZ = voxelZ - minZ;

          for (int localY = VoxelConstants.ChunkSize - 1; localY >= 0; localY--)
          {
            if (!densityChunk.GetVoxel(localX, localY, localZ).IsSolid)
            {
              continue;
            }

            int worldY = chunkCoord.y * VoxelConstants.ChunkSize + localY;

            if (worldY > bestY)
            {
              bestY = worldY;
              found = true;
            }

            break;
          }
        }
      }

      if (!found)
      {
        return false;
      }

      surfaceVoxelY = bestY;
      return true;
    }

    private void QueueBlockChunkAndNeighboursForRender(Vector3Int chunkCoord)
    {
      QueueRender(chunkCoord);
      QueueRender(chunkCoord + Vector3Int.left);
      QueueRender(chunkCoord + Vector3Int.right);
      QueueRender(chunkCoord + Vector3Int.down);
      QueueRender(chunkCoord + Vector3Int.up);
      QueueRender(chunkCoord + new Vector3Int(0, 0, -1));
      QueueRender(chunkCoord + new Vector3Int(0, 0, 1));
    }
  }
}