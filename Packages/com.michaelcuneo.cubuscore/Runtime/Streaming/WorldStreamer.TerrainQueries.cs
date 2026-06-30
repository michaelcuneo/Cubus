using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    public int ActiveChunkViewCount => worldRenderer != null ? worldRenderer.ActiveChunkViews.Count : 0;

    public bool HasChunkView(Vector3Int chunkCoord)
    {
      return worldRenderer != null && worldRenderer.HasChunkView(chunkCoord);
    }

    public Vector3Int VoxelToChunkCoord(Vector3Int voxelCoord)
    {
      return new Vector3Int(
        VoxelMath.FloorDiv(voxelCoord.x, VoxelConstants.ChunkSize),
        VoxelMath.FloorDiv(voxelCoord.y, VoxelConstants.ChunkSize),
        VoxelMath.FloorDiv(voxelCoord.z, VoxelConstants.ChunkSize)
      );
    }

    public Vector3 VoxelToWorldPosition(Vector3Int voxelCoord)
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);

      Vector3 local = new(
        voxelCoord.x * voxelSize,
        voxelCoord.y * voxelSize,
        voxelCoord.z * voxelSize
      );

      return transform.TransformPoint(local);
    }

    public Vector3Int WorldToChunkCoord(Vector3 worldPos)
    {
      Vector3 local = transform.InverseTransformPoint(worldPos);
      Vector3 voxel = local / Mathf.Max(0.0001f, world.Settings.VoxelSize);

      return VoxelToChunkCoord(new Vector3Int(
        Mathf.FloorToInt(voxel.x),
        Mathf.FloorToInt(voxel.y),
        Mathf.FloorToInt(voxel.z)
      ));
    }

    public Vector3Int GetSurfaceChunkCoordFromVoxel(Vector3Int voxelCoord)
    {
      Vector3Int chunkCoord = VoxelToChunkCoord(voxelCoord);
      chunkCoord.y = GetSurfaceChunkYForColumn(
        chunkCoord.x,
        chunkCoord.z,
        voxelCoord.y
      );

      return chunkCoord;
    }
    public Vector3 CalculateSurfaceWorldPositionFromVoxel(Vector3Int targetVoxel, float clearance)
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);

      TerrainSample sample = BiomeTerrainSampler.Sample(
        world.Settings,
        targetVoxel,
        densityScale
      );

      float localVoxelX = targetVoxel.x;
      float localVoxelY = sample.SurfaceHeight + Mathf.Max(0.0f, clearance);
      float localVoxelZ = targetVoxel.z;

      return transform.TransformPoint(new Vector3(
        localVoxelX * voxelSize,
        localVoxelY * voxelSize,
        localVoxelZ * voxelSize
      ));
    }

    public void EnsureChunkQueuedForRender(Vector3Int chunkCoord)
    {
      if (!EnsureRuntimeReferences())
      {
        return;
      }

      if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
      {
        return;
      }

      desiredChunkCoords.Add(chunkCoord);
      keepChunkCoords.Add(chunkCoord);
      knownEmptyChunks.Remove(chunkCoord);
      knownEmptyDensityChunks.Remove(chunkCoord);

      bool needsBlock = IsBlockTerrainEnabled &&
                        !HasRenderedBlockChunk(chunkCoord) &&
                        !pendingBlockRenderSet.Contains(chunkCoord) &&
                        !pendingBlockRenderRetrySet.Contains(chunkCoord);

      bool needsDensity = IsDensityTerrainEnabled &&
                          !HasRenderedDensityChunk(chunkCoord) &&
                          !pendingDensityRenderSet.Contains(chunkCoord) &&
                          !pendingDensityRenderRetrySet.Contains(chunkCoord);

      if (!needsBlock && !needsDensity)
      {
        return;
      }

      if (pendingLoadSet.Contains(chunkCoord) || chunkLoadQueue.IsInFlight(chunkCoord))
      {
        return;
      }

      bool queuedRender = false;

      if (needsBlock)
      {
        if (world.Data.BlockChunks.ContainsKey(chunkCoord))
        {
          queuedRender |= QueueBlockRender(chunkCoord);
        }
        else
        {
          QueueLoad(chunkCoord);
          return;
        }
      }

      if (needsDensity)
      {
        if (world.Data.DensityChunks.ContainsKey(chunkCoord))
        {
          queuedRender |= QueueDensityRender(chunkCoord);
        }
        else
        {
          QueueLoad(chunkCoord);
          return;
        }
      }

      if (queuedRender)
      {
        pendingRenderQueueNeedsPrioritization = true;
      }
    }

    public void SetStreamingFocusVoxel(Vector3Int voxelCoord)
    {
      overrideStreamingFocusVoxel = voxelCoord;
      hasOverrideStreamingFocusVoxel = true;
      ForceRefreshStreamingSet();
    }

    public void ClearStreamingFocusVoxel()
    {
      hasOverrideStreamingFocusVoxel = false;
      ForceRefreshStreamingSet();
    }

    private Vector3Int GetStreamingFocusChunkCoord()
    {
      if (hasOverrideStreamingFocusVoxel)
      {
        return VoxelToChunkCoord(overrideStreamingFocusVoxel);
      }

      if (viewer != null)
      {
        return WorldToChunkCoord(viewer.position);
      }

      return Vector3Int.zero;
    }

    private int GetSurfaceChunkYForColumn(int x, int z, int fallbackVoxelY)
    {
      if (generator == null)
      {
        return VoxelMath.FloorDiv(fallbackVoxelY, VoxelConstants.ChunkSize);
      }

      Vector2Int column = new(x, z);

      if (surfaceChunkYCache.TryGetValue(column, out int cached))
      {
        return cached;
      }

      int y = generator.GetSurfaceChunkYForChunkColumn(column);
      surfaceChunkYCache[column] = y;
      return y;
    }
  }
}
