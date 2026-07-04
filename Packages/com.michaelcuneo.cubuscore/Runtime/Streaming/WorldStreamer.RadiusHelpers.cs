using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private int GetConfiguredViewRadiusInChunks()
    {
      return Mathf.Max(1, world != null && world.Settings != null ? world.Settings.ViewDistanceInChunks : 1);
    }

    private int GetActiveBuildRadiusInChunks()
    {
      if (UseInitialStreamingStageNow)
      {
        return Mathf.Max(0, initialStreamingRadiusInChunks);
      }

      int viewRadius = GetConfiguredViewRadiusInChunks();
      return settings != null && settings.BuildRadiusInChunks > 0
        ? Mathf.Max(1, settings.BuildRadiusInChunks)
        : viewRadius;
    }

    private int GetActiveRenderRadiusInChunks()
    {
      if (UseInitialStreamingStageNow)
      {
        return Mathf.Max(0, initialStreamingRadiusInChunks);
      }

      int buildRadius = GetActiveBuildRadiusInChunks();
      return settings != null && settings.RenderRadiusInChunks > 0
        ? Mathf.Clamp(settings.RenderRadiusInChunks, 1, buildRadius)
        : buildRadius;
    }

    private int GetActiveLoadRadiusInChunks()
    {
      if (UseInitialStreamingStageNow)
      {
        return GetActiveBuildRadiusInChunks() + Mathf.Max(0, initialKeepPaddingInChunks);
      }

      int buildRadius = GetActiveBuildRadiusInChunks();
      int defaultLoadRadius = buildRadius + UnloadPaddingInChunks;
      return settings != null && settings.LoadRadiusInChunks > 0
        ? Mathf.Max(buildRadius, settings.LoadRadiusInChunks)
        : defaultLoadRadius;
    }

    private bool IsChunkInsideHorizontalRadius(Vector3Int chunkCoord, int radiusInChunks)
    {
      if (!hasLastViewerChunkCoord)
      {
        return true;
      }

      int dx = Mathf.Abs(chunkCoord.x - lastViewerChunkCoord.x);
      int dz = Mathf.Abs(chunkCoord.z - lastViewerChunkCoord.z);
      return dx <= radiusInChunks && dz <= radiusInChunks;
    }

    private bool IsChunkInsideActiveBuildRadius(Vector3Int chunkCoord)
    {
      return IsChunkInsideHorizontalRadius(chunkCoord, GetActiveBuildRadiusInChunks());
    }

    private bool IsChunkInsideActiveRenderRadius(Vector3Int chunkCoord)
    {
      return IsChunkInsideHorizontalRadius(chunkCoord, GetActiveRenderRadiusInChunks());
    }
  }
}
