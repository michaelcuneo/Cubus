using System;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace Assets.Demo.Scripts.Persistence
{
  public sealed class DemoLocalWorldAutoSaver : MonoBehaviour
  {
    private bool hasSaved;
    private CubusWorld world;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<DemoLocalWorldAutoSaver>() != null)
      {
        return;
      }

      GameObject go = new("Demo Local World Auto Saver");
      go.AddComponent<DemoLocalWorldAutoSaver>();
      DontDestroyOnLoad(go);
    }

    private void Update()
    {
      if (hasSaved || !CubusGameLaunchContext.HasLaunch || CubusGameLaunchContext.Mode != CubusGameLaunchMode.Local)
      {
        return;
      }

      world ??= FindAnyObjectByType<CubusWorld>();
      if (world == null || !world.IsInitialTerrainReady || world.Settings == null)
      {
        return;
      }

      SaveLocalWorld();
      hasSaved = true;
    }

    private void SaveLocalWorld()
    {
      string worldId = string.IsNullOrWhiteSpace(CubusGameLaunchContext.WorldId)
          ? "demo_world"
          : CubusGameLaunchContext.WorldId.Trim();

      string root = Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds");
      FileWorldChunkStore store = new(root);
      WorldManifest manifest = BuildManifest(worldId);

      store.SaveWorldManifest(manifest);

      int writtenChunks = 0;
      if (world.Settings.TerrainSystem == TerrainSystem.Block)
      {
        foreach (var pair in world.Data.BlockChunks)
        {
          if (pair.Value == null) continue;
          store.SaveChunk(CubusChunkPayloadCodec.EncodeBlockChunk(worldId, pair.Key, pair.Value));
          writtenChunks++;
        }
      }
      else
      {
        foreach (var pair in world.Data.DensityChunks)
        {
          if (pair.Value == null) continue;
          store.SaveChunk(CubusChunkPayloadCodec.EncodeDensityChunk(worldId, pair.Key, pair.Value));
          writtenChunks++;
        }
      }

      Debug.Log($"[CubusLocalSave] Saved local world. WorldId={worldId}, Root={root}, ManifestChunks={manifest.ChunkCount}, WrittenChunks={writtenChunks}");
    }

    private WorldManifest BuildManifest(string worldId)
    {
      WorldSettings settings = world.Settings;
      settings.GetEffectiveGenerationChunkBounds3D(
        out int minChunkX,
        out int maxChunkX,
        out int minChunkY,
        out int maxChunkY,
        out int minChunkZ,
        out int maxChunkZ
      );

      long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      return new WorldManifest
      {
        SchemaVersion = 1,
        WorldId = worldId,
        DisplayName = worldId,
        TerrainSystem = settings.TerrainSystem,
        WorldSeed = settings.WorldSeed,
        ChunkSize = VoxelConstants.ChunkSize,
        VoxelSize = settings.VoxelSize,
        MinChunkX = minChunkX,
        MaxChunkX = maxChunkX,
        MinChunkY = minChunkY,
        MaxChunkY = maxChunkY,
        MinChunkZ = minChunkZ,
        MaxChunkZ = maxChunkZ,
        ChunkCount = settings.TerrainSystem == TerrainSystem.Block ? world.Data.BlockChunks.Count : world.Data.DensityChunks.Count,
        CreatedUnixTime = now,
        UpdatedUnixTime = now
      };
    }
  }
}
