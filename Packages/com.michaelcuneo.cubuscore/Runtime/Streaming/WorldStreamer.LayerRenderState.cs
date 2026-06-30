using System.Collections.Generic;
using System.Reflection;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private static readonly FieldInfo ActiveBlockChunkViewsField = typeof(WorldRenderer).GetField(
      "activeBlockChunkViews",
      BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo ActiveDensityChunkViewsField = typeof(WorldRenderer).GetField(
      "activeDensityChunkViews",
      BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HashSet<Vector3Int> knownEmptyDensityChunks = new();

    private bool HasRenderedBlockChunk(Vector3Int chunkCoord)
    {
      return TryGetLayerViews(ActiveBlockChunkViewsField, out Dictionary<Vector3Int, ChunkView> views) &&
             views.ContainsKey(chunkCoord);
    }

    private bool HasRenderedDensityChunk(Vector3Int chunkCoord)
    {
      return TryGetLayerViews(ActiveDensityChunkViewsField, out Dictionary<Vector3Int, ChunkView> views) &&
             views.ContainsKey(chunkCoord);
    }

    private bool TryGetLayerViews(FieldInfo field, out Dictionary<Vector3Int, ChunkView> views)
    {
      views = null;

      if (worldRenderer == null || field == null)
      {
        return false;
      }

      views = field.GetValue(worldRenderer) as Dictionary<Vector3Int, ChunkView>;
      return views != null;
    }
  }
}
