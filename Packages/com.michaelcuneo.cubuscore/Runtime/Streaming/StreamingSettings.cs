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
    private const int MaxRuntimeMeshAppliesPerFrame = 32;
    private const int MaxRuntimeMeshApplyTimeBudgetMs = 12;
    private const int MaxRuntimeAsyncChunkTasks = 32;

    // Minimum throughput floors. These guarantee that even a conservatively
    // serialized scene component streams fast enough to draw nearby chunks
    // promptly instead of leaving holes right in front of the viewer.
    private const int MinRuntimeChunksGeneratedPerFrame = 8;
    private const int MinRuntimeChunksRenderedPerFrame = 16;
    private const int MinRuntimeMeshAppliesPerFrame = 6;
    private const int MinRuntimeMeshApplyTimeBudgetMs = 4;
    // Floor the worker-task budget high enough that streaming uses most of the
    // machine. WorldStreamer clamps this to (cores - 1), so on <=13-core CPUs the
    // effective total becomes the full hardware concurrency; this only ever
    // raises a conservatively-serialized component, never exceeds the cores.
    private const int MinRuntimeAsyncChunkTasks = 12;

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
    public int ChunksGeneratedPerFrame = 8;

    [Min(1)]
    public int InitialChunksGeneratedPerFrame = 16;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = 16;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 8;

    [Tooltip("Maximum time budget in milliseconds spent applying completed mesh builds per frame.")]
    [Min(1)]
    public int MeshApplyTimeBudgetMs = 4;

    [Header("Async")]
    public bool UseAsyncGeneration = true;

    [Min(1)]
    public int MaxAsyncChunkTasks = 16;

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
      ChunksGeneratedPerFrame = Mathf.Clamp(ChunksGeneratedPerFrame, MinRuntimeChunksGeneratedPerFrame, MaxRuntimeChunksGeneratedPerFrame);
      InitialChunksGeneratedPerFrame = Mathf.Clamp(InitialChunksGeneratedPerFrame, 1, MaxRuntimeInitialChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(ChunksRenderedPerFrame, MinRuntimeChunksRenderedPerFrame, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(MeshAppliesPerFrame, MinRuntimeMeshAppliesPerFrame, MaxRuntimeMeshAppliesPerFrame);
      MeshApplyTimeBudgetMs = Mathf.Clamp(MeshApplyTimeBudgetMs, MinRuntimeMeshApplyTimeBudgetMs, MaxRuntimeMeshApplyTimeBudgetMs);
      MaxAsyncChunkTasks = Mathf.Clamp(MaxAsyncChunkTasks, MinRuntimeAsyncChunkTasks, MaxRuntimeAsyncChunkTasks);
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
