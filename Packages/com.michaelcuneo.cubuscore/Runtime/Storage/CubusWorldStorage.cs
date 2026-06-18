using System;
using System.Diagnostics;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
    [SerializeField] private bool autoLoadManifestOnlyOnStart = true;

    [Header("Diagnostics")]
    [SerializeField] private bool logCompressionStats = true;
    [SerializeField] private bool logTimingStats = true;
    [SerializeField] private bool logSingleChunkCompressionStats = false;

    private CubusWorld world;
    private IWorldChunkStore activeStore;
    private WorldManifest loadedManifest;
    private bool storageReadsDisabledForTerrainSystem;

    public bool HasLoadedManifest => loadedManifest != null;
    public WorldManifest LoadedManifest => loadedManifest;
    public string WorldId => string.IsNullOrWhiteSpace(worldId) ? "demo_world" : worldId.Trim();

    // The streamer uses ActiveStore for lazy chunk reads. When the saved manifest belongs
    // to another terrain system, returning the raw store lets block mode load density
    // records (or vice versa), after which the streamer never generates replacement chunks.
    // Hide the store for reads until a compatible world is saved or loaded.
    public IWorldChunkStore ActiveStore => storageReadsDisabledForTerrainSystem ? null : activeStore;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      activeStore = CreateStore();
    }

    private void Start()
    {
      if (!autoLoadOnStart)
      {
        return;
      }

      if (GetComponent<WorldStreamer>() != null)
      {
        return;
      }

      if (autoLoadManifestOnlyOnStart)
      {
        LoadWorldManifestOnly();
      }
      else
      {
        LoadWorldDatabase();
      }
    }

    public bool SaveChunk(Vector3Int chunkCoord)
    {
      EnsureStore();
      StorageTimingStats timingStats = new();
      Stopwatch totalWatch = Stopwatch.StartNew();

      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          if (!world.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData blockChunk) || blockChunk == null)
          {
            return false;
          }

          Stopwatch encodeBlockWatch = Stopwatch.StartNew();
          WorldChunkRecord blockRecord = CubusChunkPayloadCodec.EncodeBlockChunk(WorldId, chunkCoord, blockChunk);
          encodeBlockWatch.Stop();
          timingStats.EncodeTicks += encodeBlockWatch.ElapsedTicks;

          Stopwatch writeBlockWatch = Stopwatch.StartNew();
          activeStore.SaveChunk(blockRecord);
          writeBlockWatch.Stop();
          timingStats.WriteTicks += writeBlockWatch.ElapsedTicks;
          timingStats.Chunks = 1;

          storageReadsDisabledForTerrainSystem = false;
          totalWatch.Stop();
          timingStats.TotalTicks = totalWatch.ElapsedTicks;
          LogSingleChunkCompressionIfEnabled("Saved", blockRecord);
          LogTimingStatsIfEnabled("Saved chunk", timingStats);
          return true;

        case TerrainSystem.SmoothDensity:
        default:
          if (!world.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData densityChunk) || densityChunk == null)
          {
            return false;
          }

          Stopwatch encodeDensityWatch = Stopwatch.StartNew();
          WorldChunkRecord densityRecord = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, chunkCoord, densityChunk);
          encodeDensityWatch.Stop();
          timingStats.EncodeTicks += encodeDensityWatch.ElapsedTicks;

          Stopwatch writeDensityWatch = Stopwatch.StartNew();
          activeStore.SaveChunk(densityRecord);
          writeDensityWatch.Stop();
          timingStats.WriteTicks += writeDensityWatch.ElapsedTicks;
          timingStats.Chunks = 1;

          storageReadsDisabledForTerrainSystem = false;
          totalWatch.Stop();
          timingStats.TotalTicks = totalWatch.ElapsedTicks;
          LogSingleChunkCompressionIfEnabled("Saved", densityRecord);
          LogTimingStatsIfEnabled("Saved chunk", timingStats);
          return true;
      }
    }

    public void SaveWorldDatabase()
    {
      EnsureStore();
      Stopwatch totalWatch = Stopwatch.StartNew();
      StorageTimingStats timingStats = new();

      Stopwatch manifestWatch = Stopwatch.StartNew();
      WorldManifest manifest = CreateManifest();
      activeStore.SaveWorldManifest(manifest);
      manifestWatch.Stop();
      timingStats.ManifestTicks += manifestWatch.ElapsedTicks;

      CompressionStats compressionStats = new();

      switch (world.Settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          foreach (var pair in world.Data.BlockChunks)
          {
            Stopwatch encodeWatch = Stopwatch.StartNew();
            WorldChunkRecord record = CubusChunkPayloadCodec.EncodeBlockChunk(WorldId, pair.Key, pair.Value);
            encodeWatch.Stop();
            timingStats.EncodeTicks += encodeWatch.ElapsedTicks;
            compressionStats.Add(record);

            Stopwatch writeWatch = Stopwatch.StartNew();
            activeStore.SaveChunk(record);
            writeWatch.Stop();
            timingStats.WriteTicks += writeWatch.ElapsedTicks;
            timingStats.Chunks++;
          }
          break;

        case TerrainSystem.SmoothDensity:
        default:
          foreach (var pair in world.Data.DensityChunks)
          {
            Stopwatch encodeWatch = Stopwatch.StartNew();
            WorldChunkRecord record = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, pair.Key, pair.Value);
            encodeWatch.Stop();
            timingStats.EncodeTicks += encodeWatch.ElapsedTicks;
            compressionStats.Add(record);

            Stopwatch writeWatch = Stopwatch.StartNew();
            activeStore.SaveChunk(record);
            writeWatch.Stop();
            timingStats.WriteTicks += writeWatch.ElapsedTicks;
            timingStats.Chunks++;
          }
          break;
      }

      storageReadsDisabledForTerrainSystem = false;
      loadedManifest = manifest;
      totalWatch.Stop();
      timingStats.TotalTicks = totalWatch.ElapsedTicks;

      Debug.Log(
          $"Saved Cubus world database. WorldId={WorldId}, Backend={backend}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, DensityChunks={world.Data.DensityChunks.Count}"
      );

      LogCompressionStatsIfEnabled("Saved", compressionStats);
      LogTimingStatsIfEnabled("Saved", timingStats);
    }

    public bool LoadWorldDatabase()
    {
      EnsureStore();
      Stopwatch totalWatch = Stopwatch.StartNew();
      StorageTimingStats timingStats = new();

      Stopwatch manifestWatch = Stopwatch.StartNew();
      bool hasManifest = activeStore.TryLoadWorldManifest(WorldId, out WorldManifest manifest);
      manifestWatch.Stop();
      timingStats.ManifestTicks += manifestWatch.ElapsedTicks;

      if (!hasManifest || !CanUseManifestForCurrentTerrainSystem(manifest, "database"))
      {
        return false;
      }

      loadedManifest = manifest;
      storageReadsDisabledForTerrainSystem = false;
      ApplyManifest(manifest);

      world.Data.ClearGeneratedChunks();
      CompressionStats compressionStats = new();

      foreach (Vector3Int chunkCoord in activeStore.EnumerateChunkCoords(WorldId))
      {
        Stopwatch readWatch = Stopwatch.StartNew();
        bool loaded = activeStore.TryLoadChunk(WorldId, chunkCoord, out WorldChunkRecord record);
        readWatch.Stop();
        timingStats.ReadTicks += readWatch.ElapsedTicks;

        if (!loaded || record.TerrainSystem != world.Settings.TerrainSystem)
        {
          continue;
        }

        compressionStats.Add(record);
        timingStats.Chunks++;

        try
        {
          Stopwatch decodeWatch = Stopwatch.StartNew();

          switch (record.TerrainSystem)
          {
            case TerrainSystem.Block:
              world.Data.BlockChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeBlockChunk(record);
              break;

            case TerrainSystem.SmoothDensity:
              world.Data.DensityChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeDensityChunk(record);
              break;

            default:
              Debug.LogWarning($"Unsupported terrain system in chunk record. Chunk={chunkCoord}, Terrain={record.TerrainSystem}");
              break;
          }

          decodeWatch.Stop();
          timingStats.DecodeTicks += decodeWatch.ElapsedTicks;
        }
        catch (Exception ex)
        {
          Debug.LogWarning($"Failed to decode Cubus chunk during full load. WorldId={WorldId}, Chunk={chunkCoord}, Error={ex.Message}");
        }
      }

      world.MarkDatabaseLoaded();
      totalWatch.Stop();
      timingStats.TotalTicks = totalWatch.ElapsedTicks;

      Debug.Log(
          $"Loaded Cubus world database. WorldId={WorldId}, Backend={backend}, " +
          $"BlockChunks={world.Data.BlockChunks.Count}, DensityChunks={world.Data.DensityChunks.Count}"
      );

      LogCompressionStatsIfEnabled("Loaded", compressionStats);
      LogTimingStatsIfEnabled("Loaded", timingStats);
      return true;
    }

    public bool LoadWorldManifestOnly()
    {
      EnsureStore();
      Stopwatch totalWatch = Stopwatch.StartNew();

      if (!activeStore.TryLoadWorldManifest(WorldId, out WorldManifest manifest) ||
          !CanUseManifestForCurrentTerrainSystem(manifest, "manifest"))
      {
        return false;
      }

      loadedManifest = manifest;
      storageReadsDisabledForTerrainSystem = false;
      ApplyManifest(manifest);

      world.Data.ClearGeneratedChunks();
      world.MarkDatabaseLoaded();

      totalWatch.Stop();

      Debug.Log(
          $"Loaded Cubus world manifest. WorldId={WorldId}, Backend={backend}, " +
          $"Mode={manifest.TerrainSystem}, Chunks={manifest.ChunkCount}, " +
          $"Bounds=({manifest.MinChunkX},{manifest.MinChunkY},{manifest.MinChunkZ}) -> " +
          $"({manifest.MaxChunkX},{manifest.MaxChunkY},{manifest.MaxChunkZ})"
      );

      if (logTimingStats)
      {
        Debug.Log($"Cubus chunk storage timing Loaded manifest. Total={FormatMilliseconds(totalWatch.ElapsedTicks)}");
      }

      return true;
    }

    public bool TryLoadChunk(Vector3Int chunkCoord)
    {
      EnsureStore();
      if (storageReadsDisabledForTerrainSystem)
      {
        return false;
      }

      StorageTimingStats timingStats = new();
      Stopwatch totalWatch = Stopwatch.StartNew();

      Stopwatch readWatch = Stopwatch.StartNew();
      bool loaded = activeStore.TryLoadChunk(WorldId, chunkCoord, out WorldChunkRecord record);
      readWatch.Stop();
      timingStats.ReadTicks += readWatch.ElapsedTicks;

      if (!loaded || record.TerrainSystem != world.Settings.TerrainSystem)
      {
        return false;
      }

      timingStats.Chunks = 1;
      LogSingleChunkCompressionIfEnabled("Loaded", record);

      try
      {
        Stopwatch decodeWatch = Stopwatch.StartNew();

        switch (record.TerrainSystem)
        {
          case TerrainSystem.Block:
            world.Data.BlockChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeBlockChunk(record);
            break;

          case TerrainSystem.SmoothDensity:
            world.Data.DensityChunks[chunkCoord] = CubusChunkPayloadCodec.DecodeDensityChunk(record);
            break;

          default:
            Debug.LogWarning($"Unsupported terrain system in chunk record. Chunk={chunkCoord}, Terrain={record.TerrainSystem}");
            return false;
        }

        decodeWatch.Stop();
        timingStats.DecodeTicks += decodeWatch.ElapsedTicks;
        totalWatch.Stop();
        timingStats.TotalTicks = totalWatch.ElapsedTicks;
        LogTimingStatsIfEnabled("Loaded chunk", timingStats);
        return true;
      }
      catch (Exception ex)
      {
        Debug.LogWarning($"Failed to decode Cubus chunk. WorldId={WorldId}, Chunk={chunkCoord}, Error={ex.Message}");
        return false;
      }
    }

    public void DeleteWorldDatabase()
    {
      EnsureStore();
      activeStore.DeleteWorld(WorldId);
      loadedManifest = null;
      storageReadsDisabledForTerrainSystem = false;
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
          return new FileWorldChunkStore(Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds"));

        case WorldStorageBackend.SpacetimeDb:
          Debug.LogWarning("SpacetimeDB backend is selected, but the SpacetimeDB integration package is not installed. Falling back to local file storage.");
          return new FileWorldChunkStore(Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds"));

        case WorldStorageBackend.Custom:
        default:
          Debug.LogWarning("Custom backend selected, but no custom store provider is assigned. Falling back to local file storage.");
          return new FileWorldChunkStore(Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds"));
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
      settings.GetGenerationChunkBoundsXZ(out int minChunkX, out int maxChunkX, out int minChunkZ, out int maxChunkZ);

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

      long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
        ChunkCount = settings.TerrainSystem == TerrainSystem.Block ? world.Data.BlockChunks.Count : world.Data.DensityChunks.Count,
        UpdatedUnixTime = now,
        CreatedUnixTime = now
      };
    }

    private bool CanUseManifestForCurrentTerrainSystem(WorldManifest manifest, string loadKind)
    {
      if (manifest == null)
      {
        return false;
      }

      TerrainSystem runtimeTerrainSystem = world != null && world.Settings != null
          ? world.Settings.TerrainSystem
          : manifest.TerrainSystem;

      if (manifest.TerrainSystem == runtimeTerrainSystem)
      {
        return true;
      }

      loadedManifest = null;
      storageReadsDisabledForTerrainSystem = true;

      Debug.LogWarning(
          $"Ignoring Cubus world {loadKind}. WorldId={WorldId}, Backend={backend}, " +
          $"SavedMode={manifest.TerrainSystem}, RuntimeMode={runtimeTerrainSystem}. " +
          "Generate or save the world in the active terrain mode to replace the stored manifest."
      );

      return false;
    }

    private void ApplyManifest(WorldManifest manifest)
    {
      if (manifest == null)
      {
        return;
      }

      if (manifest.ChunkSize != VoxelConstants.ChunkSize)
      {
        Debug.LogWarning($"Loaded world chunk size {manifest.ChunkSize} differs from runtime chunk size {VoxelConstants.ChunkSize}.");
      }
    }

    private void LogSingleChunkCompressionIfEnabled(string operation, WorldChunkRecord record)
    {
      if (!logCompressionStats || !logSingleChunkCompressionStats)
      {
        return;
      }

      int rawBytes = GetEstimatedRawPayloadBytes(record.TerrainSystem);
      int payloadBytes = record.PayloadBytes?.Length ?? 0;
      float ratio = rawBytes > 0 ? payloadBytes / (float)rawBytes : 0.0f;

      Debug.Log(
          $"Cubus chunk payload {operation}. Chunk={record.ChunkCoord}, Terrain={record.TerrainSystem}, " +
          $"Format={record.PayloadFormat}, Version={record.PayloadVersion}, Payload={FormatBytes(payloadBytes)}, " +
          $"Raw={FormatBytes(rawBytes)}, Ratio={ratio:P1}"
      );
    }

    private void LogCompressionStatsIfEnabled(string operation, CompressionStats stats)
    {
      if (!logCompressionStats || stats.TotalChunks <= 0)
      {
        return;
      }

      float ratio = stats.RawBytes > 0 ? stats.PayloadBytes / (float)stats.RawBytes : 0.0f;
      long savedBytes = Math.Max(0L, stats.RawBytes - stats.PayloadBytes);

      Debug.Log(
          $"Cubus chunk payload stats {operation}. " +
          $"Chunks={stats.TotalChunks}, Block={stats.BlockChunks}, Density={stats.DensityChunks}, " +
          $"Raw={FormatBytes(stats.RawBytes)}, Payload={FormatBytes(stats.PayloadBytes)}, " +
          $"Saved={FormatBytes(savedBytes)}, Ratio={ratio:P1}, " +
          $"DensityRaw={stats.DensityRawChunks}, DensityCompressed={stats.DensityCompressedChunks}"
      );
    }

    private void LogTimingStatsIfEnabled(string operation, StorageTimingStats stats)
    {
      if (!logTimingStats || stats.Chunks <= 0)
      {
        return;
      }

      Debug.Log(
          $"Cubus chunk storage timing {operation}. " +
          $"Chunks={stats.Chunks}, Total={FormatMilliseconds(stats.TotalTicks)}, " +
          $"Manifest={FormatMilliseconds(stats.ManifestTicks)}, Encode={FormatMilliseconds(stats.EncodeTicks)}, " +
          $"Write={FormatMilliseconds(stats.WriteTicks)}, Read={FormatMilliseconds(stats.ReadTicks)}, " +
          $"Decode={FormatMilliseconds(stats.DecodeTicks)}"
      );
    }

    private static int GetEstimatedRawPayloadBytes(TerrainSystem terrainSystem)
    {
      return terrainSystem == TerrainSystem.Block
          ? VoxelConstants.ChunkVolume * sizeof(ushort)
          : VoxelConstants.ChunkVolume * (sizeof(float) + sizeof(ushort));
    }

    private static string FormatBytes(long bytes)
    {
      const double kib = 1024.0;
      const double mib = 1024.0 * 1024.0;

      if (bytes >= mib)
      {
        return $"{bytes / mib:0.00} MiB";
      }

      if (bytes >= kib)
      {
        return $"{bytes / kib:0.00} KiB";
      }

      return $"{bytes} B";
    }

    private static string FormatMilliseconds(long ticks)
    {
      double milliseconds = ticks * 1000.0 / Stopwatch.Frequency;
      return $"{milliseconds:0.00} ms";
    }

    private struct StorageTimingStats
    {
      public int Chunks;
      public long TotalTicks;
      public long ManifestTicks;
      public long EncodeTicks;
      public long WriteTicks;
      public long ReadTicks;
      public long DecodeTicks;
    }

    private struct CompressionStats
    {
      public int TotalChunks;
      public int BlockChunks;
      public int DensityChunks;
      public int DensityRawChunks;
      public int DensityCompressedChunks;
      public long RawBytes;
      public long PayloadBytes;

      public void Add(WorldChunkRecord record)
      {
        TotalChunks++;
        PayloadBytes += record.PayloadBytes?.Length ?? 0;
        RawBytes += GetEstimatedRawPayloadBytes(record.TerrainSystem);

        if (record.TerrainSystem == TerrainSystem.Block)
        {
          BlockChunks++;
          return;
        }

        if (record.TerrainSystem == TerrainSystem.SmoothDensity)
        {
          DensityChunks++;

          if (record.PayloadFormat == CubusChunkPayloadFormat.DensityF32MaterialU16Raw)
          {
            DensityRawChunks++;
          }
          else if (record.PayloadFormat == CubusChunkPayloadFormat.DensityI16MaterialU16Rle)
          {
            DensityCompressedChunks++;
          }
        }
      }
    }
  }
}
