using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  internal static class WorldPersistenceCompatibilityExtensions
  {
    public static void BuildMesh(this WorldRenderer renderer)
    {
      renderer?.RebuildAll();
    }

    public static void ForceRefreshAll(this WorldStreamer streamer)
    {
      streamer?.ClearStreamingState();
      streamer?.ForceRefreshStreamingSet();
    }
  }
}
