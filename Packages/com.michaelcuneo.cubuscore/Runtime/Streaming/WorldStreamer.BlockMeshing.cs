using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
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
        BlockNeighborhood = CreateBlockMeshNeighborhood(chunkCoord, chunkData)
      };

      return buildQueue.TryStartBuild(request, MaxBlockAsyncTasks);
    }

    private BlockChunkNeighborhood CreateBlockMeshNeighborhood(Vector3Int root, BlockChunkData center)
    {
      world.Data.BlockChunks.TryGetValue(root + Vector3Int.right, out BlockChunkData xPositive);
      world.Data.BlockChunks.TryGetValue(root + Vector3Int.left, out BlockChunkData xNegative);
      world.Data.BlockChunks.TryGetValue(root + Vector3Int.up, out BlockChunkData yPositive);
      world.Data.BlockChunks.TryGetValue(root + Vector3Int.down, out BlockChunkData yNegative);
      world.Data.BlockChunks.TryGetValue(root + new Vector3Int(0, 0, 1), out BlockChunkData zPositive);
      world.Data.BlockChunks.TryGetValue(root + new Vector3Int(0, 0, -1), out BlockChunkData zNegative);

      bool useSolidFallback = !IsHybridTerrainEnabled;

      return new BlockChunkNeighborhood(
        center,
        xPositive,
        xNegative,
        yPositive,
        yNegative,
        zPositive,
        zNegative,
        useSolidFallback && xPositive == null && IsInBoundsBlockNeighbor(root + Vector3Int.right),
        useSolidFallback && xNegative == null && IsInBoundsBlockNeighbor(root + Vector3Int.left),
        useSolidFallback && yPositive == null && IsInBoundsBlockNeighbor(root + Vector3Int.up),
        useSolidFallback && yNegative == null && IsInBoundsBlockNeighbor(root + Vector3Int.down),
        useSolidFallback && zPositive == null && IsInBoundsBlockNeighbor(root + new Vector3Int(0, 0, 1)),
        useSolidFallback && zNegative == null && IsInBoundsBlockNeighbor(root + new Vector3Int(0, 0, -1))
      );
    }

    private bool IsInBoundsBlockNeighbor(Vector3Int chunkCoord)
    {
      return world != null && world.Settings != null && world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord);
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