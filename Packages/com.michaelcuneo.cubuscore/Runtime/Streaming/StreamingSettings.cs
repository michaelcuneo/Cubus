using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings
  {
    [Header("Horizontal")]

    [Min(0)]
    public int UnloadPaddingInChunks = 1;

    [Header("Vertical Around Surface")]
    [Min(0)]
    public int ChunksBelowSurface = 1;

    [Min(0)]
    public int ChunksAboveSurface = 1;

    [Header("Budgets")]
    [Min(1)]
    public int ChunksGeneratedPerFrame = 2;

    [Tooltip("Higher generation budget used during the initial load burst before the first chunk is visible. Reverts to ChunksGeneratedPerFrame once initial terrain is ready.")]
    [Min(1)]
    public int InitialChunksGeneratedPerFrame = 4;

    [Min(1)]
    public int ChunksRenderedPerFrame = 1;

    [Tooltip("How many completed async mesh results are applied to the scene per frame. Keep low to avoid frame spikes.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 1;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(1)]
    public int MaxAsyncChunkTasks = 2;
  }
}