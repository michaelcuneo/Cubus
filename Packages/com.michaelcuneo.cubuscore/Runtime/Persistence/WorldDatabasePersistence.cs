using System;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence
{
  public static class WorldDatabasePersistence
  {
    private const int Magic = 0x43554244; // CUBD
    private const int Version = 1;

    public static string GetDefaultDatabaseDirectory()
    {
      return Path.Combine(Application.persistentDataPath, "CubusCore", "Databases");
    }

    public static string GetDatabasePath(string databaseName)
    {
      string safeName = string.IsNullOrWhiteSpace(databaseName)
          ? "cubus_world"
          : databaseName.Trim();

      return Path.Combine(GetDefaultDatabaseDirectory(), $"{safeName}.cubusdb");
    }

    public static bool Exists(string databaseName)
    {
      return File.Exists(GetDatabasePath(databaseName));
    }

    public static void Save(string databaseName, CubusWorld world)
    {
      if (world == null)
      {
        throw new ArgumentNullException(nameof(world));
      }

      string path = GetDatabasePath(databaseName);
      Directory.CreateDirectory(Path.GetDirectoryName(path));

      using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
      using BinaryWriter writer = new(stream);

      WorldSettings settings = world.Settings;

      writer.Write(Magic);
      writer.Write(Version);

      writer.Write((int)settings.TerrainSystem);
      writer.Write(VoxelConstants.ChunkSize);
      writer.Write(settings.VoxelSize);

      writer.Write(settings.BlockMinChunkY);
      writer.Write(settings.BlockMaxChunkY);
      writer.Write(settings.DensityMinChunkY);
      writer.Write(settings.DensityMaxChunkY);

      settings.GetEffectiveGenerationChunkBounds3D(
          out int minChunkX,
          out int maxChunkX,
          out _,
          out _,
          out int minChunkZ,
          out int maxChunkZ
      );

      writer.Write(minChunkX);
      writer.Write(maxChunkX);
      writer.Write(minChunkZ);
      writer.Write(maxChunkZ);

      switch (settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          SaveBlockChunks(writer, world);
          break;

        case TerrainSystem.SmoothDensity:
        default:
          SaveDensityChunks(writer, world);
          break;
      }

      Debug.Log(
          $"Saved Cubus world database. " +
          $"Path={path}, Mode={settings.TerrainSystem}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, " +
          $"DensityChunks={world.Data.DensityChunks.Count}"
      );
    }

    public static bool Load(string databaseName, CubusWorld world)
    {
      if (world == null)
      {
        throw new ArgumentNullException(nameof(world));
      }

      string path = GetDatabasePath(databaseName);

      if (!File.Exists(path))
      {
        Debug.LogWarning($"No Cubus world database exists. Path={path}");
        return false;
      }

      using FileStream stream = File.OpenRead(path);
      using BinaryReader reader = new(stream);

      int magic = reader.ReadInt32();
      if (magic != Magic)
      {
        Debug.LogWarning($"Invalid Cubus world database magic. Path={path}");
        return false;
      }

      int version = reader.ReadInt32();
      if (version != Version)
      {
        Debug.LogWarning($"Unsupported Cubus world database version. Version={version}, Path={path}");
        return false;
      }

      TerrainSystem terrainSystem = (TerrainSystem)reader.ReadInt32();

      int chunkSize = reader.ReadInt32();
      if (chunkSize != VoxelConstants.ChunkSize)
      {
        Debug.LogWarning(
            $"Cubus world database chunk size mismatch. Save={chunkSize}, Current={VoxelConstants.ChunkSize}, Path={path}"
        );
        return false;
      }

      WorldSettings settings = world.Settings;

      settings.TerrainSystem = terrainSystem;
      settings.VoxelSize = Mathf.Max(0.01f, reader.ReadSingle());

      settings.BlockMinChunkY = reader.ReadInt32();
      settings.BlockMaxChunkY = reader.ReadInt32();
      settings.DensityMinChunkY = reader.ReadInt32();
      settings.DensityMaxChunkY = reader.ReadInt32();

      Vector2Int minXZ = new(reader.ReadInt32(), reader.ReadInt32());
      Vector2Int maxXZ = new(reader.ReadInt32(), reader.ReadInt32());

      // Careful: file writes X min/max then Z min/max.
      settings.GenerationMinChunkXZ = new(minXZ.x, maxXZ.x);
      settings.GenerationMaxChunkXZ = new(minXZ.y, maxXZ.y);
      settings.UseFixedGenerationBounds = true;

      world.Data.ClearGeneratedChunks();

      switch (terrainSystem)
      {
        case TerrainSystem.Block:
          LoadBlockChunks(reader, world);
          break;

        case TerrainSystem.SmoothDensity:
        default:
          LoadDensityChunks(reader, world);
          break;
      }

      world.MarkDatabaseLoaded();

      Debug.Log(
          $"Loaded Cubus world database. " +
          $"Path={path}, Mode={terrainSystem}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, " +
          $"DensityChunks={world.Data.DensityChunks.Count}"
      );

      return true;
    }

    private static void SaveBlockChunks(BinaryWriter writer, CubusWorld world)
    {
      writer.Write(world.Data.BlockChunks.Count);

      foreach (var pair in world.Data.BlockChunks)
      {
        Vector3Int chunkCoord = pair.Key;
        BlockChunkData chunkData = pair.Value;

        writer.Write(chunkCoord.x);
        writer.Write(chunkCoord.y);
        writer.Write(chunkCoord.z);

        Voxel[] voxels = chunkData.GetRawVoxelArray();

        for (int i = 0; i < voxels.Length; i++)
        {
          writer.Write(voxels[i].MaterialId);
        }
      }
    }

    private static void LoadBlockChunks(BinaryReader reader, CubusWorld world)
    {
      int chunkCount = reader.ReadInt32();

      for (int c = 0; c < chunkCount; c++)
      {
        Vector3Int chunkCoord = new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32()
        );

        BlockChunkData chunkData = new(chunkCoord);
        Voxel[] voxels = chunkData.GetRawVoxelArray();

        for (int i = 0; i < voxels.Length; i++)
        {
          voxels[i] = new Voxel(reader.ReadUInt16());
        }

        world.Data.BlockChunks[chunkCoord] = chunkData;
      }
    }

    private static void SaveDensityChunks(BinaryWriter writer, CubusWorld world)
    {
      writer.Write(world.Data.DensityChunks.Count);

      foreach (var pair in world.Data.DensityChunks)
      {
        Vector3Int chunkCoord = pair.Key;
        DensityChunkData chunkData = pair.Value;

        writer.Write(chunkCoord.x);
        writer.Write(chunkCoord.y);
        writer.Write(chunkCoord.z);

        DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

        for (int i = 0; i < voxels.Length; i++)
        {
          writer.Write(voxels[i].Density);
          writer.Write(voxels[i].MaterialId);
        }
      }
    }

    private static void LoadDensityChunks(BinaryReader reader, CubusWorld world)
    {
      int chunkCount = reader.ReadInt32();

      for (int c = 0; c < chunkCount; c++)
      {
        Vector3Int chunkCoord = new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32()
        );

        DensityChunkData chunkData = new(chunkCoord);
        DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

        for (int i = 0; i < voxels.Length; i++)
        {
          float density = reader.ReadSingle();
          ushort materialId = reader.ReadUInt16();

          voxels[i] = new DensityVoxel(density, materialId);
        }

        world.Data.DensityChunks[chunkCoord] = chunkData;
      }
    }
  }
}
