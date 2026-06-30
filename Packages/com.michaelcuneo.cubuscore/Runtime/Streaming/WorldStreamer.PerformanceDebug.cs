using System.Text;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    [Header("Streaming Debug")]
    [SerializeField] private bool logStreamingPerformance = true;
    [SerializeField] private bool drawStreamingPerformanceOverlay = true;
    [SerializeField][Min(0.1f)] private float streamingPerformanceLogIntervalSeconds = 1.0f;

    private readonly StringBuilder streamingPerformanceBuilder = new(1024);

    private float nextStreamingPerformanceLogTime;
    private float lastStreamingPerformanceLogTime;
    private int streamingPerformanceFrames;

    private long lastPerfLoadRequestsStarted;
    private long lastPerfLoadsCompleted;
    private long lastPerfLoadFailures;
    private long lastPerfBlockMeshApplies;
    private long lastPerfDensityMeshApplies;
    private long lastPerfChunkUnloads;

    private float perfSetMs;
    private float perfVisibilityMs;
    private float perfPrioritizeMs;
    private float perfCompletedLoadsMs;
    private float perfRenderQueueMs;
    private float perfLoadQueueMs;
    private float perfUnloadMs;
    private float perfMeshApplyMs;
    private float perfReconcileMs;
    private float perfUpdateTotalMs;

    private string lastStreamingPerformanceLine;

    private void ResetStreamingPhaseTimings()
    {
      perfSetMs = 0.0f;
      perfVisibilityMs = 0.0f;
      perfPrioritizeMs = 0.0f;
      perfCompletedLoadsMs = 0.0f;
      perfRenderQueueMs = 0.0f;
      perfLoadQueueMs = 0.0f;
      perfUnloadMs = 0.0f;
      perfMeshApplyMs = 0.0f;
      perfReconcileMs = 0.0f;
      perfUpdateTotalMs = 0.0f;
    }

    private static float NowMs()
    {
      return Time.realtimeSinceStartup * 1000.0f;
    }

    private void AccumulateStreamingPhaseTimings(
      float setMs,
      float visibilityMs,
      float prioritizeMs,
      float completedLoadsMs,
      float renderQueueMs,
      float loadQueueMs,
      float unloadMs,
      float meshApplyMs,
      float reconcileMs,
      float totalMs)
    {
      perfSetMs += setMs;
      perfVisibilityMs += visibilityMs;
      perfPrioritizeMs += prioritizeMs;
      perfCompletedLoadsMs += completedLoadsMs;
      perfRenderQueueMs += renderQueueMs;
      perfLoadQueueMs += loadQueueMs;
      perfUnloadMs += unloadMs;
      perfMeshApplyMs += meshApplyMs;
      perfReconcileMs += reconcileMs;
      perfUpdateTotalMs += totalMs;
    }

    private void LateUpdate()
    {
      if (!logStreamingPerformance && !drawStreamingPerformanceOverlay)
      {
        return;
      }

      streamingPerformanceFrames++;
      float now = Time.unscaledTime;
      float interval = Mathf.Max(0.1f, streamingPerformanceLogIntervalSeconds);

      if (now < nextStreamingPerformanceLogTime)
      {
        return;
      }

      float elapsed = Mathf.Max(0.0001f, now - lastStreamingPerformanceLogTime);
      if (lastStreamingPerformanceLogTime <= 0.0f)
      {
        elapsed = interval;
      }

      long loadStartedDelta = totalChunkLoadRequestsStarted - lastPerfLoadRequestsStarted;
      long loadsCompletedDelta = totalChunkLoadsCompleted - lastPerfLoadsCompleted;
      long loadFailuresDelta = totalChunkLoadFailures - lastPerfLoadFailures;
      long blockAppliesDelta = totalBlockMeshApplies - lastPerfBlockMeshApplies;
      long densityAppliesDelta = totalDensityMeshApplies - lastPerfDensityMeshApplies;
      long unloadsDelta = totalChunkUnloadsApplied - lastPerfChunkUnloads;

      lastPerfLoadRequestsStarted = totalChunkLoadRequestsStarted;
      lastPerfLoadsCompleted = totalChunkLoadsCompleted;
      lastPerfLoadFailures = totalChunkLoadFailures;
      lastPerfBlockMeshApplies = totalBlockMeshApplies;
      lastPerfDensityMeshApplies = totalDensityMeshApplies;
      lastPerfChunkUnloads = totalChunkUnloadsApplied;

      float fps = streamingPerformanceFrames / elapsed;
      float invFrames = streamingPerformanceFrames > 0 ? 1.0f / streamingPerformanceFrames : 0.0f;
      streamingPerformanceFrames = 0;
      lastStreamingPerformanceLogTime = now;
      nextStreamingPerformanceLogTime = now + interval;

      int renderedBlock = CountRenderedBlockChunks();
      int renderedDensity = CountRenderedDensityChunks();

      streamingPerformanceBuilder.Clear();
      streamingPerformanceBuilder
        .Append("[CubusStream] ")
        .Append("fps=").Append(fps.ToString("0.0"))
        .Append(" updMs=").Append((perfUpdateTotalMs * invFrames).ToString("0.00"))
        .Append(" set=").Append((perfSetMs * invFrames).ToString("0.00"))
        .Append(" vis=").Append((perfVisibilityMs * invFrames).ToString("0.00"))
        .Append(" pri=").Append((perfPrioritizeMs * invFrames).ToString("0.00"))
        .Append(" doneL=").Append((perfCompletedLoadsMs * invFrames).ToString("0.00"))
        .Append(" renderQ=").Append((perfRenderQueueMs * invFrames).ToString("0.00"))
        .Append(" loadQ=").Append((perfLoadQueueMs * invFrames).ToString("0.00"))
        .Append(" apply=").Append((perfMeshApplyMs * invFrames).ToString("0.00"))
        .Append(" desired=").Append(DesiredChunkCount)
        .Append(" keep=").Append(KeepChunkCount)
        .Append(" renderedB=").Append(renderedBlock)
        .Append(" renderedD=").Append(renderedDensity)
        .Append(" pendingL=").Append(PendingLoadCount)
        .Append(" pendingB=").Append(PendingBlockRenderCount)
        .Append(" pendingD=").Append(PendingDensityRenderCount)
        .Append(" activeL=").Append(ActiveChunkLoadTaskCount)
        .Append(" activeB=").Append(ActiveBlockBuildTaskCount)
        .Append(" activeD=").Append(ActiveDensityBuildTaskCount)
        .Append(" loads/s=").Append((loadStartedDelta / elapsed).ToString("0.0"))
        .Append(" loadDone/s=").Append((loadsCompletedDelta / elapsed).ToString("0.0"))
        .Append(" fail/s=").Append((loadFailuresDelta / elapsed).ToString("0.0"))
        .Append(" blockMesh/s=").Append((blockAppliesDelta / elapsed).ToString("0.0"))
        .Append(" densityMesh/s=").Append((densityAppliesDelta / elapsed).ToString("0.0"))
        .Append(" unload/s=").Append((unloadsDelta / elapsed).ToString("0.0"))
        .Append(" emptyB=").Append(KnownEmptyChunkCount)
        .Append(" emptyD=").Append(knownEmptyDensityChunks.Count)
        .Append(" throttle=").Append(adaptiveThrottleFramesRemaining)
        .Append(" init=").Append(IsInitialStreamingStageActive);

      lastStreamingPerformanceLine = streamingPerformanceBuilder.ToString();
      ResetStreamingPhaseTimings();

      if (logStreamingPerformance)
      {
        Debug.Log(lastStreamingPerformanceLine);
      }
    }

    private int CountRenderedBlockChunks()
    {
      if (!TryGetLayerViews(ActiveBlockChunkViewsField, out var views) || views == null)
      {
        return 0;
      }

      return views.Count;
    }

    private int CountRenderedDensityChunks()
    {
      if (!TryGetLayerViews(ActiveDensityChunkViewsField, out var views) || views == null)
      {
        return 0;
      }

      return views.Count;
    }

    private void OnGUI()
    {
      if (!drawStreamingPerformanceOverlay || string.IsNullOrWhiteSpace(lastStreamingPerformanceLine))
      {
        return;
      }

      GUI.Box(new Rect(8, 8, Mathf.Min(Screen.width - 16, 1320), 72), lastStreamingPerformanceLine);
    }
  }
}
