using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Safe launcher fallback. This deliberately avoids TextMeshPro because the launcher
  /// is the first screen and must not depend on TMP font assets being initialised.
  /// </summary>
  public sealed class CubusLoadingLauncherMenu : MonoBehaviour
  {
    private sealed class LocalWorldEntry
    {
      public string WorldId;
      public WorldManifest Manifest;
      public string Path;
    }

    private enum SetupMode
    {
      Local,
      Connected,
    }

    [SerializeField] private string loadingSceneName = "CubusLoading";
    [SerializeField] private string defaultServerUri = "http://cubus.michaelcuneo.com.au";
    [SerializeField] private string defaultModuleName = "cubus";
    [SerializeField] private string defaultWorldId = "demo_world";
    [SerializeField] private Vector2 panelSize = new(640.0f, 760.0f);

    private readonly List<LocalWorldEntry> localWorlds = new();

    private SetupMode mode = SetupMode.Connected;
    private string serverUri;
    private string moduleName;
    private string worldId;
    private string seed;
    private string voxelSize = "1";
    private string minChunkY = "-1";
    private string maxChunkY = "2";
    private string minChunkX = "-8";
    private string minChunkZ = "-8";
    private string maxChunkX = "8";
    private string maxChunkZ = "8";
    private TerrainSystem terrainSystem = TerrainSystem.Block;
    private string status = "Choose how to start Cubus.";
    private Vector2 scroll;
    private bool isLoading;
    private float loadingProgress;
    private float loadingStartedAt;
    private string loadingScenePath;
    private int selectedLocalWorldIndex = -1;
    private bool deleteLocalWorldBeforeLaunch;
    private bool resetConnectedWorldOnLaunch;

    private void Awake()
    {
      serverUri = defaultServerUri;
      moduleName = defaultModuleName;
      worldId = defaultWorldId;
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      RefreshLocalWorlds();
    }

    private void OnGUI()
    {
      if (CubusUiInput.ConsoleOpen)
      {
        return;
      }

      GUI.backgroundColor = new Color(0.08f, 0.12f, 0.16f, 1.0f);

      Rect backgroundRect = new Rect(0.0f, 0.0f, Screen.width, Screen.height);
      GUI.Box(backgroundRect, GUIContent.none);

      Rect panelRect = new Rect(
          Mathf.Max(8.0f, (Screen.width - panelSize.x) * 0.5f),
          Mathf.Max(8.0f, (Screen.height - panelSize.y) * 0.5f),
          Mathf.Min(panelSize.x, Screen.width - 16.0f),
          Mathf.Min(panelSize.y, Screen.height - 16.0f));

      GUI.backgroundColor = Color.white;
      GUILayout.BeginArea(panelRect, GUI.skin.window);
      scroll = GUILayout.BeginScrollView(scroll);

      GUILayout.Label("<b>Cubus</b>");
      GUILayout.Label("Create a local world, or connect to a hosted Cubus world.");
      GUILayout.Space(10.0f);

      GUI.enabled = !isLoading;

      GUILayout.BeginHorizontal();
      if (GUILayout.Toggle(mode == SetupMode.Local, "Local Game", GUI.skin.button)) mode = SetupMode.Local;
      if (GUILayout.Toggle(mode == SetupMode.Connected, "Connected Game", GUI.skin.button)) mode = SetupMode.Connected;
      GUILayout.EndHorizontal();

      GUILayout.Space(10.0f);
      DrawWorldFields();

      if (mode == SetupMode.Connected)
      {
        GUILayout.Space(8.0f);
        DrawServerFields();
      }

      if (CubusDeveloperMode.IsEnabled)
      {
        GUILayout.Space(12.0f);
        DrawDeveloperWorldTools();
      }
      else
      {
        GUILayout.Space(8.0f);
        GUILayout.Label("Dev tools hidden. Open runtime console with ` and run: dev on");
      }

      GUILayout.Space(12.0f);
      if (GUILayout.Button(mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game", GUILayout.Height(36.0f)))
      {
        StartSelectedGame(deleteLocalWorldBeforeLaunch, resetConnectedWorldOnLaunch);
      }

      GUI.enabled = true;

      GUILayout.Space(8.0f);
      GUILayout.Label($"<b>Status:</b> {status}");

      if (isLoading)
      {
        DrawLoadingStatus();
      }

      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    private void DrawLoadingStatus()
    {
      float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
      GUILayout.Space(12.0f);
      GUILayout.Label("<b>Loading CubusLoading</b>");
      GUILayout.Label($"Scene: {loadingScenePath}");
      GUILayout.Label($"Elapsed: {elapsed:0.0}s");
      GUILayout.Label($"Progress: {loadingProgress * 100.0f:0}%");

      Rect progressRect = GUILayoutUtility.GetRect(1.0f, 18.0f, GUILayout.ExpandWidth(true));
      GUI.Box(progressRect, GUIContent.none);
      Rect fill = progressRect;
      fill.width *= Mathf.Clamp01(loadingProgress);
      GUI.Box(fill, GUIContent.none);
      GUILayout.Label("Next stage: CubusLoading loads CubusGame additively and shows terrain progress.");
    }

    private void DrawWorldFields()
    {
      GUILayout.Label("<b>World</b>");
      DrawTextField("World ID", ref worldId);

      GUILayout.BeginHorizontal();
      GUILayout.Label("Terrain", GUILayout.Width(130.0f));
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Block, "Block", GUI.skin.button)) terrainSystem = TerrainSystem.Block;
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.SmoothDensity, "Smooth", GUI.skin.button)) terrainSystem = TerrainSystem.SmoothDensity;
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Seed", GUILayout.Width(130.0f));
      seed = GUILayout.TextField(seed ?? string.Empty);
      if (GUILayout.Button("Random", GUILayout.Width(90.0f))) seed = Random.Range(int.MinValue, int.MaxValue).ToString();
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
    }

    private void DrawDeveloperWorldTools()
    {
      GUILayout.BeginVertical(GUI.skin.box);
      GUILayout.Label("<b>Developer World Tools</b>");

      if (mode == SetupMode.Local)
      {
        DrawLocalDeveloperWorldTools();
      }
      else
      {
        DrawConnectedDeveloperWorldTools();
      }

      GUILayout.EndVertical();
    }

    private void DrawLocalDeveloperWorldTools()
    {
      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Refresh Local Worlds", GUILayout.Height(26.0f)))
      {
        RefreshLocalWorlds();
      }

      if (GUILayout.Button("New Random World ID", GUILayout.Height(26.0f)))
      {
        worldId = $"local_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        seed = Random.Range(int.MinValue, int.MaxValue).ToString();
        selectedLocalWorldIndex = -1;
        deleteLocalWorldBeforeLaunch = false;
        status = $"Prepared new local world id '{worldId}'.";
      }
      GUILayout.EndHorizontal();

      GUILayout.Label($"Storage: {GetLocalWorldRootDirectory()}");

      if (localWorlds.Count == 0)
      {
        GUILayout.Label("No saved local worlds found.");
      }
      else
      {
        for (int i = 0; i < localWorlds.Count; i++)
        {
          LocalWorldEntry entry = localWorlds[i];
          WorldManifest manifest = entry.Manifest;
          string label = manifest == null
              ? entry.WorldId
              : $"{entry.WorldId} | {manifest.TerrainSystem} | Seed {manifest.WorldSeed} | Chunks {manifest.ChunkCount}";

          bool selected = GUILayout.Toggle(selectedLocalWorldIndex == i, label, GUI.skin.button);
          if (selected && selectedLocalWorldIndex != i)
          {
            SelectLocalWorld(i);
          }
        }
      }

      GUILayout.BeginHorizontal();
      GUI.enabled = !isLoading && selectedLocalWorldIndex >= 0;
      if (GUILayout.Button("Use Selected", GUILayout.Height(28.0f)))
      {
        SelectLocalWorld(selectedLocalWorldIndex);
      }

      if (GUILayout.Button("Delete Selected", GUILayout.Height(28.0f)))
      {
        DeleteSelectedLocalWorld();
      }

      if (GUILayout.Button("Regenerate Selected", GUILayout.Height(28.0f)))
      {
        deleteLocalWorldBeforeLaunch = true;
        StartSelectedGame(deleteLocalWorldBeforeLaunch: true, resetConnectedWorld: false);
      }
      GUI.enabled = !isLoading;
      GUILayout.EndHorizontal();

      deleteLocalWorldBeforeLaunch = GUILayout.Toggle(deleteLocalWorldBeforeLaunch, "Delete/regenerate current local World ID before launch");
      GUILayout.Label("Local regenerate deletes the selected local files, then launches using the current World ID/seed/settings.");
    }

    private void DrawConnectedDeveloperWorldTools()
    {
      bool isKnownDevServer = (serverUri ?? string.Empty).IndexOf("cubus.michaelcuneo.com.au", StringComparison.OrdinalIgnoreCase) >= 0;
      GUILayout.Label("Connected worlds are server-authoritative, so local world listing is hidden.");

      if (!isKnownDevServer)
      {
        GUILayout.Label("Reset is intended for the dev server cubus.michaelcuneo.com.au.");
      }

      resetConnectedWorldOnLaunch = GUILayout.Toggle(
          resetConnectedWorldOnLaunch,
          "Reset/regenerate connected world on launch");

      GUILayout.Label("Connected reset publishes current launch settings to SpaceTimeDB and clears server voxel edits/chunks for this World ID.");
    }

    private static void DrawTextField(string label, ref string value)
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label(label, GUILayout.Width(130.0f));
      value = GUILayout.TextField(value ?? string.Empty);
      GUILayout.EndHorizontal();
    }

    private void StartSelectedGame(bool deleteLocalWorldBeforeLaunch, bool resetConnectedWorld)
    {
      if (isLoading || !TryBuildLaunchContext(deleteLocalWorldBeforeLaunch, resetConnectedWorld)) return;

      if (mode == SetupMode.Local && deleteLocalWorldBeforeLaunch)
      {
        DeleteLocalWorldById(worldId);
        RefreshLocalWorlds();
      }

      if (!TryFindSceneInBuildSettings(loadingSceneName, out string scenePath))
      {
        status = $"Scene '{loadingSceneName}' is not in Build Settings.";
        Debug.LogError($"[CubusLauncher] Scene '{loadingSceneName}' was not found in Build Settings.");
        return;
      }

      StartCoroutine(LoadLoadingSceneRoutine(scenePath));
    }

    private IEnumerator LoadLoadingSceneRoutine(string scenePath)
    {
      isLoading = true;
      loadingProgress = 0.0f;
      loadingStartedAt = Time.realtimeSinceStartup;
      loadingScenePath = scenePath;
      status = $"Loading scene {scenePath}...";

      AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
      if (operation == null)
      {
        status = $"Failed to load scene {scenePath}.";
        isLoading = false;
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        loadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
        yield return null;
      }
    }

    private bool TryBuildLaunchContext(bool deleteLocalWorldBeforeLaunch, bool resetConnectedWorld)
    {
      if (!int.TryParse(seed, out int parsedSeed)) { status = "Seed must be an integer."; return false; }
      if (!float.TryParse(voxelSize, out float parsedVoxelSize) || parsedVoxelSize <= 0.0f) { status = "Voxel Size must be a positive number."; return false; }
      if (!int.TryParse(minChunkY, out int parsedMinY) || !int.TryParse(maxChunkY, out int parsedMaxY)) { status = "Chunk Y bounds must be integers."; return false; }
      if (!int.TryParse(minChunkX, out int parsedMinX) || !int.TryParse(minChunkZ, out int parsedMinZ) || !int.TryParse(maxChunkX, out int parsedMaxX) || !int.TryParse(maxChunkZ, out int parsedMaxZ)) { status = "World X/Z bounds must be integers."; return false; }
      if (parsedMinY > parsedMaxY || parsedMinX > parsedMaxX || parsedMinZ > parsedMaxZ) { status = "Minimum bounds must be <= maximum bounds."; return false; }

      if (mode == SetupMode.Local)
      {
        int chunkWidthX = parsedMaxX - parsedMinX + 1;
        int chunkWidthZ = parsedMaxZ - parsedMinZ + 1;
        if (chunkWidthX * chunkWidthZ > 1024)
        {
          status = "Local world bounds are too large for starter generation. Use roughly -8..8 first.";
          return false;
        }
      }

      CubusGameLaunchContext.Set(
          mode == SetupMode.Local ? CubusGameLaunchMode.Local : CubusGameLaunchMode.Connected,
          serverUri,
          moduleName,
          worldId,
          terrainSystem,
          parsedSeed,
          parsedVoxelSize,
          parsedMinY,
          parsedMaxY,
          new Vector2Int(parsedMinX, parsedMinZ),
          new Vector2Int(parsedMaxX, parsedMaxZ),
          deleteLocalWorldBeforeLaunch && mode == SetupMode.Local,
          resetConnectedWorld && mode == SetupMode.Connected);

      return true;
    }

    private void SelectLocalWorld(int index)
    {
      if (index < 0 || index >= localWorlds.Count)
      {
        selectedLocalWorldIndex = -1;
        return;
      }

      selectedLocalWorldIndex = index;
      LocalWorldEntry entry = localWorlds[index];
      worldId = entry.WorldId;

      if (entry.Manifest != null)
      {
        terrainSystem = entry.Manifest.TerrainSystem;
        seed = entry.Manifest.WorldSeed.ToString();
        voxelSize = entry.Manifest.VoxelSize.ToString("R");
        minChunkY = entry.Manifest.MinChunkY.ToString();
        maxChunkY = entry.Manifest.MaxChunkY.ToString();
        minChunkX = entry.Manifest.MinChunkX.ToString();
        minChunkZ = entry.Manifest.MinChunkZ.ToString();
        maxChunkX = entry.Manifest.MaxChunkX.ToString();
        maxChunkZ = entry.Manifest.MaxChunkZ.ToString();
      }

      deleteLocalWorldBeforeLaunch = false;
      status = $"Selected local world '{worldId}'.";
    }

    private void RefreshLocalWorlds()
    {
      localWorlds.Clear();
      selectedLocalWorldIndex = -1;

      string root = GetLocalWorldRootDirectory();
      if (!Directory.Exists(root))
      {
        status = "No saved local worlds found.";
        return;
      }

      foreach (string dir in Directory.GetDirectories(root))
      {
        string manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
          continue;
        }

        try
        {
          WorldManifest manifest = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(manifestPath));
          string id = !string.IsNullOrWhiteSpace(manifest?.WorldId)
              ? manifest.WorldId
              : Path.GetFileName(dir);

          localWorlds.Add(new LocalWorldEntry
          {
            WorldId = id,
            Manifest = manifest,
            Path = dir
          });
        }
        catch (Exception ex)
        {
          Debug.LogWarning($"[CubusLauncher] Failed to read local world manifest '{manifestPath}': {ex.Message}");
        }
      }

      localWorlds.Sort((a, b) => string.Compare(a.WorldId, b.WorldId, StringComparison.OrdinalIgnoreCase));
      status = $"Found {localWorlds.Count} local world(s).";
    }

    private void DeleteSelectedLocalWorld()
    {
      if (selectedLocalWorldIndex < 0 || selectedLocalWorldIndex >= localWorlds.Count)
      {
        return;
      }

      string id = localWorlds[selectedLocalWorldIndex].WorldId;
      DeleteLocalWorldById(id);
      RefreshLocalWorlds();
      selectedLocalWorldIndex = -1;
      status = $"Deleted local world '{id}'.";
    }

    private void DeleteLocalWorldById(string id)
    {
      if (string.IsNullOrWhiteSpace(id))
      {
        return;
      }

      string path = Path.Combine(GetLocalWorldRootDirectory(), SanitizeWorldId(id));
      if (Directory.Exists(path))
      {
        Directory.Delete(path, true);
        Debug.Log($"[CubusLauncher] Deleted local world '{id}' at '{path}'.");
      }
    }

    private static string GetLocalWorldRootDirectory()
    {
      return Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds");
    }

    private static string SanitizeWorldId(string value)
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

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;
      if (string.IsNullOrWhiteSpace(requestedSceneName)) return false;

      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, requestedSceneName, StringComparison.OrdinalIgnoreCase) || string.Equals(path, requestedSceneName, StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }

      return false;
    }
  }
}
