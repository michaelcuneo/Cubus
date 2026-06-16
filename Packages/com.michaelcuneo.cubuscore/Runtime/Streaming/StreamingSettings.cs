using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings
  {
    [Header("Horizontal")]
    [Min(2)]
    public int UnloadPaddingInChunks = 4;

    [Header("Vertical Around Surface")]
    [Tooltip("Number of chunks below the sampled surface kept in the desired streaming set.")]
    [Min(0)]
    public int ChunksBelowSurface = 8;

    [Tooltip("Number of chunks above the sampled surface kept in the desired streaming set.")]
    [Min(0)]
    public int ChunksAboveSurface = 8;

    [Header("Budgets")]
    [Min(4)]
    public int ChunksGeneratedPerFrame = 16;

    [Min(8)]
    public int InitialChunksGeneratedPerFrame = 64;

    [Tooltip("For smooth-density this now limits async mesh jobs started per frame, not main-thread marching-cubes builds.")]
    [Min(4)]
    public int ChunksRenderedPerFrame = 32;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(4)]
    public int MeshAppliesPerFrame = 16;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(2)]
    public int MaxAsyncChunkTasks = 8;
  }
}
