using System.Collections.Generic;
using System.Threading.Tasks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed class DensityChunkBuildQueue
  {
    private readonly Queue<DensityChunkBuildResult> completedResults = new();
    private readonly HashSet<Vector3Int> inFlightChunkCoords = new();
    private readonly object stateLock = new();

    private int activeTaskCount;
    private int generationId;

    public int ActiveTaskCount
    {
      get
      {
        lock (stateLock)
        {
          return activeTaskCount;
        }
      }
    }

    public int CompletedCount
    {
      get
      {
        lock (completedResults)
        {
          return completedResults.Count;
        }
      }
    }

    public int GenerationId
    {
      get
      {
        lock (stateLock)
        {
          return generationId;
        }
      }
    }

    public bool IsInFlight(Vector3Int chunkCoord)
    {
      lock (stateLock)
      {
        return inFlightChunkCoords.Contains(chunkCoord);
      }
    }

    public void IncrementGeneration()
    {
      lock (stateLock)
      {
        generationId++;
        activeTaskCount = 0;
        inFlightChunkCoords.Clear();
      }

      lock (completedResults)
      {
        completedResults.Clear();
      }
    }

    public bool TryStartBuild(DensityChunkBuildRequest request, int maxActiveTasks)
    {
      if (request == null)
      {
        return false;
      }

      lock (stateLock)
      {
        if (activeTaskCount >= maxActiveTasks)
        {
          return false;
        }

        if (inFlightChunkCoords.Contains(request.ChunkCoord))
        {
          return false;
        }

        inFlightChunkCoords.Add(request.ChunkCoord);
        activeTaskCount++;
      }

      _ = Task.Run(() => Build(request)).ContinueWith(task =>
      {
        DensityChunkBuildResult result = null;

        if (task.Status == TaskStatus.RanToCompletion)
        {
          result = task.Result;
        }
        else if (task.Exception != null)
        {
          Debug.LogException(task.Exception);
        }

        lock (completedResults)
        {
          completedResults.Enqueue(result ?? new DensityChunkBuildResult
          {
            ChunkCoord = request.ChunkCoord,
            ChunkData = null,
            MeshData = null,
            IsEmpty = true,
            HasSurfaceCrossing = false,
            Failed = true,
            GenerationId = request.GenerationId
          });
        }

        lock (stateLock)
        {
          activeTaskCount = Mathf.Max(0, activeTaskCount - 1);
          inFlightChunkCoords.Remove(request.ChunkCoord);
        }
      });

      return true;
    }

    public bool TryDequeueCompleted(out DensityChunkBuildResult result)
    {
      lock (completedResults)
      {
        if (completedResults.Count > 0)
        {
          result = completedResults.Dequeue();
          return true;
        }
      }

      result = null;
      return false;
    }

    private static DensityChunkBuildResult Build(DensityChunkBuildRequest request)
    {
      DensityChunkData chunkData;

      if (request.ChunkDataSnapshot != null)
      {
        chunkData = request.ChunkDataSnapshot;

        if (request.OverrideSnapshot != null && request.OverrideSnapshot.Count > 0)
        {
          DensityChunkBuilder.ApplyOverrides(chunkData, request.OverrideSnapshot);
        }
      }
      else
      {
        chunkData = DensityChunkBuilder.GenerateChunkData(
            request.ChunkCoord,
            request.WorldSnapshot,
            request.OverrideSnapshot
        );
      }

      request.ChunkDataSnapshots ??= new Dictionary<Vector3Int, DensityChunkData>();
      request.ChunkDataSnapshots[request.ChunkCoord] = chunkData;

      if (!chunkData.HasSurfaceCrossing())
      {
        return new DensityChunkBuildResult
        {
          ChunkCoord = request.ChunkCoord,
          ChunkData = chunkData,
          MeshData = null,
          IsEmpty = true,
          HasSurfaceCrossing = false,
          GenerationId = request.GenerationId
        };
      }

      MeshData meshData = DensityMeshDataBuilder.GenerateFromChunkSnapshots(
          request.ChunkCoord,
          request.WorldSnapshot,
          request.CellStep,
          request.FlipWinding,
          chunkData,
          request.ChunkDataSnapshots,
          worldVoxel => SampleVoxelForBuild(worldVoxel, request)
      );

      return new DensityChunkBuildResult
      {
        ChunkCoord = request.ChunkCoord,
        ChunkData = chunkData,
        MeshData = meshData,
        IsEmpty = meshData == null || meshData.IsEmpty,
        HasSurfaceCrossing = meshData != null && !meshData.IsEmpty,
        GenerationId = request.GenerationId
      };
    }

    private static DensityVoxel SampleVoxelForBuild(Vector3Int worldVoxelCoord, DensityChunkBuildRequest request)
    {
      Vector3Int chunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxelCoord);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxelCoord);

      if (request.ChunkDataSnapshots != null &&
          request.ChunkDataSnapshots.TryGetValue(chunkCoord, out DensityChunkData chunkData) &&
          chunkData != null &&
          chunkData.IsInBounds(localCoord.x, localCoord.y, localCoord.z))
      {
        return chunkData.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      bool outsideGeneratedBounds =
          chunkCoord.x < request.GeneratedMinChunkX ||
          chunkCoord.x > request.GeneratedMaxChunkX ||
          chunkCoord.y < request.GeneratedMinChunkY ||
          chunkCoord.y > request.GeneratedMaxChunkY ||
          chunkCoord.z < request.GeneratedMinChunkZ ||
          chunkCoord.z > request.GeneratedMaxChunkZ;

      if (outsideGeneratedBounds)
      {
        // Do not seal the upper scalar-field boundary with solid density. That creates the
        // giant flat roof seen over every streamed density region.
        if (chunkCoord.y > request.GeneratedMaxChunkY)
        {
          return DensityVoxel.Empty;
        }

        // Keep the lower / horizontal world edges solid so the generated field does not open
        // visible side holes at the finite world boundary. Use the biome material at this
        // world coordinate instead of hard-coding material 1, otherwise boundary faces become
        // green regardless of the actual biome.
        return SampleGeneratedVoxel(worldVoxelCoord, request.WorldSnapshot, forcedDensity: 1.0f);
      }

      return SampleGeneratedVoxel(worldVoxelCoord, request.WorldSnapshot, forcedDensity: null);
    }

    private static DensityVoxel SampleGeneratedVoxel(
        Vector3Int worldVoxelCoord,
        WorldGenerationSnapshot snapshot,
        float? forcedDensity)
    {
      double scale = snapshot.DensitySampleScale <= 0.0f
          ? 1.0
          : snapshot.DensitySampleScale;

      snapshot.ResolveBiomeAtWorldXZ(
          worldVoxelCoord.x,
          worldVoxelCoord.z,
          out TerrainGenerationProfileSnapshot profile,
          out _
      );

      TerrainSamplerBurst.Sample(
          profile,
          worldVoxelCoord.x * scale,
          worldVoxelCoord.y * scale,
          worldVoxelCoord.z * scale,
          out float density,
          out int solidMaterialId
      );

      if (forcedDensity.HasValue)
      {
        density = forcedDensity.Value;
      }

      ushort materialId = density > 0.0f
          ? (ushort)Mathf.Clamp(solidMaterialId, 1, 65535)
          : (ushort)0;

      return new DensityVoxel(density, materialId);
    }
  }
}
