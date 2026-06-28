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
      return DensityMeshSampleDependencyPlanner.QueueMissingSampleChunks(
          root,
          world,
          keepChunkCoords,
          knownEmptyChunks,
          HasChunkData,
          chunkLoadQueue.IsInFlight,
          pendingLoadSet.Contains,
          c => QueueLoad(c)
      );
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
      return DensityMeshSampleDependencyPlanner.CreateSnapshotMap(
          root,
          world.Data.DensityChunks
      );
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