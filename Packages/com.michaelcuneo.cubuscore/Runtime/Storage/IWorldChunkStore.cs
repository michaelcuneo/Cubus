using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  public interface IWorldChunkStore
  {
    bool HasWorld(string worldId);

    void SaveWorldManifest(WorldManifest manifest);
    bool TryLoadWorldManifest(string worldId, out WorldManifest manifest);

    void SaveChunk(WorldChunkRecord chunk);
    bool TryLoadChunk(string worldId, Vector3Int chunkCoord, out WorldChunkRecord chunk);

    IEnumerable<Vector3Int> EnumerateChunkCoords(string worldId);

    void DeleteWorld(string worldId);
  }
}