using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  /// <summary>
  /// Describes the neighbour sample chunks used by the density marching-cubes mesh
  /// builder.
  ///
  /// A density chunk mesh samples the root chunk plus the +X/+Y/+Z boundary shell.
  /// Missing sample chunks are queued for their own full mesh builds, but they do
  /// not block the current chunk from meshing. The density build queue has a
  /// procedural sampler fallback for missing snapshots, which keeps terrain reveal
  /// moving while neighbouring chunks catch up.
  /// </summary>
  internal static class DensityMeshSampleDependencyPlanner
  {
    public static readonly Vector3Int[] SampleChunkOffsets =
    {
      new(0, 0, 0),
      new(1, 0, 0),
      new(0, 1, 0),
      new(0, 0, 1),
      new(1, 1, 0),
      new(1, 0, 1),
      new(0, 1, 1),
      new(1, 1, 1)
    };

    public static bool QueueMissingSampleChunks(
        Vector3Int root,
        CubusWorld world,
        ISet<Vector3Int> keepChunkCoords,
        ISet<Vector3Int> knownEmptyChunks,
        Func<Vector3Int, bool> hasChunkData,
        Func<Vector3Int, bool> isLoadInFlight,
        Func<Vector3Int, bool> isPendingLoad,
        Action<Vector3Int> queueLoad)
    {
      for (int i = 0; i < SampleChunkOffsets.Length; i++)
      {
        Vector3Int chunkCoord = root + SampleChunkOffsets[i];
        if (hasChunkData(chunkCoord) || !world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) continue;

        knownEmptyChunks.Remove(chunkCoord);
        keepChunkCoords.Add(chunkCoord);

        if (!isLoadInFlight(chunkCoord) && !isPendingLoad(chunkCoord))
        {
          queueLoad(chunkCoord);
        }
      }

      return true;
    }

    public static Dictionary<Vector3Int, DensityChunkData> CreateSnapshotMap(
        Vector3Int root,
        IReadOnlyDictionary<Vector3Int, DensityChunkData> densityChunks)
    {
      Dictionary<Vector3Int, DensityChunkData> snapshots = new();

      for (int i = 0; i < SampleChunkOffsets.Length; i++)
      {
        Vector3Int chunkCoord = root + SampleChunkOffsets[i];
        if (chunkCoord == root) continue;
        if (!densityChunks.TryGetValue(chunkCoord, out DensityChunkData source) || source == null) continue;

        snapshots[chunkCoord] = source.Clone();
      }

      return snapshots;
    }
  }
}