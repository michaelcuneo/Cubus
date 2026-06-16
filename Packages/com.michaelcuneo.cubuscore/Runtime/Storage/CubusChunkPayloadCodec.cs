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
    public const int PayloadVersion = 2;
    private const int LegacyPayloadVersion = 1;

    private const int BlockRawPayloadBytes = VoxelConstants.ChunkVolume * sizeof(ushort);
    private const int DensityRawPayloadBytes = VoxelConstants.ChunkVolume * (sizeof(float) + sizeof(ushort));
    private const float DensityQuantizationScale = 4096.0f;

    private enum DensityCompressedKind : byte
    {
      Empty = 0,
      Solid = 1,
      MixedRle = 2
    }

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

      if (record.PayloadVersion != LegacyPayloadVersion && record.PayloadVersion != PayloadVersion)
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

      if (record.PayloadFormat == CubusChunkPayloadFormat.DensityF32MaterialU16Raw)
      {
        if (record.PayloadVersion != LegacyPayloadVersion && record.PayloadVersion != PayloadVersion)
        {
          error = $"Unsupported raw density payload version {record.PayloadVersion}.";
          return false;
        }

        if (record.PayloadBytes == null || record.PayloadBytes.Length != DensityRawPayloadBytes)
        {
          error = $"Invalid raw density payload size. Expected={DensityRawPayloadBytes}, Actual={record.PayloadBytes?.Length ?? 0}.";
          return false;
        }

        error = null;
        return true;
      }

      if (record.PayloadFormat == CubusChunkPayloadFormat.DensityI16MaterialU16Rle)
      {
        if (record.PayloadVersion != PayloadVersion)
        {
          error = $"Unsupported compressed density payload version {record.PayloadVersion}.";
          return false;
        }

        if (record.PayloadBytes == null || record.PayloadBytes.Length < 1)
        {
          error = "Invalid compressed density payload: empty payload.";
          return false;
        }

        error = null;
        return true;
      }

      error = $"Unsupported density payload format {record.PayloadFormat}.";
      return false;
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
      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();
      bool hasSolid = chunkData.HasAnySolidVoxel();
      bool hasSurface = chunkData.HasSurfaceCrossing();

      using MemoryStream stream = new();
      using BinaryWriter writer = new(stream);

      if (!hasSolid)
      {
        writer.Write((byte)DensityCompressedKind.Empty);
      }
      else if (!hasSurface)
      {
        ushort materialId = FindFirstSolidMaterial(voxels);
        writer.Write((byte)DensityCompressedKind.Solid);
        writer.Write(materialId);
      }
      else
      {
        writer.Write((byte)DensityCompressedKind.MixedRle);
        WriteDensityRle(writer, voxels);
      }

      return new WorldChunkRecord(
          worldId,
          chunkCoord,
          TerrainSystem.SmoothDensity,
          CubusChunkPayloadFormat.DensityI16MaterialU16Rle,
          PayloadVersion,
          !hasSolid,
          hasSurface,
          stream.ToArray()
      );
    }

    public static DensityChunkData DecodeDensityChunk(WorldChunkRecord record)
    {
      if (!CanDecodeDensityChunk(record, out string error))
      {
        throw new InvalidDataException(error);
      }

      if (record.PayloadFormat == CubusChunkPayloadFormat.DensityF32MaterialU16Raw)
      {
        return DecodeRawDensityChunk(record);
      }

      return DecodeCompressedDensityChunk(record);
    }

    private static DensityChunkData DecodeRawDensityChunk(WorldChunkRecord record)
    {
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

    private static DensityChunkData DecodeCompressedDensityChunk(WorldChunkRecord record)
    {
      DensityChunkData chunkData = new(record.ChunkCoord);
      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

      using MemoryStream stream = new(record.PayloadBytes);
      using BinaryReader reader = new(stream);

      DensityCompressedKind kind = (DensityCompressedKind)reader.ReadByte();

      switch (kind)
      {
        case DensityCompressedKind.Empty:
          for (int i = 0; i < voxels.Length; i++)
          {
            voxels[i] = DensityVoxel.Empty;
          }
          break;

        case DensityCompressedKind.Solid:
          {
            ushort materialId = reader.ReadUInt16();
            DensityVoxel solid = new(1.0f, materialId == 0 ? (ushort)1 : materialId);
            for (int i = 0; i < voxels.Length; i++)
            {
              voxels[i] = solid;
            }
            break;
          }

        case DensityCompressedKind.MixedRle:
          ReadDensityRle(reader, voxels);
          break;

        default:
          throw new InvalidDataException($"Unknown compressed density payload kind {kind}.");
      }

      return chunkData;
    }

    private static void WriteDensityRle(BinaryWriter writer, DensityVoxel[] voxels)
    {
      int runCountPosition = (int)writer.BaseStream.Position;
      writer.Write(0);

      int runCount = 0;
      int index = 0;

      while (index < voxels.Length)
      {
        short density = QuantizeDensity(voxels[index].Density);
        ushort materialId = density > 0 ? NormalizeMaterial(voxels[index].MaterialId) : (ushort)0;
        ushort length = 1;
        index++;

        while (index < voxels.Length && length < ushort.MaxValue)
        {
          short nextDensity = QuantizeDensity(voxels[index].Density);
          ushort nextMaterialId = nextDensity > 0 ? NormalizeMaterial(voxels[index].MaterialId) : (ushort)0;

          if (nextDensity != density || nextMaterialId != materialId)
          {
            break;
          }

          length++;
          index++;
        }

        writer.Write(length);
        writer.Write(density);
        writer.Write(materialId);
        runCount++;
      }

      long endPosition = writer.BaseStream.Position;
      writer.BaseStream.Position = runCountPosition;
      writer.Write(runCount);
      writer.BaseStream.Position = endPosition;
    }

    private static void ReadDensityRle(BinaryReader reader, DensityVoxel[] voxels)
    {
      int runCount = reader.ReadInt32();
      int index = 0;

      for (int run = 0; run < runCount; run++)
      {
        int length = reader.ReadUInt16();
        short densityQ = reader.ReadInt16();
        ushort materialId = reader.ReadUInt16();
        float density = DequantizeDensity(densityQ);

        for (int i = 0; i < length; i++)
        {
          if (index >= voxels.Length)
          {
            throw new InvalidDataException("Compressed density payload contains too many voxels.");
          }

          voxels[index++] = new DensityVoxel(density, density > 0.0f ? NormalizeMaterial(materialId) : (ushort)0);
        }
      }

      if (index != voxels.Length)
      {
        throw new InvalidDataException($"Compressed density payload decoded {index} voxels, expected {voxels.Length}.");
      }
    }

    private static short QuantizeDensity(float density)
    {
      return (short)Mathf.Clamp(
          Mathf.RoundToInt(density * DensityQuantizationScale),
          short.MinValue,
          short.MaxValue
      );
    }

    private static float DequantizeDensity(short density)
    {
      return density / DensityQuantizationScale;
    }

    private static ushort FindFirstSolidMaterial(DensityVoxel[] voxels)
    {
      for (int i = 0; i < voxels.Length; i++)
      {
        if (voxels[i].Density > 0.0f && voxels[i].MaterialId != 0)
        {
          return voxels[i].MaterialId;
        }
      }

      return 1;
    }

    private static ushort NormalizeMaterial(ushort materialId)
    {
      return materialId == 0 ? (ushort)1 : materialId;
    }
  }
}
