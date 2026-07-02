using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private const int AlwaysMeshNearViewerHorizontalChunks = 3;
    private const int AlwaysMeshNearViewerVerticalChunks = 2;
    private const float VisibilityBoundsPaddingChunks = 0.5f;
    private const float VisibilityRefreshPositionEpsilon = 0.25f;
    private const float VisibilityRefreshDotThreshold = 0.9975f;
    private const int VisibilityRefreshMaxFrameInterval = 8;

    // How long a chunk keeps drawing after it leaves the camera view before it is
    // hidden. Prevents show/hide popping when the camera flicks across a chunk edge.
    private const float RenderCullGraceSeconds = 0.35f;

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

    private bool ShouldQueueMeshWorkForChunk(Vector3Int chunkCoord)
    {
      if (UseInitialStreamingStageNow)
      {
        return true;
      }

      if (hasPriorityChunkCoord && chunkCoord == priorityChunkCoord)
      {
        return true;
      }

      if (IsChunkNearViewerForImmediateMesh(chunkCoord))
      {
        return true;
      }

      Camera camera = ResolveVisibilityCamera();
      if (camera == null)
      {
        return true;
      }

      Bounds bounds = GetChunkWorldBounds(chunkCoord);
      return GeometryUtility.TestPlanesAABB(visibilityFrustumPlanes, bounds);
    }

    private bool ShouldStartChunkLoadForCurrentVisibility(Vector3Int chunkCoord)
    {
      if (UseInitialStreamingStageNow)
      {
        return true;
      }

      if (hasPriorityChunkCoord && chunkCoord == priorityChunkCoord)
      {
        return true;
      }

      if (IsChunkNearViewerForImmediateMesh(chunkCoord))
      {
        return true;
      }

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

      return dx <= AlwaysMeshNearViewerHorizontalChunks &&
             dz <= AlwaysMeshNearViewerHorizontalChunks &&
             dy <= AlwaysMeshNearViewerVerticalChunks;
    }

    private Bounds GetChunkWorldBounds(Vector3Int chunkCoord)
    {
      float voxelSize = world != null && world.Settings != null ? world.Settings.VoxelSize : 1.0f;
      float chunkWorldSize = VoxelConstants.ChunkSize * voxelSize;
      float padding = chunkWorldSize * VisibilityBoundsPaddingChunks;
      Vector3 min = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      Vector3 size = Vector3.one * (chunkWorldSize + padding * 2.0f);
      return new Bounds(min + Vector3.one * (chunkWorldSize * 0.5f), size);
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
      QueueGeneratedChunksForRender(lastViewerChunkCoord);
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

        bool inView = IsChunkNearViewerForImmediateMesh(chunkCoord) ||
                      GeometryUtility.TestPlanesAABB(visibilityFrustumPlanes, GetChunkWorldBounds(chunkCoord));

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
