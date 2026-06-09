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
    [SerializeField] private Material worldMaterial;
    [SerializeField] private bool logRenderedChunkMeshes;

    [Header("Collision")]
    [SerializeField] private WorldCollisionMode collisionMode = WorldCollisionMode.AllChunks;
    [SerializeField] private Transform collisionViewer;
    [SerializeField] private float collisionActivationRadius = 64.0f;
    [SerializeField] private float collisionUpdateInterval = 0.25f;

    public WorldCollisionMode CollisionMode
    {
      get => collisionMode;
      set
      {
        collisionMode = value;
        RefreshChunkCollision();
      }
    }

    private float timeSinceLastCollisionUpdate;
    private readonly Dictionary<Vector3Int, ChunkView> activeChunkViews = new();

    private CubusWorld world;
    private ChunkPool chunkPool;

    public IReadOnlyDictionary<Vector3Int, ChunkView> ActiveChunkViews => activeChunkViews;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      chunkPool = new ChunkPool(transform);
    }

    private void Update()
    {
      if (collisionMode != WorldCollisionMode.NearViewerOnly)
      {
        return;
      }

      timeSinceLastCollisionUpdate += Time.deltaTime;

      if (timeSinceLastCollisionUpdate < collisionUpdateInterval)
      {
        return;
      }

      timeSinceLastCollisionUpdate = 0.0f;
      RefreshChunkCollision();
    }

    public void SetCollisionViewer(Transform viewer)
    {
      collisionViewer = viewer;
      RefreshChunkCollision();
    }

    public void RefreshChunkCollision()
    {
      foreach (var pair in activeChunkViews)
      {
        if (pair.Value != null)
        {
          ApplyCollisionStateToChunk(pair.Value);
        }
      }
    }

    public void RenderBlockChunkMesh(Vector3Int chunkCoord, MeshData meshData)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (meshData == null || meshData.IsEmpty)
      {
        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);
      chunkView.ApplyMesh(meshData, collisionMode != WorldCollisionMode.None);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
    }

    public void RenderUnityMesh(Vector3Int chunkCoord, Mesh unityMesh)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (unityMesh == null || unityMesh.vertexCount == 0 || unityMesh.GetIndexCount(0) == 0)
      {
        if (logRenderedChunkMeshes)
        {
          Debug.LogWarning(
              $"Skipping Unity mesh chunk {chunkCoord}. " +
              $"MeshNull={unityMesh == null}, " +
              $"Verts={(unityMesh != null ? unityMesh.vertexCount : 0)}, " +
              $"Indices={(unityMesh != null && unityMesh.subMeshCount > 0 ? unityMesh.GetIndexCount(0) : 0)}"
          );
        }

        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);
      chunkView.ApplyMesh(unityMesh, collisionMode != WorldCollisionMode.None);

      if (logRenderedChunkMeshes)
      {
        Debug.Log(
            $"Rendered Unity mesh chunk {chunkCoord}. " +
            $"Verts={unityMesh.vertexCount}, " +
            $"Indices={unityMesh.GetIndexCount(0)}, " +
            $"Bounds={unityMesh.bounds}, " +
            $"Material={(chunkView.MeshRenderer != null ? chunkView.MeshRenderer.sharedMaterial : null)}, " +
            $"RendererEnabled={(chunkView.MeshRenderer != null && chunkView.MeshRenderer.enabled)}"
        );
      }

      ApplyCollisionStateToChunk(chunkView);
    }

    private void ApplyCollisionStateToChunk(ChunkView chunkView)
    {
      if (chunkView == null)
      {
        return;
      }

      switch (collisionMode)
      {
        case WorldCollisionMode.None:
          chunkView.SetCollisionEnabled(false);
          break;

        case WorldCollisionMode.AllChunks:
          chunkView.SetCollisionEnabled(true);
          break;

        case WorldCollisionMode.NearViewerOnly:
          chunkView.SetCollisionEnabled(IsChunkNearCollisionViewer(chunkView));
          break;
      }
    }

    private bool IsChunkNearCollisionViewer(ChunkView chunkView)
    {
      if (collisionViewer == null)
      {
        return true;
      }

      Vector3 chunkCenter =
          chunkView.transform.position +
          Vector3.one * world.Settings.VoxelSize * VoxelConstants.ChunkSize * 0.5f;

      float distanceSquared = (chunkCenter - collisionViewer.position).sqrMagnitude;
      float radiusSquared = collisionActivationRadius * collisionActivationRadius;

      return distanceSquared <= radiusSquared;
    }

    private void OnDestroy()
    {
      ClearAll();

      if (chunkPool != null)
      {
        chunkPool.DestroyAll();
        chunkPool = null;
      }
    }

    [ContextMenu("Rebuild All")]
    public void RebuildAll()
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (chunkPool == null)
      {
        chunkPool = new ChunkPool(transform);
      }

      if (!world.IsWorldReady)
      {
        world.GenerateWorld();
      }

      ClearAll();

      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        foreach (KeyValuePair<Vector3Int, BlockChunkData> pair in world.Data.BlockChunks)
        {
          RenderBlockChunk(pair.Key, pair.Value);
        }

        Debug.Log($"Rendered block chunks: {activeChunkViews.Count}");
        return;
      }

      if (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity)
      {
        var snapshot = WorldGenerationSnapshot.FromSettings(world.Settings);
        int rendered = 0;

        foreach (KeyValuePair<Vector3Int, DensityChunkData> pair in world.Data.DensityChunks)
        {
          var chunkCoord = pair.Key;
          var chunkData = pair.Value;

          if (chunkData == null)
          {
            RemoveChunk(chunkCoord);
            continue;
          }

          int cellStep = Mathf.Max(1, world.Settings.DensityMeshStep);
          var unityMesh = MarchingCubesMesher.GenerateMeshDirect(
            chunkCoord,
            snapshot,
            cellStep,
            true,
            world.GetDensityVoxelAtWorldVoxel
        );

          if (unityMesh == null || unityMesh.vertexCount == 0)
          {
            RemoveChunk(chunkCoord);
            continue;
          }

          ChunkView cv = GetOrCreateChunkView(chunkCoord);
          cv.ApplyMesh(unityMesh, collisionMode != WorldCollisionMode.None);
          ApplyCollisionStateToChunk(cv);
          rendered++;
        }

        Debug.Log($"Rendered density chunks: {rendered}");
        return;
      }
    }

    public void RenderBlockChunk(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      if (chunkData == null || !chunkData.HasAnySolidVoxel())
      {
        RemoveChunk(chunkCoord);
        return;
      }

      MeshData meshData = MeshDataPool.Rent(4096, 6144);
      BlockGreedyMesher.GenerateNeighbourAware(
        chunkData,
        world.GetBlockMaterialAtWorldVoxel,
        world.Settings.VoxelSize,
        meshData
      );

      if (meshData.IsEmpty)
      {
        RemoveChunk(chunkCoord);
        return;
      }

      ChunkView chunkView = GetOrCreateChunkView(chunkCoord);
      chunkView.ApplyMesh(meshData, collisionMode != WorldCollisionMode.None);
      MeshDataPool.Return(meshData);
      ApplyCollisionStateToChunk(chunkView);
    }

    public void RebuildBlockChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      int rebuiltCount = 0;
      int removedCount = 0;

      foreach (Vector3Int chunkCoord in dirtyChunks)
      {
        bool hasData = world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData chunkData);

        if (!hasData || chunkData == null || !chunkData.HasAnySolidVoxel())
        {
          if (RemoveChunk(chunkCoord))
          {
            removedCount++;
          }

          continue;
        }

        RenderBlockChunk(chunkCoord, chunkData);
        rebuiltCount++;
      }

      Debug.Log($"Rebuilt block chunks. Rebuilt={rebuiltCount}, Removed={removedCount}");
    }

    public bool RemoveChunk(Vector3Int chunkCoord)
    {
      if (!activeChunkViews.TryGetValue(chunkCoord, out ChunkView chunkView))
      {
        return false;
      }

      activeChunkViews.Remove(chunkCoord);

      if (chunkPool != null)
      {
        chunkPool.Release(chunkView);
      }
      else if (chunkView != null)
      {
        if (Application.isPlaying)
        {
          Destroy(chunkView.gameObject);
        }
        else
        {
          DestroyImmediate(chunkView.gameObject);
        }
      }

      return true;
    }

    [ContextMenu("Clear Rendered Chunks")]
    public void ClearAll()
    {
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeChunkViews)
      {
        if (pair.Value != null)
        {
          if (chunkPool != null)
          {
            chunkPool.Release(pair.Value);
          }
          else
          {
            if (Application.isPlaying)
            {
              Destroy(pair.Value.gameObject);
            }
            else
            {
              DestroyImmediate(pair.Value.gameObject);
            }
          }
        }
      }

      activeChunkViews.Clear();
    }

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return activeChunkViews.ContainsKey(chunkCoord);
    }

    private ChunkView GetOrCreateChunkView(Vector3Int chunkCoord)
    {
      if (activeChunkViews.TryGetValue(chunkCoord, out ChunkView existingView) && existingView != null)
      {
        return existingView;
      }

      ChunkView chunkView = chunkPool.Acquire(
          chunkCoord,
          world.Settings.VoxelSize,
          worldMaterial
      );

      activeChunkViews[chunkCoord] = chunkView;
      ApplyCollisionStateToChunk(chunkView);

      return chunkView;
    }
  }
}