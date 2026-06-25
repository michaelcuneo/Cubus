using SpacetimeDB;

public static partial class Module
{
  // A connected player: presence + last-known transform.
  // Keyed by the client's SpacetimeDB Identity so it survives reconnects.
  [Table(Accessor = "player", Public = true)]
  public partial struct Player
  {
    [PrimaryKey]
    public Identity Identity;

    public string Name;
    public bool Online;

    // Last-known transform (world space).
    public float X;
    public float Y;
    public float Z;
    public float Yaw;
    public float Pitch;

    // Stable per-player display color (packed 0xRRGGBBAA).
    public uint ColorRgba;

    public Timestamp LastSeen;
  }

  // Authoritative deterministic generation contract for a world.
  // Clients generate base terrain locally from this tiny row, then layer
  // authoritative voxel_edit rows on top. Do not store generated chunks here.
  [Table(Accessor = "world_state", Public = true)]
  public partial struct WorldState
  {
    [PrimaryKey]
    public string WorldId;

    public int WorldSeed;
    public uint GeneratorVersion;
    public byte TerrainSystem;
    public float VoxelSize;

    public int MinChunkY;
    public int MaxChunkY;

    public int WorldMinChunkX;
    public int WorldMinChunkZ;
    public int WorldMaxChunkX;
    public int WorldMaxChunkZ;

    public string GenerationProfileId;
    public string GenerationSignature;

    public Identity UpdatedBy;
    public Timestamp UpdatedAt;
  }

  // A single authoritative voxel override (block add/remove).
  // Material 0 means the voxel was removed (air).
  [Table(Accessor = "voxel_edit", Public = true)]
  public partial struct VoxelEdit
  {
    // Composite key encoded as "worldId:x:y:z" so edits are idempotent.
    [PrimaryKey]
    public string Key;

    [SpacetimeDB.Index.BTree]
    public string WorldId;

    public int X;
    public int Y;
    public int Z;

    public uint Material;

    public Identity EditedBy;
    public Timestamp EditedAt;
  }

  // An authoritative compressed chunk payload owned by the server.
  // Used by the SpacetimeDb world chunk store on the client.
  [Table(Accessor = "world_chunk", Public = true)]
  public partial struct WorldChunk
  {
    // Composite key encoded as "worldId:cx:cy:cz".
    [PrimaryKey]
    public string Key;

    [SpacetimeDB.Index.BTree]
    public string WorldId;

    public int Cx;
    public int Cy;
    public int Cz;

    public byte TerrainSystem;
    public byte PayloadFormat;
    public int PayloadVersion;
    public bool IsEmpty;
    public bool HasSurface;

    public byte[] Payload;
    public Timestamp UpdatedAt;
  }

  // Append-only chat log. Trimmed to a bounded length by SendChat.
  [Table(Accessor = "chat_message", Public = true)]
  public partial struct ChatMessage
  {
    [PrimaryKey]
    [AutoInc]
    public ulong Id;

    public Identity Sender;
    public string SenderName;
    public string Text;
    public Timestamp SentAt;
  }
}
