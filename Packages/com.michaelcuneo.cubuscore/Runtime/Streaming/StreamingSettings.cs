using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeUnloadPaddingInChunks = 4;
    private const int MaxRuntimeChunksGeneratedPerFrame = 64;
    private const int MaxRuntimeInitialChunksGeneratedPerFrame = 128;
    private const int MaxRuntimeChunksRenderedPerFrame = 128;
    private const int MaxRuntimeMeshAppliesPerFrame = 64;
    private const int MaxRuntimeMeshApplyTimeBudgetMs = 16;
    private const int MaxRuntimeAsyncChunkTasks = 32;

    // Minimum throughput floors. These guarantee that even a conservatively
    // serialized scene component streams fast enough to draw nearby chunks
    // promptly instead of leaving holes right in front of the viewer.
    private const int MinRuntimeChunksGeneratedPerFrame = 16;
    private const int MinRuntimeChunksRenderedPerFrame = 32;
    private const int MinRuntimeMeshAppliesPerFrame = 12;
    private const int MinRuntimeMeshApplyTimeBudgetMs = 6;
    // Floor the worker-task budget high enough that streaming uses most of the
    // machine. WorldStreamer clamps this to (cores - 1), so the effective total
    // still never exceeds the available hardware concurrency.
    private const int MinRuntimeAsyncChunkTasks = 16;

    [Header("Horizontal")]
    [Min(1)]
    public int UnloadPaddingInChunks = 3;

    [Header("Budgets")]
    [Min(1)]
    public int ChunksGeneratedPerFrame = 32;

    [Min(1)]
    public int InitialChunksGeneratedPerFrame = 64;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = 64;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 32;

    [Tooltip("Maximum time budget in milliseconds spent applying completed mesh builds per frame.")]
    [Min(1)]
    public int MeshApplyTimeBudgetMs = 8;

    [Header("Async")]
    [Min(1)]
    public int MaxAsyncChunkTasks = 32;

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
      NormalizeRuntimeBudgets();
    }

    public void NormalizeRuntimeBudgets()
    {
      UnloadPaddingInChunks = Mathf.Clamp(UnloadPaddingInChunks, 1, MaxRuntimeUnloadPaddingInChunks);
      ChunksGeneratedPerFrame = Mathf.Clamp(ChunksGeneratedPerFrame, MinRuntimeChunksGeneratedPerFrame, MaxRuntimeChunksGeneratedPerFrame);
      InitialChunksGeneratedPerFrame = Mathf.Clamp(InitialChunksGeneratedPerFrame, MinRuntimeChunksGeneratedPerFrame, MaxRuntimeInitialChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(ChunksRenderedPerFrame, MinRuntimeChunksRenderedPerFrame, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(MeshAppliesPerFrame, MinRuntimeMeshAppliesPerFrame, MaxRuntimeMeshAppliesPerFrame);
      MeshApplyTimeBudgetMs = Mathf.Clamp(MeshApplyTimeBudgetMs, MinRuntimeMeshApplyTimeBudgetMs, MaxRuntimeMeshApplyTimeBudgetMs);
      MaxAsyncChunkTasks = Mathf.Clamp(MaxAsyncChunkTasks, MinRuntimeAsyncChunkTasks, MaxRuntimeAsyncChunkTasks);
    }
  }
}