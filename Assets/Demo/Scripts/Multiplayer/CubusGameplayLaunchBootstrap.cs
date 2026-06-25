using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  [DefaultExecutionOrder(-10000)]
  public sealed class CubusGameplayLaunchBootstrap : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private CubusNetworkManager network;
    [SerializeField] private bool requireLauncherSelection = true;

    private void Awake()
    {
      WorldPersistence.SuppressAutoLoadOnStart = true;
      FindReferences();
    }

    private void Start()
    {
      if (!CubusGameLaunchContext.HasLaunch)
      {
        if (requireLauncherSelection)
        {
          Debug.LogWarning("[CubusLaunch] Gameplay scene loaded without launcher selection. Startup is blocked.");
          network?.Disconnect();
          world?.ClearWorldAndOverrides();
        }

        return;
      }

      ApplyLaunchSettingsToWorld();

      if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Local)
      {
        StartLocalWorld();
      }
      else if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Connected)
      {
        StartConnectedWorld();
      }
      else
      {
        Debug.LogWarning($"[CubusLaunch] Unsupported launch mode: {CubusGameLaunchContext.Mode}");
      }
    }

    private void ApplyLaunchSettingsToWorld()
    {
      if (world == null || world.Settings == null)
      {
        Debug.LogError("[CubusLaunch] No CubusWorld found in gameplay scene.");
        return;
      }

      WorldSettings settings = world.Settings;
      settings.TerrainSystem = CubusGameLaunchContext.TerrainSystem;
      settings.WorldSeed = CubusGameLaunchContext.WorldSeed;
      settings.VoxelSize = CubusGameLaunchContext.VoxelSize;
      settings.UseWorldBounds = true;
      settings.WorldMinChunkXZ = CubusGameLaunchContext.WorldMinChunkXZ;
      settings.WorldMaxChunkXZ = CubusGameLaunchContext.WorldMaxChunkXZ;
      settings.BlockMinChunkY = CubusGameLaunchContext.MinChunkY;
      settings.BlockMaxChunkY = CubusGameLaunchContext.MaxChunkY;
      settings.DensityMinChunkY = CubusGameLaunchContext.MinChunkY;
      settings.DensityMaxChunkY = CubusGameLaunchContext.MaxChunkY;
      settings.InvalidateBiomeVariableCache();

      if (network != null)
      {
        network.WorldId = CubusGameLaunchContext.WorldId;
      }
    }

    private void StartLocalWorld()
    {
      network?.Disconnect();
      storage?.DeleteWorldDatabase();

      if (world == null)
      {
        return;
      }

      world.ClearWorldAndOverrides();
      WorldStreamer streamer = world.GetComponent<WorldStreamer>();
      if (streamer == null)
      {
        world.GenerateWorld();
      }

      Debug.Log($"[CubusLaunch] Started local world '{CubusGameLaunchContext.WorldId}'.");
    }

    private void StartConnectedWorld()
    {
      if (network == null)
      {
        Debug.LogError("[CubusLaunch] No CubusNetworkManager found in gameplay scene.");
        return;
      }

      if (network.IsConnected || network.Conn != null)
      {
        network.Disconnect();
      }

      network.ConfigureServer(
          CubusGameLaunchContext.ServerUri,
          CubusGameLaunchContext.ModuleName,
          CubusGameLaunchContext.WorldId);

      network.Connect();

      Debug.Log($"[CubusLaunch] Connecting to {CubusGameLaunchContext.ServerUri} / {CubusGameLaunchContext.ModuleName} / {CubusGameLaunchContext.WorldId}.");
    }

    private void FindReferences()
    {
      if (world == null)
      {
        world = FindObjectOfType<CubusWorld>();
      }

      if (storage == null && world != null)
      {
        storage = world.GetComponent<CubusWorldStorage>();
      }

      if (network == null)
      {
        network = CubusNetworkManager.Instance != null
            ? CubusNetworkManager.Instance
            : FindObjectOfType<CubusNetworkManager>();
      }
    }
  }
}
