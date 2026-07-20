using System;
using System.Collections;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
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
    private readonly Queue<Vector3Int> pendingRenderRetryQueue = new();
    private readonly HashSet<Vector3Int> pendingRenderRetrySet = new();

    private readonly Queue<Vector3Int> pendingBlockRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingBlockRenderSet = new();
    private readonly Queue<Vector3Int> pendingBlockRenderRetryQueue = new();
    private readonly HashSet<Vector3Int> pendingBlockRenderRetrySet = new();

    private readonly Queue<Vector3Int> pendingDensityRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingDensityRenderSet = new();
    private readonly Queue<Vector3Int> pendingDensityRenderRetryQueue = new();
    private readonly HashSet<Vector3Int> pendingDensityRenderRetrySet = new();

    // Density chunks re-queued for render while a build was already in flight. That in-flight
    // build snapshotted stale (pre-edit) data, so the chunk is rebuilt once the build completes
    // - otherwise an edit near a chunk boundary leaves a permanent crack (and a matching collider
    // gap you fall through) until another edit happens to catch the neighbour idle.
    private readonly HashSet<Vector3Int> densityRebuildAfterInFlight = new();

    // Density chunks that an EDIT dirtied and must re-mesh even if they are only in the keep ring
    // (not the desired set). Normal streaming meshes desired-only; without this pass an edit that
    // touches a keep-ring chunk (or its sampled neighbours) is queued then silently dropped by the
    // desired-only render filter, leaving a permanent gap. Entries are cleared once the chunk's
    // build applies.
    private readonly HashSet<Vector3Int> densityEditRenderSet = new();

    private readonly WorldStreamingQueueSet pendingUnload = new();
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly Dictionary<Vector2Int, int> surfaceChunkYCache = new();
    private readonly List<Vector3Int> candidateChunksBuffer = new();
    private readonly List<Vector3Int> queueSortBuffer = new();
    private readonly List<Vector3Int> unloadChunksBuffer = new();
    private readonly HashSet<Vector3Int> editedChunkSet = new();
    private readonly List<Vector3Int> editedChunksBuffer = new();
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
    private Vector3Int priorityChunkCoord;
    private bool hasPriorityChunkCoord;
    private bool pendingLoadQueueNeedsPrioritization;
    private bool pendingRenderQueueNeedsPrioritization;
    private bool renderReconciliationPending;
    private int cachedMaxHardwareConcurrency = 1;
    private Vector3Int sortPivotChunkCoord;
    private Comparison<Vector3Int> cachedChunkPriorityComparison;
    private readonly Dictionary<Vector3Int, int> chunkScoreCache = new();
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
    public bool IsInitialStreamingStageActive => UseInitialStreamingStageNow;
    public int DesiredChunkCount => desiredChunkCoords.Count;
    public int KeepChunkCount => keepChunkCoords.Count;
    public int PendingLoadCount => pendingLoadQueue.Count;
    public int PendingRenderCount =>
      pendingRenderQueue.Count +
      pendingRenderRetryQueue.Count +
      pendingBlockRenderQueue.Count +
      pendingBlockRenderRetryQueue.Count +
      pendingDensityRenderQueue.Count +
      pendingDensityRenderRetryQueue.Count;
    public int PendingBlockRenderCount => pendingBlockRenderQueue.Count + pendingBlockRenderRetryQueue.Count;
    public int PendingDensityRenderCount => pendingDensityRenderQueue.Count + pendingDensityRenderRetryQueue.Count;
    public int PendingUnloadCount => pendingUnload.Count;
    public int PendingLoadSetCount => pendingLoadSet.Count;
    public int PendingRenderSetCount =>
      pendingRenderSet.Count +
      pendingRenderRetrySet.Count +
      pendingBlockRenderSet.Count +
      pendingBlockRenderRetrySet.Count +
      pendingDensityRenderSet.Count +
      pendingDensityRenderRetrySet.Count;
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

    private bool hasOverrideStreamingFocusVoxel;
    private Vector3Int overrideStreamingFocusVoxel;

    public int FullDetailChunkRadius =>
        Mathf.Max(1, world != null && world.Settings != null ? world.Settings.ViewDistanceInChunks : 1);

    public int FullDetailKeepRadius => FullDetailChunkRadius + UnloadPaddingInChunks;

    private bool UseInitialStreamingStageNow => useInitialStreamingStage && world != null && !world.IsInitialTerrainReady;
    private int ActiveDesiredRadiusInChunks => UseInitialStreamingStageNow ? Mathf.Max(0, initialStreamingRadiusInChunks) : Mathf.Max(1, world != null && world.Settings != null ? world.Settings.ViewDistanceInChunks : 1);
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
    private int MaxTotalAsyncTasks => Mathf.Clamp(MaxAsyncChunkTasks, 1, cachedMaxHardwareConcurrency);

    // Async worker slots are shared elastically between the load stage and the mesh
    // stages against the single MaxTotalAsyncTasks budget. Each stage may borrow the
    // other's currently-idle slots but always leaves AsyncStageReserve free so the
    // other stage can spin back up. This keeps the whole machine busy whether the
    // pipeline is load-bound (exploring fresh terrain, where density meshing is
    // starved waiting on neighbour-shell loads) or mesh-bound (revisiting cached
    // terrain), instead of stranding half the cores on a fixed 50/50 split. The
    // per-frame dispatch gates still enforce MaxTotalAsyncTasks, so the elastic
    // caps can never oversubscribe the total.
    private int AsyncStageReserve => Mathf.Max(1, MaxTotalAsyncTasks / 4);
    private int MaxLoadAsyncTasks
    {
      get
      {
        int total = MaxTotalAsyncTasks;
        int meshActive = (buildQueue?.ActiveTaskCount ?? 0) + (densityBuildQueue?.ActiveTaskCount ?? 0);
        return Mathf.Clamp(total - Mathf.Max(meshActive, AsyncStageReserve), 1, total);
      }
    }
    private int MaxMeshAsyncTasks
    {
      get
      {
        int total = MaxTotalAsyncTasks;
        int loadActive = chunkLoadQueue?.ActiveTaskCount ?? 0;
        return Mathf.Clamp(total - Mathf.Max(loadActive, AsyncStageReserve), 1, total);
      }
    }
    private int MaxBlockAsyncTasks => IsBlockTerrainEnabled ? Mathf.Max(1, IsDensityTerrainEnabled ? MaxMeshAsyncTasks / 2 : MaxMeshAsyncTasks) : 0;
    private int MaxDensityAsyncTasks => IsDensityTerrainEnabled ? Mathf.Max(1, IsBlockTerrainEnabled ? MaxMeshAsyncTasks - Mathf.Max(1, MaxMeshAsyncTasks / 2) : MaxMeshAsyncTasks) : 0;
    private float MeshApplyTimeBudgetSeconds => Mathf.Max(0.001f, (settings != null ? settings.MeshApplyTimeBudgetMs : 2) / 1000.0f);

    private static readonly Vector3Int[] DensityMeshSampleChunkOffsets =
    {
      new(0, 0, 0),
      new(-1, -1, -1), new(0, -1, -1), new(1, -1, -1),
      new(-1, 0, -1), new(0, 0, -1), new(1, 0, -1),
      new(-1, 1, -1), new(0, 1, -1), new(1, 1, -1),
      new(-1, -1, 0), new(0, -1, 0), new(1, -1, 0),
      new(-1, 0, 0), new(1, 0, 0),
      new(-1, 1, 0), new(0, 1, 0), new(1, 1, 0),
      new(-1, -1, 1), new(0, -1, 1), new(1, -1, 1),
      new(-1, 0, 1), new(0, 0, 1), new(1, 0, 1),
      new(-1, 1, 1), new(0, 1, 1), new(1, 1, 1)
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
      cachedVisibilityCamera = null;
      hasLastVisibilityCameraState = false;
      worldRenderer?.SetCollisionViewer(newViewer);
      ForceRefreshStreamingSet();
    }

    private void Awake()
    {
      cachedMaxHardwareConcurrency = Mathf.Max(1, SystemInfo.processorCount - 1);
      cachedChunkPriorityComparison = CompareChunkPriorityCached;
      EnsureRuntimeReferences();
      world?.Settings?.EnsureRuntimeStreamingBounds();
      EnsureLodStreamer();
    }

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
      world.Settings.EnsureRuntimeStreamingBounds();
      if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
      worldRenderer.SetCollisionViewer(viewer);
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
      world.Settings.EnsureRuntimeStreamingBounds();
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

      world.Settings.EnsureRuntimeStreamingBounds();

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
          world.Settings.EnsureRuntimeStreamingBounds();
          if (!world.IsWorldReady) world.MarkDatabaseLoaded();
        }
      }

      world.Settings.EnsureRuntimeStreamingBounds();
      generator = new WorldGenerator(world.Settings);
      StreamingGenerationContext.Set(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      worldRenderer.ClearAll();
      ClearStreamingState();
      ForceRefreshStreamingSet();
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
      if (!IsAnyTerrainEnabled) return;

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
        loadBudget = Mathf.Max(1, loadBudget / 4);
        renderBudget = Mathf.Max(1, renderBudget / 2);
        unloadBudget = Mathf.Max(1, unloadBudget / 2);
      }

      UpdateStreamingSetIfNeeded();
      RefreshVisibilitySchedulingIfNeeded();
      PrioritizePendingQueues();
      ProcessCompletedChunkLoads();
      ProcessRenderQueue(renderBudget);
      ProcessLoadQueue(loadBudget, renderBudget);
      ProcessUnloadQueue(unloadBudget);
      ProcessCompletedBuildResults(meshApplyBudget);
      ReconcileRenderCoverageIfSettled();
      UpdateChunkRenderVisibility();
    }

    private void ReconcileRenderCoverageIfSettled()
    {
      bool busy =
          pendingLoadQueue.Count > 0 ||
          pendingRenderQueue.Count > 0 ||
          pendingRenderRetryQueue.Count > 0 ||
          pendingBlockRenderQueue.Count > 0 ||
          pendingBlockRenderRetryQueue.Count > 0 ||
          pendingDensityRenderQueue.Count > 0 ||
          pendingDensityRenderRetryQueue.Count > 0 ||
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
      world.Settings.EnsureRuntimeStreamingBounds();
      StreamingGenerationContext.Set(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      generator ??= new WorldGenerator(world.Settings);
      surfaceChunkYCache.Clear();
      buildQueue.IncrementGeneration();
      densityBuildQueue.IncrementGeneration();
      chunkLoadQueue.IncrementGeneration();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      pendingRenderRetryQueue.Clear(); pendingRenderRetrySet.Clear();
      pendingBlockRenderQueue.Clear(); pendingBlockRenderSet.Clear();
      pendingBlockRenderRetryQueue.Clear(); pendingBlockRenderRetrySet.Clear();
      pendingDensityRenderQueue.Clear(); pendingDensityRenderSet.Clear();
      pendingDensityRenderRetryQueue.Clear(); pendingDensityRenderRetrySet.Clear();
      densityRebuildAfterInFlight.Clear();
      densityEditRenderSet.Clear();
      pendingUnload.Clear();
      hasLastViewerChunkCoord = false;
      hasLastVisibilityCameraState = false;
      pendingLoadQueueNeedsPrioritization = false;
      pendingRenderQueueNeedsPrioritization = false;
      UpdateStreamingSetIfNeeded(true);
    }

    public void ClearStreamingState()
    {
      desiredChunkCoords.Clear(); keepChunkCoords.Clear();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      pendingRenderRetryQueue.Clear(); pendingRenderRetrySet.Clear();
      pendingBlockRenderQueue.Clear(); pendingBlockRenderSet.Clear();
      pendingBlockRenderRetryQueue.Clear(); pendingBlockRenderRetrySet.Clear();
      pendingDensityRenderQueue.Clear(); pendingDensityRenderSet.Clear();
      pendingDensityRenderRetryQueue.Clear(); pendingDensityRenderRetrySet.Clear();
      densityRebuildAfterInFlight.Clear();
      densityEditRenderSet.Clear();
      pendingUnload.Clear();
      knownEmptyChunks.Clear();
      buildQueue.IncrementGeneration(); densityBuildQueue.IncrementGeneration(); chunkLoadQueue.IncrementGeneration();
      hasLastViewerChunkCoord = false;
      hasLastVisibilityCameraState = false;
      pendingLoadQueueNeedsPrioritization = false;
      pendingRenderQueueNeedsPrioritization = false;
    }
  }
}
