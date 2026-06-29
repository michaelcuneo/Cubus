using System.Collections;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using Assets.Demo.Scripts.Core;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class DemoLoadingLauncherMenu : MonoBehaviour
  {
    private enum SetupMode
    {
      Local,
      Connected,
    }

    [SerializeField] private string loadingSceneName = "DemoLoading";
    [SerializeField] private string defaultServerUri = "";
    [SerializeField] private string defaultModuleName = "demo";
    [SerializeField] private string defaultWorldId = "demo_world";
    [SerializeField] private Vector2 panelSize = new(620.0f, 720.0f);

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

    private void Awake()
    {
      serverUri = defaultServerUri;
      moduleName = defaultModuleName;
      worldId = defaultWorldId;
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }

    private void OnGUI()
    {
      Rect panelRect = new(
        Mathf.Max(8.0f, (Screen.width - panelSize.x) * 0.5f),
        Mathf.Max(8.0f, (Screen.height - panelSize.y) * 0.5f),
        Mathf.Min(panelSize.x, Screen.width - 16.0f),
        Mathf.Min(panelSize.y, Screen.height - 16.0f));

      GUILayout.BeginArea(panelRect, GUI.skin.window);
      scroll = GUILayout.BeginScrollView(scroll);
      GUILayout.Label("<b>DEMO</b>");
      GUILayout.Label("Create a local world, or connect to a hosted Demo world.");
      GUILayout.Space(10.0f);

      GUI.enabled = !isLoading;
      GUILayout.BeginHorizontal();
      if (GUILayout.Toggle(mode == SetupMode.Local, mode == SetupMode.Local ? "LOCAL GAME" : "Local Game", GUI.skin.button)) mode = SetupMode.Local;
      if (GUILayout.Toggle(mode == SetupMode.Connected, mode == SetupMode.Connected ? "CONNECTED GAME" : "Connected Game", GUI.skin.button)) mode = SetupMode.Connected;
      GUILayout.EndHorizontal();

      GUILayout.Space(10.0f);
      DrawWorldFields();

      if (mode == SetupMode.Connected)
      {
        GUILayout.Space(8.0f);
        DrawServerFields();
      }

      GUILayout.Space(12.0f);
      if (GUILayout.Button(mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game", GUILayout.Height(42.0f)))
      {
        StartSelectedGame();
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

    private void DrawWorldFields()
    {
      GUILayout.Label("<b>World</b>");
      DrawTextField("World ID", ref worldId);
      GUILayout.BeginHorizontal();
      GUILayout.Label("Terrain", GUILayout.Width(140.0f));
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Block, terrainSystem == TerrainSystem.Block ? "BLOCK" : "Block", GUI.skin.button)) terrainSystem = TerrainSystem.Block;
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.SmoothDensity, terrainSystem == TerrainSystem.SmoothDensity ? "SMOOTH" : "Smooth", GUI.skin.button)) terrainSystem = TerrainSystem.SmoothDensity;
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Hybrid, terrainSystem == TerrainSystem.Hybrid ? "HYBRID" : "Hybrid", GUI.skin.button)) terrainSystem = TerrainSystem.Hybrid;
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Seed", GUILayout.Width(140.0f));
      seed = GUILayout.TextField(seed ?? string.Empty);
      if (GUILayout.Button("Random", GUILayout.Width(100.0f))) seed = Random.Range(int.MinValue, int.MaxValue).ToString();
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

    private void DrawTextField(string label, ref string value)
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label(label, GUILayout.Width(140.0f));
      value = GUILayout.TextField(value ?? string.Empty);
      GUILayout.EndHorizontal();
    }

    private void DrawLoadingStatus()
    {
      float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
      GUILayout.Space(10.0f);
      GUILayout.Label("<b>Loading</b>");
      GUILayout.Label($"Scene: {loadingScenePath}");
      GUILayout.Label($"Elapsed: {elapsed:0.0}s");
      GUILayout.Label($"Progress: {loadingProgress * 100.0f:0}%");
    }

    private void StartSelectedGame()
    {
      if (isLoading || !TryBuildLaunchContext()) return;
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

    private bool TryBuildLaunchContext()
    {
      if (!int.TryParse(seed, out int parsedSeed)) { status = "Seed must be an integer."; return false; }
      if (!float.TryParse(voxelSize, out float parsedVoxelSize) || parsedVoxelSize <= 0.0f) { status = "Voxel Size must be a positive number."; return false; }
      if (!int.TryParse(minChunkY, out int parsedMinY) || !int.TryParse(maxChunkY, out int parsedMaxY)) { status = "Chunk Y bounds must be integers."; return false; }
      if (!int.TryParse(minChunkX, out int parsedMinX) || !int.TryParse(minChunkZ, out int parsedMinZ) || !int.TryParse(maxChunkX, out int parsedMaxX) || !int.TryParse(maxChunkZ, out int parsedMaxZ)) { status = "World X/Z bounds must be integers."; return false; }
      if (parsedMinY > parsedMaxY || parsedMinX > parsedMaxX || parsedMinZ > parsedMaxZ) { status = "Minimum bounds must be <= maximum bounds."; return false; }

      DemoGameLaunchContext.Set(
        mode == SetupMode.Local ? DemoGameLaunchMode.Local : DemoGameLaunchMode.Connected,
        serverUri,
        moduleName,
        worldId,
        terrainSystem,
        parsedSeed,
        parsedVoxelSize,
        parsedMinY,
        parsedMaxY,
        new Vector2Int(parsedMinX, parsedMinZ),
        new Vector2Int(parsedMaxX, parsedMaxZ));
      return true;
    }

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;
      if (string.IsNullOrWhiteSpace(requestedSceneName)) return false;
      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, requestedSceneName, System.StringComparison.OrdinalIgnoreCase) || string.Equals(path, requestedSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }
      return false;
    }
  }
}
