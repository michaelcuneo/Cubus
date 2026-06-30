using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeUnloadPaddingInChunks = 4;
    private const int MaxRuntimeChunksGeneratedPerFrame = 128;
    private const int MaxRuntimeInitialChunksGeneratedPerFrame = 256;
    private const int MaxRuntimeChunksRenderedPerFrame = 192;
    private const int MaxRuntimeMeshAppliesPerFrame = 96;
    private const int MaxRuntimeMeshApplyTimeBudgetMs = 24;
    private const int MaxRuntimeAsyncChunkTasks = 64;

    // Aggressive throughput floors. The streamer still clamps async work to the
    // machine's hardware concurrency, but these defaults stop old serialized
    // scene values from quietly throttling the new fast path.
    private const int MinRuntimeChunksGeneratedPerFrame = 32;
    private const int MinRuntimeChunksRenderedPerFrame = 64;
    private const int MinRuntimeMeshAppliesPerFrame = 24;
    private const int MinRuntimeMeshApplyTimeBudgetMs = 8;
    private const int MinRuntimeAsyncChunkTasks = 24;

    [Header("Horizontal")]
    [Min(1)]
    public int UnloadPaddingInChunks = 3;

    [Header("Budgets")]
    [Min(1)]
    public int ChunksGeneratedPerFrame = 96;

    [Min(1)]
    public int InitialChunksGeneratedPerFrame = 192;

    [Tooltip("Limits how many chunk render/build operations are started per frame.")]
    [Min(1)]
    public int ChunksRenderedPerFrame = 128;

    [Tooltip("Maximum completed mesh data objects converted to Unity meshes per frame.")]
    [Min(1)]
    public int MeshAppliesPerFrame = 64;

    [Tooltip("Maximum time budget in milliseconds spent applying completed mesh builds per frame.")]
    [Min(1)]
    public int MeshApplyTimeBudgetMs = 12;

    [Header("Async")]
    [Min(1)]
    public int MaxAsyncChunkTasks = 64;

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