using System.Collections.Concurrent;
using System.Threading;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  // Thread-safe pool of full-chunk DensityVoxel[] arrays (ChunkVolume length)
  // used for the throwaway neighbour boundary snapshots created for async density
  // mesh builds. Each array is ~0.75 MB, so a single build previously allocated
  // up to seven of them (one per +X/+Y/+Z sample neighbour) as fresh clones,
  // which was the dominant streaming GC/main-thread cost in hybrid worlds.
  // Arrays are rented on the main thread when a density build is queued and
  // returned on the build's worker thread when it completes.
  internal static class DensityVoxelArrayPool
  {
    // Caps retained memory at MaxRetainedArrays * ~0.75 MB. Rent/return is
    // balanced by the number of in-flight builds, so this is only a defensive
    // ceiling.
    private const int MaxRetainedArrays = 64;

    private static readonly ConcurrentBag<DensityVoxel[]> pool = new();
    private static int retainedCount;

    public static DensityVoxel[] Rent()
    {
      if (pool.TryTake(out DensityVoxel[] array))
      {
        Interlocked.Decrement(ref retainedCount);
        return array;
      }

      return new DensityVoxel[VoxelConstants.ChunkVolume];
    }

    public static void Return(DensityVoxel[] array)
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
