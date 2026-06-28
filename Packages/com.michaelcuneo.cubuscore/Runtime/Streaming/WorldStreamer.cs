using System;
using System.Collections;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  [DefaultExecutionOrder(-10)]
  public sealed partial class WorldStreamer : MonoBehaviour
  {
    [SerializeField] private Transform viewer;
    [SerializeField] private StreamingSettings settings = new();
    [SerializeField][Min(1)] private int initialSpawnRequiredRenderedChunks = 9;
    [SerializeField] private Vector3 desiredInitialSpawnLocation = Vector3.zero;
    [SerializeField] private float initialSpawnClearance = 2.0f;
    [SerializeField] private bool evictCachedChunkDataOutsideKeepSet = true;
    [SerializeField] private bool generateWorldDatabaseBeforeStreaming = false;
    [SerializeField] private bool persistStreamedChunks = true;

    [Header("Initial Streaming Stage")]
    [SerializeField][Min(0)] private int initialStreamingRadiusInChunks = 1;
    [SerializeField] private bool useInitialStreamingStage = true;
    [SerializeField][Min(0)] private int initialKeepPaddingInChunks = 1;

    private readonly HashSet<Vector3Int> desiredChunkCoords = new();
    private readonly HashSet<Vector3Int> keepChunkCoords = new();
    private readonly Queue<Vector3Int> pendingLoadQueue = new();
    private readonly HashSet<Vector3Int> pendingLoadSet = new();
    private readonly Queue<Vector3Int> pendingRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingRenderSet = new();
    private readonly WorldStreamingQueueSet pendingUnload = new();
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly Dictionary<Vector2Int, int> surfaceChunkYCache = new();
    private readonly List<Vector3Int> candidateChunksBuffer = new();
    private readonly List<Vector3Int> queueSortBuffer = new();
    private readonly List<Vector3Int> unloadChunksBuffer = new();

    private readonly BlockChunkBuildQueue buildQueue = new();
    private readonly DensityChunkBuildQueue densityBuildQueue = new();
    private readonly ChunkLoadQueue chunkLoadQueue = new();

    private CubusWorld world;
    private CubusWorldStorage storage;
    private WorldRenderer worldRenderer;
    private WorldGenerator generator;
    private BlockEditTool editTool;
    private WorldGenerationSnapshot worldSnapshot;
    private Coroutine bootstrapCoroutine;
    private Vector3Int lastViewerChunkCoord;
    private bool hasLastViewerChunkCoord;
    private Vector3Int viewerHeading;
    private Vector3Int spawnTargetChunkCoord;
    private bool hasBroadcastInitialTerrainReady;
    private bool pendingLoadQueueNeedsPrioritization;
    private bool pendingRenderQueueNeedsPrioritization;
    private bool renderReconciliationPending;
    // Cached once on the main thread; SystemInfo.processorCount is a native call
    // that was previously hit many times per frame inside the streaming loops.
    private int cachedMaxHardwareConcurrency = 1;
    // Reusable sort state so the priority comparison can be a single cached
    // delegate instead of allocating a closure + Comparison delegate per sort.
    private Vector3Int sortPivotChunkCoord;
    private Comparison<Vector3Int> chunkPriorityComparison;
    private long totalChunkLoadRequestsStarted;
    private long totalChunkLoadsCompleted;
    private long totalChunkLoadFailures;
    private long totalBlockMeshApplies;
    private long totalDensityMeshApplies;
    private long totalChunkUnloadsApplied;
    private int adaptiveThrottleFramesRemaining;

    private const float AdaptiveThrottleTriggerFrameMs = 33.0f;
    private const int AdaptiveThrottleDurationFrames = 8;

    public StreamingSettings Settings => settings;
    public Vector3Int LastViewerChunkCoord => lastViewerChunkCoord;
    public bool HasLastViewerChunkCoord => hasLastViewerChunkCoord;
    public Vector3Int SpawnTargetChunkCoord => spawnTargetChunkCoord;
    public bool HasBroadcastInitialTerrainReady => hasBroadcastInitialTerrainReady;
    public bool IsInitialStreamingStageActive => UseInitialStreamingStageNow;
    public int DesiredChunkCount => desiredChunkCoords.Count;
    public int KeepChunkCount => keepChunkCoords.Count;
    public int PendingLoadCount => pendingLoadQueue.Count;
    public int PendingRenderCount => pendingRenderQueue.Count;
    public int PendingUnloadCount => pendingUnload.Count;
    public int PendingLoadSetCount => pendingLoadSet.Count;
    public int PendingRenderSetCount => pendingRenderSet.Count;
    public int KnownEmptyChunkCount => knownEmptyChunks.Count;
    public int ActiveChunkLoadTaskCount => chunkLoadQueue.ActiveTaskCount;
    public int ActiveBlockBuildTaskCount => buildQueue.ActiveTaskCount;
    public int ActiveDensityBuildTaskCount => densityBuildQueue.ActiveTaskCount;
    public long TotalChunkLoadRequestsStarted => totalChunkLoadRequestsStarted;
    public long TotalChunkLoadsCompleted => totalChunkLoadsCompleted;
    public long TotalChunkLoadFailures => totalChunkLoadFailures;
    public long TotalBlockMeshApplies => totalBlockMeshApplies;
    public long TotalDensityMeshApplies => totalDensityMeshApplies;
    public long TotalChunkUnloadsApplied => totalChunkUnloadsApplied;
    public IReadOnlyCollection<Vector3Int> DesiredChunkCoords => desiredChunkCoords;
    public IReadOnlyCollection<Vector3Int> KeepChunkCoords => keepChunkCoords;
    public IReadOnlyCollection<Vector3Int> PendingLoadCoords => pendingLoadSet;
    public IReadOnlyCollection<Vector3Int> PendingRenderCoords => pendingRenderSet;
    public IReadOnlyCollection<Vector3Int> KnownEmptyChunks => knownEmptyChunks;

    /// <summary>
    /// Base-chunk radius the full-detail streamer fills around the viewer (the
    /// LOD0 region). The LOD streamer reads this so both systems share a single
    /// hand-off boundary instead of each deriving it independently. Uses the
    /// steady-state view distance so the LOD boundary does not jump while the
    /// transient initial-streaming stage is active.
    /// </summary>
    public int FullDetailChunkRadius =>
        Mathf.Max(1, world != null && world.Settings != null ? world.Settings.ViewDistanceInChunks : 1);

    /// <summary>
    /// Outer base-chunk radius full-detail chunk meshes can still occupy before
    /// they are unloaded (full-detail radius plus the unload-hysteresis padding).
    /// </summary>
    public int FullDetailKeepRadius => FullDetailChunkRadius + UnloadPaddingInChunks;

    private bool UseInitialStreamingStageNow => useInitialStreamingStage && !hasBroadcastInitialTerrainReady;

    private int ActiveDesiredRadiusInChunks => UseInitialStreamingStageNow ? Mathf.Max(0, initialStreamingRadiusInChunks) : Mathf.Max(1, world.Settings.ViewDistanceInChunks);
    private int ActiveKeepRadiusInChunks => ActiveDesiredRadiusInChunks + (UseInitialStreamingStageNow ? Mathf.Max(0, initialKeepPaddingInChunks) : UnloadPaddingInChunks);

    private int UnloadPaddingInChunks => Mathf.Max(2, settings != null ? settings.UnloadPaddingInChunks : 4);
    private int ChunksLoadedPerFrame => Mathf.Clamp(
      settings != null
        ? (UseInitialStreamingStageNow ? settings.InitialChunksGeneratedPerFrame : settings.ChunksGeneratedPerFrame)
        : 8,
      1,
      64
    );
    private int ChunksRenderedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame : 32, 1, 128);
    private int ChunksUnloadedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame / 4 : 4, 1, 8);
    private int MeshAppliesPerFrame => Mathf.Clamp(settings != null ? settings.MeshAppliesPerFrame : 32, 1, 128);
    private int MaxAsyncChunkTasks => Mathf.Clamp(settings != null ? settings.MaxAsyncChunkTasks : 8, 1, 32);
    private bool IsSmoothDensityMode => world != null && world.Settings != null && world.Settings.TerrainSystem == TerrainSystem.SmoothDensity;
    private int MaxTotalAsyncTasks => Mathf.Clamp(MaxAsyncChunkTasks, 1, cachedMaxHardwareConcurrency);
    // Block chunks now mesh immediately (unloaded in-bounds neighbours are drawn
    // as solid and the chunk re-meshes once they load), so loading no longer
    // gates meshing. Loading feeds meshing and meshing additionally re-meshes per
    // neighbour load, so both stages carry comparable work; split the worker pool
    // evenly between them. Both are cheap now (Burst column gen + merged-quad
    // meshing), so the even split keeps data flowing in while meshes keep up.
    private int MaxLoadAsyncTasks => Mathf.Max(1, MaxTotalAsyncTasks / 2);
    private int MaxBlockAsyncTasks => IsSmoothDensityMode ? 0 : Mathf.Max(1, MaxTotalAsyncTasks - MaxLoadAsyncTasks);
    private int MaxDensityAsyncTasks => IsSmoothDensityMode ? Mathf.Max(1, MaxTotalAsyncTasks - MaxLoadAsyncTasks) : 0;
    private float MeshApplyTimeBudgetSeconds => Mathf.Max(0.001f, (settings != null ? settings.MeshApplyTimeBudgetMs : 2) / 1000.0f);

    private static readonly Vector3Int[] DensityMeshSampleChunkOffsets =
    {
      new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
      new(1, 1, 0), new(1, 0, 1), new(0, 1, 1), new(1, 1, 1)
    };

    private static readonly Vector3Int[] DensityEditAffectedChunkOffsets =
    {
      new(0, 0, 0), new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1),
      new(-1, -1, 0), new(-1, 0, -1), new(0, -1, -1), new(-1, -1, -1)
    };

    private static readonly Vector3Int[] BlockMeshNeighborOffsets =
    {
      Vector3Int.left,
      Vector3Int.right,
      Vector3Int.up,
      Vector3Int.down,
      new Vector3Int(0, 0, -1),
      new Vector3Int(0, 0, 1)
    };

    public void SetViewer(Transform newViewer)
    {
      viewer = newViewer;
      worldRenderer?.SetCollisionViewer(newViewer);
      ForceRefreshStreamingSet();
    }

    private void Awake()
    {
      cachedMaxHardwareConcurrency = Mathf.Max(1, SystemInfo.processorCount - 1);
      chunkPriorityComparison = CompareChunkPriorityByPivot;
      EnsureRuntimeReferences();
      EnsureLodStreamer();
    }

    // The Distant-Horizon LOD lives in a sibling LodStreamer component. Ensure it
    // exists when LOD terrain is enabled so far terrain fills in without manual
    // scene wiring. If one was added/tuned by hand it is left untouched. The
    // LodStreamer is fully gated (EnableLodTerrain + valid mode + world ready),
    // so a stray instance does nothing when LOD is off.
    private void EnsureLodStreamer()
    {
      if (world == null || world.Settings == null || !world.Settings.EnableLodTerrain)
      {
        return;
      }

      if (GetComponent<LodStreamer>() == null)
      {
        gameObject.AddComponent<LodStreamer>();
      }
    }

    private bool EnsureRuntimeReferences()
    {
      world ??= GetComponent<CubusWorld>();
      storage ??= GetComponent<CubusWorldStorage>();
      worldRenderer ??= GetComponent<WorldRenderer>();
      editTool ??= GetComponent<BlockEditTool>();
      return world != null && worldRenderer != null && world.Settings != null;
    }

    private void OnEnable()
    {
      EnsureRuntimeReferences();
      if (editTool != null) editTool.BlockChunksEdited += HandleBlockChunksEdited;
    }

    private void OnDisable()
    {
      if (editTool != null) editTool.BlockChunksEdited -= HandleBlockChunksEdited;
    }

    private void Start()
    {
      if (!EnsureRuntimeReferences()) return;
      if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
      if (!world.Settings.TryValidateConfiguration(out string configError))
      {
        Debug.LogError($"WorldStreamer disabled due to invalid world settings: {configError}");
        enabled = false;
        return;
      }

      ApplyRuntimePerformanceProfile();

      StartBootstrap(false);
    }

    [ContextMenu("Regenerate Streamed World")]
    public void RegenerateStreamedWorld()
    {
      if (!EnsureRuntimeReferences()) return;
      world.SyncBiomeMaterialLayersFromRules();
      StartBootstrap(true);
    }

    private void StartBootstrap(bool logRegenerateMessage)
    {
      if (bootstrapCoroutine != null) StopCoroutine(bootstrapCoroutine);
      bootstrapCoroutine = StartCoroutine(BootstrapRoutine(logRegenerateMessage));
    }

    private IEnumerator BootstrapRoutine(bool logRegenerateMessage)
    {
      if (!EnsureRuntimeReferences()) yield break;

      if (!world.IsWorldReady)
      {
        if (generateWorldDatabaseBeforeStreaming)
        {
          if (storage == null || !storage.LoadWorldManifestOnly())
          {
            yield return world.GenerateWorldAsync();
            storage?.LoadWorldManifestOnly();
          }
        }
        else
        {
          storage?.LoadWorldManifestOnly();
          if (!world.IsWorldReady) world.MarkDatabaseLoaded();
        }
      }

      generator = new WorldGenerator(world.Settings);
      StreamingGenerationContext.Set(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      spawnTargetChunkCoord = WorldToSurfaceChunkCoord(GetSpawnReferencePosition());
      worldRenderer.ClearAll();
      ClearStreamingState();
      ForceRefreshStreamingSet();
      TryBroadcastInitialTerrainReady();
      if (logRegenerateMessage) Debug.Log($"WorldStreamer refreshed. Mode={world.Settings.TerrainSystem}");
      bootstrapCoroutine = null;
    }

    private void ApplyRuntimePerformanceProfile()
    {
      if (settings == null || world == null || world.Settings == null) return;
      settings.NormalizeRuntimeBudgets();
    }

    private void Update()
    {
      if (!EnsureRuntimeReferences()) return;
      if (bootstrapCoroutine != null) return;
      if (world.Settings.TerrainSystem != TerrainSystem.Block && world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;

      float frameMs = Time.unscaledDeltaTime * 1000.0f;
      if (frameMs >= AdaptiveThrottleTriggerFrameMs)
      {
        adaptiveThrottleFramesRemaining = AdaptiveThrottleDurationFrames;
      }
      else if (adaptiveThrottleFramesRemaining > 0)
      {
        adaptiveThrottleFramesRemaining--;
      }

      int loadBudget = ChunksLoadedPerFrame;
      int renderBudget = ChunksRenderedPerFrame;
      int unloadBudget = ChunksUnloadedPerFrame;
      int meshApplyBudget = MeshAppliesPerFrame;

      if (adaptiveThrottleFramesRemaining > 0)
      {
        // Favor smooth frame time after a hitch by throttling how much NEW work we
        // start, but keep applying already-built meshes at full budget so chunks
        // the player is standing in front of still become visible promptly.
        loadBudget = Mathf.Max(1, loadBudget / 4);
        renderBudget = Mathf.Max(1, renderBudget / 2);
        unloadBudget = Mathf.Max(1, unloadBudget / 2);
      }

      UpdateStreamingSetIfNeeded();
      PrioritizePendingQueues();
      ProcessCompletedChunkLoads();
      ProcessRenderQueue(renderBudget);
      ProcessLoadQueue(loadBudget, renderBudget);
      ProcessUnloadQueue(unloadBudget);
      ProcessCompletedBuildResults(meshApplyBudget);
      ReconcileRenderCoverageIfSettled();
      TryBroadcastInitialTerrainReady();
    }

    // QueueGeneratedChunksForRender only runs when the viewer chunk changes. If a
    // build result is dropped mid-flight (generation bump or a chunk that briefly
    // left the desired set during bootstrap), the chunk keeps its data but never
    // gets a view, and a stationary viewer would never re-queue it. Once the
    // streamer has fully drained its queues, run a single reconciliation pass to
    // re-queue any desired chunk that still lacks a view. This converges: the pass
    // only enqueues genuinely stuck chunks, and clears the flag once nothing is
    // left to do.
    private void ReconcileRenderCoverageIfSettled()
    {
      bool busy =
          pendingLoadQueue.Count > 0 ||
          pendingRenderQueue.Count > 0 ||
          chunkLoadQueue.ActiveTaskCount > 0 ||
          buildQueue.ActiveTaskCount > 0 ||
          densityBuildQueue.ActiveTaskCount > 0;

      if (busy)
      {
        renderReconciliationPending = true;
        return;
      }

      if (!renderReconciliationPending || !hasLastViewerChunkCoord)
      {
        return;
      }

      renderReconciliationPending = false;
      QueueGeneratedChunksForRender(lastViewerChunkCoord);
    }

    [ContextMenu("Force Refresh Streaming Set")]
    public void ForceRefreshStreamingSet()
    {
      if (!EnsureRuntimeReferences()) return;
      StreamingGenerationContext.Set(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      generator ??= new WorldGenerator(world.Settings);
      surfaceChunkYCache.Clear();
      buildQueue.IncrementGeneration();
      densityBuildQueue.IncrementGeneration();
      chunkLoadQueue.IncrementGeneration();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      pendingUnload.Clear();
      hasLastViewerChunkCoord = false;
      pendingLoadQueueNeedsPrioritization = false;
      pendingRenderQueueNeedsPrioritization = false;
      UpdateStreamingSetIfNeeded(true);
    }

    public void ClearStreamingState()
    {
      desiredChunkCoords.Clear(); keepChunkCoords.Clear();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      pendingUnload.Clear();
      knownEmptyChunks.Clear();
      buildQueue.IncrementGeneration(); densityBuildQueue.IncrementGeneration(); chunkLoadQueue.IncrementGeneration();
      hasLastViewerChunkCoord = false;
      hasBroadcastInitialTerrainReady = false;
      pendingLoadQueueNeedsPrioritization = false;
      pendingRenderQueueNeedsPrioritization = false;
    }

    private void UpdateStreamingSetIfNeeded(bool force = false)
    {
      Vector3Int viewerChunkCoord = WorldToChunkCoord(GetSpawnReferencePosition());
      if (!force && hasLastViewerChunkCoord && viewerChunkCoord == lastViewerChunkCoord) return;

      // Remember which way the viewer is travelling (in chunk space) so the build
      // queues can be biased toward the frontier ahead instead of always nearest-
      // first. Without this the chunks you are walking INTO are the lowest priority
      // and only get built once you are on top of them, leaving gaps at the edge.
      if (hasLastViewerChunkCoord)
      {
        Vector3Int delta = viewerChunkCoord - lastViewerChunkCoord;
        if (delta.x != 0 || delta.z != 0)
        {
          viewerHeading = delta;
        }
      }

      lastViewerChunkCoord = viewerChunkCoord;
      hasLastViewerChunkCoord = true;
      BuildChunkSet(viewerChunkCoord, ActiveDesiredRadiusInChunks, desiredChunkCoords);
      BuildChunkSet(viewerChunkCoord, ActiveKeepRadiusInChunks, keepChunkCoords);
      PruneKnownEmptyChunksOutsideCurrentInterest(); QueueGeneratedChunksForRender(viewerChunkCoord);
      QueueSpawnTargetForRender();
      UnloadOutsideKeepSet();
      pendingLoadQueueNeedsPrioritization = true;
      pendingRenderQueueNeedsPrioritization = true;
    }

    private void PruneKnownEmptyChunksOutsideCurrentInterest()
    {
      if (knownEmptyChunks.Count == 0)
      {
        return;
      }

      candidateChunksBuffer.Clear();

      foreach (Vector3Int chunkCoord in knownEmptyChunks)
      {
        if (!desiredChunkCoords.Contains(chunkCoord) && !keepChunkCoords.Contains(chunkCoord))
        {
          candidateChunksBuffer.Add(chunkCoord);
        }
      }

      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        knownEmptyChunks.Remove(candidateChunksBuffer[i]);
      }

      candidateChunksBuffer.Clear();
    }

    // Number of chunks of vertical headroom kept above each column's surface
    // chunk so the chunk straddling the surface (and any thin overhang) is always
    // streamed.
    // Number of chunks of vertical headroom kept above/below each column's
    // surface chunk so the chunk straddling the surface (and any thin overhang)
    // is always streamed.
    private const int SurfaceVerticalChunkMargin = 1;

    // Builds a desired/keep set whose horizontal extent is the view radius. The
    // vertical extent is surface-AWARE AND honours the configured world band: for
    // every (x, z) column we cover the UNION of the world band
    // (BlockMin/MaxChunkY or DensityMin/MaxChunkY) and this column's surface chunk
    // (+/- a small margin).
    //
    // The world-band union is the SAFETY NET: it guarantees every column always
    // covers the full playable terrain band regardless of how the per-column
    // surface sample lands, so steep/undulating terrain never drops the chunk that
    // actually contains the surface (which shows up as missing chunks/holes). The
    // surface term extends that coverage upward/downward when terrain rises above
    // or sinks below the band. Empty chunks above the terrain are cheap - flagged
    // known-empty as soon as they mesh to nothing.
    private void BuildChunkSet(Vector3Int viewerChunkCoord, int horizontalRadius, HashSet<Vector3Int> targetSet)
    {
      targetSet.Clear();
      world.Settings.GetActiveVerticalChunkBounds(out int minChunkY, out int maxChunkY);

      for (int z = -horizontalRadius; z <= horizontalRadius; z++)
        for (int x = -horizontalRadius; x <= horizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;

          int surfaceChunkY = GetSurfaceChunkYForColumn(
            chunkX,
            chunkZ,
            maxChunkY * VoxelConstants.ChunkSize
          );

          int columnMinY = surfaceChunkY - SurfaceVerticalChunkMargin;
          int columnMaxY = surfaceChunkY + SurfaceVerticalChunkMargin;

          columnMinY = Mathf.Clamp(columnMinY, minChunkY, maxChunkY);
          columnMaxY = Mathf.Clamp(columnMaxY, minChunkY, maxChunkY);

          for (int y = columnMinY; y <= columnMaxY; y++)
          {
            Vector3Int chunkCoord = new(chunkX, y, chunkZ);
            if (world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
            {
              targetSet.Add(chunkCoord);
            }
          }
        }
    }

    private void QueueGeneratedChunksForRender(Vector3Int viewerChunkCoord)
    {
      candidateChunksBuffer.Clear();
      // Proactively load + mesh ONLY the desired (view-radius) set. The keep set
      // is a DATA / hysteresis cache, not a render region: meshing the whole keep
      // set is catastrophic when the unload padding is large (e.g. view 16 +
      // padding 16 => keep radius 32 => ~4x the desired chunk count), which buries
      // the worker pool under tens of thousands of invisible chunks and stalls the
      // frontier ("walk to the edge and it never loads"). Already-built meshes are
      // left untouched out to the keep radius (UnloadOutsideKeepSet only removes
      // beyond it), so terrain still lingers smoothly as you move - we just stop
      // GENERATING terrain you cannot see past the render radius.
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (worldRenderer.HasChunkView(chunkCoord) || pendingLoadSet.Contains(chunkCoord) || pendingRenderSet.Contains(chunkCoord) || knownEmptyChunks.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord)) continue;
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) { knownEmptyChunks.Add(chunkCoord); continue; }
        candidateChunksBuffer.Add(chunkCoord);
      }

      candidateChunksBuffer.Sort(GetChunkPriorityComparison(ComputeSortPivot()));
      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        if (HasChunkData(candidateChunksBuffer[i])) QueueRender(candidateChunksBuffer[i]);
        else QueueLoad(candidateChunksBuffer[i]);
      }
    }

    // Returns the cached comparison delegate configured for the given pivot.
    // Avoids allocating a new closure/Comparison on every Sort call.
    private Comparison<Vector3Int> GetChunkPriorityComparison(Vector3Int pivotChunkCoord)
    {
      sortPivotChunkCoord = pivotChunkCoord;
      chunkPriorityComparison ??= CompareChunkPriorityByPivot;
      return chunkPriorityComparison;
    }

    // The point the build queues sort around. Normally the viewer, but shifted
    // forward along the recent heading by half the desired radius so chunks in the
    // direction of travel are generated BEFORE the viewer arrives (predictive
    // prefetch). Falls back to the viewer when stationary.
    private Vector3Int ComputeSortPivot()
    {
      if (!hasLastViewerChunkCoord) return lastViewerChunkCoord;

      int lead = Mathf.Max(2, ActiveDesiredRadiusInChunks / 2);
      Vector3 heading = new(viewerHeading.x, 0.0f, viewerHeading.z);
      if (heading.sqrMagnitude < 0.0001f) return lastViewerChunkCoord;

      heading.Normalize();
      return new Vector3Int(
        lastViewerChunkCoord.x + Mathf.RoundToInt(heading.x * lead),
        lastViewerChunkCoord.y,
        lastViewerChunkCoord.z + Mathf.RoundToInt(heading.z * lead));
    }

    private int CompareChunkPriorityByPivot(Vector3Int a, Vector3Int b) => CompareChunkPriority(a, b, sortPivotChunkCoord);

    private int CompareChunkPriority(Vector3Int a, Vector3Int b, Vector3Int viewerChunkCoord)
    {
      if (a == spawnTargetChunkCoord) return -1;
      if (b == spawnTargetChunkCoord) return 1;

      int ad = ChunkDistanceSquared(a, viewerChunkCoord);
      int bd = ChunkDistanceSquared(b, viewerChunkCoord);
      if (ad != bd) return ad.CompareTo(bd);

      int av = Mathf.Abs(a.y - viewerChunkCoord.y);
      int bv = Mathf.Abs(b.y - viewerChunkCoord.y);
      if (av != bv) return av.CompareTo(bv);

      int ah = HorizontalChunkDistanceSquared(a, viewerChunkCoord);
      int bh = HorizontalChunkDistanceSquared(b, viewerChunkCoord);
      return ah.CompareTo(bh);
    }

    private static int ChunkDistanceSquared(Vector3Int a, Vector3Int b)
    {
      int dx = a.x - b.x;
      int dy = a.y - b.y;
      int dz = a.z - b.z;
      return dx * dx + dy * dy + dz * dz;
    }

    private static int HorizontalChunkDistanceSquared(Vector3Int a, Vector3Int b)
    {
      int dx = a.x - b.x;
      int dz = a.z - b.z;
      return dx * dx + dz * dz;
    }

    private void QueueSpawnTargetForRender()
    {
      desiredChunkCoords.Add(spawnTargetChunkCoord);
      keepChunkCoords.Add(spawnTargetChunkCoord);
      if (worldRenderer.HasChunkView(spawnTargetChunkCoord) || pendingRenderSet.Contains(spawnTargetChunkCoord) || chunkLoadQueue.IsInFlight(spawnTargetChunkCoord)) return;
      if (HasChunkData(spawnTargetChunkCoord)) QueueRender(spawnTargetChunkCoord); else QueueLoad(spawnTargetChunkCoord);
    }

    private void ProcessLoadQueue(int loadBudget, int renderBudget)
    {
      int maxLoadsThisFrame = loadBudget;
      if (pendingRenderQueue.Count > renderBudget * 2)
      {
        // When render backlog is high, throttle loading so mesh generation can catch up.
        maxLoadsThisFrame = Mathf.Max(1, loadBudget / 2);
      }

      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(maxLoadsThisFrame * 8, pendingLoadQueue.Count);
      int maxLoadTasks = MaxLoadAsyncTasks;
      int maxTotalTasks = MaxTotalAsyncTasks;

      while (pendingLoadQueue.Count > 0 && count < maxLoadsThisFrame && scanned < maxScans)
      {
        scanned++;
        Vector3Int c = pendingLoadQueue.Dequeue();
        pendingLoadSet.Remove(c);

        bool isDesiredChunk = desiredChunkCoords.Contains(c);
        bool isKeepChunk = keepChunkCoords.Contains(c);

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (!isDesiredChunk)
          {
            continue;
          }
        }
        else if (!isDesiredChunk && !isKeepChunk)
        {
          continue;
        }

        if (worldRenderer.HasChunkView(c) || knownEmptyChunks.Contains(c))
        {
          continue;
        }

        if (HasChunkData(c))
        {
          if (desiredChunkCoords.Contains(c))
          {
            QueueRender(c);
          }

          continue;
        }

        if (!world.Settings.IsInsideEffectiveWorldBounds3D(c))
        {
          knownEmptyChunks.Add(c);
          continue;
        }

        if (chunkLoadQueue.IsInFlight(c)) continue;

        int totalActiveAsyncTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
        if (chunkLoadQueue.ActiveTaskCount >= maxLoadTasks || totalActiveAsyncTasks >= maxTotalTasks)
        {
          QueueLoad(c, false);
          break;
        }

        if (chunkLoadQueue.TryStartLoad(
              storage != null ? storage.ActiveStore : null,
              storage != null ? storage.WorldId : null,
              c,
              MaxLoadAsyncTasks,
              world.Settings.TerrainSystem == TerrainSystem.Block ? world.CreateBlockOverrideSnapshot(c) : null,
              world.Settings.TerrainSystem == TerrainSystem.SmoothDensity ? world.CreateDensityOverrideSnapshot(c) : null))
        {
          totalChunkLoadRequestsStarted++;
          count++;
          continue;
        }

        QueueLoad(c, false);
        break;
      }
    }

    private void ProcessCompletedChunkLoads()
    {
      int count = 0;
      int completedBudget = Mathf.Max(ChunksLoadedPerFrame, MaxLoadAsyncTasks * 2);
      while (count < completedBudget && chunkLoadQueue.TryDequeueCompleted(out ChunkLoadResult result))
      {
        count++;
        if (result == null || result.GenerationId != chunkLoadQueue.GenerationId) continue;
        Vector3Int c = result.ChunkCoord;

        if (result.Loaded)
        {
          totalChunkLoadsCompleted++;
          if (result.TerrainSystem == TerrainSystem.Block && result.BlockChunkData != null)
          {
            world.Data.BlockChunks[c] = result.BlockChunkData;
            // Avoid scanning the full voxel array on the main thread; emptiness is resolved by mesh build results.
            knownEmptyChunks.Remove(c);

            // A newly-available chunk changes its neighbours' boundary faces.
            // Re-mesh neighbours that have already been meshed so they use this
            // chunk's real voxels instead of the terrain-sampling fallback,
            // which otherwise leaves stale boundary walls at the load frontier.
            RequeueSettledBlockNeighbors(c);
          }
          else if (result.TerrainSystem == TerrainSystem.SmoothDensity && result.DensityChunkData != null) world.Data.DensityChunks[c] = result.DensityChunkData;

          // Chunks that were just generated on the streaming worker (no record on
          // disk yet) are written back so subsequent visits load them from storage
          // instead of regenerating. This is the wired-up counterpart to
          // ChunkLoadResult.IsMissingFromStorage. Skipped when the store is hidden
          // for a terrain-system mismatch (ActiveStore == null), so we never
          // overwrite a world saved in the other terrain mode.
          if (result.IsMissingFromStorage && persistStreamedChunks && storage != null && storage.ActiveStore != null && HasChunkData(c))
          {
            storage.SaveChunk(c);
          }

          if (desiredChunkCoords.Contains(c) && HasChunkData(c))
          {
            QueueRender(c);
          }

          continue;
        }

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;
        totalChunkLoadFailures++;
        QueueLoad(c);
      }
    }

    private void ProcessRenderQueue(int renderBudget)
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(renderBudget * 4, pendingRenderQueue.Count);

      while (pendingRenderQueue.Count > 0 && count < renderBudget && scanned < maxScans)
      {
        scanned++;
        Vector3Int c = pendingRenderQueue.Dequeue();
        pendingRenderSet.Remove(c);

        if (!desiredChunkCoords.Contains(c))
        {
          continue;
        }

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (world.Data.BlockChunks.TryGetValue(c, out BlockChunkData b) && b != null)
          {
            if (buildQueue.IsInFlight(c))
            {
              QueueRender(c, false);
              continue;
            }

            int totalActiveTasksForBlockBuild = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
            if (buildQueue.ActiveTaskCount >= MaxBlockAsyncTasks || totalActiveTasksForBlockBuild >= MaxTotalAsyncTasks)
            {
              QueueRender(c, false);
              break;
            }

            if (TryStartBlockMeshBuild(c, b))
            {
              count++;
              continue;
            }

            QueueRender(c, false);
            continue;
          }

          if (!knownEmptyChunks.Contains(c)) QueueLoad(c, false);
          continue;
        }

        if (!world.Data.DensityChunks.TryGetValue(c, out DensityChunkData d) || d == null)
        {
          QueueLoad(c);
          continue;
        }

        if (densityBuildQueue.IsInFlight(c))
        {
          QueueRender(c, false);
          continue;
        }

        if (!EnsureDensitySampleChunksAvailableForMesh(c))
        {
          QueueRender(c, false);
          continue;
        }

        int totalActiveAsyncTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
        if (densityBuildQueue.ActiveTaskCount >= MaxDensityAsyncTasks || totalActiveAsyncTasks >= MaxTotalAsyncTasks)
        {
          QueueRender(c, false);
          break;
        }

        if (TryStartDensityMeshBuild(c, d))
        {
          count++;
          continue;
        }

        QueueRender(c, false);
        continue;
      }
    }

    private void ProcessCompletedBuildResults(int meshApplyBudget)
    {
      float applyStartTime = Time.realtimeSinceStartup;

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        int blockCount = 0;
        while (blockCount < meshApplyBudget && (blockCount == 0 || Time.realtimeSinceStartup - applyStartTime < MeshApplyTimeBudgetSeconds) && buildQueue.TryDequeueCompleted(out BlockChunkBuildResult r))
        {
          if (r == null) { blockCount++; continue; }

          if (r.GenerationId != buildQueue.GenerationId || (!desiredChunkCoords.Contains(r.ChunkCoord) && !keepChunkCoords.Contains(r.ChunkCoord)))
          {
            ReturnMeshData(r?.MeshData);
            blockCount++;
            continue;
          }

          if (r.ChunkData != null)
          {
            if (
              world.Data.BlockChunks.TryGetValue(r.ChunkCoord, out BlockChunkData previousChunk) &&
              previousChunk != null &&
              !ReferenceEquals(previousChunk, r.ChunkData)
            )
            {
              VoxelArrayPool.Return(previousChunk.GetRawVoxelArray());
              world.Data.BlockChunks[r.ChunkCoord] = r.ChunkData;
            }
            else if (!world.Data.BlockChunks.ContainsKey(r.ChunkCoord))
            {
              world.Data.BlockChunks[r.ChunkCoord] = r.ChunkData;
            }
          }

          if (r.Failed)
          {
            // The background build threw (typically transient). The chunk is NOT
            // genuinely empty, so it must not be marked known-empty: that would
            // leave a permanent hole that only a voxel edit could clear. The
            // chunk data is still present, so requeue it to retry the mesh build.
            if (world.Data.BlockChunks.ContainsKey(r.ChunkCoord))
            {
              QueueRender(r.ChunkCoord, false);
            }
            ReturnMeshData(r.MeshData);
            blockCount++;
            continue;
          }

          if (r.IsEmpty || r.MeshData == null || r.MeshData.IsEmpty)
          {
            // Only TRUST an empty mesh - and blacklist the chunk as known-empty -
            // when it is genuinely empty: either it has no solid voxels at all, or
            // it has solids but every in-bounds face neighbour was loaded at build
            // time (a real buried/air chunk). A chunk that HAS solids and meshed
            // empty while a face neighbour was still unloaded had that neighbour
            // treated as SOLID, hiding its only visible face - a FALSE empty. Don't
            // blacklist that, or it becomes a permanent hole; the neighbour is
            // queued to load and a re-mesh (or settled reconciliation) fills it in.
            bool hasSolids = world.Data.BlockChunks.TryGetValue(r.ChunkCoord, out BlockChunkData rb) && rb != null && rb.HasAnySolidVoxel();
            if (!hasSolids || HasAllInBoundsBlockNeighborsLoaded(r.ChunkCoord))
            {
              knownEmptyChunks.Add(r.ChunkCoord);
            }
            worldRenderer.RemoveChunk(r.ChunkCoord);
            ReturnMeshData(r.MeshData);
            blockCount++;
            continue;
          }

          knownEmptyChunks.Remove(r.ChunkCoord);
          worldRenderer.RenderBlockChunkMesh(r.ChunkCoord, r.MeshData);
          totalBlockMeshApplies++;
          r.MeshData = null;
          blockCount++;
        }

        return;
      }

      if (world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;
      int count = 0;
      while (count < meshApplyBudget && (count == 0 || Time.realtimeSinceStartup - applyStartTime < MeshApplyTimeBudgetSeconds) && densityBuildQueue.TryDequeueCompleted(out DensityChunkBuildResult r))
      {
        if (r == null) { count++; continue; }
        if (r.GenerationId != densityBuildQueue.GenerationId || (!desiredChunkCoords.Contains(r.ChunkCoord) && !keepChunkCoords.Contains(r.ChunkCoord)))
        {
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (r.ChunkData != null) world.Data.DensityChunks[r.ChunkCoord] = r.ChunkData;

        if (r.Failed)
        {
          // Transient background-build exception. Requeue to retry instead of
          // dropping the chunk and waiting on a later reconciliation pass.
          if (world.Data.DensityChunks.ContainsKey(r.ChunkCoord))
          {
            QueueRender(r.ChunkCoord, false);
          }
          ReturnMeshData(r);
          count++;
          continue;
        }

        if (r.MeshData == null || r.MeshData.IsEmpty)
        {
          worldRenderer.RemoveChunk(r.ChunkCoord);
          ReturnMeshData(r);
          count++;
          continue;
        }

        knownEmptyChunks.Remove(r.ChunkCoord);
        Mesh mesh = r.MeshData.ToUnityMeshFast();
        MeshDataPool.Return(r.MeshData); r.MeshData = null;
        worldRenderer.RenderDensityChunkMesh(
            r.ChunkCoord,
            mesh,
            r.ChunkCoord == spawnTargetChunkCoord && !hasBroadcastInitialTerrainReady
        );
        totalDensityMeshApplies++;
        TryBroadcastInitialTerrainReady();
        count++;
      }
    }

    private static void ReturnMeshData(MeshData meshData)
    {
      if (meshData != null) MeshDataPool.Return(meshData);
    }

    private bool HasChunkData(Vector3Int c) => world.Settings.TerrainSystem switch
    {
      TerrainSystem.Block => world.Data.BlockChunks.ContainsKey(c),
      TerrainSystem.SmoothDensity => world.Data.DensityChunks.ContainsKey(c),
      _ => false
    };

    public void RebuildDensityChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (!EnsureRuntimeReferences() || world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;
      densityBuildQueue.IncrementGeneration();

      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        for (int i = 0; i < DensityEditAffectedChunkOffsets.Length; i++)
        {
          Vector3Int c = dirtyChunk + DensityEditAffectedChunkOffsets[i];
          if (!world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;
          knownEmptyChunks.Remove(c);
          desiredChunkCoords.Add(c);
          keepChunkCoords.Add(c);

          QueueRender(c);
        }
      }
    }

    private void HandleBlockChunksEdited(IReadOnlyCollection<Vector3Int> dirtyChunks)
    {
      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        QueueEditedBlockChunk(dirtyChunk);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.left);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.right);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.down);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.up);
        QueueEditedBlockChunk(dirtyChunk + new Vector3Int(0, 0, -1));
        QueueEditedBlockChunk(dirtyChunk + new Vector3Int(0, 0, 1));

        // Write the edited chunk straight to the store so the change survives
        // eviction/quit and reloads fast. The edit also lives in the sparse
        // override layer (re-applied on every streamed load). SaveEditedChunk
        // writes an explicit empty record when the edit erased the whole chunk,
        // so a fully-cleared chunk doesn't reload its stale pre-edit terrain.
        if (persistStreamedChunks && storage != null && storage.ActiveStore != null)
        {
          storage.SaveEditedChunk(dirtyChunk);
        }
      }
    }

    private void QueueEditedBlockChunk(Vector3Int chunkCoord)
    {
      if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) return;
      knownEmptyChunks.Remove(chunkCoord);
      desiredChunkCoords.Add(chunkCoord);
      keepChunkCoords.Add(chunkCoord);
      QueueRender(chunkCoord);
    }
  }
}
