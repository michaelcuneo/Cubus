using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// A server-authoritative <see cref="IWorldChunkStore"/> backed by the SpacetimeDB <c>world_chunk</c>
  /// table for shared edits, layered over a local on-disk <see cref="FileWorldChunkStore"/> cache for
  /// the deterministic base terrain. Base chunks are written to disk so re-entry and restarts load
  /// blisteringly fast instead of regenerating, while the (large, binary) base terrain is NEVER bulk
  /// uploaded to the DB - only edits replicate via <c>voxel_edit</c>. Missing chunks return
  /// <c>false</c> so the streamer generates them locally, then they are cached to disk.
  /// </summary>
  public sealed class SpacetimeDbWorldChunkStore : IAuthoritativeWorldChunkStore
  {
    private readonly CubusNetworkManager net;
    private readonly string worldId;
    private readonly ConcurrentDictionary<Vector3Int, WorldChunkRecord> cache = new();
    private readonly FileWorldChunkStore localCache;

    private bool callbacksRegistered;

    public SpacetimeDbWorldChunkStore(CubusNetworkManager net, string worldId)
    {
      this.net = net;
      this.worldId = string.IsNullOrWhiteSpace(worldId) ? "demo_world" : worldId;
      this.localCache = new FileWorldChunkStore(
          Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds"));

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

    public bool HasWorld(string id) => id == worldId && (!cache.IsEmpty || localCache.HasWorld(id));

    public void SaveWorldManifest(WorldManifest manifest)
    {
      // The SpacetimeDB world has no manifest table (bounds live on WorldSettings),
      // but persist one to the local disk cache so a saved world is recognised on
      // restart and chunks load from disk instead of regenerating.
      localCache.SaveWorldManifest(manifest);
    }

    public bool TryLoadWorldManifest(string id, out WorldManifest manifest)
    {
      return localCache.TryLoadWorldManifest(id, out manifest);
    }

    public void SaveChunk(WorldChunkRecord chunk)
    {
      if (chunk.WorldId != worldId || net == null)
      {
        return;
      }

      // Cache locally immediately so a just-generated chunk is treated as known.
      cache[chunk.ChunkCoord] = chunk;

      // Persist the deterministic base chunk to disk so re-entry and restarts load
      // fast instead of regenerating. This is the local, per-client cache - not the
      // shared DB - so it never bloats world_chunk.
      localCache.SaveChunk(chunk);

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
      if (id != worldId)
      {
        chunk = default;
        return false;
      }

      // RAM cache first (chunks generated/received this session), then the on-disk
      // cache (chunks saved by previous sessions) - both far cheaper than
      // regenerating the terrain.
      if (cache.TryGetValue(chunkCoord, out chunk))
      {
        return true;
      }

      if (localCache.TryLoadChunk(id, chunkCoord, out chunk))
      {
        // Promote to the RAM cache so subsequent reads this session skip disk IO.
        cache[chunkCoord] = chunk;
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

      HashSet<Vector3Int> seen = new();

      foreach (Vector3Int coord in cache.Keys)
      {
        if (seen.Add(coord))
        {
          yield return coord;
        }
      }

      foreach (Vector3Int coord in localCache.EnumerateChunkCoords(id))
      {
        if (seen.Add(coord))
        {
          yield return coord;
        }
      }
    }

    public void DeleteWorld(string id)
    {
      if (id == worldId)
      {
        cache.Clear();
        localCache.DeleteWorld(id);
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
