using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private bool TryStartBlockMeshBuild(Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      BlockChunkBuildRequest request = new()
      {
        ChunkCoord = chunkCoord,
        GenerationId = buildQueue.GenerationId,
        WorldSnapshot = worldSnapshot,
        VoxelSize = world.Settings.VoxelSize,
        ChunkDataSnapshot = chunkData,
        NeighborChunkSnapshots = CreateBlockMeshChunkSnapshots(chunkCoord),
        SolidFallbackNeighborChunks = IsHybridTerrainEnabled ? null : CollectUnloadedInBoundsBlockNeighbors(chunkCoord)
      };

      return buildQueue.TryStartBuild(request, MaxBlockAsyncTasks);
    }

    private Dictionary<Vector3Int, BlockChunkData> CreateBlockMeshChunkSnapshots(Vector3Int root)
    {
      Dictionary<Vector3Int, BlockChunkData> snapshots = null;

      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int offset = BlockMeshNeighborOffsets[i];
        Vector3Int c = root + offset;

        if (!world.Data.BlockChunks.TryGetValue(c, out BlockChunkData source) || source == null)
        {
          continue;
        }

        snapshots ??= new Dictionary<Vector3Int, BlockChunkData>();
        snapshots[c] = CloneNeighborBoundaryFace(source, offset);
      }

      return snapshots;
    }

    private bool HasAllInBoundsBlockNeighborsLoaded(Vector3Int root)
    {
      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int c = root + BlockMeshNeighborOffsets[i];
        if (!world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;
        if (!world.Data.BlockChunks.ContainsKey(c)) return false;
      }

      return true;
    }

    private HashSet<Vector3Int> CollectUnloadedInBoundsBlockNeighbors(Vector3Int root)
    {
      HashSet<Vector3Int> unloaded = null;

      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int c = root + BlockMeshNeighborOffsets[i];

        if (
          world.Data.BlockChunks.ContainsKey(c) ||
          !world.Settings.IsInsideEffectiveWorldBounds3D(c)
        )
        {
          continue;
        }

        (unloaded ??= new HashSet<Vector3Int>()).Add(c);
      }

      return unloaded;
    }

    private static BlockChunkData CloneNeighborBoundaryFace(BlockChunkData source, Vector3Int offset)
    {
      const int size = VoxelConstants.ChunkSize;
      Voxel[] rented = VoxelArrayPool.Rent();
      Voxel[] src = source.GetRawVoxelArray();

      if (offset.x != 0)
      {
        int x = offset.x < 0 ? size - 1 : 0;
        for (int z = 0; z < size; z++)
        {
          int planeBase = size * size * z + x;
          for (int y = 0; y < size; y++)
          {
            int idx = planeBase + size * y;
            rented[idx] = src[idx];
          }
        }
      }
      else if (offset.y != 0)
      {
        int y = offset.y < 0 ? size - 1 : 0;
        for (int z = 0; z < size; z++)
        {
          int rowBase = size * (y + size * z);
          System.Array.Copy(src, rowBase, rented, rowBase, size);
        }
      }
      else
      {
        int z = offset.z < 0 ? size - 1 : 0;
        int planeBase = size * size * z;
        System.Array.Copy(src, planeBase, rented, planeBase, size * size);
      }

      return new BlockChunkData(source.ChunkCoord, rented);
    }

    private void RequeueSettledBlockNeighbors(Vector3Int chunkCoord)
    {
      for (int i = 0; i < BlockMeshNeighborOffsets.Length; i++)
      {
        Vector3Int n = chunkCoord + BlockMeshNeighborOffsets[i];

        if (!desiredChunkCoords.Contains(n))
        {
          continue;
        }

        if (pendingBlockRenderSet.Contains(n) || pendingBlockRenderRetrySet.Contains(n) || buildQueue.IsInFlight(n))
        {
          continue;
        }

        if (world.Data.BlockChunks.TryGetValue(n, out BlockChunkData nb) && nb != null)
        {
          QueueBlockRender(n);
        }
      }
    }
  }
}
