using System;
using UnityEngine;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  public enum WorldStorageBackend
  {
    LocalFile = 0,
    SpacetimeDb = 1,
    Custom = 2
  }

  public enum MissingChunkPolicy
  {
    TreatAsEmpty = 0,
    GenerateLocally = 1,
    RequestFromAuthority = 2,
  }

  public enum CubusChunkPayloadFormat : byte
  {
    BlockMaterialU16Raw = 1,
    DensityF32MaterialU16Raw = 2,
    DensityI16MaterialU16Rle = 3
  }

  [Serializable]
  public sealed class WorldManifest
  {
    public int SchemaVersion = 1;
    public string WorldId = "default_world";
    public string DisplayName = "Default World";

    public TerrainSystem TerrainSystem;
    public int ChunkSize;
    public float VoxelSize;

    public int MinChunkX;
    public int MaxChunkX;
    public int MinChunkY;
    public int MaxChunkY;
    public int MinChunkZ;
    public int MaxChunkZ;

    public int ChunkCount;
    public long CreatedUnixTime;
    public long UpdatedUnixTime;
  }

  public readonly struct WorldChunkRecord
  {
    public readonly string WorldId;
    public readonly Vector3Int ChunkCoord;
    public readonly TerrainSystem TerrainSystem;
    public readonly CubusChunkPayloadFormat PayloadFormat;
    public readonly int PayloadVersion;
    public readonly bool IsEmpty;
    public readonly bool HasSurface;
    public readonly byte[] PayloadBytes;

    public WorldChunkRecord(
        string worldId,
        Vector3Int chunkCoord,
        TerrainSystem terrainSystem,
        CubusChunkPayloadFormat payloadFormat,
        int payloadVersion,
        bool isEmpty,
        bool hasSurface,
        byte[] payloadBytes)
    {
      WorldId = worldId;
      ChunkCoord = chunkCoord;
      TerrainSystem = terrainSystem;
      PayloadFormat = payloadFormat;
      PayloadVersion = payloadVersion;
      IsEmpty = isEmpty;
      HasSurface = hasSurface;
      PayloadBytes = payloadBytes ?? Array.Empty<byte>();
    }

    public string ChunkKey =>
        $"{WorldId}:{ChunkCoord.x}:{ChunkCoord.y}:{ChunkCoord.z}";
  }
}
