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
    [SerializeField] private bool persistStreamedChunks = false;

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

    private readonly WorldStreamingQueueSet pendingUnload = new();
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly HashSet<Vector3Int> knownEmptyDensityChunks = new();
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
    private Comparison<Vector3Int> chunkPriorityComparison;
    private long totalChunkLoadRequestsStarted;
    private long totalChunkLoadsCompleted;
    private long totalChunkLoadFailures;
    private long totalBlockMeshApplies;
    private long totalDensityMeshApplies;
    private long totalChunkUnloadsApplied;
    private int adaptiveThrottleFramesRemaining;

    private const float AdaptiveThrottleTriggerFrameMs = 45.0f;
    private const int AdaptiveThrottleDurationFrames = 2;

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

    public int FullDetailKeepRadius => FullDetailChunkRadius + ActiveUnloadPaddingInChunks;

    private bool UseInitialStreamingStageNow => useInitialStreamingStage && world != null && !world.IsInitialTerrainReady;
    private int ActiveDesiredRadiusInChunks => UseInitialStreamingStageNow ? Mathf.Max(0, initialStreamingRadiusInChunks) : Mathf.Max(1, world != null && world.Settings != null ? world.Settings.ViewDistanceInChunks : 1);
    private int ActiveKeepRadiusInChunks => ActiveDesiredRadiusInChunks + (UseInitialStreamingStageNow ? Mathf.Max(0, initialKeepPaddingInChunks) : ActiveUnloadPaddingInChunks);

    private int ActiveUnloadPaddingInChunks => IsDensityTerrainEnabled ? 0 : UnloadPaddingInChunks;
    private int UnloadPaddingInChunks => Mathf.Max(2, settings != null ? settings.UnloadPaddingInChunks : 4);
    private int ChunksLoadedPerFrame => Mathf.Clamp(
      settings != null
        ? (UseInitialStreamingStageNow ? settings.InitialChunksGeneratedPerFrame : settings.ChunksGeneratedPerFrame)
        : 96,
      1,
      256
    );
    private int ChunksRenderedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame : 128, 1, 256);
    private int ChunksUnloadedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame / 2 : 32, 1, 64);
    private int MeshAppliesPerFrame => Mathf.Clamp(settings != null ? settings.MeshAppliesPerFrame : 64, 1, 256);
    private int MaxAsyncChunkTasks => Mathf.Clamp(settings != null ? settings.MaxAsyncChunkTasks : 64, 1, 128);
    private int MaxTotalAsyncTasks => Mathf.Clamp(MaxAsyncChunkTasks, 1, Mathf.Max(1, cachedMaxHardwareConcurrency * 2));
    private int MaxLoadAsyncTasks => IsDensityTerrainEnabled ? Mathf.Max(1, Mathf.CeilToInt(MaxTotalAsyncTasks * 0.6f)) : Mathf.Max(1, MaxTotalAsyncTasks / 2);
    private int MaxMeshAsyncTasks => Mathf.Max(1, MaxTotalAsyncTasks - MaxLoadAsyncTasks);
    private int MaxBlockAsyncTasks => IsBlockTerrainEnabled ? Mathf.Max(1, IsDensityTerrainEnabled ? Mathf.Max(1, MaxMeshAsyncTasks / 6) : MaxMeshAsyncTasks) : 0;
    private int MaxDensityAsyncTasks => IsDensityTerrainEnabled ? Mathf.Max(1, IsBlockTerrainEnabled ? MaxMeshAsyncTasks - MaxBlockAsyncTasks : MaxMeshAsyncTasks) : 0;
    private float MeshApplyTimeBudgetSeconds => Mathf.Max(0.001f, (settings != null ? settings.MeshApplyTimeBudgetMs : 12) / 1000.0f);

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
      new(0, 0, 1),
      new(0, 0, -1)
    };

    private readonly HashSet<Vector3Int> knownEmptyDensityChunks = new();
  }
}