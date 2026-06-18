using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeVerticalChunksAroundSurface = 1;
    private const int MaxRuntimeChunksGeneratedPerFrame = 4;
    private const int MaxRuntimeChunksRenderedPerFrame = 8;
    private const int MaxRuntimeMeshAppliesPerFrame = 4;
    private const int MaxRuntimeAsyncChunkTasks = 2;

    [Header("Horizontal")]
    [Min(1)]
    public int UnloadPaddingInChunks = 3;

    [Header("Vertical Around Surface")]
    [Tooltip("Number of chunks below the sampled surface kept in the desired streaming set.")]
    [Min(0)]
    public int ChunksBelowSurface = 1;

    [Tooltip("Number of chunks above the sampled surface kept in the desired streaming set.")]
    [Min(0)]
    public int ChunksAboveSurface = 1;

    [Header("Budgets")]
    [Min(1)]
    public int ChunksGeneratedPerFrame = 4;

    [Min(1)]
    public int InitialChunksGeneratedPerFrame = 8;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = 8;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 4;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(1)]
    public int MaxAsyncChunkTasks = 2;

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
      NormalizeRuntimeBudgets();
    }

    public void NormalizeRuntimeBudgets()
    {
      UnloadPaddingInChunks = Mathf.Max(1, UnloadPaddingInChunks);
      ChunksBelowSurface = Mathf.Clamp(ChunksBelowSurface, 0, MaxRuntimeVerticalChunksAroundSurface);
      ChunksAboveSurface = Mathf.Clamp(ChunksAboveSurface, 0, MaxRuntimeVerticalChunksAroundSurface);
      ChunksGeneratedPerFrame = Mathf.Clamp(ChunksGeneratedPerFrame, 1, MaxRuntimeChunksGeneratedPerFrame);
      InitialChunksGeneratedPerFrame = Mathf.Clamp(InitialChunksGeneratedPerFrame, 1, MaxRuntimeChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(ChunksRenderedPerFrame, 1, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(MeshAppliesPerFrame, 1, MaxRuntimeMeshAppliesPerFrame);
      MaxAsyncChunkTasks = Mathf.Clamp(MaxAsyncChunkTasks, 1, MaxRuntimeAsyncChunkTasks);
    }
  }
}
