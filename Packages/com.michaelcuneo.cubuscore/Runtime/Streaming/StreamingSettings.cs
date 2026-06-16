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
    public int ChunksGeneratedPerFrame = 8;

    [Min(8)]
    public int InitialChunksGeneratedPerFrame = 32;

    [Tooltip("For smooth-density this is the number of synchronous density meshes allowed per frame. Keep high enough to reveal terrain quickly, low enough to avoid huge frame spikes.")]
    [Min(4)]
    public int ChunksRenderedPerFrame = 32;

    [Min(4)]
    public int MeshAppliesPerFrame = 32;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(2)]
    public int MaxAsyncChunkTasks = 8;
  }
}
