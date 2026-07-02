using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  [Serializable]
  public sealed class StreamingSettings : ISerializationCallbackReceiver
  {
    private const int MaxRuntimeRadiusInChunks = 256;
    private const int MaxRuntimeUnloadPaddingInChunks = 4;
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

    [Header("Radii")]
    [Tooltip("Chunks kept in memory around the viewer. 0 = ViewDistanceInChunks + UnloadPaddingInChunks.")]
    [Min(0)]
    public int LoadRadiusInChunks = 0;

    [Tooltip("Chunks eligible for mesh generation around the viewer. 0 = ViewDistanceInChunks.")]
    [Min(0)]
    public int BuildRadiusInChunks = 0;

    [Tooltip("Maximum horizontal radius that can be drawn. 0 = BuildRadiusInChunks/ViewDistanceInChunks. Frustum culling still applies inside this radius.")]
    [Min(0)]
    public int RenderRadiusInChunks = 0;

    [Header("Horizontal")]
    [Min(1)]
    public int UnloadPaddingInChunks = 3;

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
      LoadRadiusInChunks = Mathf.Clamp(LoadRadiusInChunks, 0, MaxRuntimeRadiusInChunks);
      BuildRadiusInChunks = Mathf.Clamp(BuildRadiusInChunks, 0, MaxRuntimeRadiusInChunks);
      RenderRadiusInChunks = Mathf.Clamp(RenderRadiusInChunks, 0, MaxRuntimeRadiusInChunks);
      UnloadPaddingInChunks = Mathf.Clamp(UnloadPaddingInChunks, 1, MaxRuntimeUnloadPaddingInChunks);
      ChunksGeneratedPerFrame = Mathf.Clamp(ChunksGeneratedPerFrame, MinRuntimeChunksGeneratedPerFrame, MaxRuntimeChunksGeneratedPerFrame);
      InitialChunksGeneratedPerFrame = Mathf.Clamp(InitialChunksGeneratedPerFrame, 1, MaxRuntimeInitialChunksGeneratedPerFrame);
      ChunksRenderedPerFrame = Mathf.Clamp(ChunksRenderedPerFrame, MinRuntimeChunksRenderedPerFrame, MaxRuntimeChunksRenderedPerFrame);
      MeshAppliesPerFrame = Mathf.Clamp(MeshAppliesPerFrame, MinRuntimeMeshAppliesPerFrame, MaxRuntimeMeshAppliesPerFrame);
      MeshApplyTimeBudgetMs = Mathf.Clamp(MeshApplyTimeBudgetMs, MinRuntimeMeshApplyTimeBudgetMs, MaxRuntimeMeshApplyTimeBudgetMs);
      MaxAsyncChunkTasks = Mathf.Clamp(MaxAsyncChunkTasks, MinRuntimeAsyncChunkTasks, MaxRuntimeAsyncChunkTasks);
    }
  }
}
