using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
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

    private readonly HashSet<Vector3Int> desiredChunkCoords = new();
    private readonly HashSet<Vector3Int> keepChunkCoords = new();
    private readonly Queue<Vector3Int> pendingGenerateQueue = new();
    private readonly HashSet<Vector3Int> pendingGenerateSet = new();
    private readonly Queue<Vector3Int> pendingRenderQueue = new();
    private readonly HashSet<Vector3Int> pendingRenderSet = new();
    private readonly HashSet<Vector3Int> knownEmptyChunks = new();
    private readonly HashSet<Vector3Int> forcedDensityRebuildChunks = new();

    private readonly BlockChunkBuildQueue buildQueue = new();
    private readonly DensityChunkBuildQueue densityBuildQueue = new();
    private WorldGenerationSnapshot worldSnapshot;

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private WorldGenerator generator;
    private BlockEditTool editTool;

    private Vector3Int lastViewerChunkCoord;
    private bool hasLastViewerChunkCoord;

    public StreamingSettings Settings => settings;

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
      int chunkY = generator != null
          ? generator.GetSurfaceChunkYForChunkColumn(new Vector2Int(chunkX, chunkZ))
          : VoxelMath.FloorDiv(Mathf.FloorToInt(voxel.y), VoxelConstants.ChunkSize);

      return new Vector3Int(chunkX, chunkY, chunkZ);
    }

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      worldRenderer = GetComponent<WorldRenderer>();
      editTool = GetComponent<BlockEditTool>();
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

      generator = new WorldGenerator(world.Settings);
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
      // Determine initial spawn target as the terrain surface chunk for the viewer's X/Z column.
      spawnTargetChunkCoord = WorldToSurfaceChunkCoord(GetSpawnReferencePosition());

      // Sync streaming view distance with world settings if not configured explicitly
      // View distance is driven by WorldSettings.ViewDistanceInChunks

      world.ClearWorld();
      worldRenderer.ClearAll();

      TryGenerateAndRenderSpawnChunkSync();
      TryBroadcastInitialTerrainReady();

      ForceRefreshStreamingSet();
    }

    private void TryGenerateAndRenderSpawnChunkSync()
    {
      Vector3Int coord = spawnTargetChunkCoord;
      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          {
            if (!world.Data.BlockChunks.ContainsKey(coord))
            {
              BlockChunkData chunkData = new(coord);
              if (world.TryGetBlockOverrides(coord, out var overrides))
              {
                generator.GenerateBlockChunkDataWithOverrides(chunkData, overrides);
              }
              else
              {
                generator.GenerateBlockChunkData(chunkData);
              }

              if (chunkData.HasAnySolidVoxel())
              {
                world.Data.BlockChunks[coord] = chunkData;
                worldRenderer.RenderBlockChunk(coord, chunkData);
              }
            }
            break;
          }
        case TerrainSystem.SmoothDensity:
          {
            if (!world.Data.DensityChunks.ContainsKey(coord))
            {
              var chunkData = DensityChunkBuilder.GenerateChunkData(coord, worldSnapshot, null);
              if (chunkData.HasAnySolidVoxel())
              {
                world.Data.DensityChunks[coord] = chunkData;
                int step = Mathf.Max(1, world.Settings.DensityMeshStep);
                float densityScale = Mathf.Max(0.001f, worldSnapshot.DensitySampleScale);
                TerrainSampler terrainSampler = new(worldSnapshot.TerrainProfile, worldSnapshot.BiomeId);
                var mesh = MarchingCubesMesher.GenerateMeshDirect(
                  coord,
                  worldSnapshot,
                  step,
                  true,
                  worldVoxel => SampleDensityForMeshing(worldVoxel, coord, chunkData, terrainSampler, densityScale)
                );
                if (mesh != null && mesh.vertexCount > 0)
                {
                  worldRenderer.RenderUnityMesh(coord, mesh);
                }
              }
            }
            break;
          }
      }
    }

    private void Update()
    {
      // Continuously track spawn target to the viewer's current chunk until spawned
      if (!hasBroadcastInitialTerrainReady)
      {
        spawnTargetChunkCoord = WorldToSurfaceChunkCoord(GetSpawnReferencePosition());
      }

      if (
          world.Settings.TerrainSystem != TerrainSystem.Block &&
          world.Settings.TerrainSystem != TerrainSystem.SmoothDensity)
      {
        return;
      }

      UpdateStreamingSetIfNeeded();
      ProcessGenerateQueue();
      ProcessCompletedBuildResults();
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
              $"PendingGenerate={pendingGenerateQueue.Count}, " +
              $"PendingRender={pendingRenderQueue.Count}, " +
              $"KnownEmpty={knownEmptyChunks.Count}, " +
              $"BlockTasks={buildQueue.ActiveTaskCount}, " +
              $"DensityTasks={densityBuildQueue.ActiveTaskCount}"
          );
        }
      }
    }

    [ContextMenu("Force Refresh Streaming Set")]
    public void ForceRefreshStreamingSet()
    {
      worldSnapshot = WorldGenerationSnapshot.FromSettings(world.Settings);

      buildQueue.IncrementGeneration();
      densityBuildQueue.IncrementGeneration();

      hasLastViewerChunkCoord = false;
      UpdateStreamingSetIfNeeded(force: true);
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

      BuildChunkSet(
        viewerChunkCoord,
        Mathf.Max(1, world.Settings.ViewDistanceInChunks),
        desiredChunkCoords
      );

      QueueMissingChunks(viewerChunkCoord);

      BuildChunkSet(
        viewerChunkCoord,
        Mathf.Max(1, world.Settings.ViewDistanceInChunks) + settings.UnloadPaddingInChunks,
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

      hasLastViewerChunkCoord = false;
      hasBroadcastInitialTerrainReady = false;
    }

    private void BuildChunkSet(
        Vector3Int viewerChunkCoord,
        int radius,
        HashSet<Vector3Int> output)
    {
      output.Clear();

      int safeRadius = Mathf.Max(0, radius);
      int belowSurface = Mathf.Max(0, settings.ChunksBelowSurface);
      int aboveSurface = Mathf.Max(0, settings.ChunksAboveSurface);

      for (int z = viewerChunkCoord.z - safeRadius; z <= viewerChunkCoord.z + safeRadius; z++)
      {
        for (int x = viewerChunkCoord.x - safeRadius; x <= viewerChunkCoord.x + safeRadius; x++)
        {
          Vector2Int column = new(x, z);

          int surfaceChunkY =
              generator.GetSurfaceChunkYForChunkColumn(column);

          int minY = surfaceChunkY - belowSurface;
          int maxY = surfaceChunkY + aboveSurface;

          for (int y = minY; y <= maxY; y++)
          {
            output.Add(new Vector3Int(x, y, z));
          }
        }
      }

      AddNearbyEditedChunks(viewerChunkCoord, safeRadius, output);
    }

    private void AddNearbyEditedChunks(
        Vector3Int viewerChunkCoord,
        int radius,
        HashSet<Vector3Int> output)
    {
      int radiusWithPadding = radius + settings.UnloadPaddingInChunks;

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
          if (settings.UseAsyncGeneration)
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
          ? settings.ChunksGeneratedPerFrame
          : settings.InitialChunksGeneratedPerFrame;

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

        QueueRender(chunkCoord);

        generatedThisFrame++;
      }
    }

    private void ProcessBlockGenerateQueueAsync()
    {
      int startedThisFrame = 0;

      int generateBudget = hasBroadcastInitialTerrainReady
          ? settings.ChunksGeneratedPerFrame
          : settings.InitialChunksGeneratedPerFrame;

      while (
          pendingGenerateQueue.Count > 0 &&
          startedThisFrame < generateBudget &&
          buildQueue.ActiveTaskCount < settings.MaxAsyncChunkTasks)
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

        if (buildQueue.TryStartBuild(request, settings.MaxAsyncChunkTasks))
        {
          startedThisFrame++;
        }
      }
    }

    private void ProcessDensityGenerateQueueAsync()
    {
      int startedThisFrame = 0;

      int generateBudget = hasBroadcastInitialTerrainReady
          ? settings.ChunksGeneratedPerFrame
          : settings.InitialChunksGeneratedPerFrame;

      while (
          pendingGenerateQueue.Count > 0 &&
          startedThisFrame < generateBudget &&
          densityBuildQueue.ActiveTaskCount < settings.MaxAsyncChunkTasks)
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

        DensityChunkBuildRequest request = new()
        {
          ChunkCoord = chunkCoord,
          GenerationId = densityBuildQueue.GenerationId,
          WorldSnapshot = worldSnapshot,
          CellStep = Mathf.Max(1, world.Settings.DensityMeshStep),
          FlipWinding = true,
          OverrideSnapshot = world.CreateDensityOverrideSnapshot(chunkCoord),
          ChunkDataSnapshot = existingSnapshot
        };

        if (densityBuildQueue.TryStartBuild(
            request,
            settings.MaxAsyncChunkTasks))
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
          appliedThisFrame < settings.MeshAppliesPerFrame &&
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

        if (result.MeshData != null && !result.MeshData.IsEmpty)
        {
          worldRenderer.RenderBlockChunkMesh(
            result.ChunkCoord,
            result.MeshData
        );
          TryBroadcastInitialTerrainReady();
        }
        else
        {
          QueueRender(result.ChunkCoord);
        }

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
        desiredChunkCoords.Add(chunkCoord);
        forcedDensityRebuildChunks.Add(chunkCoord);

        // Keep the currently rendered chunk/collider active until the rebuilt mesh is ready.
        // Removing it immediately creates a temporary hole that the player can fall through.

        if (pendingGenerateSet.Contains(chunkCoord))
        {
          continue;
        }

        pendingGenerateQueue.Enqueue(chunkCoord);
        pendingGenerateSet.Add(chunkCoord);
      }
    }

    private void ProcessCompletedDensityBuildResults()
    {
      int appliedThisFrame = 0;

      while (
          appliedThisFrame < settings.MeshAppliesPerFrame &&
          densityBuildQueue.TryDequeueCompleted(out DensityChunkBuildResult result))
      {
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

        if (result.ChunkData == null)
        {
          knownEmptyChunks.Add(result.ChunkCoord);
          world.Data.DensityChunks.Remove(result.ChunkCoord);
          worldRenderer.RemoveChunk(result.ChunkCoord);

          appliedThisFrame++;
          continue;
        }

        knownEmptyChunks.Remove(result.ChunkCoord);
        world.Data.DensityChunks[result.ChunkCoord] = result.ChunkData;

        // Build optimized mesh now on main thread using jobs
        int cellStep = Mathf.Max(1, world.Settings.DensityMeshStep);
        float densityScale = Mathf.Max(0.001f, worldSnapshot.DensitySampleScale);
        TerrainSampler terrainSampler = new(worldSnapshot.TerrainProfile, worldSnapshot.BiomeId);
        var mesh = MarchingCubesMesher.GenerateMeshDirect(
            result.ChunkCoord,
            worldSnapshot,
            cellStep,
          true,
          worldVoxel => SampleDensityForMeshing(worldVoxel, result.ChunkCoord, result.ChunkData, terrainSampler, densityScale)
        );

        if (mesh == null || mesh.vertexCount == 0)
        {
          knownEmptyChunks.Add(result.ChunkCoord);

          if (logStreamingStats)
          {
            Debug.LogWarning($"Density chunk has no mesh. Chunk={result.ChunkCoord}");
          }

          worldRenderer.RemoveChunk(result.ChunkCoord);
        }
        else
        {
          knownEmptyChunks.Remove(result.ChunkCoord);

          if (logStreamingStats)
          {
            Debug.Log($"Rendering density mesh. Chunk={result.ChunkCoord}, Verts={mesh.vertexCount}");
          }
          worldRenderer.RenderUnityMesh(result.ChunkCoord, mesh);
          TryBroadcastInitialTerrainReady();
        }

        appliedThisFrame++;
      }
    }

    private DensityVoxel SampleDensityForMeshing(
        Vector3Int worldVoxelCoord,
        Vector3Int rootChunkCoord,
        DensityChunkData rootChunkData,
        TerrainSampler sampler,
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

      TerrainSample sample = sampler.Sample(
          new Vector3(
              worldVoxelCoord.x * densityScale,
              worldVoxelCoord.y * densityScale,
              worldVoxelCoord.z * densityScale
          )
      );

      ushort materialId = sample.Density > 0.0f
          ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
          : (ushort)0;

      if (world.TryGetDensityOverrides(chunkCoord, out Dictionary<int, DensityVoxelOverride> overrides) &&
          overrides != null)
      {
        int voxelIndex = VoxelMath.FlattenIndex(localCoord.x, localCoord.y, localCoord.z);

        if (overrides.TryGetValue(voxelIndex, out DensityVoxelOverride densityOverride))
        {
          return new DensityVoxel(densityOverride.Density, densityOverride.MaterialId);
        }
      }

      return new DensityVoxel(sample.Density, materialId);
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
          renderedThisFrame < settings.ChunksRenderedPerFrame)
      {
        Vector3Int chunkCoord = pendingRenderQueue.Dequeue();
        pendingRenderSet.Remove(chunkCoord);

        if (!desiredChunkCoords.Contains(chunkCoord))
        {
          continue;
        }

        if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData))
        {
          continue;
        }

        worldRenderer.RenderBlockChunk(chunkCoord, chunkData);
        renderedThisFrame++;
      }
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

        // Remove generated chunk data, but keep BlockVoxelOverridesByChunk.
        // Overrides are the persistence/edit layer and must survive streaming unloads.
        RemoveChunkData(chunkCoord);
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

        if (!pendingRenderSet.Contains(chunkCoord))
        {
          pendingRenderQueue.Enqueue(chunkCoord);
          pendingRenderSet.Add(chunkCoord);
        }
      }
    }

    private void TryBroadcastInitialTerrainReady()
    {
      if (hasBroadcastInitialTerrainReady)
      {
        return;
      }

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        // In block mode, only spawn once the target chunk is rendered and the
        // computed spawn support chunk is also rendered. This avoids spawning
        // the player before collider support exists.
        if (!worldRenderer.HasChunkView(spawnTargetChunkCoord))
        {
          return;
        }

        if (world.FindInitialSpawnLocation(
            GetSpawnReferencePosition(),
            initialSpawnSearchRadius,
            initialSpawnSearchBelowVoxels,
            initialSpawnSearchAboveVoxels,
            initialSpawnClearance,
            out Vector3 spawnLocation))
        {
          Vector3 supportProbeWorld = spawnLocation - Vector3.up * Mathf.Max(world.Settings.VoxelSize, 0.1f);
          Vector3Int supportChunkCoord = WorldToChunkCoord(supportProbeWorld);

          if (!worldRenderer.HasChunkView(supportChunkCoord))
          {
            return;
          }

          hasBroadcastInitialTerrainReady = true;
          world.BroadcastInitialTerrainReady(spawnLocation);
        }

        return;
      }

      // Density fallback: if any chunk is visible, try to find spawn (no need to wait for all queues)
      if (worldRenderer.ActiveChunkViews.Count > 0)
      {
        if (world.FindInitialSpawnLocation(
            GetSpawnReferencePosition(),
            initialSpawnSearchRadius,
            initialSpawnSearchBelowVoxels,
            initialSpawnSearchAboveVoxels,
            initialSpawnClearance,
            out Vector3 spawnLocation))
        {
          hasBroadcastInitialTerrainReady = true;
          world.BroadcastInitialTerrainReady(spawnLocation);
        }
      }
    }
  }
}