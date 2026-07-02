using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private const int AlwaysDrawNearViewerHorizontalChunks = 2;
    private const int AlwaysDrawNearViewerVerticalChunks = 1;
    private const int HighPriorityNearViewerHorizontalChunks = 3;
    private const int HighPriorityNearViewerVerticalChunks = 2;
    private const float VisibilityBoundsPaddingChunks = 0.75f;
    private const float VisibilityRefreshPositionEpsilon = 0.25f;
    private const float VisibilityRefreshDotThreshold = 0.9975f;
    private const int VisibilityRefreshMaxFrameInterval = 6;

    // How long a chunk keeps drawing after it leaves the camera view before it is
    // hidden. Prevents show/hide popping when the camera flicks across a chunk edge.
    private const float RenderCullGraceSeconds = 0.25f;

    private readonly Plane[] visibilityFrustumPlanes = new Plane[6];
    private Camera cachedVisibilityCamera;
    private Vector3 lastVisibilityCameraPosition;
    private Vector3 lastVisibilityCameraForward;
    private int lastVisibilityRefreshFrame = -9999;
    private bool hasLastVisibilityCameraState;
    private int frustumPlanesFrame = -1;
    private Camera frustumPlanesCamera;

    // Last time (unscaled) each rendered chunk was inside the view frustum, used to
    // apply the hide grace period. Pruned as chunk views are unloaded.
    private readonly Dictionary<Vector3Int, float> chunkLastInViewTime = new();
    private readonly List<Vector3Int> renderCullPruneBuffer = new();

    /// <summary>
    /// Minecraft-style rule: do not use the camera frustum as a hard mesh/load gate.
    /// Keep the world around the player generated/renderable, then use the camera only
    /// for priority and renderer visibility. Otherwise a fast camera turn exposes empty
    /// chunks that were never built because they were behind the player.
    /// </summary>
    private bool ShouldQueueMeshWorkForChunk(Vector3Int chunkCoord)
    {
      return world == null || world.Settings == null || world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord);
    }

    private bool ShouldStartChunkLoadForCurrentVisibility(Vector3Int chunkCoord)
    {
      return ShouldQueueMeshWorkForChunk(chunkCoord);
    }

    private bool IsChunkNearViewerForImmediateMesh(Vector3Int chunkCoord)
    {
      if (!hasLastViewerChunkCoord)
      {
        return true;
      }

      int dx = Mathf.Abs(chunkCoord.x - lastViewerChunkCoord.x);
      int dy = Mathf.Abs(chunkCoord.y - lastViewerChunkCoord.y);
      int dz = Mathf.Abs(chunkCoord.z - lastViewerChunkCoord.z);

      return dx <= HighPriorityNearViewerHorizontalChunks &&
             dz <= HighPriorityNearViewerHorizontalChunks &&
             dy <= HighPriorityNearViewerVerticalChunks;
    }

    private bool IsChunkNearViewerForAlwaysDraw(Vector3Int chunkCoord)
    {
      if (!hasLastViewerChunkCoord)
      {
        return true;
      }

      int dx = Mathf.Abs(chunkCoord.x - lastViewerChunkCoord.x);
      int dy = Mathf.Abs(chunkCoord.y - lastViewerChunkCoord.y);
      int dz = Mathf.Abs(chunkCoord.z - lastViewerChunkCoord.z);

      return dx <= AlwaysDrawNearViewerHorizontalChunks &&
             dz <= AlwaysDrawNearViewerHorizontalChunks &&
             dy <= AlwaysDrawNearViewerVerticalChunks;
    }

    private Bounds GetChunkWorldBounds(Vector3Int chunkCoord)
    {
      float voxelSize = world != null && world.Settings != null ? world.Settings.VoxelSize : 1.0f;
      float chunkWorldSize = VoxelConstants.ChunkSize * voxelSize;
      float padding = chunkWorldSize * VisibilityBoundsPaddingChunks;
      float paddedSize = chunkWorldSize + padding * 2.0f;

      Vector3 localMin = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize) - Vector3.one * padding;
      Vector3 localCenter = localMin + Vector3.one * (paddedSize * 0.5f);

      Transform worldTransform = world != null ? world.transform : transform;
      if (worldTransform == null)
      {
        return new Bounds(localCenter, Vector3.one * paddedSize);
      }

      Vector3 worldCenter = worldTransform.TransformPoint(localCenter);
      Vector3 scale = worldTransform.lossyScale;
      Vector3 worldSize = new(
        Mathf.Abs(scale.x) * paddedSize,
        Mathf.Abs(scale.y) * paddedSize,
        Mathf.Abs(scale.z) * paddedSize);

      return new Bounds(worldCenter, worldSize);
    }

    private Vector3 GetChunkWorldCenter(Vector3Int chunkCoord)
    {
      return GetChunkWorldBounds(chunkCoord).center;
    }

    private bool IsChunkInCameraFrustum(Vector3Int chunkCoord)
    {
      Camera camera = ResolveVisibilityCamera();
      return camera == null || GeometryUtility.TestPlanesAABB(visibilityFrustumPlanes, GetChunkWorldBounds(chunkCoord));
    }

    private Camera ResolveVisibilityCamera()
    {
      if (cachedVisibilityCamera != null && cachedVisibilityCamera.isActiveAndEnabled)
      {
        EnsureFrustumPlanes(cachedVisibilityCamera);
        return cachedVisibilityCamera;
      }

      if (viewer != null)
      {
        cachedVisibilityCamera = viewer.GetComponent<Camera>();
      }

      if (cachedVisibilityCamera == null || !cachedVisibilityCamera.isActiveAndEnabled)
      {
        cachedVisibilityCamera = Camera.main;
      }

      if (cachedVisibilityCamera != null)
      {
        EnsureFrustumPlanes(cachedVisibilityCamera);
      }

      return cachedVisibilityCamera;
    }

    private void EnsureFrustumPlanes(Camera camera)
    {
      int frame = Time.frameCount;
      if (frustumPlanesFrame == frame && ReferenceEquals(frustumPlanesCamera, camera))
      {
        return;
      }

      GeometryUtility.CalculateFrustumPlanes(camera, visibilityFrustumPlanes);
      frustumPlanesFrame = frame;
      frustumPlanesCamera = camera;
    }

    private void RefreshVisibilitySchedulingIfNeeded()
    {
      if (UseInitialStreamingStageNow || !hasLastViewerChunkCoord || desiredChunkCoords.Count == 0)
      {
        return;
      }

      Camera camera = ResolveVisibilityCamera();
      if (camera == null)
      {
        return;
      }

      int frame = Time.frameCount;
      Vector3 position = camera.transform.position;
      Vector3 forward = camera.transform.forward;

      bool shouldRefresh = !hasLastVisibilityCameraState ||
                           frame - lastVisibilityRefreshFrame >= VisibilityRefreshMaxFrameInterval ||
                           (position - lastVisibilityCameraPosition).sqrMagnitude >= VisibilityRefreshPositionEpsilon * VisibilityRefreshPositionEpsilon ||
                           Vector3.Dot(forward, lastVisibilityCameraForward) < VisibilityRefreshDotThreshold;

      if (!shouldRefresh)
      {
        return;
      }

      hasLastVisibilityCameraState = true;
      lastVisibilityCameraPosition = position;
      lastVisibilityCameraForward = forward;
      lastVisibilityRefreshFrame = frame;

      // Camera changes should reprioritise the existing work backlog. The streaming
      // set still stays radius-based around the player; the camera only changes what
      // gets built/applied first.
      pendingLoadQueueNeedsPrioritization = true;
      pendingRenderQueueNeedsPrioritization = true;
      QueueGeneratedChunksForRender(lastViewerChunkCoord);
    }

    /// <summary>
    /// Priority score used by load/render queues. Lower is better. This keeps chunks
    /// around the player alive like Minecraft, but drains visible/front-facing work
    /// first so the world appears to fill in the direction the player is looking.
    /// </summary>
    private int GetChunkPriorityScore(Vector3Int chunkCoord, Vector3Int pivotChunkCoord)
    {
      if (hasPriorityChunkCoord && chunkCoord == priorityChunkCoord)
      {
        return int.MinValue + 1024;
      }

      int score = 0;

      int distanceFromViewer = hasLastViewerChunkCoord
        ? ChunkDistanceSquared(chunkCoord, lastViewerChunkCoord)
        : ChunkDistanceSquared(chunkCoord, pivotChunkCoord);

      int distanceFromPivot = ChunkDistanceSquared(chunkCoord, pivotChunkCoord);
      int verticalDistance = hasLastViewerChunkCoord ? Mathf.Abs(chunkCoord.y - lastViewerChunkCoord.y) : 0;

      score += distanceFromViewer * 100;
      score += distanceFromPivot * 35;
      score += verticalDistance * 200;

      if (IsChunkNearViewerForImmediateMesh(chunkCoord))
      {
        score -= 250000;
      }

      Camera camera = ResolveVisibilityCamera();
      if (camera == null)
      {
        return score;
      }

      Vector3 chunkCenter = GetChunkWorldCenter(chunkCoord);
      Vector3 toChunk = chunkCenter - camera.transform.position;
      float distSq = toChunk.sqrMagnitude;

      if (distSq > 0.0001f)
      {
        Vector3 dir = toChunk / Mathf.Sqrt(distSq);
        float dot = Vector3.Dot(camera.transform.forward, dir);

        // Front-facing chunks should beat side chunks, and chunks behind the player
        // still get built eventually but never steal the front queue.
        score -= Mathf.RoundToInt(Mathf.Clamp(dot, -1.0f, 1.0f) * 65000.0f);

        // A small centre-screen bias makes the generated front line feel less random.
        Vector3 viewport = camera.WorldToViewportPoint(chunkCenter);
        if (viewport.z > 0.0f)
        {
          float centreDx = viewport.x - 0.5f;
          float centreDy = viewport.y - 0.5f;
          score += Mathf.RoundToInt((centreDx * centreDx + centreDy * centreDy) * 15000.0f);
        }
      }

      if (IsChunkInCameraFrustum(chunkCoord))
      {
        score -= 150000;
      }

      return score;
    }

    /// <summary>
    /// Draws loaded chunks the camera can see and hides the ones it cannot. The mesh
    /// stays resident while hidden so a hidden chunk is shown again instantly (no
    /// re-meshing). A short grace period avoids popping when the camera flicks around.
    /// </summary>
    private void UpdateChunkRenderVisibility()
    {
      if (worldRenderer == null)
      {
        return;
      }

      IReadOnlyDictionary<Vector3Int, ChunkView> activeViews = worldRenderer.ActiveChunkViews;
      if (activeViews == null || activeViews.Count == 0)
      {
        if (chunkLastInViewTime.Count > 0)
        {
          chunkLastInViewTime.Clear();
        }

        return;
      }

      Camera camera = ResolveVisibilityCamera();

      // During the initial fill (or with no camera to test against) keep everything
      // drawn - hiding here would make the world momentarily vanish.
      if (UseInitialStreamingStageNow || camera == null)
      {
        ShowAllRenderedChunks(activeViews);
        return;
      }

      float now = Time.unscaledTime;

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeViews)
      {
        Vector3Int chunkCoord = pair.Key;

        bool inView = IsChunkNearViewerForAlwaysDraw(chunkCoord) || IsChunkInCameraFrustum(chunkCoord);

        if (inView)
        {
          chunkLastInViewTime[chunkCoord] = now;
          worldRenderer.SetChunkRenderVisible(chunkCoord, true);
          continue;
        }

        // Out of view. Start (or honour) a grace window so brief camera flicks across
        // a chunk edge don't hide and re-show it every frame.
        if (!chunkLastInViewTime.TryGetValue(chunkCoord, out float lastInView))
        {
          chunkLastInViewTime[chunkCoord] = now;
          worldRenderer.SetChunkRenderVisible(chunkCoord, true);
          continue;
        }

        if (now - lastInView >= RenderCullGraceSeconds)
        {
          worldRenderer.SetChunkRenderVisible(chunkCoord, false);
        }
      }

      PruneRenderVisibilityTracking(activeViews);
    }

    private void ShowAllRenderedChunks(IReadOnlyDictionary<Vector3Int, ChunkView> activeViews)
    {
      float now = Time.unscaledTime;
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeViews)
      {
        chunkLastInViewTime[pair.Key] = now;
        worldRenderer.SetChunkRenderVisible(pair.Key, true);
      }
    }

    private void PruneRenderVisibilityTracking(IReadOnlyDictionary<Vector3Int, ChunkView> activeViews)
    {
      if (chunkLastInViewTime.Count <= activeViews.Count)
      {
        return;
      }

      renderCullPruneBuffer.Clear();
      foreach (Vector3Int tracked in chunkLastInViewTime.Keys)
      {
        if (!activeViews.ContainsKey(tracked))
        {
          renderCullPruneBuffer.Add(tracked);
        }
      }

      for (int i = 0; i < renderCullPruneBuffer.Count; i++)
      {
        chunkLastInViewTime.Remove(renderCullPruneBuffer[i]);
      }
    }
  }
}
