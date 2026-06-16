using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  public static class CubusChunkPayloadCodec
  {
    public const int PayloadVersion = 1;

    private const int BlockRawPayloadBytes = VoxelConstants.ChunkVolume * sizeof(ushort);
    private const int DensityRawPayloadBytes = VoxelConstants.ChunkVolume * (sizeof(float) + sizeof(ushort));

    public static bool CanDecodeBlockChunk(WorldChunkRecord record, out string error)
    {
      if (record.TerrainSystem != TerrainSystem.Block)
      {
        error = $"Expected Block terrain, got {record.TerrainSystem}.";
        return false;
      }

      if (record.PayloadFormat != CubusChunkPayloadFormat.BlockMaterialU16Raw)
      {
        error = $"Unsupported block payload format {record.PayloadFormat}.";
        return false;
      }

      if (record.PayloadVersion != PayloadVersion)
      {
        error = $"Unsupported block payload version {record.PayloadVersion}.";
        return false;
      }

      if (record.PayloadBytes == null || record.PayloadBytes.Length != BlockRawPayloadBytes)
      {
        error = $"Invalid block payload size. Expected={BlockRawPayloadBytes}, Actual={record.PayloadBytes?.Length ?? 0}.";
        return false;
      }

      error = null;
      return true;
    }

    public static bool CanDecodeDensityChunk(WorldChunkRecord record, out string error)
    {
      if (record.TerrainSystem != TerrainSystem.SmoothDensity)
      {
        error = $"Expected SmoothDensity terrain, got {record.TerrainSystem}.";
        return false;
      }

      if (record.PayloadFormat != CubusChunkPayloadFormat.DensityF32MaterialU16Raw)
      {
        error = $"Unsupported density payload format {record.PayloadFormat}.";
        return false;
      }

      if (record.PayloadVersion != PayloadVersion)
      {
        error = $"Unsupported density payload version {record.PayloadVersion}.";
        return false;
      }

      if (record.PayloadBytes == null || record.PayloadBytes.Length != DensityRawPayloadBytes)
      {
        error = $"Invalid density payload size. Expected={DensityRawPayloadBytes}, Actual={record.PayloadBytes?.Length ?? 0}.";
        return false;
      }

      error = null;
      return true;
    }

    public static WorldChunkRecord EncodeBlockChunk(string worldId, Vector3Int chunkCoord, BlockChunkData chunkData)
    {
      using MemoryStream stream = new();
      using BinaryWriter writer = new(stream);

      Voxel[] voxels = chunkData.GetRawVoxelArray();

      for (int i = 0; i < voxels.Length; i++)
      {
        writer.Write(voxels[i].MaterialId);
      }

      return new WorldChunkRecord(
          worldId,
          chunkCoord,
          TerrainSystem.Block,
          CubusChunkPayloadFormat.BlockMaterialU16Raw,
          PayloadVersion,
          !chunkData.HasAnySolidVoxel(),
          chunkData.HasAnySolidVoxel(),
          stream.ToArray()
      );
    }

    public static BlockChunkData DecodeBlockChunk(WorldChunkRecord record)
    {
      if (!CanDecodeBlockChunk(record, out string error))
      {
        throw new InvalidDataException(error);
      }

      BlockChunkData chunkData = new(record.ChunkCoord);
      Voxel[] voxels = chunkData.GetRawVoxelArray();

      using MemoryStream stream = new(record.PayloadBytes);
      using BinaryReader reader = new(stream);

      for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
      {
        voxels[i] = new Voxel(reader.ReadUInt16());
      }

      return chunkData;
    }

    public static WorldChunkRecord EncodeDensityChunk(string worldId, Vector3Int chunkCoord, DensityChunkData chunkData)
    {
      using MemoryStream stream = new();
      using BinaryWriter writer = new(stream);

      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

      for (int i = 0; i < voxels.Length; i++)
      {
        writer.Write(voxels[i].Density);
        writer.Write(voxels[i].MaterialId);
      }

      return new WorldChunkRecord(
          worldId,
          chunkCoord,
          TerrainSystem.SmoothDensity,
          CubusChunkPayloadFormat.DensityF32MaterialU16Raw,
          PayloadVersion,
          !chunkData.HasAnySolidVoxel(),
          chunkData.HasSurfaceCrossing(),
          stream.ToArray()
      );
    }

    public static DensityChunkData DecodeDensityChunk(WorldChunkRecord record)
    {
      if (!CanDecodeDensityChunk(record, out string error))
      {
        throw new InvalidDataException(error);
      }

      DensityChunkData chunkData = new(record.ChunkCoord);
      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

      using MemoryStream stream = new(record.PayloadBytes);
      using BinaryReader reader = new(stream);

      for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
      {
        float density = reader.ReadSingle();
        ushort materialId = reader.ReadUInt16();

        voxels[i] = new DensityVoxel(density, materialId);
      }

      return chunkData;
    }
  }
}