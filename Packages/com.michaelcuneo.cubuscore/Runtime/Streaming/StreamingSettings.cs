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
    [Tooltip("Developer hint only. WorldStreamer clamps this to a safe minimum.")]
    [Min(0)]
    public int ChunksBelowSurface = 8;

    [Tooltip("Developer hint only. WorldStreamer clamps this to a safe minimum.")]
    [Min(0)]
    public int ChunksAboveSurface = 8;

    [Header("Budgets")]
    [Min(4)]
    public int ChunksGeneratedPerFrame = 8;

    [Min(8)]
    public int InitialChunksGeneratedPerFrame = 32;

    [Min(4)]
    public int ChunksRenderedPerFrame = 16;

    [Min(4)]
    public int MeshAppliesPerFrame = 16;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(2)]
    public int MaxAsyncChunkTasks = 8;
  }
}