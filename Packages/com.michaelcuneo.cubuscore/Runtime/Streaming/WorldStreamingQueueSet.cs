using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  /// <summary>
  /// Owns the queue + membership set pair used by the runtime streamer.
  ///
  /// WorldStreamer currently keeps several of these pairs inline. This type is the
  /// first small extraction step: it keeps queue semantics in one place without
  /// changing the existing streaming behaviour yet. The next refactor can replace
  /// pendingLoadQueue/pendingLoadSet, pendingRenderQueue/pendingRenderSet and
  /// pendingUnloadQueue/pendingUnloadSet with instances of this class.
  /// </summary>
  internal sealed class WorldStreamingQueueSet
  {
    private readonly Queue<Vector3Int> queue = new();
    private readonly HashSet<Vector3Int> set = new();

    public int Count => queue.Count;
    public int SetCount => set.Count;
    public IReadOnlyCollection<Vector3Int> Items => set;

    public bool Contains(Vector3Int chunkCoord)
    {
      return set.Contains(chunkCoord);
    }

    public bool Enqueue(Vector3Int chunkCoord)
    {
      if (!set.Add(chunkCoord))
      {
        return false;
      }

      queue.Enqueue(chunkCoord);
      return true;
    }

    public bool TryDequeue(out Vector3Int chunkCoord)
    {
      while (queue.Count > 0)
      {
        chunkCoord = queue.Dequeue();

        if (set.Remove(chunkCoord))
        {
          return true;
        }
      }

      chunkCoord = default;
      return false;
    }

    public void Remove(Vector3Int chunkCoord)
    {
      set.Remove(chunkCoord);
    }

    public void Clear()
    {
      queue.Clear();
      set.Clear();
    }

    public void RebuildPrioritized(
        List<Vector3Int> scratch,
        ISet<Vector3Int> desiredChunks,
        ISet<Vector3Int> keepChunks,
        bool allowKeepOnlyChunks,
        Comparison<Vector3Int> comparison)
    {
      if (queue.Count < 2 || scratch == null || comparison == null)
      {
        return;
      }

      scratch.Clear();

      while (queue.Count > 0)
      {
        Vector3Int chunkCoord = queue.Dequeue();
        if (!set.Contains(chunkCoord)) continue;
        if (!desiredChunks.Contains(chunkCoord) && !(allowKeepOnlyChunks && keepChunks.Contains(chunkCoord))) continue;
        scratch.Add(chunkCoord);
      }

      scratch.Sort(comparison);

      for (int i = 0; i < scratch.Count; i++)
      {
        queue.Enqueue(scratch[i]);
      }
    }
  }
}
