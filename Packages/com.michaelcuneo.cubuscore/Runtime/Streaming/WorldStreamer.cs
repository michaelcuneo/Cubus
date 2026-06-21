using System.Collections;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
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
  public sealed class WorldStreamer : MonoBehaviour
  {
    [SerializeField] private Transform viewer;
    [SerializeField] private StreamingSettings settings = new();
    [SerializeField][Min(1)] private int initialSpawnRequiredRenderedChunks = 9;
    [SerializeField] private Vector3 desiredInitialSpawnLocation = Vector3.zero;
    [SerializeField] private float initialSpawnClearance = 2.0f;
    [SerializeField] private bool evictCachedChunkDataOutsideKeepSet = true;
    [SerializeField] private bool generateWorldDatabaseBeforeStreaming = false;
    [SerializeField] private bool enforcePrePr30BlockPerformanceProfile = true;

    [Header("Initial Streaming Stage")]
    [SerializeField][Min(0)] private int initialStreamingRadiusInChunks = 1;
    [SerializeField] private bool useInitialStreamingStage = true;
    [SerializeField][Min(0)] private int initialChunksBelowSurface = 1;
    [SerializeField][Min(0)] private int initialChunksAboveSurface = 1;
    [SerializeField][Min(0)] private int initialKeepPaddingInChunks = 1;

    private readonly HashSet<Vector3Int> desiredChunkCoords = new();
    private readonly HashSet<Vector3Int> keepChunkCoords = new();
    private readonly Queue<Vector3Int> pendingLoadQueue = new();
    private readonly HashSet<Vector3Int> pendingLoadSet = new();
    private readonly Queue<Vector3Int> pendingRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingRenderSet = new();
    private readonly Queue<Vector3Int> pendingUnloadQueue = new();
    private readonly HashSet<Vector3Int> pendingUnloadSet = new();
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
    private Vector3Int spawnTargetChunkCoord;
    private bool hasBroadcastInitialTerrainReady;
    private bool pendingQueuesNeedPrioritization;
    private long totalChunkLoadRequestsStarted;
    private long totalChunkLoadsCompleted;
    private long totalChunkLoadFailures;
    private long totalBlockMeshApplies;
    private long totalDensityMeshApplies;
    private long totalChunkUnloadsApplied;
    private int adaptiveThrottleFramesRemaining;

    private const float AdaptiveThrottleTriggerFrameMs = 20.0f;
    private const int AdaptiveThrottleDurationFrames = 20;

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
    public int PendingUnloadCount => pendingUnloadQueue.Count;
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

    private bool UseInitialStreamingStageNow => useInitialStreamingStage && !hasBroadcastInitialTerrainReady;
    private int ActiveDesiredRadiusInChunks => UseInitialStreamingStageNow ? Mathf.Max(0, initialStreamingRadiusInChunks) : Mathf.Max(1, world.Settings.ViewDistanceInChunks);
    private int ActiveKeepRadiusInChunks => ActiveDesiredRadiusInChunks + (UseInitialStreamingStageNow ? Mathf.Max(0, initialKeepPaddingInChunks) : UnloadPaddingInChunks);
    private int ActiveChunksBelowSurface => UseInitialStreamingStageNow ? Mathf.Max(0, initialChunksBelowSurface) : ChunksBelowSurface;
    private int ActiveChunksAboveSurface => UseInitialStreamingStageNow ? Mathf.Max(0, initialChunksAboveSurface) : ChunksAboveSurface;

    private int UnloadPaddingInChunks => Mathf.Max(2, settings != null ? settings.UnloadPaddingInChunks : 4);
    private int ChunksBelowSurface => Mathf.Max(0, settings != null ? settings.ChunksBelowSurface : 8);
    private int ChunksAboveSurface => Mathf.Max(0, settings != null ? settings.ChunksAboveSurface : 8);
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
    private int MaxTotalAsyncTasks => Mathf.Clamp(MaxAsyncChunkTasks, 1, Mathf.Max(1, SystemInfo.processorCount - 1));
    private int MaxLoadAsyncTasks => IsSmoothDensityMode ? Mathf.Max(1, MaxTotalAsyncTasks / 2) : Mathf.Max(1, MaxTotalAsyncTasks - 2);
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
      EnsureRuntimeReferences();
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
        // Favor smooth frame time after a hitch by temporarily reducing streaming pressure.
        loadBudget = Mathf.Max(1, loadBudget / 4);
        renderBudget = Mathf.Max(1, renderBudget / 2);
        unloadBudget = Mathf.Max(1, unloadBudget / 2);
        meshApplyBudget = Mathf.Max(1, meshApplyBudget / 2);
      }

      UpdateStreamingSetIfNeeded();
      PrioritizePendingQueues();
      ProcessCompletedChunkLoads();
      ProcessRenderQueue(renderBudget);
      ProcessLoadQueue(loadBudget, renderBudget);
      ProcessUnloadQueue(unloadBudget);
      ProcessCompletedBuildResults(meshApplyBudget);
      TryBroadcastInitialTerrainReady();
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
      pendingUnloadQueue.Clear(); pendingUnloadSet.Clear();
      hasLastViewerChunkCoord = false;
      pendingQueuesNeedPrioritization = false;
      UpdateStreamingSetIfNeeded(true);
    }

    public void ClearStreamingState()
    {
      desiredChunkCoords.Clear(); keepChunkCoords.Clear();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      pendingUnloadQueue.Clear(); pendingUnloadSet.Clear();
      knownEmptyChunks.Clear();
      buildQueue.IncrementGeneration(); densityBuildQueue.IncrementGeneration(); chunkLoadQueue.IncrementGeneration();
      hasLastViewerChunkCoord = false;
      hasBroadcastInitialTerrainReady = false;
      pendingQueuesNeedPrioritization = false;
    }

    private void UpdateStreamingSetIfNeeded(bool force = false)
    {
      Vector3Int viewerChunkCoord = WorldToChunkCoord(GetSpawnReferencePosition());
      if (!force && hasLastViewerChunkCoord && viewerChunkCoord == lastViewerChunkCoord) return;

      lastViewerChunkCoord = viewerChunkCoord;
      hasLastViewerChunkCoord = true;
      BuildChunkSet(viewerChunkCoord, ActiveDesiredRadiusInChunks, ActiveChunksBelowSurface, ActiveChunksAboveSurface, desiredChunkCoords);
      BuildChunkSet(viewerChunkCoord, ActiveKeepRadiusInChunks, ActiveChunksBelowSurface, ActiveChunksAboveSurface, keepChunkCoords);
      QueueGeneratedChunksForRender(viewerChunkCoord);
      QueueSpawnTargetForRender();
      UnloadOutsideKeepSet();
      pendingQueuesNeedPrioritization = true;
    }

    private void BuildChunkSet(Vector3Int viewerChunkCoord, int horizontalRadius, int chunksBelowSurface, int chunksAboveSurface, HashSet<Vector3Int> targetSet)
    {
      targetSet.Clear();
      for (int z = -horizontalRadius; z <= horizontalRadius; z++)
        for (int x = -horizontalRadius; x <= horizontalRadius; x++)
        {
          int chunkX = viewerChunkCoord.x + x;
          int chunkZ = viewerChunkCoord.z + z;
          int surfaceChunkY = GetSurfaceChunkYForColumn(chunkX, chunkZ, viewerChunkCoord.y * VoxelConstants.ChunkSize);
          int minY = surfaceChunkY - chunksBelowSurface;
          int maxY = surfaceChunkY + chunksAboveSurface;

          for (int y = minY; y <= maxY; y++)
          {
            Vector3Int chunkCoord = new(chunkX, y, chunkZ);
            if (world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) targetSet.Add(chunkCoord);
          }
        }
    }

    private void QueueGeneratedChunksForRender(Vector3Int viewerChunkCoord)
    {
      candidateChunksBuffer.Clear();
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (worldRenderer.HasChunkView(chunkCoord) || pendingLoadSet.Contains(chunkCoord) || pendingRenderSet.Contains(chunkCoord) || knownEmptyChunks.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord)) continue;
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) { knownEmptyChunks.Add(chunkCoord); continue; }
        candidateChunksBuffer.Add(chunkCoord);
      }

      candidateChunksBuffer.Sort((a, b) => CompareChunkPriority(a, b, viewerChunkCoord));
      for (int i = 0; i < candidateChunksBuffer.Count; i++)
      {
        if (HasChunkData(candidateChunksBuffer[i])) QueueRender(candidateChunksBuffer[i]);
        else QueueLoad(candidateChunksBuffer[i]);
      }
    }

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

    private void QueueLoad(Vector3Int chunkCoord)
    {
      if (pendingLoadSet.Add(chunkCoord))
      {
        pendingLoadQueue.Enqueue(chunkCoord);
        pendingQueuesNeedPrioritization = true;
      }
    }

    private void QueueRender(Vector3Int chunkCoord)
    {
      if (pendingRenderSet.Add(chunkCoord))
      {
        pendingRenderQueue.Enqueue(chunkCoord);
        pendingQueuesNeedPrioritization = true;
      }
    }

    private void PrioritizePendingQueues()
    {
      if (!hasLastViewerChunkCoord || !pendingQueuesNeedPrioritization) return;
      PrioritizeQueue(pendingLoadQueue, pendingLoadSet, true);
      PrioritizeQueue(pendingRenderQueue, pendingRenderSet, false);
      pendingQueuesNeedPrioritization = false;
    }

    private void PrioritizeQueue(Queue<Vector3Int> queue, HashSet<Vector3Int> membership, bool allowKeepOnlyChunks)
    {
      if (queue.Count < 2) return;
      queueSortBuffer.Clear();
      while (queue.Count > 0)
      {
        Vector3Int c = queue.Dequeue();
        if (!membership.Contains(c)) continue;
        if (!desiredChunkCoords.Contains(c) && !(allowKeepOnlyChunks && keepChunkCoords.Contains(c))) continue;
        queueSortBuffer.Add(c);
      }

      queueSortBuffer.Sort((a, b) => CompareChunkPriority(a, b, lastViewerChunkCoord));
      for (int i = 0; i < queueSortBuffer.Count; i++) queue.Enqueue(queueSortBuffer[i]);
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

      while (pendingLoadQueue.Count > 0 && count < maxLoadsThisFrame && scanned < maxScans)
      {
        scanned++;
        Vector3Int c = pendingLoadQueue.Dequeue();
        pendingLoadSet.Remove(c);

        if ((!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) || worldRenderer.HasChunkView(c) || knownEmptyChunks.Contains(c)) continue;

        if (HasChunkData(c))
        {
          if (desiredChunkCoords.Contains(c) || keepChunkCoords.Contains(c)) QueueRender(c);
          continue;
        }

        if (!world.Settings.IsInsideEffectiveWorldBounds3D(c))
        {
          knownEmptyChunks.Add(c);
          continue;
        }

        if (chunkLoadQueue.IsInFlight(c)) continue;

        int totalActiveAsyncTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
        if (chunkLoadQueue.ActiveTaskCount >= MaxLoadAsyncTasks || totalActiveAsyncTasks >= MaxTotalAsyncTasks)
        {
          QueueLoad(c);
          break;
        }

        if (chunkLoadQueue.TryStartLoad(storage != null ? storage.ActiveStore : null, storage != null ? storage.WorldId : null, c, MaxLoadAsyncTasks))
        {
          totalChunkLoadRequestsStarted++;
          count++;
          continue;
        }

        QueueLoad(c);
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
          }
          else if (result.TerrainSystem == TerrainSystem.SmoothDensity && result.DensityChunkData != null) world.Data.DensityChunks[c] = result.DensityChunkData;
          if ((desiredChunkCoords.Contains(c) || keepChunkCoords.Contains(c)) && HasChunkData(c)) QueueRender(c);
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

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (world.Data.BlockChunks.TryGetValue(c, out BlockChunkData b) && b != null)
          {
            if (buildQueue.IsInFlight(c))
            {
              QueueRender(c);
              continue;
            }

            int totalActiveTasksForBlockBuild = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
            if (buildQueue.ActiveTaskCount >= MaxBlockAsyncTasks || totalActiveTasksForBlockBuild >= MaxTotalAsyncTasks)
            {
              QueueRender(c);
              break;
            }

            if (TryStartBlockMeshBuild(c, b))
            {
              count++;
              continue;
            }

            QueueRender(c);
            continue;
          }

          if (!knownEmptyChunks.Contains(c)) QueueLoad(c);
          continue;
        }

        if (!world.Data.DensityChunks.TryGetValue(c, out DensityChunkData d) || d == null)
        {
          QueueLoad(c);
          continue;
        }

        if (densityBuildQueue.IsInFlight(c))
        {
          QueueRender(c);
          continue;
        }

        if (!EnsureDensitySampleChunksAvailableForMesh(c))
        {
          QueueRender(c);
          continue;
        }

        int totalActiveAsyncTasks = chunkLoadQueue.ActiveTaskCount + buildQueue.ActiveTaskCount + densityBuildQueue.ActiveTaskCount;
        if (densityBuildQueue.ActiveTaskCount >= MaxDensityAsyncTasks || totalActiveAsyncTasks >= MaxTotalAsyncTasks)
        {
          QueueRender(c);
          break;
        }

        if (TryStartDensityMeshBuild(c, d)) count++;
      }
    }

    private bool EnsureDensitySampleChunksAvailableForMesh(Vector3Int root)
    {
      bool allSampleChunksAvailable = true;

      for (int i = 0; i < DensityMeshSampleChunkOffsets.Length; i++)
      {
        Vector3Int c = root + DensityMeshSampleChunkOffsets[i];
        if (HasChunkData(c) || !world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;

        allSampleChunksAvailable = false;
        knownEmptyChunks.Remove(c);
        keepChunkCoords.Add(c);

        if (!chunkLoadQueue.IsInFlight(c) && !pendingLoadSet.Contains(c)) QueueLoad(c);
      }

      return allSampleChunksAvailable;
    }

    private bool EnsureBlockNeighborChunksAvailableForMesh(Vector3Int root)
    {
      bool allNeighborChunksAvailable = true;

      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int c = root + BlockMeshNeighborOffsets[i];
        if (world.Data.BlockChunks.ContainsKey(c) || !world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;

        allNeighborChunksAvailable = false;
        keepChunkCoords.Add(c);
        if (!chunkLoadQueue.IsInFlight(c) && !pendingLoadSet.Contains(c)) QueueLoad(c);
      }

      return allNeighborChunksAvailable;
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

          if (r.ChunkData != null) world.Data.BlockChunks[r.ChunkCoord] = r.ChunkData;

          if (r.IsEmpty || r.MeshData == null || r.MeshData.IsEmpty)
          {
            knownEmptyChunks.Add(r.ChunkCoord);
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
        worldRenderer.RenderUnityMesh(r.ChunkCoord, mesh, r.ChunkCoord == spawnTargetChunkCoord && !hasBroadcastInitialTerrainReady);
        totalDensityMeshApplies++;
        TryBroadcastInitialTerrainReady();
        count++;
      }
    }

    private bool TryStartBlockMeshBuild(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      BlockChunkBuildRequest request = new()
      {
        ChunkCoord = chunkCoord,
        GenerationId = buildQueue.GenerationId,
        WorldSnapshot = worldSnapshot,
        VoxelSize = world.Settings.VoxelSize,
        ChunkDataSnapshot = CloneBlockChunkData(chunkData),
        NeighborChunkSnapshots = CreateBlockMeshChunkSnapshots(chunkCoord)
      };

      return buildQueue.TryStartBuild(request, MaxBlockAsyncTasks);
    }

    private static BlockChunkData CloneBlockChunkData(BlockChunkData source)
    {
      if (source == null) return null;

      BlockChunkData clone = new(source.ChunkCoord);
      Voxel[] sourceVoxels = source.GetRawVoxelArray();
      Voxel[] targetVoxels = clone.GetRawVoxelArray();
      System.Array.Copy(sourceVoxels, targetVoxels, sourceVoxels.Length);
      return clone;
    }

    private Dictionary<Vector3Int, BlockChunkData> CreateBlockMeshChunkSnapshots(Vector3Int root)
    {
      Dictionary<Vector3Int, BlockChunkData> snapshots = new();

      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int c = root + BlockMeshNeighborOffsets[i];
        if (!world.Data.BlockChunks.TryGetValue(c, out BlockChunkData source) || source == null) continue;
        snapshots[c] = CloneBlockChunkData(source);
      }

      return snapshots;
    }

    private static void ReturnMeshData(DensityChunkBuildResult result)
    {
      if (result?.MeshData != null) { MeshDataPool.Return(result.MeshData); result.MeshData = null; }
    }

    private bool TryStartDensityMeshBuild(Vector3Int chunkCoord, DensityChunkData chunkData)
    {
      world.Settings.GetEffectiveWorldChunkBounds3D(out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);
      DensityChunkBuildRequest request = new()
      {
        ChunkCoord = chunkCoord,
        GenerationId = densityBuildQueue.GenerationId,
        WorldSnapshot = worldSnapshot,
        CellStep = Mathf.Clamp(world.Settings.DensityMeshStep, 1, 8),
        FlipWinding = true,
        OverrideSnapshot = world.CreateDensityOverrideSnapshot(chunkCoord),
        ChunkDataSnapshot = chunkData.Clone(),
        ChunkDataSnapshots = CreateDensityMeshChunkSnapshots(chunkCoord),
        GeneratedMinChunkX = minX,
        GeneratedMaxChunkX = maxX,
        GeneratedMinChunkY = minY,
        GeneratedMaxChunkY = maxY,
        GeneratedMinChunkZ = minZ,
        GeneratedMaxChunkZ = maxZ
      };
      return densityBuildQueue.TryStartBuild(request, MaxDensityAsyncTasks);
    }

    private Dictionary<Vector3Int, DensityChunkData> CreateDensityMeshChunkSnapshots(Vector3Int root)
    {
      Dictionary<Vector3Int, DensityChunkData> snapshots = new();
      for (int i = 0; i < DensityMeshSampleChunkOffsets.Length; i++)
      {
        Vector3Int c = root + DensityMeshSampleChunkOffsets[i];
        if (c == root) continue;
        world.Data.DensityChunks.TryGetValue(c, out DensityChunkData source);
        if (source != null) snapshots[c] = source.Clone();
      }
      return snapshots;
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

    private Vector3 GetSpawnReferencePosition() => viewer != null ? viewer.position : desiredInitialSpawnLocation;

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
      Vector3Int c = WorldToChunkCoord(worldPos);
      c.y = GetSurfaceChunkYForColumn(c.x, c.z, c.y * VoxelConstants.ChunkSize);
      return c;
    }

    private int GetSurfaceChunkYForColumn(int x, int z, int fallbackVoxelY)
    {
      if (generator == null) return VoxelMath.FloorDiv(fallbackVoxelY, VoxelConstants.ChunkSize);
      Vector2Int column = new(x, z);
      if (surfaceChunkYCache.TryGetValue(column, out int cached)) return cached;
      int y = generator.GetSurfaceChunkYForChunkColumn(column); surfaceChunkYCache[column] = y; return y;
    }

    private void UnloadOutsideKeepSet()
    {
      unloadChunksBuffer.Clear();
      foreach (Vector3Int c in worldRenderer.ActiveChunkViews.Keys) if (!keepChunkCoords.Contains(c)) unloadChunksBuffer.Add(c);
      for (int i = 0; i < unloadChunksBuffer.Count; i++)
      {
        Vector3Int chunkCoord = unloadChunksBuffer[i];
        if (pendingUnloadSet.Add(chunkCoord)) pendingUnloadQueue.Enqueue(chunkCoord);
      }
    }

    private void ProcessUnloadQueue(int unloadBudget)
    {
      int count = 0;
      while (count < unloadBudget && pendingUnloadQueue.Count > 0)
      {
        Vector3Int chunkCoord = pendingUnloadQueue.Dequeue();
        pendingUnloadSet.Remove(chunkCoord);

        if (keepChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        worldRenderer.RemoveChunk(chunkCoord);
        if (evictCachedChunkDataOutsideKeepSet && storage != null)
        {
          world.Data.BlockChunks.Remove(chunkCoord);
          world.Data.DensityChunks.Remove(chunkCoord);
        }

        totalChunkUnloadsApplied++;
        count++;
      }
    }

    private void TryBroadcastInitialTerrainReady()
    {
      if (hasBroadcastInitialTerrainReady) return;
      if (!worldRenderer.HasChunkView(spawnTargetChunkCoord))
      {
        QueueSpawnTargetForRender();
        return;
      }
      if (worldRenderer.ActiveChunkViews.Count < initialSpawnRequiredRenderedChunks) return;

      Vector3 spawn = CalculateInitialSpawnLocation();
      world.BroadcastInitialTerrainReady(spawn);
      hasBroadcastInitialTerrainReady = true;
      ForceRefreshStreamingSet();
    }

    private Vector3 CalculateInitialSpawnLocation()
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);

      float localVoxelX = spawnTargetChunkCoord.x * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;
      float localVoxelZ = spawnTargetChunkCoord.z * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;

      Vector3Int sampleVoxel = new(
        Mathf.FloorToInt(localVoxelX),
        0,
        Mathf.FloorToInt(localVoxelZ)
      );

      TerrainSample sample = BiomeTerrainSampler.Sample(world.Settings, sampleVoxel, densityScale);
      float localVoxelY = sample.SurfaceHeight + Mathf.Max(0.0f, initialSpawnClearance);

      return transform.TransformPoint(new Vector3(
        localVoxelX * voxelSize,
        localVoxelY * voxelSize,
        localVoxelZ * voxelSize
      ));
    }
  }
}
