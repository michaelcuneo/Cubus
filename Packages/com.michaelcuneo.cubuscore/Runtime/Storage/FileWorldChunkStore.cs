using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  public sealed class FileWorldChunkStore : IWorldChunkStore
  {
    private readonly string rootDirectory;

    public FileWorldChunkStore(string rootDirectory)
    {
      this.rootDirectory = rootDirectory;
    }

    public bool HasWorld(string worldId)
    {
      return File.Exists(GetManifestPath(worldId));
    }

    public void SaveWorldManifest(WorldManifest manifest)
    {
      string worldDir = GetWorldDirectory(manifest.WorldId);
      Directory.CreateDirectory(worldDir);

      string json = JsonUtility.ToJson(manifest, true);
      File.WriteAllText(GetManifestPath(manifest.WorldId), json);
    }

    public bool TryLoadWorldManifest(string worldId, out WorldManifest manifest)
    {
      string path = GetManifestPath(worldId);

      if (!File.Exists(path))
      {
        manifest = null;
        return false;
      }

      manifest = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(path));
      return manifest != null;
    }

    public void SaveChunk(WorldChunkRecord chunk)
    {
      _ = Task.Run(() => WriteChunk(chunk));
    }

    private void WriteChunk(WorldChunkRecord chunk)
    {
      try
      {
        string dir = GetChunkDirectory(chunk.WorldId);
        Directory.CreateDirectory(dir);

        string path = GetChunkPath(chunk.WorldId, chunk.ChunkCoord);

        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(stream);

        writer.Write((int)chunk.TerrainSystem);
        writer.Write((byte)chunk.PayloadFormat);
        writer.Write(chunk.PayloadVersion);
        writer.Write(chunk.IsEmpty);
        writer.Write(chunk.HasSurface);
        writer.Write(chunk.PayloadBytes.Length);
        writer.Write(chunk.PayloadBytes);
      }
      catch (System.Exception ex)
      {
        Debug.LogWarning($"Failed to save Cubus chunk file. WorldId={chunk.WorldId}, Chunk={chunk.ChunkCoord}, Error={ex.Message}");
      }
    }

    public bool TryLoadChunk(string worldId, Vector3Int chunkCoord, out WorldChunkRecord chunk)
    {
      string path = GetChunkPath(worldId, chunkCoord);

      if (!File.Exists(path))
      {
        chunk = default;
        return false;
      }

      using FileStream stream = File.OpenRead(path);
      using BinaryReader reader = new(stream);

      var terrainSystem = (Runtime.Terrain.TerrainSystem)reader.ReadInt32();
      var payloadFormat = (CubusChunkPayloadFormat)reader.ReadByte();
      int payloadVersion = reader.ReadInt32();
      bool isEmpty = reader.ReadBoolean();
      bool hasSurface = reader.ReadBoolean();
      int length = reader.ReadInt32();
      byte[] payloadBytes = reader.ReadBytes(length);

      chunk = new WorldChunkRecord(
          worldId,
          chunkCoord,
          terrainSystem,
          payloadFormat,
          payloadVersion,
          isEmpty,
          hasSurface,
          payloadBytes
      );

      return true;
    }

    public IEnumerable<Vector3Int> EnumerateChunkCoords(string worldId)
    {
      string dir = GetChunkDirectory(worldId);

      if (!Directory.Exists(dir))
      {
        yield break;
      }

      string[] files = Directory.GetFiles(dir, "*.chunk", SearchOption.TopDirectoryOnly);

      for (int i = 0; i < files.Length; i++)
      {
        string name = Path.GetFileNameWithoutExtension(files[i]);
        string[] parts = name.Split('_');

        if (parts.Length != 3)
        {
          continue;
        }

        if (!int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y) ||
            !int.TryParse(parts[2], out int z))
        {
          continue;
        }

        yield return new Vector3Int(x, y, z);
      }
    }

    public void DeleteWorld(string worldId)
    {
      string dir = GetWorldDirectory(worldId);

      if (Directory.Exists(dir))
      {
        Directory.Delete(dir, true);
      }
    }

    private string GetWorldDirectory(string worldId)
    {
      return Path.Combine(rootDirectory, Sanitize(worldId));
    }

    private string GetManifestPath(string worldId)
    {
      return Path.Combine(GetWorldDirectory(worldId), "manifest.json");
    }

    private string GetChunkDirectory(string worldId)
    {
      return Path.Combine(GetWorldDirectory(worldId), "chunks");
    }

    private string GetChunkPath(string worldId, Vector3Int chunkCoord)
    {
      return Path.Combine(
          GetChunkDirectory(worldId),
          $"{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}.chunk"
      );
    }

    private static string Sanitize(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        return "default_world";
      }

      foreach (char invalid in Path.GetInvalidFileNameChars())
      {
        value = value.Replace(invalid, '_');
      }

      return value.Trim();
    }
  }
}
