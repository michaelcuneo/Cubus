using SpacetimeDB;

// Cubus multiplayer server module.
//
// Build / publish from the Server/ folder:
//   spacetime build
//   spacetime publish cubus            (local)  or  --server <name>
// Regenerate Unity client bindings (run from repo root):
//   spacetime generate --lang csharp --out-dir Assets/Demo/Scripts/Multiplayer/Autogen --project-path Server
public static partial class Module
{
  // Maximum number of chat messages retained in the log.
  private const int MaxChatMessages = 200;

  // Authoritative chunk storage is disabled: clients persisted EVERY generated
  // chunk here, which grew world_chunk into tens of thousands of rows and made
  // the initial subscription too large to decode ("Stream was too long").
  // Base terrain is deterministic; only edits (voxel_edit) need to be shared.
  // Re-enable only behind a near-player subscription design.
  private const bool AcceptChunkUploads = false;

  [Reducer(ReducerKind.ClientConnected)]
  public static void ClientConnected(ReducerContext ctx)
  {
    var existing = ctx.Db.player.Identity.Find(ctx.Sender);
    if (existing is Player player)
    {
      player.Online = true;
      player.LastSeen = ctx.Timestamp;
      ctx.Db.player.Identity.Update(player);
      Log.Info($"Player reconnected: {player.Name}");
      return;
    }

    var created = ctx.Db.player.Insert(new Player
    {
      Identity = ctx.Sender,
      Name = DefaultName(ctx.Sender),
      Online = true,
      ColorRgba = ColorFromIdentity(ctx.Sender),
      LastSeen = ctx.Timestamp,
    });
    Log.Info($"Player connected: {created.Name}");
  }

  [Reducer(ReducerKind.ClientDisconnected)]
  public static void ClientDisconnected(ReducerContext ctx)
  {
    if (ctx.Db.player.Identity.Find(ctx.Sender) is Player player)
    {
      player.Online = false;
      player.LastSeen = ctx.Timestamp;
      ctx.Db.player.Identity.Update(player);
      Log.Info($"Player disconnected: {player.Name}");
    }
  }

  [Reducer]
  public static void SetPlayerName(ReducerContext ctx, string name)
  {
    if (ctx.Db.player.Identity.Find(ctx.Sender) is not Player player)
    {
      throw new Exception("SetPlayerName called before the player was registered.");
    }

    var trimmed = (name ?? string.Empty).Trim();
    if (trimmed.Length == 0)
    {
      throw new Exception("Player name must not be empty.");
    }
    if (trimmed.Length > 32)
    {
      trimmed = trimmed.Substring(0, 32);
    }

    player.Name = trimmed;
    player.LastSeen = ctx.Timestamp;
    ctx.Db.player.Identity.Update(player);
  }

  [Reducer]
  public static void UpdatePlayerTransform(
      ReducerContext ctx,
      float x,
      float y,
      float z,
      float yaw,
      float pitch)
  {
    if (ctx.Db.player.Identity.Find(ctx.Sender) is not Player player)
    {
      // Position updates can race ahead of connection registration; ignore quietly.
      return;
    }

    player.X = x;
    player.Y = y;
    player.Z = z;
    player.Yaw = yaw;
    player.Pitch = pitch;
    player.LastSeen = ctx.Timestamp;
    ctx.Db.player.Identity.Update(player);
  }

  [Reducer]
  public static void EditBlock(
      ReducerContext ctx,
      string worldId,
      int x,
      int y,
      int z,
      uint material)
  {
    if (string.IsNullOrEmpty(worldId))
    {
      throw new Exception("EditBlock requires a worldId.");
    }

    var key = VoxelKey(worldId, x, y, z);
    var edit = new VoxelEdit
    {
      Key = key,
      WorldId = worldId,
      X = x,
      Y = y,
      Z = z,
      Material = material,
      EditedBy = ctx.Sender,
      EditedAt = ctx.Timestamp,
    };

    if (ctx.Db.voxel_edit.Key.Find(key) is not null)
    {
      ctx.Db.voxel_edit.Key.Update(edit);
    }
    else
    {
      ctx.Db.voxel_edit.Insert(edit);
    }
  }

