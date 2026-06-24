using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// A server-authoritative <see cref="IWorldChunkStore"/> backed by the SpacetimeDB <c>world_chunk</c>
  /// table. Chunks the server already holds are streamed into a thread-safe cache and returned by
  /// <see cref="TryLoadChunk"/>; chunks the local client generates are uploaded so other players share
  /// the same world. Missing chunks return <c>false</c> so the streamer can generate them locally.
  /// </summary>
  public sealed class SpacetimeDbWorldChunkStore : IAuthoritativeWorldChunkStore
  {
    private readonly CubusNetworkManager net;
    private readonly string worldId;
    private readonly ConcurrentDictionary<Vector3Int, WorldChunkRecord> cache = new();

    private bool callbacksRegistered;

    public SpacetimeDbWorldChunkStore(CubusNetworkManager net, string worldId)
    {
      this.net = net;
      this.worldId = string.IsNullOrWhiteSpace(worldId) ? "demo_world" : worldId;

      if (net != null)
      {
        net.Connected += HandleConnected;
        net.Disconnected += HandleDisconnected;
        if (net.IsConnected)
        {
          HandleConnected(net.Conn, net.LocalIdentity);
        }
      }
    }

    public bool CanRequestChunks => net != null && net.IsConnected && net.IsSubscriptionApplied;

    public event Action<WorldChunkRecord> ChunkReceived;
    public event Action<string, Vector3Int> ChunkUnavailable;

    private void HandleConnected(DbConnection conn, Identity identity) => RegisterCallbacks(conn);

    private void HandleDisconnected()
    {
      callbacksRegistered = false;
      cache.Clear();
    }

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.WorldChunk.OnInsert += HandleChunkInsert;
      conn.Db.WorldChunk.OnUpdate += HandleChunkUpdate;
      conn.Db.WorldChunk.OnDelete += HandleChunkDelete;
      callbacksRegistered = true;

      foreach (WorldChunk chunk in conn.Db.WorldChunk.Iter())
      {
        CacheRow(chunk);
      }
    }

    private void HandleChunkInsert(EventContext ctx, WorldChunk chunk) => CacheRow(chunk);

    private void HandleChunkUpdate(EventContext ctx, WorldChunk oldChunk, WorldChunk newChunk) => CacheRow(newChunk);

    private void HandleChunkDelete(EventContext ctx, WorldChunk chunk)
    {
      if (chunk.WorldId == worldId)
      {
        cache.TryRemove(new Vector3Int(chunk.Cx, chunk.Cy, chunk.Cz), out _);
      }
    }

    private void CacheRow(WorldChunk chunk)
    {
      if (chunk.WorldId != worldId)
      {
        return;
      }

      var coord = new Vector3Int(chunk.Cx, chunk.Cy, chunk.Cz);
      var record = new WorldChunkRecord(
          chunk.WorldId,
          coord,
          (TerrainSystem)chunk.TerrainSystem,
          (CubusChunkPayloadFormat)chunk.PayloadFormat,
          chunk.PayloadVersion,
          chunk.IsEmpty,
          chunk.HasSurface,
          chunk.Payload?.ToArray() ?? Array.Empty<byte>());

      cache[coord] = record;
      ChunkReceived?.Invoke(record);
    }

    // IWorldChunkStore -------------------------------------------------------

    public bool HasWorld(string id) => id == worldId && !cache.IsEmpty;

    public void SaveWorldManifest(WorldManifest manifest)
    {
      // The SpacetimeDB world has no separate manifest table; world bounds are
      // owned by the scene's WorldSettings. Nothing to persist here.
    }

    public bool TryLoadWorldManifest(string id, out WorldManifest manifest)
    {
      manifest = null;
      return false;
    }

    public void SaveChunk(WorldChunkRecord chunk)
    {
      if (chunk.WorldId != worldId || net == null)
      {
        return;
      }

      // Cache locally immediately so a just-generated chunk is treated as known.
      cache[chunk.ChunkCoord] = chunk;

      // Uploading every generated chunk floods world_chunk (tens of thousands of
      // rows) and makes the initial subscription too large to decode. Only push
      // to the server when authoritative chunk sync is explicitly enabled.
      if (!net.WorldChunkSyncEnabled)
      {
        return;
      }

      // Reducer calls must run on the connection's main thread.
      net.RunOnMainThread(() =>
      {
        if (!CanRequestChunks)
        {
          return;
        }

        net.Conn.Reducers.UploadChunk(
            chunk.WorldId,
            chunk.ChunkCoord.x,
            chunk.ChunkCoord.y,
            chunk.ChunkCoord.z,
            (byte)chunk.TerrainSystem,
            (byte)chunk.PayloadFormat,
            chunk.PayloadVersion,
            chunk.IsEmpty,
            chunk.HasSurface,
            new List<byte>(chunk.PayloadBytes ?? Array.Empty<byte>()));
      });
    }

    public bool TryLoadChunk(string id, Vector3Int chunkCoord, out WorldChunkRecord chunk)
    {
      if (id == worldId && cache.TryGetValue(chunkCoord, out chunk))
      {
        return true;
      }

      chunk = default;
      return false;
    }

    public IEnumerable<Vector3Int> EnumerateChunkCoords(string id)
    {
      if (id != worldId)
      {
        yield break;
      }

      foreach (Vector3Int coord in cache.Keys)
      {
        yield return coord;
      }
    }

    public void DeleteWorld(string id)
    {
      if (id == worldId)
      {
        cache.Clear();
      }
    }

    // IAuthoritativeWorldChunkStore -----------------------------------------

    public void RequestChunk(string id, Vector3Int chunkCoord)
    {
      if (id == worldId && cache.TryGetValue(chunkCoord, out WorldChunkRecord record))
      {
        ChunkReceived?.Invoke(record);
      }
      else
      {
        ChunkUnavailable?.Invoke(id, chunkCoord);
      }
    }
  }
}
