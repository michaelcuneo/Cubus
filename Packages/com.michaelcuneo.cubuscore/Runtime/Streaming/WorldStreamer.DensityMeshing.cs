using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private bool EnsureDensitySampleChunksAvailableForMesh(Vector3Int root)
    {
      // Edge/sample chunks are populated inside DensityChunkBuildQueue.Build on
      // the worker thread. Do not generate sample cache data here; this method
      // runs during render scheduling on the main thread.
      return true;
    }

    private bool TryStartDensityMeshBuild(Vector3Int chunkCoord, DensityChunkData chunkData)
    {
      world.Settings.GetEffectiveWorldChunkBounds3D(
          out int minX,
          out int maxX,
          out int minY,
          out int maxY,
          out int minZ,
          out int maxZ
      );

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

      for (int i = 0; i < DensityMeshSampleDependencyPlanner.SampleChunkOffsets.Length; i++)
      {
        Vector3Int sampleCoord = root + DensityMeshSampleDependencyPlanner.SampleChunkOffsets[i];
        if (sampleCoord == root) continue;

        if (world.Data.DensityChunks.TryGetValue(sampleCoord, out DensityChunkData fullChunk) && fullChunk != null)
        {
          snapshots[sampleCoord] = fullChunk.Clone();
          continue;
        }

        if (DensitySampleChunkCache.TryGet(sampleCoord, out DensityChunkData cachedChunk) && cachedChunk != null)
        {
          snapshots[sampleCoord] = cachedChunk.Clone();
        }
      }

      return snapshots;
    }

    private static void ReturnMeshData(DensityChunkBuildResult result)
    {
      if (result?.MeshData != null)
      {
        MeshDataPool.Return(result.MeshData);
        result.MeshData = null;
      }
    }
  }
}