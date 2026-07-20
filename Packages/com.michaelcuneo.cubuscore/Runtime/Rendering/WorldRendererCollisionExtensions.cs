using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Compatibility helpers used by WorldStreamer terrain readiness and spawn
  /// collision checks. These keep collision requests routed through ChunkView's
  /// existing asynchronous collision baker.
  /// </summary>
  public static class WorldRendererCollisionExtensions
  {
    public static bool HasBlockChunkCollider(
        this WorldRenderer renderer,
        Vector3Int chunkCoord)
    {
      return TryGetChunkView(renderer, chunkCoord, out ChunkView view) &&
             view.HasActiveCollisionMesh;
    }

    public static bool HasDensityChunkCollider(
        this WorldRenderer renderer,
        Vector3Int chunkCoord)
    {
      return TryGetChunkView(renderer, chunkCoord, out ChunkView view) &&
             view.HasActiveCollisionMesh;
    }

    public static void EnsureBlockChunkCollision(
        this WorldRenderer renderer,
        Vector3Int chunkCoord)
    {
      if (TryGetChunkView(renderer, chunkCoord, out ChunkView view))
      {
        view.SetCollisionEnabled(true);
      }
    }

    public static void EnsureDensityChunkCollision(
        this WorldRenderer renderer,
        Vector3Int chunkCoord)
    {
      if (TryGetChunkView(renderer, chunkCoord, out ChunkView view))
      {
        view.SetCollisionEnabled(true);
      }
    }

    private static bool TryGetChunkView(
        WorldRenderer renderer,
        Vector3Int chunkCoord,
        out ChunkView view)
    {
      view = null;

      if (renderer == null || renderer.ActiveChunkViews == null)
      {
        return false;
      }

      return renderer.ActiveChunkViews.TryGetValue(chunkCoord, out view) &&
             view != null &&
             view.HasRenderableMesh;
    }
  }
}
