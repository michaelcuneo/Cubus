using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private const int AlwaysMeshNearViewerHorizontalChunks = 2;
    private const int AlwaysMeshNearViewerVerticalChunks = 1;
    private const float VisibilityBoundsPaddingChunks = 0.5f;
    private const float VisibilityRefreshPositionEpsilon = 0.25f;
    private const float VisibilityRefreshDotThreshold = 0.9975f;
    private const int VisibilityRefreshMaxFrameInterval = 8;

    private readonly Plane[] visibilityFrustumPlanes = new Plane[6];
    private Camera cachedVisibilityCamera;
    private Vector3 lastVisibilityCameraPosition;
    private Vector3 lastVisibilityCameraForward;
    private int lastVisibilityRefreshFrame = -9999;
    private bool hasLastVisibilityCameraState;

    private bool ShouldQueueMeshWorkForChunk(Vector3Int chunkCoord)
    {
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
        GeometryUtility.CalculateFrustumPlanes(cachedVisibilityCamera, visibilityFrustumPlanes);
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
        GeometryUtility.CalculateFrustumPlanes(cachedVisibilityCamera, visibilityFrustumPlanes);
      }

      return cachedVisibilityCamera;
    }

    private void RefreshVisibilitySchedulingIfNeeded()
    {
      if (!hasLastViewerChunkCoord || desiredChunkCoords.Count == 0)
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
  }
}
