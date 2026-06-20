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
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly Dictionary<Vector2Int, int> surfaceChunkYCache = new();

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
    public int PendingLoadSetCount => pendingLoadSet.Count;
    public int PendingRenderSetCount => pendingRenderSet.Count;
    public int KnownEmptyChunkCount => knownEmptyChunks.Count;
    public int ActiveChunkLoadTaskCount => chunkLoadQueue.ActiveTaskCount;
    public int ActiveDensityBuildTaskCount => densityBuildQueue.ActiveTaskCount;
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
    private int ChunksLoadedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksGeneratedPerFrame : 8, 1, 64);
    private int ChunksRenderedPerFrame => Mathf.Clamp(settings != null ? settings.ChunksRenderedPerFrame : 32, 1, 128);
    private int MeshAppliesPerFrame => Mathf.Clamp(settings != null ? settings.MeshAppliesPerFrame : 32, 1, 128);
    private int MaxAsyncChunkTasks => Mathf.Clamp(settings != null ? settings.MaxAsyncChunkTasks : 8, 1, 32);

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
        if (storage == null || !storage.LoadWorldManifestOnly())
        {
          yield return world.GenerateWorldAsync();
          storage?.LoadWorldManifestOnly();
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

    private void Update()
    {
      if (!EnsureRuntimeReferences()) return;
      if (bootstrapCoroutine != null) return;
      if (world.Settings.TerrainSystem != TerrainSystem.Block && world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;

      UpdateStreamingSetIfNeeded();
      PrioritizePendingQueues();
      ProcessCompletedChunkLoads();
      ProcessLoadQueue();
      ProcessRenderQueue();
      ProcessCompletedBuildResults();
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
      hasLastViewerChunkCoord = false;
      UpdateStreamingSetIfNeeded(true);
    }

    public void ClearStreamingState()
    {
      desiredChunkCoords.Clear(); keepChunkCoords.Clear();
      pendingLoadQueue.Clear(); pendingLoadSet.Clear();
      pendingRenderQueue.Clear(); pendingRenderSet.Clear();
      knownEmptyChunks.Clear();
      buildQueue.IncrementGeneration(); densityBuildQueue.IncrementGeneration(); chunkLoadQueue.IncrementGeneration();
      hasLastViewerChunkCoord = false;
      hasBroadcastInitialTerrainReady = false;
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
      List<Vector3Int> candidates = new();
      foreach (Vector3Int chunkCoord in desiredChunkCoords)
      {
        if (worldRenderer.HasChunkView(chunkCoord) || pendingLoadSet.Contains(chunkCoord) || pendingRenderSet.Contains(chunkCoord) || knownEmptyChunks.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord)) continue;
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) { knownEmptyChunks.Add(chunkCoord); continue; }
        candidates.Add(chunkCoord);
      }

      candidates.Sort((a, b) => CompareChunkPriority(a, b, viewerChunkCoord));
      for (int i = 0; i < candidates.Count; i++)
      {
        if (HasChunkData(candidates[i])) QueueRender(candidates[i]);
        else QueueLoad(candidates[i]);
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

    private void QueueLoad(Vector3Int chunkCoord) { if (pendingLoadSet.Add(chunkCoord)) pendingLoadQueue.Enqueue(chunkCoord); }
    private void QueueRender(Vector3Int chunkCoord) { if (pendingRenderSet.Add(chunkCoord)) pendingRenderQueue.Enqueue(chunkCoord); }

    private void PrioritizePendingQueues()
    {
      if (!hasLastViewerChunkCoord) return;
      PrioritizeQueue(pendingLoadQueue, pendingLoadSet, true);
      PrioritizeQueue(pendingRenderQueue, pendingRenderSet, false);
    }

    private void PrioritizeQueue(Queue<Vector3Int> queue, HashSet<Vector3Int> membership, bool allowKeepOnlyChunks)
    {
      if (queue.Count < 2) return;
      List<Vector3Int> items = new(queue.Count);
      while (queue.Count > 0)
      {
        Vector3Int c = queue.Dequeue();
        if (!membership.Contains(c)) continue;
        if (!desiredChunkCoords.Contains(c) && !(allowKeepOnlyChunks && keepChunkCoords.Contains(c))) continue;
        items.Add(c);
      }

      items.Sort((a, b) => CompareChunkPriority(a, b, lastViewerChunkCoord));
      for (int i = 0; i < items.Count; i++) queue.Enqueue(items[i]);
    }

    private void ProcessLoadQueue()
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(ChunksLoadedPerFrame * 8, pendingLoadQueue.Count);

      while (pendingLoadQueue.Count > 0 && count < ChunksLoadedPerFrame && scanned < maxScans)
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

        if (storage != null && storage.ActiveStore != null)
        {
          if (chunkLoadQueue.ActiveTaskCount >= MaxAsyncChunkTasks)
          {
            QueueLoad(c);
            break;
          }

          if (chunkLoadQueue.TryStartLoad(storage.ActiveStore, storage.WorldId, c, MaxAsyncChunkTasks))
          {
            count++;
            continue;
          }

          QueueLoad(c);
          break;
        }

        if ((desiredChunkCoords.Contains(c) || keepChunkCoords.Contains(c)) && EnsureGeneratedChunkDataAvailable(c))
        {
          QueueRender(c);
          count++;
        }
      }
    }

    private void ProcessCompletedChunkLoads()
    {
      int count = 0;
      int completedBudget = Mathf.Max(ChunksLoadedPerFrame, MaxAsyncChunkTasks * 2);
      while (count < completedBudget && chunkLoadQueue.TryDequeueCompleted(out ChunkLoadResult result))
      {
        count++;
        if (result == null || result.GenerationId != chunkLoadQueue.GenerationId) continue;
        Vector3Int c = result.ChunkCoord;

        if (result.Loaded)
        {
          knownEmptyChunks.Remove(c);
          if (result.TerrainSystem == TerrainSystem.Block && result.BlockChunkData != null) world.Data.BlockChunks[c] = result.BlockChunkData;
          else if (result.TerrainSystem == TerrainSystem.SmoothDensity && result.DensityChunkData != null) world.Data.DensityChunks[c] = result.DensityChunkData;
          if ((desiredChunkCoords.Contains(c) || keepChunkCoords.Contains(c)) && HasChunkData(c)) QueueRender(c);
          continue;
        }

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;
        QueueLoad(c);
      }
    }

    private void ProcessRenderQueue()
    {
      int count = 0;
      int scanned = 0;
      int maxScans = Mathf.Max(ChunksRenderedPerFrame * 4, pendingRenderQueue.Count);

      while (pendingRenderQueue.Count > 0 && count < ChunksRenderedPerFrame && scanned < maxScans)
      {
        scanned++;
        Vector3Int c = pendingRenderQueue.Dequeue();
        pendingRenderSet.Remove(c);

        if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c)) continue;

        if (world.Settings.TerrainSystem == TerrainSystem.Block)
        {
          if (world.Data.BlockChunks.TryGetValue(c, out BlockChunkData b) && b != null && b.HasAnySolidVoxel())
          {
            worldRenderer.RenderBlockChunk(c, b);
            count++;
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

        EnsureDensitySampleChunksAvailableForMesh(c);

        if (densityBuildQueue.ActiveTaskCount >= MaxAsyncChunkTasks)
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

    private void ProcessCompletedBuildResults()
    {
      if (world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;
      int count = 0;
      while (count < MeshAppliesPerFrame && densityBuildQueue.TryDequeueCompleted(out DensityChunkBuildResult r))
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
        TryBroadcastInitialTerrainReady();
        count++;
      }
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
        CellStep = 1,
        FlipWinding = true,
        OverrideSnapshot = world.CreateDensityOverrideSnapshot(chunkCoord),
        ChunkDataSnapshot = chunkData.Clone(),
        ChunkDataSnapshots = CreateDensityMeshChunkSnapshots(chunkCoord, chunkData),
        GeneratedMinChunkX = minX, GeneratedMaxChunkX = maxX,
        GeneratedMinChunkY = minY, GeneratedMaxChunkY = maxY,
        GeneratedMinChunkZ = minZ, GeneratedMaxChunkZ = maxZ
      };
      return densityBuildQueue.TryStartBuild(request, MaxAsyncChunkTasks);
    }

    private Dictionary<Vector3Int, DensityChunkData> CreateDensityMeshChunkSnapshots(Vector3Int root, DensityChunkData rootData)
    {
      Dictionary<Vector3Int, DensityChunkData> snapshots = new();
      for (int i = 0; i < DensityMeshSampleChunkOffsets.Length; i++)
      {
        Vector3Int c = root + DensityMeshSampleChunkOffsets[i];
        DensityChunkData source = c == root ? rootData : null;
        if (source == null) world.Data.DensityChunks.TryGetValue(c, out source);
        if (source != null) snapshots[c] = source.Clone();
      }
      return snapshots;
    }

    private bool TryRebuildDensityMeshImmediate(Vector3Int chunkCoord)
    {
      if (!world.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData chunkData) || chunkData == null) return false;
      if (!EnsureDensitySampleChunksAvailableForMesh(chunkCoord)) return false;

      world.Settings.GetEffectiveWorldChunkBounds3D(out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);
      Dictionary<Vector3Int, DensityChunkData> snapshots = CreateDensityMeshChunkSnapshots(chunkCoord, chunkData);

      MeshData meshData = DensityMeshDataBuilder.Generate(
        chunkCoord,
        worldSnapshot,
        1,
        true,
        worldVoxel => SampleDensityForImmediateBuild(worldVoxel, snapshots, minX, maxX, minY, maxY, minZ, maxZ)
      );

      if (meshData == null || meshData.IsEmpty)
      {
        ReturnMeshData(meshData);
        worldRenderer.RemoveChunk(chunkCoord);
        return true;
      }

      Mesh mesh = meshData.ToUnityMeshFast();
      MeshDataPool.Return(meshData);
      worldRenderer.RenderUnityMesh(chunkCoord, mesh, true);
      return true;
    }

    private DensityVoxel SampleDensityForImmediateBuild(Vector3Int worldVoxelCoord, Dictionary<Vector3Int, DensityChunkData> snapshots, int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (snapshots != null && snapshots.TryGetValue(chunkCoord, out DensityChunkData chunkData) && chunkData != null && chunkData.IsInBounds(localCoord.x, localCoord.y, localCoord.z))
      {
        return chunkData.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      bool outsideGeneratedBounds = chunkCoord.x < minX || chunkCoord.x > maxX || chunkCoord.y < minY || chunkCoord.y > maxY || chunkCoord.z < minZ || chunkCoord.z > maxZ;
      if (outsideGeneratedBounds) return chunkCoord.y > maxY ? DensityVoxel.Empty : new DensityVoxel(1.0f, 1);

      double scale = worldSnapshot.DensitySampleScale <= 0.0f ? 1.0 : worldSnapshot.DensitySampleScale;
      TerrainSamplerBurst.Sample(worldSnapshot.TerrainProfile, worldVoxelCoord.x * scale, worldVoxelCoord.y * scale, worldVoxelCoord.z * scale, out float density, out int solidMaterialId);
      ushort materialId = density > 0.0f ? (ushort)Mathf.Clamp(solidMaterialId, 1, 65535) : (ushort)0;
      return new DensityVoxel(density, materialId);
    }

    private static void ReturnMeshData(MeshData meshData)
    {
      if (meshData != null) MeshDataPool.Return(meshData);
    }

    private bool EnsureGeneratedChunkDataAvailable(Vector3Int c)
    {
      if (HasChunkData(c)) return true;
      if (!world.Settings.IsInsideEffectiveWorldBounds3D(c)) { knownEmptyChunks.Add(c); return false; }

      generator ??= new WorldGenerator(world.Settings);
      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        BlockChunkData b = new(c); generator.GenerateBlockChunkData(b);
        if (!b.HasAnySolidVoxel()) { knownEmptyChunks.Add(c); return false; }
        world.Data.BlockChunks[c] = b; storage?.SaveChunk(c); return true;
      }

      DensityChunkData d = new(c); generator.FillDensityChunkFromTerrainSampler(d);
      world.Data.DensityChunks[c] = d; storage?.SaveChunk(c); return true;
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

          if (!TryRebuildDensityMeshImmediate(c)) QueueRender(c);
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
      List<Vector3Int> unload = new();
      foreach (Vector3Int c in worldRenderer.ActiveChunkViews.Keys) if (!keepChunkCoords.Contains(c)) unload.Add(c);
      for (int i = 0; i < unload.Count; i++)
      {
        worldRenderer.RemoveChunk(unload[i]);
        if (evictCachedChunkDataOutsideKeepSet && storage != null)
        {
          world.Data.BlockChunks.Remove(unload[i]);
          world.Data.DensityChunks.Remove(unload[i]);
        }
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
