using UnityEngine;
using System.Collections.Generic;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  public readonly struct BlockEditResult
  {
    public readonly int ChangedVoxelCount;
    public readonly HashSet<Vector3Int> DirtyChunks;

    public bool Changed => ChangedVoxelCount > 0;

    public BlockEditResult(int changedVoxelCount, HashSet<Vector3Int> dirtyChunks)
    {
      ChangedVoxelCount = changedVoxelCount;
      DirtyChunks = dirtyChunks;
    }
  }
}