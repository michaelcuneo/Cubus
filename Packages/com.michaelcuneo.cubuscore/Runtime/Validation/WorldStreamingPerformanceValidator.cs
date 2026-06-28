using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldStreamingPerformanceValidator : MonoBehaviour
  {
    [SerializeField] private WorldStreamer sourceStreamer;
    [SerializeField] private bool runOnStart;
    [SerializeField, Min(0.25f)] private float sampleDurationSeconds = 8.0f;
    [SerializeField, Min(0.02f)] private float sampleIntervalSeconds = 0.1f;
    [SerializeField, Min(0)] private int targetPendingLoad = 0;
    [SerializeField, Min(0)] private int targetPendingRender = 0;
    [SerializeField, Min(0)] private int toleratedFramesWithPendingRenderAndNoActiveDensityBuild = 10;
    [SerializeField, Min(0)] private int toleratedFramesWithPendingLoadAndNoActiveLoadTask = 10;

    private Coroutine samplingCoroutine;

    public bool LastValidationPassed { get; private set; }
    public string LastValidationMessage { get; private set; } = "Not run.";

    private void Start()
    {
      if (runOnStart)
      {
        StartSampling();
      }
    }

    [ContextMenu("Start Streaming Performance Sample")]
    public void StartSampling()
    {
      ResolveReferences();

      if (sourceStreamer == null)
      {
        LastValidationPassed = false;
        LastValidationMessage = "Streaming performance validation failed: no WorldStreamer was found.";
        Debug.LogWarning(LastValidationMessage, this);
        return;
      }

      if (samplingCoroutine != null)
      {
        StopCoroutine(samplingCoroutine);
      }

      samplingCoroutine = StartCoroutine(SampleRoutine());
    }

    private IEnumerator SampleRoutine()
    {
      float startedAt = Time.realtimeSinceStartup;
      float lastSampleAt = startedAt - sampleIntervalSeconds;

      int samples = 0;
      int peakPendingLoad = 0;
      int peakPendingRender = 0;
      int peakPendingLoadSet = 0;
      int peakPendingRenderSet = 0;
      int peakActiveChunkLoadTasks = 0;
      int peakActiveDensityBuildTasks = 0;
      int peakKnownEmpty = 0;
      int peakDesiredChunks = 0;
      int peakKeepChunks = 0;
      int framesWithPendingRenderAndNoActiveDensityBuild = 0;
      int framesWithPendingLoadAndNoActiveLoadTask = 0;
      int framesAbovePendingLoadTarget = 0;
      int framesAbovePendingRenderTarget = 0;
      float firstPendingLoadZeroAt = -1.0f;
      float firstPendingRenderZeroAt = -1.0f;
      float firstBothQueuesZeroAt = -1.0f;
      long pendingLoadSum = 0;
      long pendingRenderSum = 0;

      while (Time.realtimeSinceStartup - startedAt < sampleDurationSeconds)
      {
        float now = Time.realtimeSinceStartup;
        if (now - lastSampleAt >= sampleIntervalSeconds)
        {
          lastSampleAt = now;
          samples++;

          int pendingLoad = sourceStreamer.PendingLoadCount;
          int pendingRender = sourceStreamer.PendingRenderCount;
          int pendingLoadSet = sourceStreamer.PendingLoadSetCount;
          int pendingRenderSet = sourceStreamer.PendingRenderSetCount;
          int activeLoadTasks = sourceStreamer.ActiveChunkLoadTaskCount;
          int activeDensityTasks = sourceStreamer.ActiveDensityBuildTaskCount;

          peakPendingLoad = Mathf.Max(peakPendingLoad, pendingLoad);
          peakPendingRender = Mathf.Max(peakPendingRender, pendingRender);
          peakPendingLoadSet = Mathf.Max(peakPendingLoadSet, pendingLoadSet);
          peakPendingRenderSet = Mathf.Max(peakPendingRenderSet, pendingRenderSet);
          peakActiveChunkLoadTasks = Mathf.Max(peakActiveChunkLoadTasks, activeLoadTasks);
          peakActiveDensityBuildTasks = Mathf.Max(peakActiveDensityBuildTasks, activeDensityTasks);
          peakKnownEmpty = Mathf.Max(peakKnownEmpty, sourceStreamer.KnownEmptyChunkCount);
          peakDesiredChunks = Mathf.Max(peakDesiredChunks, sourceStreamer.DesiredChunkCount);
          peakKeepChunks = Mathf.Max(peakKeepChunks, sourceStreamer.KeepChunkCount);

          pendingLoadSum += pendingLoad;
          pendingRenderSum += pendingRender;

          if (pendingRender > 0 && activeDensityTasks == 0)
          {
            framesWithPendingRenderAndNoActiveDensityBuild++;
          }

          if (pendingLoad > 0 && activeLoadTasks == 0)
          {
            framesWithPendingLoadAndNoActiveLoadTask++;
          }

          if (pendingLoad > targetPendingLoad)
          {
            framesAbovePendingLoadTarget++;
          }

          if (pendingRender > targetPendingRender)
          {
            framesAbovePendingRenderTarget++;
          }

          if (firstPendingLoadZeroAt < 0.0f && pendingLoad == 0 && pendingLoadSet == 0)
          {
            firstPendingLoadZeroAt = now - startedAt;
          }

          if (firstPendingRenderZeroAt < 0.0f && pendingRender == 0 && pendingRenderSet == 0)
          {
            firstPendingRenderZeroAt = now - startedAt;
          }

          if (firstBothQueuesZeroAt < 0.0f && pendingLoad == 0 && pendingLoadSet == 0 && pendingRender == 0 && pendingRenderSet == 0)
          {
            firstBothQueuesZeroAt = now - startedAt;
          }
        }

        yield return null;
      }

      float averagePendingLoad = samples > 0 ? (float)pendingLoadSum / samples : 0.0f;
      float averagePendingRender = samples > 0 ? (float)pendingRenderSum / samples : 0.0f;

      bool passed = framesWithPendingRenderAndNoActiveDensityBuild <= toleratedFramesWithPendingRenderAndNoActiveDensityBuild &&
                    framesWithPendingLoadAndNoActiveLoadTask <= toleratedFramesWithPendingLoadAndNoActiveLoadTask;

      LastValidationPassed = passed;
      LastValidationMessage =
        $"Streaming performance report. " +
        $"Passed={passed}, " +
        $"Samples={samples}, " +
        $"Duration={sampleDurationSeconds:0.00}s, " +
        $"Interval={sampleIntervalSeconds:0.00}s, " +
        $"InitialStageActive={sourceStreamer.IsInitialStreamingStageActive}, " +
        $"LastViewerChunk={sourceStreamer.LastViewerChunkCoord}, " +
        $"PeakDesiredChunks={peakDesiredChunks}, " +
        $"PeakKeepChunks={peakKeepChunks}, " +
        $"PeakPendingLoad={peakPendingLoad}, " +
        $"PeakPendingRender={peakPendingRender}, " +
        $"PeakPendingLoadSet={peakPendingLoadSet}, " +
        $"PeakPendingRenderSet={peakPendingRenderSet}, " +
        $"AveragePendingLoad={averagePendingLoad:0.00}, " +
        $"AveragePendingRender={averagePendingRender:0.00}, " +
        $"PeakActiveChunkLoadTasks={peakActiveChunkLoadTasks}, " +
        $"PeakActiveDensityBuildTasks={peakActiveDensityBuildTasks}, " +
        $"FramesWithPendingRenderAndNoActiveDensityBuild={framesWithPendingRenderAndNoActiveDensityBuild}, " +
        $"FramesWithPendingLoadAndNoActiveLoadTask={framesWithPendingLoadAndNoActiveLoadTask}, " +
        $"FramesAbovePendingLoadTarget={framesAbovePendingLoadTarget}, " +
        $"FramesAbovePendingRenderTarget={framesAbovePendingRenderTarget}, " +
        $"FirstPendingLoadZeroAt={FormatTime(firstPendingLoadZeroAt)}, " +
        $"FirstPendingRenderZeroAt={FormatTime(firstPendingRenderZeroAt)}, " +
        $"FirstBothQueuesZeroAt={FormatTime(firstBothQueuesZeroAt)}, " +
        $"PeakKnownEmpty={peakKnownEmpty}";

      if (passed)
      {
        Debug.Log(LastValidationMessage, this);
      }
      else
      {
        Debug.LogWarning(LastValidationMessage, this);
      }

      samplingCoroutine = null;
    }

    private void ResolveReferences()
    {
      if (sourceStreamer == null)
      {
        sourceStreamer = GetComponent<WorldStreamer>();
      }
    }

    private static string FormatTime(float seconds)
    {
      return seconds < 0.0f ? "NotReached" : $"{seconds:0.00}s";
    }
  }
}
