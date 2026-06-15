using System;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage
{
  [DisallowMultipleComponent]
  [RequireComponent(typeof(CubusWorld))]
  public sealed class CubusWorldStorage : MonoBehaviour
  {
    [SerializeField] private WorldStorageBackend backend = WorldStorageBackend.LocalFile;
    [SerializeField] private string worldId = "demo_world";
    [SerializeField] private bool autoSaveAfterGeneration = true;
    [SerializeField] private bool autoLoadOnStart = true;

    private CubusWorld world;
    private IWorldChunkStore activeStore;

    private WorldManifest loadedManifest;
    public bool HasLoadedManifest => loadedManifest != null;
    public WorldManifest LoadedManifest => loadedManifest;

    public string WorldId => string.IsNullOrWhiteSpace(worldId) ? "demo_world" : worldId.Trim();
    public IWorldChunkStore ActiveStore => activeStore;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      activeStore = CreateStore();
    }

    private void Start()
    {
      if (autoLoadOnStart)
      {
        LoadWorldDatabase();
      }
    }

    public void SaveWorldDatabase()
    {
      EnsureStore();

      WorldManifest manifest = CreateManifest();
      activeStore.SaveWorldManifest(manifest);

      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          foreach (var pair in world.Data.BlockChunks)
          {
            WorldChunkRecord record = CubusChunkPayloadCodec.EncodeBlockChunk(WorldId, pair.Key, pair.Value);
            activeStore.SaveChunk(record);
          }
          break;

        case TerrainSystem.SmoothDensity:
        default:
          foreach (var pair in world.Data.DensityChunks)
          {
            WorldChunkRecord record = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, pair.Key, pair.Value);
            activeStore.SaveChunk(record);
          }
          break;
      }

      Debug.Log(
          $"Saved Cubus world database. WorldId={WorldId}, Backend={backend}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, DensityChunks={world.Data.DensityChunks.Count}"
      );
    }

    public bool LoadWorldDatabase()
    {
      EnsureStore();

      if (!activeStore.TryLoadWorldManifest(WorldId, out WorldManifest manifest))
      {
        return false;
      }

      ApplyManifest(manifest);

      world.Data.ClearGeneratedChunks();

      foreach (Vector3Int chunkCoord in activeStore.EnumerateChunkCoords(WorldId))
      {
        if (!activeStore.TryLoadChunk(WorldId, chunkCoord, out WorldChunkRecord record))
        {
          continue;
        }

        switch (record.TerrainSystem)
        {
          case TerrainSystem.Block:
            world.Data.BlockChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeBlockChunk(record);
            break;

          case TerrainSystem.SmoothDensity:
          default:
            world.Data.DensityChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeDensityChunk(record);
            break;
        }
      }

      world.MarkDatabaseLoaded();

      Debug.Log(
          $"Loaded Cubus world database. WorldId={WorldId}, Backend={backend}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, DensityChunks={world.Data.DensityChunks.Count}"
      );

      return true;
    }

    public bool LoadWorldManifestOnly()
    {
      EnsureStore();

      if (!activeStore.TryLoadWorldManifest(WorldId, out WorldManifest manifest))
      {
        return false;
      }

      loadedManifest = manifest;
      ApplyManifest(manifest);

      world.Data.ClearGeneratedChunks();
      world.MarkDatabaseLoaded();

      Debug.Log(
          $"Loaded Cubus world manifest. WorldId={WorldId}, Backend={backend}, " +
          $"Mode={manifest.TerrainSystem}, Chunks={manifest.ChunkCount}, " +
          $"Bounds=({manifest.MinChunkX},{manifest.MinChunkY},{manifest.MinChunkZ}) -> " +
          $"({manifest.MaxChunkX},{manifest.MaxChunkY},{manifest.MaxChunkZ})"
      );

      return true;
    }

    public bool TryLoadChunk(Vector3Int chunkCoord)
    {
      EnsureStore();

      if (!activeStore.TryLoadChunk(WorldId, chunkCoord, out WorldChunkRecord record))
      {
        return false;
      }

      switch (record.TerrainSystem)
      {
        case TerrainSystem.Block:
          world.Data.BlockChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeBlockChunk(record);
          return true;

        case TerrainSystem.SmoothDensity:
        default:
          world.Data.DensityChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeDensityChunk(record);
          return true;
      }
    }

    public void DeleteWorldDatabase()
    {
      EnsureStore();
      activeStore.DeleteWorld(WorldId);
      Debug.Log($"Deleted Cubus world database. WorldId={WorldId}, Backend={backend}");
    }

    public void SaveAfterGenerationIfEnabled()
    {
      if (autoSaveAfterGeneration)
      {
        SaveWorldDatabase();
      }
    }

    private IWorldChunkStore CreateStore()
    {
      switch (backend)
      {
        case WorldStorageBackend.LocalFile:
          return new FileWorldChunkStore(
              Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds")
          );

        case WorldStorageBackend.SpacetimeDb:
          Debug.LogWarning(
              "SpacetimeDB backend is selected, but the SpacetimeDB integration package is not installed. Falling back to local file storage."
          );
          return new FileWorldChunkStore(
              Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds")
          );

        case WorldStorageBackend.Custom:
        default:
          Debug.LogWarning("Custom backend selected, but no custom store provider is assigned. Falling back to local file storage.");
          return new FileWorldChunkStore(
              Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds")
          );
      }
    }

    private void EnsureStore()
    {
      world ??= GetComponent<CubusWorld>();
      activeStore ??= CreateStore();
    }

    private WorldManifest CreateManifest()
    {
      WorldSettings settings = world.Settings;

      settings.GetGenerationChunkBoundsXZ(
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      int minChunkY;
      int maxChunkY;

      if (settings.TerrainSystem == TerrainSystem.Block)
      {
        settings.GetEffectiveBlockChunkYRange(out minChunkY, out maxChunkY);
      }
      else
      {
        settings.GetEffectiveDensityChunkYRange(out minChunkY, out maxChunkY);
      }

      return new WorldManifest
      {
        SchemaVersion = 1,
        WorldId = WorldId,
        DisplayName = WorldId,
        TerrainSystem = settings.TerrainSystem,
        ChunkSize = VoxelConstants.ChunkSize,
        VoxelSize = settings.VoxelSize,
        MinChunkX = minChunkX,
        MaxChunkX = maxChunkX,
        MinChunkY = minChunkY,
        MaxChunkY = maxChunkY,
        MinChunkZ = minChunkZ,
        MaxChunkZ = maxChunkZ,
        ChunkCount = settings.TerrainSystem == TerrainSystem.Block
            ? world.Data.BlockChunks.Count
            : world.Data.DensityChunks.Count,
        UpdatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        CreatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
      };
    }

    private void ApplyManifest(WorldManifest manifest)
    {
      WorldSettings settings = world.Settings;

      settings.TerrainSystem = manifest.TerrainSystem;
      settings.VoxelSize = Mathf.Max(0.01f, manifest.VoxelSize);
      settings.UseFixedGenerationBounds = true;
      settings.GenerationMinChunkXZ = new Vector2Int(manifest.MinChunkX, manifest.MinChunkZ);
      settings.GenerationMaxChunkXZ = new Vector2Int(manifest.MaxChunkX, manifest.MaxChunkZ);

      switch (manifest.TerrainSystem)
      {
        case TerrainSystem.Block:
          settings.BlockMinChunkY = manifest.MinChunkY;
          settings.BlockMaxChunkY = manifest.MaxChunkY;
          break;

        case TerrainSystem.SmoothDensity:
        default:
          settings.DensityMinChunkY = manifest.MinChunkY;
          settings.DensityMaxChunkY = manifest.MaxChunkY;
          break;
      }
    }
  }
}