  [Reducer]
  public static void UploadChunk(
      ReducerContext ctx,
      string worldId,
      int cx,
      int cy,
      int cz,
      byte terrainSystem,
      byte payloadFormat,
      int payloadVersion,
      bool isEmpty,
      bool hasSurface,
      byte[] payload)
  {
    if (!AcceptChunkUploads)
    {
      // Ignore generated-chunk uploads to keep world_chunk from re-bloating.
      return;
    }

    if (string.IsNullOrEmpty(worldId))
    {
      throw new Exception("UploadChunk requires a worldId.");
    }

    var key = ChunkKey(worldId, cx, cy, cz);
    var chunk = new WorldChunk
    {
      Key = key,
      WorldId = worldId,
      Cx = cx,
      Cy = cy,
      Cz = cz,
      TerrainSystem = terrainSystem,
      PayloadFormat = payloadFormat,
      PayloadVersion = payloadVersion,
      IsEmpty = isEmpty,
      HasSurface = hasSurface,
      Payload = payload ?? System.Array.Empty<byte>(),
      UpdatedAt = ctx.Timestamp,
    };

    if (ctx.Db.world_chunk.Key.Find(key) is not null)
    {
      ctx.Db.world_chunk.Key.Update(chunk);
    }
    else
    {
      ctx.Db.world_chunk.Insert(chunk);
    }
  }

  // Wipes ALL shared state for a world from the server: every authoritative voxel
  // edit and any uploaded chunk rows. This lets the Unity editor's "Delete World
  // Database" / "Clear World" actions reset SpacetimeDB too, not just the calling
  // client's local cache, so the world is cleared consistently for every player.
  [Reducer]
  public static void ClearWorld(ReducerContext ctx, string worldId)
  {
    if (string.IsNullOrEmpty(worldId))
    {
      throw new Exception("ClearWorld requires a worldId.");
    }

    var editsRemoved = 0;
    foreach (var edit in ctx.Db.voxel_edit.WorldId.Filter(worldId))
    {
      ctx.Db.voxel_edit.Key.Delete(edit.Key);
      editsRemoved++;
    }

    var chunksRemoved = 0;
    foreach (var chunk in ctx.Db.world_chunk.WorldId.Filter(worldId))
    {
      ctx.Db.world_chunk.Key.Delete(chunk.Key);
      chunksRemoved++;
    }

    Log.Info($"ClearWorld '{worldId}': removed {editsRemoved} voxel edits and {chunksRemoved} chunks.");
  }

  [Reducer]
  public static void SendChat(ReducerContext ctx, string text)
  {
    var trimmed = (text ?? string.Empty).Trim();
    if (trimmed.Length == 0)
    {
      return;
    }
    if (trimmed.Length > 500)
    {
      trimmed = trimmed.Substring(0, 500);
    }

    var senderName = ctx.Db.player.Identity.Find(ctx.Sender) is Player player
        ? player.Name
        : DefaultName(ctx.Sender);

    ctx.Db.chat_message.Insert(new ChatMessage
    {
      Sender = ctx.Sender,
      SenderName = senderName,
      Text = trimmed,
      SentAt = ctx.Timestamp,
    });

    TrimChatLog(ctx);
  }

  private static void TrimChatLog(ReducerContext ctx)
  {
    var count = ctx.Db.chat_message.Count;
    if (count <= MaxChatMessages)
    {
      return;
    }

    var toRemove = count - MaxChatMessages;
    // chat_message is auto-incremented, so ascending Id == oldest first.
    foreach (var message in ctx.Db.chat_message.Iter())
    {
      if (toRemove <= 0)
      {
        break;
      }
      ctx.Db.chat_message.Id.Delete(message.Id);
      toRemove--;
    }
  }

  private static string VoxelKey(string worldId, int x, int y, int z)
      => $"{worldId}:{x}:{y}:{z}";

  private static string ChunkKey(string worldId, int cx, int cy, int cz)
      => $"{worldId}:{cx}:{cy}:{cz}";

  private static string DefaultName(Identity identity)
  {
    var hex = identity.ToString();
    var suffix = hex.Length >= 6 ? hex.Substring(hex.Length - 6) : hex;
    return $"Player_{suffix}";
  }

  private static uint ColorFromIdentity(Identity identity)
  {
    // Deterministic, well-saturated color per identity (FNV-1a over the hex string).
    var hex = identity.ToString();
    uint hash = 2166136261u;
    foreach (var c in hex)
    {
      hash = (hash ^ c) * 16777619u;
    }

    byte r = (byte)(64 + (hash & 0x7F));
    byte g = (byte)(64 + ((hash >> 8) & 0x7F));
    byte b = (byte)(64 + ((hash >> 16) & 0x7F));
    return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFF;
  }
}
