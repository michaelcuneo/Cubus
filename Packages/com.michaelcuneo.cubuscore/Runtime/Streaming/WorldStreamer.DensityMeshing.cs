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

    private bool HasDensityChunkData(Vector3Int chunkCoord)
    {
      return world.Data.DensityChunks.ContainsKey(chunkCoord);
    }

    private Dictionary<Vector3Int, DensityChunkData> CreateDensityMeshChunkSnapshots(Vector3Int root)
    {
      // The marching-cubes sampler only reads the +X/+Y/+Z boundary slab of each
      // neighbour (thickness bounded by the mesh cell step), so clone just that
      // slab into a pooled array instead of copying whole ~0.75 MB chunks.
      int boundaryThickness = Mathf.Clamp(world.Settings.DensityMeshStep, 1, 4);
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

      // Copy the low-corner slab [0,tx) x [0,ty) x [0,tz). For a +axis neighbour
      // the sampler only reads the low boundary planes on that axis; on the other
      // axes it reads the full sampled range. Index layout is z*size*size + y*size + x.
      int tx = offset.x != 0 ? thickness : size;
      int ty = offset.y != 0 ? thickness : size;
      int tz = offset.z != 0 ? thickness : size;

      for (int z = 0; z < tz; z++)
      {
        for (int y = 0; y < ty; y++)
        {
          int rowBase = size * (y + size * z);
          System.Array.Copy(src, rowBase, rented, rowBase, tx);
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
