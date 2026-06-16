using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  public interface IAuthoritativeWorldChunkStore : IWorldChunkStore
  {
    bool CanRequestChunks { get; }

    void RequestChunk(string worldId, Vector3Int chunkCoord);

    event Action<WorldChunkRecord> ChunkReceived;
    event Action<string, Vector3Int> ChunkUnavailable;
  }
}