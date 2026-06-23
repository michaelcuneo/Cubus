using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Drives the Distant-Horizon LOD terrain. Sits alongside <see cref="CubusWorld"/>
  /// and <see cref="WorldRenderer"/> and fills the world beyond the full-detail
  /// streaming radius with progressively coarser voxel tiles, so terrain reaches
  /// the horizon without paying for full-resolution chunks.
  ///
  /// LOD tiles are a pure function of the world snapshot, so they never need to be
  /// re-meshed when full chunks stream in; they are built off the main thread and
  /// applied (collision-free) under a per-frame budget.
  /// </summary>
  [DefaultExecutionOrder(-5)]
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  public sealed class LodStreamer : MonoBehaviour
  {
    [Header("References")]
    [Tooltip("Viewer whose position drives LOD rings. Defaults to Camera.main when empty.")]
    [SerializeField] private Transform viewer;

    [Header("Budgets")]
    [Tooltip("Maximum concurrent off-thread LOD tile builds.")]
    [SerializeField, Min(1)] private int maxConcurrentBuilds = 4;
    [Tooltip("Maximum LOD builds started per frame.")]
    [SerializeField, Min(1)] private int buildsStartedPerFrame = 8;
    [Tooltip("Maximum LOD tile meshes applied to the scene per frame.")]
    [SerializeField, Min(1)] private int tilesAppliedPerFrame = 8;

    private sealed class LodBuildResult
    {
      public LodTileKey Key;
      public MeshData Mesh;
      public int Generation;
    }

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private WorldGenerator generator;
    private WorldGenerationSnapshot snapshot;
    private LodChunkRenderer lodRenderer;

    private readonly HashSet<LodTileKey> desiredKeys = new();
    private readonly List<LodTileKey> desiredBuffer = new();
    private readonly List<LodTileKey> activeKeyBuffer = new();
    private readonly Queue<LodTileKey> pendingBuilds = new();
    private readonly HashSet<LodTileKey> pendingSet = new();
    private readonly HashSet<LodTileKey> inFlight = new();
    private readonly HashSet<LodTileKey> knownEmptyTiles = new();
    private readonly ConcurrentQueue<LodBuildResult> completed = new();

    private readonly Dictionary<Vector2Int, int> surfaceChunkYCache = new();
    private Func<int, int, int> surfaceProvider;
    private Func<Vector3Int, bool> boundsProvider;

    private int generation;
    private Vector3Int lastViewerChunkCoord;
    private bool hasLastViewerChunkCoord;
    private float baseVoxelSize = 1.0f;
    private bool snapshotInitialized;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      worldRenderer = GetComponent<WorldRenderer>();
    }

    private void Start()
    {
      if (viewer == null && Camera.main != null)
      {
        viewer = Camera.main.transform;
      }
    }

    private void OnDisable()
    {
      ResetLodState();
    }

    private void OnDestroy()
    {
      lodRenderer?.Destroy();
      lodRenderer = null;
    }

    /// <summary>Drops all LOD tiles and invalidates in-flight builds.</summary>
    public void ResetLodState()
    {
      generation++;
      desiredKeys.Clear();
      pendingBuilds.Clear();
      pendingSet.Clear();
      inFlight.Clear();
      knownEmptyTiles.Clear();
      surfaceChunkYCache.Clear();
      while (completed.TryDequeue(out _)) { }
      hasLastViewerChunkCoord = false;
      lodRenderer?.HideAll();
    }

    private void Update()
    {
      if (world == null || world.Settings == null)
      {
        return;
      }

      bool lodActive =
          world.Settings.EnableLodTerrain &&
          (world.Settings.TerrainSystem == TerrainSystem.Block ||
           world.Settings.TerrainSystem == TerrainSystem.SmoothDensity) &&
          world.Settings.LodLevelCount >= LodConstants.MinLodLevel;

      if (!lodActive)
      {
        if (lodRenderer != null && lodRenderer.ActiveTileCount > 0)
        {
          ResetLodState();
        }

        return;
      }

      if (!world.IsWorldReady)
      {
        return;
      }

      if (!EnsureRuntimeReferences())
      {
        return;
      }

      Vector3Int viewerChunkCoord = WorldToChunkCoord(viewer.position);
      if (!hasLastViewerChunkCoord || viewerChunkCoord != lastViewerChunkCoord)
      {
        lastViewerChunkCoord = viewerChunkCoord;
        hasLastViewerChunkCoord = true;
        RecomputeDesiredTiles(viewerChunkCoord);
      }

      StartPendingBuilds();
      ApplyCompletedBuilds();
    }

    private bool EnsureRuntimeReferences()
    {
      if (viewer == null)
      {
        if (Camera.main == null)
        {
          return false;
        }

        viewer = Camera.main.transform;
      }

      baseVoxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      generator ??= new WorldGenerator(world.Settings);
      surfaceProvider ??= GetSurfaceChunkYForColumn;
      boundsProvider ??= world.Settings.IsInsideEffectiveWorldBounds3D;
      lodRenderer ??= new LodChunkRenderer(worldRenderer.transform);

      if (!snapshotInitialized)
      {
        snapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
        snapshotInitialized = true;
      }

      return true;
    }

    private void RecomputeDesiredTiles(Vector3Int viewerChunkCoord)
    {
      desiredBuffer.Clear();
      LodBandPlanner.ComputeDesiredTiles(
        viewerChunkCoord,
        Mathf.Max(1, world.Settings.ViewDistanceInChunks),
        world.Settings.LodLevelCount,
        world.Settings.LodRingWidthInTiles,
        world.Settings.LodVerticalRadiusInTiles,
        surfaceProvider,
        boundsProvider,
        desiredBuffer);

      desiredKeys.Clear();
      for (int i = 0; i < desiredBuffer.Count; i++)
      {
        desiredKeys.Add(desiredBuffer[i]);
      }

      // Hide tiles that are no longer wanted.
      lodRenderer.CollectActiveKeys(activeKeyBuffer);
      for (int i = 0; i < activeKeyBuffer.Count; i++)
      {
        if (!desiredKeys.Contains(activeKeyBuffer[i]))
        {
          lodRenderer.HideTile(activeKeyBuffer[i]);
        }
      }

      // Drop stale "known empty" entries that left the desired set so they get a
      // fresh chance if the viewer returns later.
      knownEmptyTiles.RemoveWhere(key => !desiredKeys.Contains(key));

      // Rebuild the pending queue, nearest-first, for anything not yet resident.
      pendingBuilds.Clear();
      pendingSet.Clear();

      desiredBuffer.Sort((a, b) => DistanceSortKey(a, viewerChunkCoord).CompareTo(DistanceSortKey(b, viewerChunkCoord)));

      for (int i = 0; i < desiredBuffer.Count; i++)
      {
        LodTileKey key = desiredBuffer[i];
        if (lodRenderer.HasTile(key) || inFlight.Contains(key) || knownEmptyTiles.Contains(key))
        {
          continue;
        }

        if (pendingSet.Add(key))
        {
          pendingBuilds.Enqueue(key);
        }
      }
    }

    private static long DistanceSortKey(LodTileKey key, Vector3Int viewerChunkCoord)
    {
      int strideChunks = LodConstants.StrideForLevel(key.Level);
      int centreChunkX = key.Coord.x * strideChunks + strideChunks / 2;
      int centreChunkZ = key.Coord.z * strideChunks + strideChunks / 2;
      int dx = centreChunkX - viewerChunkCoord.x;
      int dz = centreChunkZ - viewerChunkCoord.z;
      return (long)dx * dx + (long)dz * dz;
    }

    private void StartPendingBuilds()
    {
      int started = 0;

      while (pendingBuilds.Count > 0 && started < buildsStartedPerFrame && inFlight.Count < maxConcurrentBuilds)
      {
        LodTileKey key = pendingBuilds.Dequeue();
        pendingSet.Remove(key);

        if (!desiredKeys.Contains(key) || lodRenderer.HasTile(key) || inFlight.Contains(key) || knownEmptyTiles.Contains(key))
        {
          continue;
        }

        inFlight.Add(key);
        int builtGeneration = generation;
        WorldGenerationSnapshot capturedSnapshot = snapshot;
        float capturedVoxelSize = baseVoxelSize;
        int capturedSkirtCells = Mathf.Max(0, world.Settings.LodSkirtDepthInCells);
        bool capturedUseDensity = world.Settings.TerrainSystem == TerrainSystem.SmoothDensity;

        Task.Run(() =>
        {
          MeshData mesh = null;
          try
          {
            mesh = capturedUseDensity
                ? LodDensityTileMesher.BuildAndMesh(capturedSnapshot, key.Level, key.Coord, capturedVoxelSize, capturedSkirtCells)
                : LodTileMesher.BuildAndMesh(capturedSnapshot, key.Level, key.Coord, capturedVoxelSize, out _, capturedSkirtCells);
          }
          catch (Exception ex)
          {
            Debug.LogException(ex);
          }

          completed.Enqueue(new LodBuildResult { Key = key, Mesh = mesh, Generation = builtGeneration });
        });

        started++;
      }
    }

    private void ApplyCompletedBuilds()
    {
      int applied = 0;
      Material material = null;

      while (applied < tilesAppliedPerFrame && completed.TryDequeue(out LodBuildResult result))
      {
        inFlight.Remove(result.Key);

        if (result.Generation != generation || !desiredKeys.Contains(result.Key))
        {
          continue;
        }

        if (result.Mesh == null || result.Mesh.IsEmpty)
        {
          knownEmptyTiles.Add(result.Key);
          continue;
        }

        material ??= worldRenderer.EnsureWorldMaterialAndGet();
        lodRenderer.ShowTile(result.Key, result.Mesh, baseVoxelSize, material);
        applied++;
      }
    }

    private int GetSurfaceChunkYForColumn(int chunkX, int chunkZ)
    {
      if (generator == null)
      {
        return 0;
      }

      var column = new Vector2Int(chunkX, chunkZ);
      if (surfaceChunkYCache.TryGetValue(column, out int cached))
      {
        return cached;
      }

      int y = generator.GetSurfaceChunkYForChunkColumn(column);
      surfaceChunkYCache[column] = y;
      return y;
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
  }
}
