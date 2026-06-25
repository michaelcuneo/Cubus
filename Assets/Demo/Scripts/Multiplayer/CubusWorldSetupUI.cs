using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Simple in-game world setup menu for the demo. This is intentionally IMGUI so
  /// it can be dropped into the scene without building a Canvas yet. A polished
  /// main menu can call the same public methods later.
  /// </summary>
  [DefaultExecutionOrder(-425)]
  public sealed class CubusWorldSetupUI : MonoBehaviour
  {
    private enum SetupMode
    {
      Local = 0,
      Connected = 1,
    }

    [Header("References")]
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private CubusNetworkManager network;
    [SerializeField] private NetworkedWorldStateBridge worldStateBridge;

    [Header("Defaults")]
    [SerializeField] private string defaultServerUri = "http://cubus.michaelcuneo.com.au";
    [SerializeField] private string defaultModuleName = "cubus";
    [SerializeField] private string defaultWorldId = "demo_world";

    [Header("UI")]
    [SerializeField] private bool showOnStart = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;
    [SerializeField] private Vector2 panelSize = new(460.0f, 620.0f);

    private SetupMode mode = SetupMode.Connected;
    private bool visible;
    private string serverUri;
    private string moduleName;
    private string worldId;
    private string seed;
    private string voxelSize;
    private string minChunkY;
    private string maxChunkY;
    private string minChunkX;
    private string minChunkZ;
    private string maxChunkX;
    private string maxChunkZ;
    private TerrainSystem terrainSystem;
    private string status = "World setup ready.";
    private Vector2 scroll;

    private void Awake()
    {
      FindReferences();
      visible = showOnStart;

      WorldSettings settings = world != null ? world.Settings : null;

      serverUri = network != null && !string.IsNullOrWhiteSpace(network.ServerUri)
          ? network.ServerUri
          : defaultServerUri;
      moduleName = network != null && !string.IsNullOrWhiteSpace(network.ModuleName)
          ? network.ModuleName
          : defaultModuleName;
      worldId = network != null && !string.IsNullOrWhiteSpace(network.WorldId)
          ? network.WorldId
          : defaultWorldId;

      terrainSystem = settings != null ? settings.TerrainSystem : TerrainSystem.Block;
      seed = (settings != null ? settings.WorldSeed : Random.Range(int.MinValue, int.MaxValue)).ToString();
      voxelSize = (settings != null ? settings.VoxelSize : 1.0f).ToString("0.###");

      if (settings != null)
      {
        settings.GetActiveVerticalChunkBounds(out int activeMinY, out int activeMaxY);
        minChunkY = activeMinY.ToString();
        maxChunkY = activeMaxY.ToString();
        minChunkX = settings.WorldMinChunkXZ.x.ToString();
        minChunkZ = settings.WorldMinChunkXZ.y.ToString();
        maxChunkX = settings.WorldMaxChunkXZ.x.ToString();
        maxChunkZ = settings.WorldMaxChunkXZ.y.ToString();
      }
      else
      {
        minChunkY = "-1";
        maxChunkY = "2";
        minChunkX = "-1024";
        minChunkZ = "-1024";
        maxChunkX = "1024";
        maxChunkZ = "1024";
      }
    }

    private void Update()
    {
      if (Input.GetKeyDown(toggleKey))
      {
        visible = !visible;
      }
    }

    private void OnGUI()
    {
      if (!visible)
      {
        return;
      }

      float x = 20.0f;
      float y = 20.0f;
      GUILayout.BeginArea(new Rect(x, y, panelSize.x, panelSize.y), GUI.skin.box);
      scroll = GUILayout.BeginScrollView(scroll);

      GUILayout.Label("<b>Cubus World Setup</b>");
      GUILayout.Label("Create a local world, or create/join a connected world on SpaceTimeDB.");
      GUILayout.Space(8.0f);

      GUILayout.BeginHorizontal();
      if (GUILayout.Toggle(mode == SetupMode.Local, "Local Game", GUI.skin.button))
      {
        mode = SetupMode.Local;
      }
      if (GUILayout.Toggle(mode == SetupMode.Connected, "Connected Game", GUI.skin.button))
      {
        mode = SetupMode.Connected;
      }
      GUILayout.EndHorizontal();

      GUILayout.Space(8.0f);
      DrawWorldFields();

      if (mode == SetupMode.Connected)
      {
        GUILayout.Space(8.0f);
        DrawServerFields();
      }

      GUILayout.Space(10.0f);
      DrawActions();

      GUILayout.Space(10.0f);
      GUILayout.Label($"<b>Status:</b> {status}");
      GUILayout.Label($"<i>Press {toggleKey} to hide/show this setup panel.</i>");

      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    private void DrawWorldFields()
    {
      GUILayout.Label("<b>World</b>");

      GUILayout.BeginHorizontal();
      GUILayout.Label("World ID", GUILayout.Width(130.0f));
      worldId = GUILayout.TextField(worldId ?? string.Empty);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Terrain", GUILayout.Width(130.0f));
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Block, "Block", GUI.skin.button))
      {
        terrainSystem = TerrainSystem.Block;
      }
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.SmoothDensity, "Smooth", GUI.skin.button))
      {
        terrainSystem = TerrainSystem.SmoothDensity;
      }
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Seed", GUILayout.Width(130.0f));
      seed = GUILayout.TextField(seed ?? string.Empty);
      if (GUILayout.Button("Random", GUILayout.Width(90.0f)))
      {
        seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      }
      GUILayout.EndHorizontal();

      DrawTextField("Voxel Size", ref voxelSize);
      DrawTextField("Min Chunk Y", ref minChunkY);
      DrawTextField("Max Chunk Y", ref maxChunkY);
      DrawTextField("Min Chunk X", ref minChunkX);
      DrawTextField("Min Chunk Z", ref minChunkZ);
      DrawTextField("Max Chunk X", ref maxChunkX);
      DrawTextField("Max Chunk Z", ref maxChunkZ);
    }

    private void DrawServerFields()
    {
      GUILayout.Label("<b>Server</b>");
      DrawTextField("Server URI", ref serverUri);
      DrawTextField("Module", ref moduleName);

      GUILayout.Label(network != null && network.IsConnected
          ? $"Connected to {network.ServerUri} / {network.ModuleName}"
          : "Not connected");
    }

    private void DrawTextField(string label, ref string value)
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label(label, GUILayout.Width(130.0f));
      value = GUILayout.TextField(value ?? string.Empty);
      GUILayout.EndHorizontal();
    }

    private void DrawActions()
    {
      GUILayout.Label("<b>Actions</b>");

      if (mode == SetupMode.Local)
      {
        if (GUILayout.Button("Create Local Game"))
        {
          CreateLocalGame();
        }

        if (GUILayout.Button("Delete Local World Cache"))
        {
          DeleteLocalWorldCache();
        }

        return;
      }

      if (GUILayout.Button(network != null && network.IsConnected ? "Reconnect" : "Connect"))
      {
        ConnectConfiguredServer();
      }

      if (GUILayout.Button("Create / Publish Connected World"))
      {
        CreateConnectedWorld();
      }

      if (GUILayout.Button("Reset Connected World From These Settings"))
      {
        ResetConnectedWorld();
      }

      if (GUILayout.Button("Delete Connected World Instance"))
      {
        DeleteConnectedWorldInstance();
      }
    }

    public void CreateLocalGame()
    {
      if (!TryApplyFieldsToWorldSettings())
      {
        return;
      }

      if (network != null && network.IsConnected)
      {
        network.Disconnect();
      }

      if (storage != null)
      {
        storage.DeleteWorldDatabase();
      }

      if (world == null)
      {
        status = "No CubusWorld found.";
        return;
      }

      world.ClearWorldAndOverrides();
      WorldStreamer streamer = world.GetComponent<WorldStreamer>();
      if (streamer == null)
      {
        world.GenerateWorld();
      }

      status = $"Created local world '{worldId}'.";
    }

    public void CreateConnectedWorld()
    {
      if (!TryApplyFieldsToWorldSettings())
      {
        return;
      }

      ConnectConfiguredServer();

      if (network != null && network.IsSubscriptionApplied && worldStateBridge != null)
      {
        worldStateBridge.PublishCurrentSceneSettings();
      }

      status = $"Creating connected world '{worldId}'. Waiting for SpaceTimeDB subscription if not applied yet.";
    }

    public void ResetConnectedWorld()
    {
      if (!TryApplyFieldsToWorldSettings())
      {
        return;
      }

      if (!EnsureConnectedSubscription())
      {
        return;
      }

      if (worldStateBridge == null)
      {
        status = "No NetworkedWorldStateBridge found.";
        return;
      }

      worldStateBridge.ResetServerWorldFromCurrentSceneSettings();

      storage?.DeleteWorldDatabase();
      world?.ClearWorldAndOverrides();

      status = $"Reset connected world '{worldId}'.";
    }

    public void DeleteConnectedWorldInstance()
    {
      if (!EnsureConnectedSubscription())
      {
        return;
      }

      string normalizedWorldId = string.IsNullOrWhiteSpace(worldId) ? defaultWorldId : worldId.Trim();
      network.Conn.Reducers.DeleteWorldInstance(normalizedWorldId);

      storage?.DeleteWorldDatabase();
      world?.ClearWorldAndOverrides();

      status = $"Deleted connected world instance '{normalizedWorldId}'.";
    }

    public void DeleteLocalWorldCache()
    {
      storage?.DeleteWorldDatabase();
      world?.ClearWorldAndOverrides();
      status = $"Deleted local cache for '{worldId}'.";
    }

    private void ConnectConfiguredServer()
    {
      FindReferences();

      if (network == null)
      {
        status = "No CubusNetworkManager found.";
        return;
      }

      if (network.IsConnected || network.Conn != null)
      {
        network.Disconnect();
      }

      network.ConfigureServer(serverUri, moduleName, worldId);
      network.Connect();
      status = $"Connecting to {serverUri} / {moduleName} / {worldId}.";
    }

    private bool EnsureConnectedSubscription()
    {
      FindReferences();

      if (network == null || !network.IsConnected || network.Conn == null)
      {
        status = "Not connected to SpaceTimeDB.";
        return false;
      }

      if (!network.IsSubscriptionApplied)
      {
        status = "Connected, but subscription has not applied yet.";
        return false;
      }

      return true;
    }

    private bool TryApplyFieldsToWorldSettings()
    {
      FindReferences();

      if (world == null || world.Settings == null)
      {
        status = "No CubusWorld found.";
        return false;
      }

      if (!int.TryParse(seed, out int parsedSeed))
      {
        status = "Seed must be an integer.";
        return false;
      }

      if (!float.TryParse(voxelSize, out float parsedVoxelSize) || parsedVoxelSize <= 0.0f)
      {
        status = "Voxel Size must be a positive number.";
        return false;
      }

      if (!int.TryParse(minChunkY, out int parsedMinY) || !int.TryParse(maxChunkY, out int parsedMaxY))
      {
        status = "Chunk Y bounds must be integers.";
        return false;
      }

      if (!int.TryParse(minChunkX, out int parsedMinX) || !int.TryParse(minChunkZ, out int parsedMinZ) ||
          !int.TryParse(maxChunkX, out int parsedMaxX) || !int.TryParse(maxChunkZ, out int parsedMaxZ))
      {
        status = "World X/Z bounds must be integers.";
        return false;
      }

      if (parsedMinY > parsedMaxY || parsedMinX > parsedMaxX || parsedMinZ > parsedMaxZ)
      {
        status = "Minimum bounds must be <= maximum bounds.";
        return false;
      }

      WorldSettings settings = world.Settings;
      settings.TerrainSystem = terrainSystem;
      settings.WorldSeed = parsedSeed;
      settings.VoxelSize = Mathf.Max(0.001f, parsedVoxelSize);
      settings.UseWorldBounds = true;
      settings.WorldMinChunkXZ = new Vector2Int(parsedMinX, parsedMinZ);
      settings.WorldMaxChunkXZ = new Vector2Int(parsedMaxX, parsedMaxZ);
      settings.BlockMinChunkY = parsedMinY;
      settings.BlockMaxChunkY = parsedMaxY;
      settings.DensityMinChunkY = parsedMinY;
      settings.DensityMaxChunkY = parsedMaxY;
      settings.InvalidateBiomeVariableCache();

      if (network != null && !network.IsConnected && !string.IsNullOrWhiteSpace(worldId))
      {
        network.WorldId = worldId.Trim();
      }

      return true;
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

      if (worldStateBridge == null)
      {
        worldStateBridge = FindObjectOfType<NetworkedWorldStateBridge>();
      }
    }
  }
}
