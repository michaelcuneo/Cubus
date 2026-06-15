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