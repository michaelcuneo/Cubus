using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeVerticalChunksAroundSurface = 1;
    private const int MinRuntimeChunksGeneratedPerFrame = 16;
    private const int MinRuntimeInitialChunksGeneratedPerFrame = 64;
    private const int MinRuntimeChunksRenderedPerFrame = 32;
    private const int MinRuntimeMeshAppliesPerFrame = 8;
    private const int MinRuntimeAsyncChunkTasks = 12;
    private const int MaxRuntimeChunksGeneratedPerFrame = 32;
    private const int MaxRuntimeInitialChunksGeneratedPerFrame = 64;
    private const int MaxRuntimeChunksRenderedPerFrame = 64;
    private const int MaxRuntimeMeshAppliesPerFrame = 16;
    private const int MaxRuntimeAsyncChunkTasks = 16;

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
    public int ChunksGeneratedPerFrame = MinRuntimeChunksGeneratedPerFrame;

    [Min(1)]
    public int InitialChunksGeneratedPerFrame = MinRuntimeInitialChunksGeneratedPerFrame;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = MinRuntimeChunksRenderedPerFrame;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = MinRuntimeMeshAppliesPerFrame;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(1)]
    public int MaxAsyncChunkTasks = MinRuntimeAsyncChunkTasks;

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
      ChunksGeneratedPerFrame = Mathf.Clamp(Mathf.Max(ChunksGeneratedPerFrame, MinRuntimeChunksGeneratedPerFrame), 1, MaxRuntimeChunksGeneratedPerFrame);
      InitialChunksGeneratedPerFrame = Mathf.Clamp(Mathf.Max(InitialChunksGeneratedPerFrame, MinRuntimeInitialChunksGeneratedPerFrame), 1, MaxRuntimeInitialChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(Mathf.Max(ChunksRenderedPerFrame, MinRuntimeChunksRenderedPerFrame), 1, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(Mathf.Max(MeshAppliesPerFrame, MinRuntimeMeshAppliesPerFrame), 1, MaxRuntimeMeshAppliesPerFrame);
      MaxAsyncChunkTasks = Mathf.Clamp(Mathf.Max(MaxAsyncChunkTasks, MinRuntimeAsyncChunkTasks), 1, MaxRuntimeAsyncChunkTasks);
    }
  }
}
