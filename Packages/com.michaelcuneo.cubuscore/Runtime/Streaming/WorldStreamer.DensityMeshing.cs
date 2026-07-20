using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
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
          knownEmptyDensityChunks,
          HasDensityChunkData,
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
        CellStep = WorldSettings.NormalizeDensityMeshStep(world.Settings.DensityMeshStep),
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

    private bool HasDensityChunkData(Vector3Int chunkCoord)
    {
      return world.Data.DensityChunks.ContainsKey(chunkCoord);
    }

    private Dictionary<Vector3Int, DensityChunkData> CreateDensityMeshChunkSnapshots(Vector3Int root)
    {
      // Marching cubes reads the +X/+Y/+Z boundary slab. Central boundary normals
      // also read the negative shell and edge/corner combinations, so copy only
      // the relevant low/high slabs for every adjacent chunk.
      int boundaryThickness = WorldSettings.NormalizeDensityMeshStep(world.Settings.DensityMeshStep) + 1;
      Dictionary<Vector3Int, DensityChunkData> snapshots = null;

      for (int i = 0; i < DensityMeshSampleChunkOffsets.Length; i++)
      {
        Vector3Int offset = DensityMeshSampleChunkOffsets[i];
        if (offset == Vector3Int.zero)
        {
          continue;
        }

        Vector3Int c = root + offset;
        if (!world.Data.DensityChunks.TryGetValue(c, out DensityChunkData source) || source == null)
        {
          continue;
        }

        snapshots ??= new Dictionary<Vector3Int, DensityChunkData>();
        snapshots[c] = CloneDensityNeighborBoundary(source, offset, boundaryThickness);
      }

      return snapshots;
    }

    private static DensityChunkData CloneDensityNeighborBoundary(DensityChunkData source, Vector3Int offset, int thickness)
    {
      const int size = VoxelConstants.ChunkSize;
      DensityVoxel[] rented = DensityVoxelArrayPool.Rent();
      DensityVoxel[] src = source.GetRawVoxelArray();

      // Copy the low or high slab for each offset axis; on zero-offset axes copy
      // the full sampled range. Index layout is z*size*size + y*size + x.
      int tx = offset.x != 0 ? thickness : size;
      int ty = offset.y != 0 ? thickness : size;
      int tz = offset.z != 0 ? thickness : size;
      int startX = offset.x < 0 ? size - tx : 0;
      int startY = offset.y < 0 ? size - ty : 0;
      int startZ = offset.z < 0 ? size - tz : 0;

      for (int z = startZ; z < startZ + tz; z++)
      {
        for (int y = startY; y < startY + ty; y++)
        {
          int rowBase = size * (y + size * z);
          System.Array.Copy(src, rowBase + startX, rented, rowBase + startX, tx);
        }
      }

      return DensityChunkData.WrapRawArray(source.ChunkCoord, rented);
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
