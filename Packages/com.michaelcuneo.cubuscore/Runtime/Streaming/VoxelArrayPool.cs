using System.Collections.Concurrent;
using System.Threading;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  // Thread-safe pool of full-chunk Voxel[] arrays (ChunkVolume length) used for
  // the throwaway snapshots created for async mesh builds: the six neighbour
  // boundary slices AND the canonical chunk clone the worker meshes against.
  // Each array is ~64 KB, so renting/returning instead of allocating eliminates
  // the dominant streaming GC churn that caused hitches while moving. Arrays are
  // rented on the main thread when a build is queued; neighbour snapshots are
  // returned on the build's worker thread when it completes, and the canonical
  // clone's predecessor is returned on the main thread when it is replaced.
  internal static class VoxelArrayPool
  {
    // Caps retained memory at MaxRetainedArrays * ~64 KB. Rent/return is balanced
    // by the number of in-flight builds, so this is only a defensive ceiling.
    private const int MaxRetainedArrays = 64;

    private static readonly ConcurrentBag<Voxel[]> pool = new();
    private static int retainedCount;

    public static Voxel[] Rent()
    {
      if (pool.TryTake(out Voxel[] array))
      {
        Interlocked.Decrement(ref retainedCount);
        return array;
      }

      return new Voxel[VoxelConstants.ChunkVolume];
    }

    public static void Return(Voxel[] array)
    {
      if (array == null || array.Length != VoxelConstants.ChunkVolume)
      {
        return;
      }

      if (Interlocked.Increment(ref retainedCount) > MaxRetainedArrays)
      {
        Interlocked.Decrement(ref retainedCount);
        return;
      }

      pool.Add(array);
    }
  }
}
