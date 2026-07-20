using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Compatibility bridge retained for WorldRenderer while biome detail
  /// scattering is handled by the demo-level renderer.
  /// </summary>
  public sealed class WorldDetailRenderer : MonoBehaviour
  {
    public void RefreshChunkDetail(Vector3Int chunkCoord)
    {
    }

    public void RemoveChunkDetail(Vector3Int chunkCoord)
    {
    }

    public void ClearAllDetail()
    {
    }
  }
}
