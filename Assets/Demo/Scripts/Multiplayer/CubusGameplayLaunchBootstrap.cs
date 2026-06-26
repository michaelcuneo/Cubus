using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Multiplayer
{
  [DefaultExecutionOrder(-10000)]
  public sealed class CubusGameplayLaunchBootstrap : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private CubusNetworkManager network;
    [SerializeField] private bool requireLauncherSelection = true;
    [SerializeField] private string launcherSceneName = "CubusLauncher";
    [SerializeField] private float connectedWorldReadyTimeoutSeconds = 30.0f;

    private void Awake()
    {
      WorldPersistence.SuppressAutoLoadOnStart = true;
      FindReferences();
    }

    private IEnumerator Start()
    {
      if (!CubusGameLaunchContext.HasLaunch)
      {
        if (requireLauncherSelection)
        {
          Debug.LogWarning("[CubusLaunch] Gameplay scene loaded without launcher selection. Loading launcher scene instead.");
          network?.Disconnect();

          Cursor.lockState = CursorLockMode.None;
          Cursor.visible = true;

          if (!string.IsNullOrWhiteSpace(launcherSceneName))
          {
            SceneManager.LoadScene(launcherSceneName);
          }
        }

        yield break;
      }

      if (!ApplyLaunchSettingsToWorld())
      {
        yield break;
      }

      if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Local)
      {
        yield return PrepareLocalWorldThenSpawn();
      }
      else if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Connected)
      {
        yield return PrepareConnectedWorldThenSpawn();
      }
      else
      {
        Debug.LogWarning($"[CubusLaunch] Unsupported launch mode: {CubusGameLaunchContext.Mode}");
      }
    }

    private bool ApplyLaunchSettingsToWorld()
    {
      if (world == null || world.Settings == null)
      {
        Debug.LogError("[CubusLaunch] No CubusWorld found in gameplay scene.");
        return false;
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

      return true;
    }

    private IEnumerator PrepareLocalWorldThenSpawn()
    {
      network?.Disconnect();
      storage?.DeleteWorldDatabase();

      if (world == null)
      {
        yield break;
      }

      world.ClearWorldAndOverrides();

      WorldStreamer streamer = world.GetComponent<WorldStreamer>();
      if (streamer != null)
      {
        Debug.Log($"[CubusLaunch] Preparing local streamed world '{CubusGameLaunchContext.WorldId}' before spawning.");
        streamer.ClearStreamingState();
        streamer.RegenerateStreamedWorld();
        yield return WaitForInitialTerrainReady("local streamed world", 0.0f);
      }
      else
      {
        Debug.Log($"[CubusLaunch] Generating local world '{CubusGameLaunchContext.WorldId}' before spawning.");
        yield return world.GenerateWorldAsync();
        world.BroadcastInitialTerrainReady(Vector3.zero);
      }

      Debug.Log($"[CubusLaunch] Local world '{CubusGameLaunchContext.WorldId}' is ready. Player spawn released.");
    }

    private IEnumerator PrepareConnectedWorldThenSpawn()
    {
      if (network == null)
      {
        Debug.LogError("[CubusLaunch] No CubusNetworkManager found in gameplay scene.");
        yield break;
      }

      if (network.IsConnected || network.Conn != null)
      {
        network.Disconnect();
      }

      WorldStreamer streamer = world != null ? world.GetComponent<WorldStreamer>() : null;
      if (streamer != null)
      {
        streamer.ClearStreamingState();
      }

      network.ConfigureServer(
          CubusGameLaunchContext.ServerUri,
          CubusGameLaunchContext.ModuleName,
          CubusGameLaunchContext.WorldId);

      network.Connect();

      Debug.Log($"[CubusLaunch] Connecting to {CubusGameLaunchContext.ServerUri} / {CubusGameLaunchContext.ModuleName} / {CubusGameLaunchContext.WorldId}. Waiting for authoritative terrain before spawning.");
      yield return WaitForInitialTerrainReady("connected world", connectedWorldReadyTimeoutSeconds);
    }

    private IEnumerator WaitForInitialTerrainReady(string label, float timeoutSeconds)
    {
      if (world == null)
      {
        yield break;
      }

      float startTime = Time.realtimeSinceStartup;
      while (!world.IsInitialTerrainReady)
      {
        if (timeoutSeconds > 0.0f && Time.realtimeSinceStartup - startTime > timeoutSeconds)
        {
          Debug.LogWarning($"[CubusLaunch] Timed out waiting for {label} terrain. Player remains gated until CubusWorld broadcasts initial terrain ready.");
          yield break;
        }

        yield return null;
      }
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
