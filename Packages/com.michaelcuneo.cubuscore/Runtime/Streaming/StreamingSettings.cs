using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeVerticalChunksAroundSurface = 1;
    private const int MaxRuntimeChunksGeneratedPerFrame = 32;
    private const int MaxRuntimeInitialChunksGeneratedPerFrame = 64;
    private const int MaxRuntimeChunksRenderedPerFrame = 64;
    private const int MaxRuntimeMeshAppliesPerFrame = 16;
    private const int MaxRuntimeMeshApplyTimeBudgetMs = 12;
    private const int MaxRuntimeAsyncChunkTasks = 16;

    public const int PrePr30BlockChunksGeneratedPerFrame = 16;
    public const int PrePr30BlockInitialChunksGeneratedPerFrame = 64;
    public const int PrePr30BlockChunksRenderedPerFrame = 32;
    public const int PrePr30BlockMeshAppliesPerFrame = 8;
    public const int PrePr30BlockMeshApplyTimeBudgetMs = 1;
    public const int PrePr30BlockMaxAsyncChunkTasks = 8;

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
    public int InitialChunksGeneratedPerFrame = 16;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = 8;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 2;

    [Tooltip("Maximum time budget in milliseconds spent applying completed mesh builds per frame.")]
    [Min(1)]
    public int MeshApplyTimeBudgetMs = 2;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(1)]
    public int MaxAsyncChunkTasks = 4;

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
      InitialChunksGeneratedPerFrame = Mathf.Clamp(InitialChunksGeneratedPerFrame, 1, MaxRuntimeInitialChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(ChunksRenderedPerFrame, 1, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(MeshAppliesPerFrame, 1, MaxRuntimeMeshAppliesPerFrame);
      MeshApplyTimeBudgetMs = Mathf.Clamp(MeshApplyTimeBudgetMs, 1, MaxRuntimeMeshApplyTimeBudgetMs);
      MaxAsyncChunkTasks = Mathf.Clamp(MaxAsyncChunkTasks, 1, MaxRuntimeAsyncChunkTasks);
    }

    public void ApplyPrePr30BlockFastProfile()
    {
      UseAsyncGeneration = true;
      ChunksGeneratedPerFrame = PrePr30BlockChunksGeneratedPerFrame;
      InitialChunksGeneratedPerFrame = PrePr30BlockInitialChunksGeneratedPerFrame;
      ChunksRenderedPerFrame = PrePr30BlockChunksRenderedPerFrame;
      MeshAppliesPerFrame = PrePr30BlockMeshAppliesPerFrame;
      MeshApplyTimeBudgetMs = PrePr30BlockMeshApplyTimeBudgetMs;
      MaxAsyncChunkTasks = PrePr30BlockMaxAsyncChunkTasks;
      NormalizeRuntimeBudgets();
    }
  }
}
