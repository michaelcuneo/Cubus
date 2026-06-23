using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CubusCore.Tests.PlayMode
{
  /// <summary>
  /// PlayMode integration coverage for the scene-bound validators that require a
  /// live streaming world (roadmap items 4, 6, 8, 9 touch points). The suite boots
  /// the real SampleScene, waits for terrain to generate and streaming to settle,
  /// then drives each validator the same way the in-editor inspector buttons do.
  /// </summary>
  [TestFixture]
  public sealed class WorldStreamingPlayModeTests
  {
    private const string SceneName = "SampleScene";
    private const float ReadyTimeoutSeconds = 30.0f;
    private const float SettleTimeoutSeconds = 30.0f;
    private const float PerfTimeoutSeconds = 15.0f;

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private WorldStreamer streamer;

    [UnitySetUp]
    public IEnumerator LoadSceneAndSettle()
    {
      yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
      yield return null;

      world = Object.FindFirstObjectByType<CubusWorld>();
      worldRenderer = Object.FindFirstObjectByType<WorldRenderer>();
      streamer = Object.FindFirstObjectByType<WorldStreamer>();

      Assert.IsNotNull(world, $"{SceneName} contains no CubusWorld.");
      Assert.IsNotNull(worldRenderer, $"{SceneName} contains no WorldRenderer.");

      // Wait for the world to report initial terrain readiness.
      float elapsed = 0.0f;
      while (!world.IsInitialTerrainReady && elapsed < ReadyTimeoutSeconds)
      {
        elapsed += Time.unscaledDeltaTime;
        yield return null;
      }

      Assert.IsTrue(
        world.IsInitialTerrainReady,
        $"World did not become ready within {ReadyTimeoutSeconds}s (streaming/generation regression?).");

      // Wait for streaming to fully settle: rendered chunks present, all queues
      // drained, and no async load/build tasks in flight. Require the settled state
      // to hold for several consecutive frames so the streamer's end-of-settle render
      // reconciliation pass can flush any late re-queued chunks before we validate.
      elapsed = 0.0f;
      int consecutiveSettledFrames = 0;
      const int requiredSettledFrames = 5;
      while (elapsed < SettleTimeoutSeconds && consecutiveSettledFrames < requiredSettledFrames)
      {
        consecutiveSettledFrames = StreamingSettled() ? consecutiveSettledFrames + 1 : 0;
        elapsed += Time.unscaledDeltaTime;
        yield return null;
      }

      Assert.Greater(
        worldRenderer.ActiveChunkViews.Count,
        0,
        $"No chunks rendered within {SettleTimeoutSeconds}s; streaming did not produce terrain.");
    }

    [TearDown]
    public void ClearAddedValidators()
    {
      if (world == null)
      {
        return;
      }

      DestroyIfPresent<WorldStreamingCoverageValidator>();
      DestroyIfPresent<WorldRenderSpatialValidator>();
      DestroyIfPresent<WorldSpawnCameraValidator>();
      DestroyIfPresent<WorldStreamingPerformanceValidator>();
    }

    [UnityTest]
    public IEnumerator StreamingCoverage_Passes_AfterSettle()
    {
      var validator = world.gameObject.AddComponent<WorldStreamingCoverageValidator>();
      SetPrivateObject(validator, "sourceWorld", world);
      SetPrivateObject(validator, "sourceRenderer", worldRenderer);
      SetPrivateObject(validator, "sourceStreamer", streamer);
      yield return null;

      bool passed = validator.Validate(out string message);
      Assert.IsTrue(passed, message);
    }

    [UnityTest]
    public IEnumerator RenderSpatial_Passes_AfterSettle()
    {
      var validator = world.gameObject.AddComponent<WorldRenderSpatialValidator>();
      SetPrivateObject(validator, "sourceWorld", world);
      SetPrivateObject(validator, "sourceRenderer", worldRenderer);
      yield return null;

      bool passed = validator.Validate(out string message);
      Assert.IsTrue(passed, message);
    }

    [UnityTest]
    public IEnumerator SpawnCamera_Passes_AfterSettle()
    {
      var validator = world.gameObject.AddComponent<WorldSpawnCameraValidator>();
      SetPrivateObject(validator, "sourceWorld", world);
      SetPrivateObject(validator, "sourceRenderer", worldRenderer);
      // Allow a frame so the validator can auto-resolve Camera.main / player root.
      yield return null;

      bool passed = validator.Validate(out string message);
      Assert.IsTrue(passed, message);
    }

    [UnityTest]
    public IEnumerator StreamingPerformance_Passes_OnSettledWorld()
    {
      var validator = world.gameObject.AddComponent<WorldStreamingPerformanceValidator>();
      SetPrivateObject(validator, "sourceStreamer", streamer);

      // Keep the sampling window short for a test run; defaults are tuned for
      // interactive 8s sweeps.
      SetPrivateFloat(validator, "sampleDurationSeconds", 1.5f);
      SetPrivateFloat(validator, "sampleIntervalSeconds", 0.1f);
      yield return null;

      string before = validator.LastValidationMessage;
      validator.StartSampling();

      float elapsed = 0.0f;
      while (validator.LastValidationMessage == before && elapsed < PerfTimeoutSeconds)
      {
        elapsed += Time.unscaledDeltaTime;
        yield return null;
      }

      Assert.AreNotEqual(before, validator.LastValidationMessage, "Performance sampling did not complete.");
      Assert.IsTrue(validator.LastValidationPassed, validator.LastValidationMessage);
    }

    [UnityTest]
    public IEnumerator ActiveChunkViews_HaveRenderableMeshes_AfterSettle()
    {
      // Roadmap item 6 - mesh lifecycle: every streamed-in view must carry a real
      // (non-null, non-empty) mesh, and the world must contain renderable geometry.
      yield return null;

      long totalVertices = 0;
      foreach (KeyValuePair<Vector3Int, ChunkView> entry in worldRenderer.ActiveChunkViews)
      {
        ChunkView view = entry.Value;
        Assert.IsNotNull(view, $"Null ChunkView registered for {entry.Key}.");
        Assert.IsNotNull(view.MeshRenderer, $"ChunkView {entry.Key} has no MeshRenderer.");

        var filter = view.GetComponent<MeshFilter>();
        Assert.IsNotNull(filter, $"ChunkView {entry.Key} has no MeshFilter.");
        Assert.IsNotNull(filter.sharedMesh, $"ChunkView {entry.Key} has a null shared mesh.");
        totalVertices += filter.sharedMesh.vertexCount;
      }

      Assert.Greater(totalVertices, 0, "Streamed world contains no mesh vertices.");
    }

    [UnityTest]
    public IEnumerator RemoveChunk_ClearsViewAndReleasesGeometry()
    {
      // Roadmap item 6/7 - chunk teardown: removing a chunk must drop it from the
      // active set and release the view. The renderer may either destroy the
      // GameObject or, when a chunk pool is active, deactivate and return it to the
      // pool. Both outcomes are valid (no leaked, still-active renderer/collider).
      Vector3Int target = FirstActiveChunkCoord();
      ChunkView view = worldRenderer.ActiveChunkViews[target];
      GameObject viewObject = view.gameObject;

      bool removed = worldRenderer.RemoveChunk(target);

      Assert.IsTrue(removed, $"RemoveChunk returned false for active chunk {target}.");
      Assert.IsFalse(worldRenderer.HasChunkView(target), $"Chunk {target} still tracked after removal.");
      Assert.IsFalse(worldRenderer.ActiveChunkViews.ContainsKey(target), $"Chunk {target} still in active set.");

      // Release is deferred to end-of-frame.
      yield return null;
      bool releasedOrPooled = viewObject == null || !viewObject.activeInHierarchy;
      Assert.IsTrue(
        releasedOrPooled,
        $"ChunkView GameObject for {target} was neither destroyed nor pooled (still active).");
    }

    private Vector3Int FirstActiveChunkCoord()
    {
      foreach (KeyValuePair<Vector3Int, ChunkView> entry in worldRenderer.ActiveChunkViews)
      {
        return entry.Key;
      }

      throw new AssertionException("No active chunk views to remove.");
    }

    private bool StreamingSettled()
    {
      if (worldRenderer == null || worldRenderer.ActiveChunkViews.Count == 0)
      {
        return false;
      }

      if (streamer == null)
      {
        return true;
      }

      // Fully settled means both pending queues are empty AND no async load/build
      // task is still in flight. Sampling while a load/build is outstanding races the
      // validator against chunks that are about to gain data or a view.
      return streamer.PendingLoadCount == 0 &&
             streamer.PendingRenderCount == 0 &&
             streamer.PendingLoadSetCount == 0 &&
             streamer.PendingRenderSetCount == 0 &&
             streamer.ActiveChunkLoadTaskCount == 0 &&
             streamer.ActiveBlockBuildTaskCount == 0 &&
             streamer.ActiveDensityBuildTaskCount == 0;
    }

    private void DestroyIfPresent<T>() where T : Component
    {
      if (world.TryGetComponent(out T component))
      {
        Object.Destroy(component);
      }
    }

    private static void SetPrivateFloat(object target, string fieldName, float value)
    {
      FieldInfo field = target.GetType().GetField(
        fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);

      if (field != null && field.FieldType == typeof(float))
      {
        field.SetValue(target, value);
      }
    }

    private static void SetPrivateObject(object target, string fieldName, Object value)
    {
      FieldInfo field = target.GetType().GetField(
        fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);

      if (field != null && field.FieldType.IsInstanceOfType(value))
      {
        field.SetValue(target, value);
      }
    }
  }
}
